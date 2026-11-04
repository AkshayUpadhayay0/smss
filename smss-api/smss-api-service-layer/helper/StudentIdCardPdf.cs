using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using QuestPDF.Elements;
using QImage = QuestPDF.Infrastructure.Image;

namespace smss_api_service_layer.helper
{
    /// <summary>What the back of every card in one PDF says about the school.</summary>
    public record IdCardSchoolModel(string Name, QImage? Logo, string? Address, string? Phone);

    /// <summary>One card's content. <see cref="Photo"/> null means "draw the initials placeholder".</summary>
    public record IdCardModel(string FullName, string ClassSection, string? RollNumber, string AdmissionNumber, string YearName, QImage? Photo,
        string? GuardianName, string? GuardianRelationship, string? GuardianMobile, string QrPayload)
    {
        public string Initials => IdCardPdf.InitialsOf(FullName);
    }

    /// <summary>
    /// Draws CR80 (85.6 x 54 mm, landscape) student ID cards with QuestPDF (Community license). Two shapes:
    /// single = one 2-page PDF of card-sized pages (front, back); sheet = A4 portrait, 2 x 5 cards per sheet with crop marks, each sheet's
    /// front page immediately followed by its back page so a duplex printer (flip on long edge) lines the two sides up.
    /// </summary>
    public static class IdCardPdf
    {
        public const float CardW = 85.6f, CardH = 54f;          // mm
        public const int Columns = 2, Rows = 5;
        public const int CardsPerSheet = Columns * Rows;
        private const float GapX = 6f, GapY = 2f;                 // gutters between cards, mm (cut room)
        private const float A4W = 210f, A4H = 297f;

        private static readonly Color Violet = Color.FromHex("#7C5CFC");
        private static readonly Color VioletDark = Color.FromHex("#4C2FD0");
        private static readonly Color VioletTint = Color.FromHex("#EDE9FE");
        private static readonly Color Ink = Color.FromHex("#1F2430");
        private static readonly Color Muted = Color.FromHex("#6B7280");

        static IdCardPdf()
        {
            // Community license: free for organisations under USD 1M annual gross revenue (see the report / docs). Set once for the process.
            QuestPDF.Settings.License = LicenseType.Community;
        }

        /// <summary>Decodes photo / logo bytes; null when they are not a readable image (so the caller falls back to the placeholder / name-only header).</summary>
        public static QImage? TryLoadImage(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try { return QImage.FromBinaryData(bytes); }
            catch { return null; }
        }

        public static string InitialsOf(string fullName)
        {
            var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return "?";
            var first = char.ToUpperInvariant(parts[0][0]);
            return parts.Length == 1 ? first.ToString() : $"{first}{char.ToUpperInvariant(parts[^1][0])}";
        }

        /// <summary>Front then back, on CR80-sized pages (one student).</summary>
        public static byte[] RenderSingle(IdCardSchoolModel school, IdCardModel card) =>
            Document.Create(doc =>
            {
                doc.Page(p => { SetupCardPage(p); p.Content().Element(c => Front(c, school, card, false)); });
                doc.Page(p => { SetupCardPage(p); p.Content().Element(c => Back(c, school, card, false)); });
            }).GeneratePdf();

        /// <summary>A4 sheets, 10 cards each: front page, back page, front page, back page ... Returns the PDF and the number of sheets.</summary>
        public static (byte[] Pdf, int Sheets) RenderSheets(IdCardSchoolModel school, IReadOnlyList<IdCardModel> cards)
        {
            var sheets = cards.Chunk(CardsPerSheet).ToList();
            var pdf = Document.Create(doc =>
            {
                foreach (var sheet in sheets)
                {
                    doc.Page(p => { SetupSheetPage(p); p.Content().Element(c => SheetGrid(c, sheet, false, (e, m) => Front(e, school, m, true))); });
                    // Long-edge duplex flips the paper left-to-right, so the back page lists each row right-to-left to meet its front
                    doc.Page(p => { SetupSheetPage(p); p.Content().Element(c => SheetGrid(c, sheet, true, (e, m) => Back(e, school, m, true))); });
                }
            }).GeneratePdf();
            return (pdf, sheets.Count);
        }

        private static void SetupCardPage(PageDescriptor p)
        {
            p.Size(new PageSize(CardW, CardH, Unit.Millimetre));
            p.Margin(0);
            p.DefaultTextStyle(t => t.FontSize(7).FontColor(Ink).FontFamily("Lato"));
        }

        private static void SetupSheetPage(PageDescriptor p)
        {
            p.Size(new PageSize(A4W, A4H, Unit.Millimetre));
            p.Margin(0);
            p.DefaultTextStyle(t => t.FontSize(7).FontColor(Ink).FontFamily("Lato"));
        }

