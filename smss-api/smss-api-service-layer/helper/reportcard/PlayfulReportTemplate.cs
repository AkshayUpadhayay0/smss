using smss_api_service_layer.helper.idcard;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static smss_api_service_layer.helper.reportcard.RcKit;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>
    /// Playful Report (GRADES, descriptive, A4 portrait): a rounded, friendly look drawn only with simple vector shapes. One page per term: the child's name, level and section, the school
    /// year and the class teacher; a table of Learning Area, Class Grade (the term's grade), Things to improve and Comments; the grading-system key as a row of chips; and a Feedback and
    /// Remarks box with the term remark. The last page also carries attendance and promotion. A long list of learning areas flows onto a further page instead of overflowing.
    /// </summary>
    public sealed class PlayfulReportTemplate : IReportCardTemplate
    {
        public string Code => "playful-report";
        public string Name => "Playful Report";
        public IReadOnlyList<string> SupportedModes { get; } = new[] { "GRADES" };
        public string Orientation => "portrait";

        public void Render(IDocumentContainer doc, RcContext x)
        {
            var m = x.Model; var pal = Pal(m);
            var terms = m.Terms.Count > 0 ? m.Terms : new List<RcTerm> { new() { TermId = 0, Name = "Term" } };
            for (var ti = 0; ti < terms.Count; ti++)
            {
                var index = ti; var last = ti == terms.Count - 1;
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(10, Unit.Millimetre);
                    page.DefaultTextStyle(s => s.FontFamily(Sans).FontSize(9).FontColor(Ink));
                    DraftMark(page, x.Draft);
                    page.Header().Column(col =>
                    {
                        col.Item().Element(c => Banner(c, x, pal, terms[index]));
                        col.Item().PaddingTop(6).Element(c => ChildCard(c, x, pal));
                        col.Item().PaddingBottom(6);
                    });
                    page.Content().Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().Element(c => AreasTable(c, m, pal, index));
                        if (m.ShowGradeLegend && m.Legend.Count > 0) col.Item().Element(c => LegendBar(c, m, pal));
                        col.Item().Element(c => FeedbackBox(c, m, pal, index));
                        if (last) col.Item().Element(c => LastPage(c, m, pal));
                    });
                    page.Footer().Column(col =>
                    {
                        col.Item().PaddingTop(4).Element(c => SignatureRow(c, x, "Class Teacher", "Parent", "Principal"));
                        col.Item().PaddingTop(4).AlignRight().Text(x.Draft ? "DRAFT preview - not an official copy" : $"Version {m.Version}" + (m.PublishedAt != null ? $" · {m.PublishedAt}" : "")).FontSize(7f).FontColor(Muted);
                        FooterText(col, m);
                    });
                });
            }
        }

        private static void Banner(IContainer c, RcContext x, IdCardPalette pal, RcTerm term)
        {
            var m = x.Model;
            c.Background(pal.Primary).CornerRadius(14).Padding(8).Row(r =>
            {
                if (x.Logo != null) r.ConstantItem(46).Height(46).Background(Colors.White).CornerRadius(23).Padding(4).Image(x.Logo).FitArea();
                r.RelativeItem().PaddingLeft(x.Logo != null ? 8 : 4).Column(col =>
                {
                    col.Item().Text(m.Header.SchoolName).FontFamily(new[] { Serif, Sans }).FontSize(FitSize(m.Header.SchoolName, 17f, 34, 9f)).Bold().FontColor(pal.OnPrimary);
                    if (!string.IsNullOrWhiteSpace(m.Header.Line)) col.Item().Text(m.Header.Line).FontSize(FitSize(m.Header.Line, 8.5f, 80, 7f)).FontColor(pal.OnPrimary);
                    var rec = string.Join("  ·  ", new[] { m.Header.Recognition, m.Header.Udise }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (rec.Length > 0) col.Item().Text(rec).FontSize(FitSize(rec, 7f, 100, 7f)).FontColor(pal.OnPrimary);
                    if (!string.IsNullOrWhiteSpace(m.Header.AddressLine)) col.Item().Text(m.Header.AddressLine + (string.IsNullOrWhiteSpace(m.Header.Phone) ? "" : "  ·  " + m.Header.Phone)).FontSize(FitSize(m.Header.AddressLine, 7f, 100, 7f)).FontColor(pal.OnPrimary);
                });
                r.AutoItem().AlignMiddle().Background(pal.Secondary).CornerRadius(12).PaddingVertical(4).PaddingHorizontal(10).Text(term.Name).FontSize(10).Bold().FontColor(pal.OnSecondary);
            });
        }

        private static void ChildCard(IContainer c, RcContext x, IdCardPalette pal)
        {
            var m = x.Model; var s = m.Student;
            c.Background(Tint(m.Primary, 0.9)).CornerRadius(12).Padding(8).Row(outer =>
            {
                outer.RelativeItem().Column(col =>
                {
                col.Item().Text(string.IsNullOrWhiteSpace(s.Name) ? "-" : s.Name).FontFamily(new[] { Serif, Sans }).FontSize(FitSize(s.Name, 18f, 26, 10f)).Bold().FontColor(pal.PrimaryText);
                col.Item().PaddingTop(3).Row(r =>
                {
                    void Fact(string key, string? value) =>
                        r.RelativeItem().Column(f => { f.Item().Text(key).FontSize(7).FontColor(Muted); f.Item().Text(string.IsNullOrWhiteSpace(value) ? "-" : value).FontSize(FitSize(value, 10f, 22, 7f)).Bold(); });
                    Fact("Level", $"{s.ClassName} {s.SectionName}".Trim());
                    Fact("School year", m.SessionName);
                    Fact("Class teacher", s.ClassTeacher);
                    Fact("Roll no.", s.Roll);
                });
                });
                if (m.ShowPhoto) outer.ConstantItem(60).PaddingLeft(8).Element(e => PhotoSlot(e, x, 52, 64, round: true));
            });
        }

        /// <summary>The grade of a learning area for the term: the subject-term grade, else the last exam's grade.</summary>
        private static string TermGrade(RcSubject s, int ti)
        {
            if (ti >= s.Terms.Count) return "-";
            var st = s.Terms[ti];
            if (!string.IsNullOrWhiteSpace(st.Grade)) return st.Grade!;
            return st.Cells.LastOrDefault()?.Grade ?? "-";
        }

        private static void AreasTable(IContainer c, ReportCardModel m, IdCardPalette pal, int ti)
        {
            var subjects = m.Subjects.ToList();
            var dense = subjects.Count > 10;
            var size = dense ? 8f : 9.5f;
            c.Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.RelativeColumn(2.2f); cd.ConstantColumn(54); cd.RelativeColumn(2.4f); cd.RelativeColumn(2.6f); });
                t.Header(h =>
                {
                    void Head(string text, bool center = false) { var e = h.Cell().Background(pal.Secondary).PaddingVertical(5).PaddingHorizontal(6).AlignMiddle(); (center ? e.AlignCenter() : e).Text(text).FontSize(size).Bold().FontColor(pal.OnSecondary); }
                    Head("Learning area"); Head("Class grade", true); Head("Things to improve"); Head("Comments");
                });
                for (var i = 0; i < subjects.Count; i++)
                {
                    var s = subjects[i]; var st = ti < s.Terms.Count ? s.Terms[ti] : null;
                    var fill = i % 2 == 0 ? Tint(m.Primary, 0.95) : "#FFFFFF";
                    IContainer Body() => t.Cell().BorderBottom(0.5f).BorderColor(Tint(m.Primary, 0.7)).Fill(fill).PaddingVertical(dense ? 3 : 5).PaddingHorizontal(6).AlignMiddle();
                    Body().Text(s.Name).FontFamily(new[] { Serif, Sans }).FontSize(FitSize(s.Name, size + 0.5f, 22, 7f)).Bold().FontColor(pal.PrimaryText);
                    Body().AlignCenter().Element(e => e.Background(pal.Primary).CornerRadius(9).PaddingVertical(2).PaddingHorizontal(9).Text(TermGrade(s, ti)).FontSize(size + 1).Bold().FontColor(pal.OnPrimary));
                    Body().Text(string.IsNullOrWhiteSpace(st?.Improvement) ? "" : st!.Improvement).FontSize(RcKit.Pt(size - 0.5f));
                    Body().Text(string.IsNullOrWhiteSpace(st?.Remark) ? "" : st!.Remark).FontSize(RcKit.Pt(size - 0.5f));
                }
            });
        }

        private static void LegendBar(IContainer c, ReportCardModel m, IdCardPalette pal)
        {
            c.Background(Tint(m.Secondary, 0.82)).CornerRadius(10).PaddingVertical(5).PaddingHorizontal(8).Row(r =>
            {
                r.AutoItem().PaddingRight(8).AlignMiddle().Text("Grading system").FontSize(8).Bold().FontColor(pal.SecondaryText);
                foreach (var b in m.Legend)
                    r.RelativeItem().AlignMiddle().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8));
                        t.Span(b.Code).Bold().FontColor(pal.PrimaryText);
                        if (!string.IsNullOrWhiteSpace(b.Label)) t.Span(" = " + b.Label);
                    });
            });
        }

        private static void FeedbackBox(IContainer c, ReportCardModel m, IdCardPalette pal, int ti)
        {
            var ts = ti < m.TermSummaries.Count ? m.TermSummaries[ti] : null;
            c.Border(1.2f).BorderColor(pal.Primary).CornerRadius(12).Padding(8).Column(col =>
            {
                col.Item().Text("Feedback and remarks").FontFamily(new[] { Serif, Sans }).FontSize(10).Bold().FontColor(pal.PrimaryText);
                col.Item().PaddingTop(3).MinHeight(36).Text(string.IsNullOrWhiteSpace(ts?.Remark) ? "" : ts!.Remark).FontSize(9.5f).LineHeight(1.3f);
                var acts = ts?.Activities.Where(a => a.Category == "DISCIPLINE").ToList() ?? new();
                if (acts.Count > 0 || !string.IsNullOrWhiteSpace(ts?.Conduct))
                    col.Item().PaddingTop(4).Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8.5f));
                        foreach (var a in acts) { t.Span(a.Area + ": ").FontColor(Muted); t.Span(a.Grade).Bold(); t.Span("    "); }
                        if (!string.IsNullOrWhiteSpace(ts?.Conduct)) { t.Span("Conduct: ").FontColor(Muted); t.Span(ts!.Conduct!).Bold(); }
                    });
            });
        }

        /// <summary>Attendance across the terms and the promotion, on the last page only.</summary>
        private static void LastPage(IContainer c, ReportCardModel m, IdCardPalette pal)
        {
            c.Row(r =>
            {
                r.Spacing(8);
                if (m.ShowAttendance && m.TermSummaries.Any(t => t.WorkingDays != null))
                    r.RelativeItem().Background(Tint(m.Primary, 0.92)).CornerRadius(12).Padding(8).Column(col =>
                    {
                        col.Item().Text("Attendance").FontSize(9.5f).Bold().FontColor(pal.PrimaryText);
                        foreach (var ts in m.TermSummaries.Where(t => t.WorkingDays != null))
                            col.Item().PaddingTop(2).Text(t =>
                            {
                                t.DefaultTextStyle(s => s.FontSize(8.5f));
                                t.Span((m.Terms.FirstOrDefault(x => x.TermId == ts.TermId)?.Name ?? "Term") + ": ").FontColor(Muted);
                                t.Span($"{ts.PresentDays?.ToString() ?? "-"} of {ts.WorkingDays} days").Bold();
                            });
                    });
                var p = m.Promotion;
                if (!string.IsNullOrWhiteSpace(p.Decision) || !string.IsNullOrWhiteSpace(p.NewSessionDate))
                    r.RelativeItem().Background(Tint(m.Secondary, 0.85)).CornerRadius(12).Padding(8).Column(col =>
                    {
                        col.Item().Text("Next year").FontSize(9.5f).Bold().FontColor(pal.SecondaryText);
                        col.Item().PaddingTop(2).Text(p.Decision switch { "PROMOTED" => $"Promoted to {p.PromotedTo}", "NOT_PROMOTED" => "Not promoted", "DETAINED" => "Detained", var d => d ?? "" }).FontSize(9).Bold();
                        if (!string.IsNullOrWhiteSpace(p.NewSessionDate)) col.Item().Text(t => { t.DefaultTextStyle(s => s.FontSize(8.5f)); t.Span("New session begins: ").FontColor(Muted); t.Span(p.NewSessionDate).Bold(); });
                    });
            });
        }
    }
}
