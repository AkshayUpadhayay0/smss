using System.Reflection;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static smss_api_service_layer.helper.idcard.Kit;

namespace smss_api_service_layer.helper.idcard
{
    /// <summary>
    /// Draws portrait CR80 (54 x 85.6 mm) student ID cards with QuestPDF (Community licence). Shapes are vector SVG, text is real text.
    /// single = one 2-page PDF of card-sized pages (front, back);
    /// sheets = A4 portrait, 3 x 3 cards per sheet with crop marks, each sheet's front page immediately followed by its back page, the back
    /// mirrored left-right so a duplex printer (flip on long edge) lines each card's two sides up;
    /// preview = the two faces as PNG images.
    /// </summary>
    public static class IdCardPdf
    {
        public const float CardW = 54f, CardH = 85.6f;          // mm
        public const int Columns = 3, Rows = 3;
        public const int CardsPerSheet = Columns * Rows;
        private const float GapX = 5f, GapY = 3f;                 // gutters between cards, mm (cut room)
        private const float A4W = 210f, A4H = 297f;

        static IdCardPdf() => PdfBootstrap.Ensure();   // licence, glyph rule and the embedded fonts: shared with every other PDF in the application

        public static IdCardPalette PaletteOf(IdCardBrand brand) => IdCardPalette.From(brand.Primary, brand.Secondary);

        private static CardCtx Ctx(IdCardSchoolModel school, IdCardBrand brand, IdCardPalette pal, IdCardModel card) =>
            new() { Pal = pal, Card = card, School = school, Brand = brand };

        private static IdCardTemplate TemplateOf(IdCardBrand brand) => IdCardTemplates.Find(brand.TemplateCode) ?? IdCardTemplates.Default;

        /// <summary>Front then back, on card-sized pages (one student).</summary>
        public static byte[] RenderSingle(IdCardSchoolModel school, IdCardBrand brand, IdCardModel card) =>
            CardDocument(school, brand, card).GeneratePdf();

        private static Document CardDocument(IdCardSchoolModel school, IdCardBrand brand, IdCardModel card)
        {
            var t = TemplateOf(brand);
            var ctx = Ctx(school, brand, PaletteOf(brand), card);
            return Document.Create(doc =>
            {
                doc.Page(p => { SetupCardPage(p); p.Content().Element(c => t.Front(c, ctx)); });
                doc.Page(p => { SetupCardPage(p); p.Content().Element(c => t.Back(c, ctx)); });
            });
        }

        /// <summary>The two faces as PNG images (front, back) for the settings preview. Rendered with the same code as the real PDF.</summary>
        public static (byte[] Front, byte[] Back) RenderPreview(IdCardSchoolModel school, IdCardBrand brand, IdCardModel card, int dpi = 200)
        {
            var images = CardDocument(school, brand, card).GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = dpi }).ToList();
            return (images[0], images[1]);
        }

        /// <summary>A4 sheets, 9 cards each: front page, back page, front page, back page ... Returns the PDF and the number of sheets.</summary>
        public static (byte[] Pdf, int Sheets) RenderSheets(IdCardSchoolModel school, IdCardBrand brand, IReadOnlyList<IdCardModel> cards)
        {
            var t = TemplateOf(brand);
            var pal = PaletteOf(brand);
            var sheets = cards.Chunk(CardsPerSheet).ToList();
            var pdf = Document.Create(doc =>
            {
                foreach (var sheet in sheets)
                {
                    doc.Page(p => { SetupSheetPage(p); p.Content().Element(c => SheetGrid(c, sheet, false, (e, m) => t.Front(e, Ctx(school, brand, pal, m)))); });
                    // Long-edge duplex flips the paper left-to-right, so the back page lists each row right-to-left to meet its front
                    doc.Page(p => { SetupSheetPage(p); p.Content().Element(c => SheetGrid(c, sheet, true, (e, m) => t.Back(e, Ctx(school, brand, pal, m)))); });
                }
            }).GeneratePdf();
            return (pdf, sheets.Count);
        }

        /// <summary>Index within a sheet of the card drawn at (row, column) on a front page or, mirrored, on a back page.</summary>
        public static int SheetIndex(int row, int column, bool mirror) => row * Columns + (mirror ? Columns - 1 - column : column);

        private static void SetupCardPage(PageDescriptor p)
        {
            p.Size(new PageSize(CardW, CardH, Unit.Millimetre));
            p.Margin(0);
            p.DefaultTextStyle(t => t.FontFamily(Sans).FontSize(7));
        }

        private static void SetupSheetPage(PageDescriptor p)
        {
            p.Size(new PageSize(A4W, A4H, Unit.Millimetre));
            p.Margin(0);
            p.DefaultTextStyle(t => t.FontFamily(Sans).FontSize(7));
        }

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
                                if (c > 0) row.ConstantItem(GapX, Unit.Millimetre);
                                var item = row.ConstantItem(CardW, Unit.Millimetre).Height(CardH, Unit.Millimetre);
                                var index = SheetIndex(r, c, mirror);
                                if (index < sheet.Count)
                                    draw(item.Border(0.1f).BorderColor("#D1D5DB"), sheet[index]);
                            }
                        });
                    }
                });

                // crop marks: short ticks in the margins, in line with every card edge, clear of the cards
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
            layers.At(x, y, w, h).Background("#000000");
    }
}