        // ---- sheet layout -----------------------------------------------------------------------------------------------------------

        private static void SheetGrid(IContainer page, IReadOnlyList<IdCardModel> sheet, bool mirror, Action<IContainer, IdCardModel> draw)
        {
            const float gridW = Columns * CardW + (Columns - 1) * GapX;
            const float gridH = Rows * CardH + (Rows - 1) * GapY;
            const float mx = (A4W - gridW) / 2, my = (A4H - gridH) / 2;

            page.Layers(layers =>
            {
                layers.PrimaryLayer().PaddingLeft(mx, Unit.Millimetre).PaddingTop(my, Unit.Millimetre).Column(col =>
                {
                    for (var r = 0; r < Rows; r++)
                    {
                        col.Item().PaddingTop(r == 0 ? 0 : GapY, Unit.Millimetre).Row(row =>
                        {
                            for (var c = 0; c < Columns; c++)
                            {
                                var index = r * Columns + (mirror ? Columns - 1 - c : c);
                                if (c > 0) row.ConstantItem(GapX, Unit.Millimetre);
                                var item = row.ConstantItem(CardW, Unit.Millimetre);
                                if (index < sheet.Count) draw(item, sheet[index]);
                            }
                        });
                    }
                });

                // crop marks: short ticks in the margins, in line with every card edge, 1 mm clear of the cards
                const float len = 3f, clear = 1.5f, t = 0.2f;
                var xs = new List<float>();
                for (var c = 0; c < Columns; c++) { var left = mx + c * (CardW + GapX); xs.Add(left); xs.Add(left + CardW); }
                var ys = new List<float>();
                for (var r = 0; r < Rows; r++) { var top = my + r * (CardH + GapY); ys.Add(top); ys.Add(top + CardH); }
                foreach (var x in xs)
                {
                    Mark(layers, x - t / 2, my - clear - len, t, len);
                    Mark(layers, x - t / 2, my + gridH + clear, t, len);
                }
                foreach (var y in ys)
                {
                    Mark(layers, mx - clear - len, y - t / 2, len, t);
                    Mark(layers, mx + gridW + clear, y - t / 2, len, t);
                }
            });
        }

        private static void Mark(LayersDescriptor layers, float x, float y, float w, float h) =>
            layers.Layer().TranslateX(x, Unit.Millimetre).TranslateY(y, Unit.Millimetre)
                .Width(w, Unit.Millimetre).Height(h, Unit.Millimetre).Background(Colors.Black);

        // ---- the card faces ---------------------------------------------------------------------------------------------------------

        private static IContainer Frame(IContainer c, bool guide) =>
            guide
                ? c.Width(CardW, Unit.Millimetre).Height(CardH, Unit.Millimetre).Border(0.15f).BorderColor(Color.FromHex("#D1D5DB"))
                : c.Width(CardW, Unit.Millimetre).Height(CardH, Unit.Millimetre);

        private static void Front(IContainer container, IdCardSchoolModel school, IdCardModel card, bool guide)
        {
            Frame(container, guide).Column(col =>
            {
                // header: logo (when the school has one) + school name
                col.Item().Height(12, Unit.Millimetre).Background(Violet).PaddingHorizontal(3, Unit.Millimetre).Row(row =>
                {
                    if (school.Logo != null)
                        row.ConstantItem(9, Unit.Millimetre).AlignMiddle().Height(9, Unit.Millimetre).Background(Colors.White).Padding(0.6f, Unit.Millimetre).Image(school.Logo).FitArea();
                    row.RelativeItem().PaddingLeft(school.Logo != null ? 2.5f : 0, Unit.Millimetre).AlignMiddle()
                        .AlignCenter().Text(school.Name).FontSize(school.Name.Length > 34 ? 8 : 9.5f).Bold().FontColor(Colors.White).ClampLines(2).AlignCenter();
                });

                col.Item().Height(37, Unit.Millimetre).PaddingHorizontal(3.5f, Unit.Millimetre).PaddingVertical(3, Unit.Millimetre).Row(row =>
                {
                    row.ConstantItem(23, Unit.Millimetre).Height(29, Unit.Millimetre).Element(p => Photo(p, card));
                    row.ConstantItem(3.5f, Unit.Millimetre);
                    row.RelativeItem().Column(info =>
                    {
                        info.Item().Text(card.FullName).FontSize(card.FullName.Length > 24 ? 9 : 10.5f).Bold().FontColor(VioletDark).ClampLines(2);
                        info.Item().PaddingTop(1.2f, Unit.Millimetre).Text(card.ClassSection).FontSize(8.5f).SemiBold();
                        info.Item().PaddingTop(2, Unit.Millimetre).Element(e => Field(e, "Roll No", string.IsNullOrWhiteSpace(card.RollNumber) ? "-" : card.RollNumber!));
                        info.Item().PaddingTop(0.8f, Unit.Millimetre).Element(e => Field(e, "Adm. No", card.AdmissionNumber));
                        info.Item().PaddingTop(0.8f, Unit.Millimetre).Element(e => Field(e, "Session", card.YearName));
                    });
                });

                col.Item().Height(5, Unit.Millimetre).Background(VioletTint).AlignMiddle().AlignCenter()
                    .Text("STUDENT IDENTITY CARD").FontSize(5.5f).Bold().FontColor(VioletDark).LetterSpacing(0.12f);
            });
        }

