using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static smss_api_service_layer.helper.reportcard.RcKit;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>
    /// The optional cover page and rules page (switched on in the school's settings; the setting is stored with each published card). They are drawn on the SAME page orientation and style as the
    /// chosen design, so a booklet prints consistently, and they are part of both a single card's PDF and a bulk PDF (cover, the card pages, then the rules page, per student).
    /// </summary>
    public static class ReportCardExtraPages
    {
        private static PageSize Size(IReportCardTemplate t) => string.Equals(t.Orientation, "landscape", StringComparison.OrdinalIgnoreCase) ? PageSizes.A4.Landscape() : PageSizes.A4;

        public static void Cover(IDocumentContainer doc, IReportCardTemplate template, RcContext x)
        {
            var m = x.Model; var mono = template is MinimalBwTemplate; var land = string.Equals(template.Orientation, "landscape", StringComparison.OrdinalIgnoreCase);
            var pal = Pal(m);
            var accent = mono ? "#000000" : pal.PrimaryText; var accent2 = mono ? "#000000" : pal.SecondaryText; var muted = mono ? "#333333" : Muted; var rule = mono ? "#000000" : Rule;
            var tint = mono ? "#FFFFFF" : Tint(m.Primary, 0.94);
            var s = m.Student;
            doc.Page(page =>
            {
                page.Size(Size(template)); page.Margin(land ? 14 : 18, Unit.Millimetre);
                page.DefaultTextStyle(d => d.FontFamily(Sans).FontSize(10).FontColor(mono ? "#000000" : Ink));
                DraftMark(page, x.Draft);
                page.Content().Border(mono ? 1.2f : 2f).BorderColor(accent).Padding(land ? 14 : 22).Column(col =>
                {
                    col.Spacing(land ? 6 : 10);
                    col.Item().AlignCenter().Column(h =>
                    {
                        if (x.Logo != null) h.Item().AlignCenter().Height(land ? 52 : 80).Image(x.Logo).FitArea();
                        h.Item().AlignCenter().PaddingTop(4).Text(m.Header.SchoolName).FontFamily(new[] { Serif, Sans }).FontSize(FitSize(m.Header.SchoolName, land ? 24f : 28f, land ? 40 : 30, 14f)).Bold().FontColor(accent).AlignCenter();
                        if (!string.IsNullOrWhiteSpace(m.Header.Line)) h.Item().AlignCenter().Text(m.Header.Line).FontFamily(new[] { Serif, Sans }).FontSize(FitSize(m.Header.Line, 11f, 70, 8f)).AlignCenter();
                        if (!string.IsNullOrWhiteSpace(m.Header.Recognition)) h.Item().AlignCenter().Text(m.Header.Recognition).FontSize(FitSize(m.Header.Recognition, 9.5f, 90, 7f)).FontColor(muted).AlignCenter();
                        if (!string.IsNullOrWhiteSpace(m.Header.Udise)) h.Item().AlignCenter().Text(m.Header.Udise).FontSize(9).FontColor(muted).AlignCenter();
                    });
                    col.Item().PaddingVertical(land ? 2 : 10).AlignCenter().Column(t =>
                    {
                        t.Item().AlignCenter().LineHorizontal(mono ? 1f : 1.5f).LineColor(accent2);
                        t.Item().AlignCenter().PaddingTop(6).Text("Progress Report").FontFamily(new[] { Serif, Sans }).FontSize(land ? 28f : 34f).Bold().FontColor(accent).AlignCenter();
                        t.Item().AlignCenter().Text($"Session {m.SessionName}").FontFamily(new[] { Serif, Sans }).FontSize(14).FontColor(accent2).AlignCenter();
                        t.Item().AlignCenter().PaddingTop(6).LineHorizontal(mono ? 1f : 1.5f).LineColor(accent2);
                    });
                    col.Item().Background(tint).Border(0.6f).BorderColor(rule).Padding(12).Column(p =>
                    {
                        p.Item().Text("Student").FontFamily(new[] { Serif, Sans }).FontSize(11).Bold().FontColor(accent);
                        p.Item().PaddingTop(4).Table(tb =>
                        {
                            tb.ColumnsDefinition(cd => { cd.ConstantColumn(88); cd.RelativeColumn(); cd.ConstantColumn(88); cd.RelativeColumn(); });
                            void Row(string k1, string? v1, string k2, string? v2)
                            {
                                tb.Cell().PaddingVertical(2.5f).Text(k1).FontSize(8.5f).FontColor(muted);
                                tb.Cell().PaddingVertical(2.5f).Text(string.IsNullOrWhiteSpace(v1) ? "-" : v1).FontSize(FitSize(v1, 10.5f, 26, 8f)).Bold();
                                tb.Cell().PaddingVertical(2.5f).Text(k2).FontSize(8.5f).FontColor(muted);
                                tb.Cell().PaddingVertical(2.5f).Text(string.IsNullOrWhiteSpace(v2) ? "-" : v2).FontSize(FitSize(v2, 10.5f, 26, 8f));
                            }
                            Row("Name", s.Name, "Class", s.ClassName);
                            Row("Roll number", s.Roll, "Section", s.SectionName);
                            Row("Father", s.Father, "Mother", s.Mother);
                            Row("Contact", s.Contact, "Admission No.", s.AdmissionNo);
                        });
                        p.Item().PaddingTop(2).Row(r => { r.ConstantItem(88).PaddingVertical(2.5f).Text("Address").FontSize(8.5f).FontColor(muted); r.RelativeItem().PaddingVertical(2.5f).Text(string.IsNullOrWhiteSpace(s.Address) ? "-" : s.Address).FontSize(FitSize(s.Address, 10f, 90, 7.5f)); });
                    });
                    col.Item().ExtendVertical().AlignBottom().AlignCenter().Column(f =>
                    {
                        if (!string.IsNullOrWhiteSpace(m.Header.AddressLine)) f.Item().AlignCenter().Text(m.Header.AddressLine + (string.IsNullOrWhiteSpace(m.Header.Phone) ? "" : "  ·  " + m.Header.Phone)).FontSize(8.5f).FontColor(muted).AlignCenter();
                        if (!string.IsNullOrWhiteSpace(m.Footer)) f.Item().AlignCenter().PaddingTop(2).Text(m.Footer).FontSize(FitSize(m.Footer, 8f, 140, 6f)).FontColor(muted).AlignCenter();
                    });
                });
            });
        }

        public static void Rules(IDocumentContainer doc, IReportCardTemplate template, RcContext x)
        {
            var m = x.Model; var mono = template is MinimalBwTemplate; var land = string.Equals(template.Orientation, "landscape", StringComparison.OrdinalIgnoreCase);
            var pal = Pal(m);
            var accent = mono ? "#000000" : pal.PrimaryText; var muted = mono ? "#333333" : Muted;
            var lines = (m.RulesText ?? "").Replace("\r", "").Split('\n').ToList();
            doc.Page(page =>
            {
                page.Size(Size(template)); page.Margin(land ? 14 : 18, Unit.Millimetre);
                page.DefaultTextStyle(d => d.FontFamily(Sans).FontSize(land ? 9.5f : 11f).FontColor(mono ? "#000000" : Ink));
                DraftMark(page, x.Draft);
                page.Header().PaddingBottom(8).BorderBottom(mono ? 1f : 1.5f).BorderColor(accent).PaddingBottom(4).Row(r =>
                {
                    if (x.Logo != null) r.ConstantItem(34).Height(34).Padding(1).Image(x.Logo).FitArea();
                    r.RelativeItem().PaddingLeft(x.Logo != null ? 6 : 0).Column(h =>
                    {
                        h.Item().Text(m.Header.SchoolName).FontFamily(new[] { Serif, Sans }).FontSize(FitSize(m.Header.SchoolName, 14f, 44, 9f)).Bold().FontColor(accent);
                        h.Item().Text("School rules").FontFamily(new[] { Serif, Sans }).FontSize(10).FontColor(muted);
                    });
                });
                page.Content().PaddingTop(4).ScaleToFit().Column(col =>
                {
                    col.Spacing(5);
                    // a blank line stays a gap; every other line is one rule that wraps inside the page
                    foreach (var l in lines)
                    {
                        if (string.IsNullOrWhiteSpace(l)) col.Item().Height(5);
                        else col.Item().Text(l.TrimEnd()).LineHeight(1.35f);
                    }
                });
                page.Footer().Column(f =>
                {
                    f.Item().BorderTop(0.5f).BorderColor(mono ? "#000000" : Rule).PaddingTop(4).AlignCenter().Text(string.Join("  ·  ", new[] { m.Header.AddressLine, m.Header.Phone }.Where(v => !string.IsNullOrWhiteSpace(v)))).FontSize(8.5f).FontColor(muted).AlignCenter();
                    if (!string.IsNullOrWhiteSpace(m.Footer)) f.Item().AlignCenter().PaddingTop(2).Text(m.Footer).FontSize(FitSize(m.Footer, 8f, 140, 6f)).FontColor(muted).AlignCenter();
                });
            });
        }
    }
}
