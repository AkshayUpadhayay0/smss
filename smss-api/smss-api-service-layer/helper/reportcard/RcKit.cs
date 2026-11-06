using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>What a template draws: the model, the logo bytes (read from storage when the card is drawn; the model only holds a key), and whether this is a draft preview.</summary>
    public sealed class RcContext
    {
        public ReportCardModel Model { get; init; } = new();
        public byte[]? Logo { get; init; }
        public byte[]? Signature { get; init; }
        public bool Draft { get; init; }
    }

    /// <summary>A report card design. Templates are a code-constant list (ReportCardDesigns.All), exactly like the ID card designs.</summary>
    public interface IReportCardTemplate
    {
        string Code { get; }
        string Name { get; }
        IReadOnlyList<string> SupportedModes { get; }
        string Orientation { get; }
        void Render(IDocumentContainer doc, RcContext ctx);
    }

    public static class ReportCardDesigns
    {
        public static readonly IReadOnlyList<IReportCardTemplate> All = new IReportCardTemplate[] { new TermSpreadTemplate() };
        public static IReportCardTemplate? Find(string? code) => All.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.Ordinal));
    }

    /// <summary>The pieces every report card design is made of, so a new design only decides where they go.</summary>
    public static class RcKit
    {
        public const string Sans = "Hind", Serif = "PT Serif";
        public const string Ink = "#111827", Muted = "#4B5563", Rule = "#9CA3AF";

        /// <summary>A colour mixed with white (0 = the colour, 1 = white): table header fills and zebra rows from the brand colour.</summary>
        public static string Tint(string hex, double amount)
        {
            var h = hex.TrimStart('#'); if (h.Length != 6) h = "1F3A5F";
            int r = Convert.ToInt32(h[..2], 16), g = Convert.ToInt32(h[2..4], 16), b = Convert.ToInt32(h[4..], 16);
            int Mix(int c) => (int)Math.Round(c + (255 - c) * amount);
            return $"#{Mix(r):X2}{Mix(g):X2}{Mix(b):X2}";
        }

        /// <summary>A marks cell: the number, "Ab" for absent, "Ex" for exempt, "-" for nothing entered.</summary>
        public static string CellText(RcCell c) => c.State switch
        {
            CellStates.Entered => c.Marks != null ? c.Marks.Value.ToString("0.##", CultureInfo.InvariantCulture) : (c.Skills.Count > 0 ? string.Join("/", c.Skills.Select(s => s.Grade ?? "-")) : c.Grade ?? "-"),
            CellStates.Absent => "Ab",
            CellStates.Exempt => "Ex",
            _ => "-"
        };

        public static string Num(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        public static string Pct(decimal? v) => v == null ? "-" : v.Value.ToString("0.00", CultureInfo.InvariantCulture) + "%";
        public static string ResultText(string status, string mode) => status switch { "PASS" => mode == "GRADES" ? "Complete" : "Passed", "FAIL" => mode == "GRADES" ? "Needs support" : "Failed", _ => "Incomplete" };

        /// <summary>Fit-or-shrink for a single line of text: the font size comes down in steps as the text gets longer, so a long school name or student name never spills over its box.</summary>
        public static float FitSize(string? text, float baseSize, int comfortableChars, float minSize = 8f)
        {
            var n = (text ?? "").Length;
            if (n <= comfortableChars) return baseSize;
            return Math.Max(minSize, baseSize * comfortableChars / n);
        }

        /// <summary>The school's name and address, with the logo when the school record has one. A long name shrinks instead of spilling over.</summary>
        public static void Header(IContainer c, RcContext x, string subtitle)
        {
            var m = x.Model;
            c.Row(r =>
            {
                if (x.Logo != null) r.ConstantItem(46).Height(46).Padding(2).Image(x.Logo).FitArea();
                r.RelativeItem().PaddingLeft(x.Logo != null ? 6 : 0).Column(col =>
                {
                    col.Item().Text(m.Header.SchoolName).FontFamily(new[] { Serif, Sans }).FontSize(FitSize(m.Header.SchoolName, 16f, 34)).Bold().FontColor(m.Primary);
                    if (!string.IsNullOrWhiteSpace(m.Header.AddressLine)) col.Item().Text(m.Header.AddressLine + (string.IsNullOrWhiteSpace(m.Header.Phone) ? "" : "  ·  " + m.Header.Phone)).FontFamily(Sans).FontSize(7.5f).FontColor(Muted);
                    col.Item().PaddingTop(1).Text(subtitle).FontFamily(new[] { Serif, Sans }).FontSize(9).Bold().FontColor(m.Secondary);
                });
            });
        }

        public static void StudentBlock(IContainer c, ReportCardModel m)
        {
            var s = m.Student;
            c.Border(0.5f).BorderColor(Rule).Padding(4).Row(r =>
            {
                r.RelativeItem(1.4f).Column(col => { KeyValue(col, "Name", s.Name, true); KeyValue(col, "Father", s.Father); KeyValue(col, "Mother", s.Mother); });
                r.RelativeItem(1f).Column(col => { KeyValue(col, "Class / Section", $"{s.ClassName} {s.SectionName}".Trim()); KeyValue(col, "Roll No.", s.Roll); KeyValue(col, "Admission No.", s.AdmissionNo); });
                r.RelativeItem(1f).Column(col => { KeyValue(col, "Date of birth", s.Dob); KeyValue(col, "Session", m.SessionName); });
            });
        }

        public static void KeyValue(ColumnDescriptor col, string key, string? value, bool bold = false) =>
            col.Item().Row(r => { r.ConstantItem(62).Text(key).FontFamily(Sans).FontSize(7).FontColor(Muted); var tx = r.RelativeItem().Text(string.IsNullOrWhiteSpace(value) ? "-" : value).FontFamily(new[] { Sans }).FontSize(FitSize(value, 8f, 34, 6f)).FontColor(Ink); if (bold) tx = tx.Bold(); });

        /// <summary>One bordered, shaded header cell and one body cell, the building blocks of every ruled table.</summary>
        public static IContainer HeadCell(IContainer c, string fill) => c.Border(0.4f).BorderColor(Rule).Background(fill).PaddingVertical(2).PaddingHorizontal(2).AlignMiddle();
        public static IContainer BodyCell(IContainer c, bool zebra, string tint) => c.Border(0.3f).BorderColor(Rule).Background(zebra ? tint : Colors.White).PaddingVertical(1.5f).PaddingHorizontal(2).AlignMiddle();

        public static void GradeLegend(IContainer c, ReportCardModel m)
        {
            if (m.Legend.Count == 0) return;
            c.Text(t =>
            {
                t.DefaultTextStyle(x => x.FontFamily(Sans).FontSize(6).FontColor(Muted));
                t.Span("Grades: ");
                var ordered = m.Legend.OrderByDescending(b => b.Min ?? 0).ToList();
                for (var i = 0; i < ordered.Count; i++)
                {
                    var b = ordered[i]; var upper = i == 0 ? 100m : ordered[i - 1].Min - 0.01m;
                    t.Span(b.Min != null ? $"{b.Code} {Num(b.Min.Value)}-{Num(upper ?? 100m)}" : b.Code + (string.IsNullOrWhiteSpace(b.Label) ? "" : " " + b.Label));
                    if (i < ordered.Count - 1) t.Span("  ·  ");
                }
            });
        }

        public static void SignatureRow(IContainer c, RcContext x, params string[] who)
        {
            c.Row(r =>
            {
                foreach (var w in who)
                    r.RelativeItem().PaddingHorizontal(14).Column(col =>
                    {
                        col.Item().Height(26).AlignBottom().Element(e => { if (w == "Principal" && x.Signature != null) e.Image(x.Signature).FitArea(); });
                        col.Item().LineHorizontal(0.6f).LineColor(Muted);
                        col.Item().AlignCenter().Text(w).FontFamily(Sans).FontSize(7.5f).FontColor(Muted);
                    });
            });
        }

        /// <summary>A light grey DRAFT mark across the page, so a preview can never be mistaken for a published card.</summary>
        public static void DraftMark(PageDescriptor page, bool draft)
        {
            if (!draft) return;
            page.Foreground().AlignCenter().AlignMiddle().Rotate(-28).Text("DRAFT").FontFamily(Sans).FontSize(120).Bold().FontColor("#D6D6D6");
        }
    }
}
