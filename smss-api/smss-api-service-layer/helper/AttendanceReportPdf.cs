using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper.idcard;

namespace smss_api_service_layer.helper
{
    /// <summary>The monthly attendance report as a print-ready A4 landscape PDF: a one-line summary and a plain grid table (no design flourishes).</summary>
    public static class AttendanceReportPdf
    {
        private const string Sans = Kit.Sans;
        private const string Grey = "#E5E7EB", Border = "#9CA3AF", Red = "#B91C1C", Ink = "#111827", Muted = "#4B5563";

        public static byte[] Render(MonthlyAttendanceReportDto r, string schoolName)
        {
            // QuestPDF's licence and the bundled fonts (Hind) are set up once by the ID-card renderer's static constructor; touching it first makes sure they are ready.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(IdCardPdf).TypeHandle);
            return Document.Create(doc => doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(10, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontFamily(Sans).FontSize(7).FontColor(Ink));

                page.Header().Column(col =>
                {
                    col.Item().Text($"{schoolName} - Monthly Attendance Report").FontSize(12).Bold();
                    col.Item().Text($"Class {r.ClassName} - {r.SectionName}   |   {r.MonthName}   |   Academic year {r.AcademicYearName}").FontSize(9);
                    var s = r.Summary;
                    col.Item().PaddingTop(2).PaddingBottom(4).Text(
                        $"Students {s.StudentCount}   |   Average attendance {(s.AveragePercentage?.ToString("0.0") ?? "-")}%   |   Below {s.ThresholdPercentage:0}%: {s.StudentsBelowThreshold}   |   Working days so far {s.WorkingDays}   |   Days not marked {s.DaysNotMarked}")
                        .FontSize(8).FontColor(Muted);
                });

                page.Content().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(8, Unit.Millimetre);        // roll
                        c.ConstantColumn(40, Unit.Millimetre);       // name
                        foreach (var _ in r.Days) c.RelativeColumn(1);
                        c.ConstantColumn(10, Unit.Millimetre);       // present
                        c.ConstantColumn(10, Unit.Millimetre);       // absent
                        c.ConstantColumn(11, Unit.Millimetre);       // %
                    });

                    IContainer Head(IContainer c, bool grey = false) => c.Border(0.4f).BorderColor(Border).Background(grey ? Grey : "#F3F4F6").Padding(1.5f).AlignCenter().AlignMiddle();
                    IContainer Cell(IContainer c, bool grey = false) => c.Border(0.4f).BorderColor(Border).Background(grey ? Grey : "#FFFFFF").PaddingVertical(1.2f).PaddingHorizontal(1f).AlignCenter().AlignMiddle();

                    t.Header(h =>
                    {
                        h.Cell().Element(c => Head(c)).Text("Roll").Bold();
                        h.Cell().Element(c => Head(c)).Text("Student").Bold();
                        foreach (var d in r.Days)
                            h.Cell().Element(c => Head(c, !d.IsWorkingDay)).Column(x =>
                            {
                                x.Item().AlignCenter().Text(d.Date.Day.ToString()).Bold().FontSize(6.5f);
                                x.Item().AlignCenter().Text("MTWTFSS"[d.DayOfWeek - 1].ToString()).FontSize(5.5f).FontColor(Muted);
                            });
                        h.Cell().Element(c => Head(c)).Text("Present").Bold();
                        h.Cell().Element(c => Head(c)).Text("Absent").Bold();
                        h.Cell().Element(c => Head(c)).Text("%").Bold();
                    });

                    foreach (var s in r.Students)
                    {
                        t.Cell().Element(c => Cell(c)).Text(s.RollNumber ?? "");
                        t.Cell().Element(c => Cell(c).AlignLeft()).Text(s.IsActive ? s.FullName : s.FullName + " (left)").FontSize(7);
                        for (var i = 0; i < r.Days.Count; i++)
                        {
                            var mark = s.Marks[i];
                            var grey = !r.Days[i].IsWorkingDay || mark == AttendanceReportBuilder.NotEnrolled;
                            var text = mark switch { AttendanceReportBuilder.Present => "P", AttendanceReportBuilder.Absent => "A", AttendanceReportBuilder.NotMarked => r.Days[i].IsFuture ? "" : "-", _ => "" };
                            var cell = t.Cell().Element(c => Cell(c, grey)).Text(text).FontSize(6.5f);
                            if (mark == AttendanceReportBuilder.Absent) cell.Bold().FontColor(Red);
                        }
                        t.Cell().Element(c => Cell(c)).Text(s.PresentDays.ToString());
                        t.Cell().Element(c => Cell(c)).Text(s.AbsentDays.ToString());
                        var pct = t.Cell().Element(c => Cell(c)).Text(s.Percentage?.ToString("0.0") ?? "-");
                        if (s.BelowThreshold) pct.Bold().FontColor(Red);
                    }
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text("P present   A absent   - not marked   grey = Sunday, holiday or not yet enrolled   % = present / (present + absent) over marked working days; red = below " + r.Summary.ThresholdPercentage.ToString("0") + "%")
                        .FontSize(6).FontColor(Muted);
                    row.AutoItem().Text(x => { x.Span("Page ").FontSize(6.5f); x.CurrentPageNumber().FontSize(6.5f); x.Span(" of ").FontSize(6.5f); x.TotalPages().FontSize(6.5f); });
                });
            })).GeneratePdf();
        }
    }
}