        private static void Field(IContainer c, string label, string value) =>
            c.Row(r =>
            {
                r.ConstantItem(13, Unit.Millimetre).Text(label).FontSize(6.5f).FontColor(Muted);
                r.RelativeItem().Text(value).FontSize(7.5f).SemiBold().ClampLines(1);
            });

        private static void Photo(IContainer c, IdCardModel card)
        {
            var box = c.Border(0.4f).BorderColor(Violet);
            if (card.Photo != null) box.Background(VioletTint).Image(card.Photo).FitArea();
            else box.Background(VioletTint).AlignMiddle().AlignCenter().Text(card.Initials).FontSize(22).Bold().FontColor(Violet);
        }

        private static void Back(IContainer container, IdCardSchoolModel school, IdCardModel card, bool guide)
        {
            Frame(container, guide).Column(col =>
            {
                col.Item().Height(4, Unit.Millimetre).Background(Violet);

                col.Item().Height(44, Unit.Millimetre).PaddingHorizontal(4, Unit.Millimetre).PaddingTop(2.5f, Unit.Millimetre).Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text("IF FOUND, PLEASE RETURN TO").FontSize(5.5f).Bold().FontColor(Muted).LetterSpacing(0.08f);
                        left.Item().PaddingTop(0.8f, Unit.Millimetre).Text(school.Name).FontSize(8).Bold().FontColor(VioletDark).ClampLines(2);
                        if (!string.IsNullOrWhiteSpace(school.Address))
                            left.Item().PaddingTop(0.5f, Unit.Millimetre).Text(school.Address).FontSize(6.5f).ClampLines(3);
                        if (!string.IsNullOrWhiteSpace(school.Phone))
                            left.Item().PaddingTop(0.5f, Unit.Millimetre).Text(t => { t.DefaultTextStyle(x => x.FontSize(6.5f)); t.Span("Phone: ").FontColor(Muted); t.Span(school.Phone!).SemiBold(); });

                        left.Item().PaddingTop(2.5f, Unit.Millimetre).LineHorizontal(0.3f).LineColor(VioletTint);
                        left.Item().PaddingTop(1.5f, Unit.Millimetre).Text("PARENT / GUARDIAN").FontSize(5.5f).Bold().FontColor(Muted).LetterSpacing(0.08f);
                        if (!string.IsNullOrWhiteSpace(card.GuardianName))
                        {
                            var rel = string.IsNullOrWhiteSpace(card.GuardianRelationship) ? "" : $" ({card.GuardianRelationship})";
                            left.Item().PaddingTop(0.6f, Unit.Millimetre).Text($"{card.GuardianName}{rel}").FontSize(8).SemiBold().ClampLines(2);
                            if (!string.IsNullOrWhiteSpace(card.GuardianMobile))
                                left.Item().PaddingTop(0.4f, Unit.Millimetre).Text(t => { t.DefaultTextStyle(x => x.FontSize(7.5f)); t.Span("Contact: ").FontColor(Muted); t.Span(card.GuardianMobile!).SemiBold(); });
                        }
                        else left.Item().PaddingTop(0.6f, Unit.Millimetre).Text("Not recorded").FontSize(7).FontColor(Muted);
                    });

                    row.ConstantItem(3, Unit.Millimetre);
                    row.ConstantItem(21, Unit.Millimetre).AlignBottom().PaddingBottom(0.5f, Unit.Millimetre).Column(q =>
                    {
                        q.Item().Width(21, Unit.Millimetre).Height(21, Unit.Millimetre).Border(0.3f).BorderColor(VioletTint).Padding(1.2f, Unit.Millimetre).Background(Colors.White)
                            .Svg(IdCardQr.Svg(card.QrPayload));
                        foreach (var part in card.QrPayload.Split(IdCardQr.Separator, 2))
                            q.Item().PaddingTop(0.3f, Unit.Millimetre).AlignCenter().Text(part).FontSize(4.6f).FontColor(Muted).ClampLines(1);
                    });
                });

                col.Item().Height(6, Unit.Millimetre).Background(VioletTint).AlignMiddle().AlignCenter()
                    .Text($"Valid for academic session {card.YearName}").FontSize(5.5f).Bold().FontColor(VioletDark);
            });
        }
    }
}
