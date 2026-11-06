using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static smss_api_service_layer.helper.reportcard.RcKit;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>
    /// Term Spread: A4 landscape, two terms per sheet side by side. Each term panel has the scholastic table (subjects by the term's exams, marks and the term total, grade, remark), the term
    /// totals, then the co-scholastic and discipline grades, attendance and the term remark. A single term gives a single panel; more than two terms continue on further sheets. The LAST panel
    /// adds the grand total, result, rank, promotion and "new session begins", and the sheet ends with the signature row (Class Teacher, Principal, Parent).
    /// An original design with a restrained default palette; the colours come from the model, so the settings stage can change them later without touching this file.
    /// </summary>
    public sealed class TermSpreadTemplate : IReportCardTemplate
    {
        public string Code => "term-spread";
        public string Name => "Term Spread";
        public IReadOnlyList<string> SupportedModes { get; } = new[] { "MARKS" };
        public string Orientation => "landscape";

        public void Render(IDocumentContainer doc, RcContext x)
        {
            var m = x.Model;
            var sheets = m.Terms.Chunk(2).ToList();
            if (sheets.Count == 0) sheets.Add(Array.Empty<RcTerm>());
            for (var i = 0; i < sheets.Count; i++)
            {
                var first = i == 0; var lastSheet = i == sheets.Count - 1; var terms = sheets[i];
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(9, Unit.Millimetre);
                    page.DefaultTextStyle(s => s.FontFamily(Sans).FontSize(7.5f).FontColor(Ink));
                    DraftMark(page, x.Draft);
                    page.Header().Column(col =>
                    {
                        if (first) col.Item().Element(c => Header(c, x, ScopeTitle(m)));
                        else col.Item().Text($"{m.Header.SchoolName} · {ScopeTitle(m)} · {m.Student.Name} ({m.Student.ClassName} {m.Student.SectionName}), continued").FontFamily(Serif).FontSize(9).Bold().FontColor(m.Primary);
                        col.Item().PaddingTop(3).Element(c => StudentBlock(c, m));
                        col.Item().PaddingBottom(3);
                    });
                    page.Content().Row(row =>
                    {
                        for (var k = 0; k < terms.Length; k++)
                        {
                            var term = terms[k]; var isLastTerm = lastSheet && k == terms.Length - 1;
                            if (k > 0) row.ConstantItem(8);
                            row.RelativeItem().Element(c => c.ScaleToFit().Element(p => Panel(p, x, term, isLastTerm)));
                        }
                    });
                    page.Footer().Column(col =>
                    {
                        if (lastSheet) col.Item().PaddingTop(6).Element(c => SignatureRow(c, x, "Class Teacher", "Principal", "Parent"));
                        col.Item().PaddingTop(3).Row(r =>
                        {
                            r.RelativeItem().Element(c => GradeLegend(c, m));
                            r.ConstantItem(150).AlignRight().Text(t =>
                            {
                                t.DefaultTextStyle(s => s.FontSize(6).FontColor(Muted));
                                t.Span(x.Draft ? "DRAFT preview - not an official copy" : $"Version {m.Version}" + (m.PublishedAt != null ? $" · {m.PublishedAt}" : ""));
                                t.Span($"   Page {i + 1}/{sheets.Count}");
                            });
                        });
                    });
                });
            }
        }

        private static string ScopeTitle(ReportCardModel m) => m.Scope == "TERM" && m.Terms.Count == 1 ? $"Progress Report · {m.Terms[0].Name} · {m.SessionName}" : $"Progress Report · {m.SessionName}";

        private static void Panel(IContainer c, RcContext x, RcTerm term, bool last)
        {
            var m = x.Model; var marks = m.Mode == "MARKS";
            var summary = m.TermSummaries.FirstOrDefault(s => s.TermId == term.TermId);
            var tint = Tint(m.Primary, 0.92); var headFill = Tint(m.Primary, 0.82);
            var exams = term.Exams;
            var size = exams.Count <= 3 ? 7.5f : exams.Count <= 5 ? 6.5f : 5.5f;
            c.Border(0.6f).BorderColor(m.Primary).Padding(5).Column(col =>
            {
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(term.Name).FontFamily(new[] { Serif, Sans }).FontSize(11).Bold().FontColor(m.Primary);
                    r.AutoItem().Text($"{term.Start} - {term.End}").FontFamily(Sans).FontSize(6.5f).FontColor(Muted);
                });
                col.Item().PaddingTop(3).Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(2.6f);
                        foreach (var _ in exams) cd.RelativeColumn(1f);
                        if (marks) cd.RelativeColumn(1.5f);
                        cd.RelativeColumn(0.8f);
                        cd.RelativeColumn(1.6f);
                    });
                    t.Header(h =>
                    {
                        h.Cell().Element(e => HeadCell(e, headFill)).Text("Subject").FontSize(size).Bold();
                        foreach (var e in exams)
                            h.Cell().Element(cc => HeadCell(cc, headFill)).AlignCenter().Column(cc =>
                            {
                                cc.Item().AlignCenter().Text(e.Name).FontSize(size).Bold();
                                if (marks && e.Max != null) cc.Item().AlignCenter().Text(e.Weight != null && e.Weight != e.Max ? $"{Num(e.Max.Value)} → {Num(e.Weight.Value)}" : $"({Num(e.Max.Value)})").FontSize(size - 1f).FontColor(Muted);
                            });
                        if (marks) h.Cell().Element(e => HeadCell(e, headFill)).AlignCenter().Text("Total").FontSize(size).Bold();
                        h.Cell().Element(e => HeadCell(e, headFill)).AlignCenter().Text("Grade").FontSize(size).Bold();
                        h.Cell().Element(e => HeadCell(e, headFill)).Text("Remark").FontSize(size).Bold();
                    });
                    var zebra = false;
                    foreach (var s in m.Subjects)
                    {
                        var st = s.Terms.FirstOrDefault(y => y.TermId == term.TermId);
                        if (st == null) continue;
                        zebra = !zebra;
                        t.Cell().Element(e => BodyCell(e, zebra, tint)).Text(s.Name + (marks && !s.Included ? " *" : "")).FontSize(size);
                        foreach (var e in exams)
                        {
                            var cell = st.Cells.FirstOrDefault(cc => cc.ExamId == e.ExamId);
                            t.Cell().Element(cc => BodyCell(cc, zebra, tint)).AlignCenter().Text(cell == null ? "" : CellText(cell)).FontSize(size).FontColor(cell?.State == CellStates.Absent ? "#B91C1C" : Ink);
                        }
                        if (marks) t.Cell().Element(e => BodyCell(e, zebra, tint)).AlignCenter().Text(s.GradeOnly ? "" : $"{Num(st.Obtained)}/{Num(st.OutOf)}").FontSize(size).Bold().FontColor(st.Failed ? "#B91C1C" : Ink);
                        t.Cell().Element(e => BodyCell(e, zebra, tint)).AlignCenter().Text(st.Grade ?? "").FontSize(size).Bold();
                        t.Cell().Element(e => BodyCell(e, zebra, tint)).Text(st.Remark ?? "").FontSize(size - 0.5f).FontColor(Muted);
                    }
                });
                if (m.Subjects.Any(s => marks && !s.Included)) col.Item().PaddingTop(1).Text("* not counted in the total").FontSize(5.5f).FontColor(Muted);

                if (summary != null && marks)
                    col.Item().PaddingTop(3).Background(tint).Padding(3).Row(r =>
                    {
                        r.RelativeItem().Text(t => { t.Span("Total  ").FontColor(Muted); t.Span($"{Num(summary.Obtained)} / {Num(summary.OutOf)}").Bold(); });
                        r.RelativeItem().Text(t => { t.Span("Percentage  ").FontColor(Muted); t.Span(Pct(summary.Percent)).Bold(); });
                        r.RelativeItem().Text(t => { t.Span("Grade  ").FontColor(Muted); t.Span(summary.Grade ?? "-").Bold(); });
                        r.RelativeItem().Text(t => { t.Span("Result  ").FontColor(Muted); t.Span(ResultText(summary.Result, m.Mode)).Bold(); });
                        if (m.ShowRank && summary.Rank != null) r.RelativeItem().Text(t => { t.Span("Rank  ").FontColor(Muted); t.Span($"{summary.Rank}").Bold(); });
                    });

                if (summary != null)
                {
                    var co = summary.Activities.Where(a => a.Category != "DISCIPLINE").ToList(); var di = summary.Activities.Where(a => a.Category == "DISCIPLINE").ToList();
                    if (summary.Activities.Count > 0)
                        col.Item().PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Column(cc => { cc.Item().Text("Co-scholastic").FontSize(7).Bold().FontColor(m.Primary); foreach (var a in co) cc.Item().Text($"{a.Area}: {a.Grade}{(string.IsNullOrWhiteSpace(a.GradeLabel) ? "" : " (" + a.GradeLabel + ")")}").FontSize(6.5f); if (co.Count == 0) cc.Item().Text("-").FontSize(6.5f).FontColor(Muted); });
                            r.ConstantItem(6);
                            r.RelativeItem().Column(cc => { cc.Item().Text("Discipline").FontSize(7).Bold().FontColor(m.Primary); foreach (var a in di) cc.Item().Text($"{a.Area}: {a.Grade}{(string.IsNullOrWhiteSpace(a.GradeLabel) ? "" : " (" + a.GradeLabel + ")")}").FontSize(6.5f); if (di.Count == 0) cc.Item().Text("-").FontSize(6.5f).FontColor(Muted); });
                        });
                    if (summary.WorkingDays != null && summary.PresentDays != null)
                        col.Item().PaddingTop(3).Text(t =>
                        {
                            t.Span("Attendance  ").FontColor(Muted); t.Span($"{summary.PresentDays} of {summary.WorkingDays} days").Bold();
                            if (summary.WorkingDays > 0) t.Span($"  ({(summary.PresentDays.Value * 100m / summary.WorkingDays.Value).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}%)").FontColor(Muted);
                        });
                    if (!string.IsNullOrWhiteSpace(summary.Conduct)) col.Item().PaddingTop(2).Text(t => { t.Span("Conduct  ").FontColor(Muted); t.Span(summary.Conduct); });
                    if (!string.IsNullOrWhiteSpace(summary.RankText)) col.Item().PaddingTop(2).Text(t => { t.Span("Rank  ").FontColor(Muted); t.Span(summary.RankText); });
                    if (!string.IsNullOrWhiteSpace(summary.Remark)) col.Item().PaddingTop(2).Text(t => { t.Span("Teacher's remark  ").FontColor(Muted); t.Span(summary.Remark); });
                }

                if (last) col.Item().PaddingTop(6).Element(e => YearSummary(e, x));
            });
        }

        /// <summary>The closing box of the last panel: the grand total, result, rank, promotion and the date the new session begins.</summary>
        private static void YearSummary(IContainer c, RcContext x)
        {
            var m = x.Model; var o = m.Overall; var marks = m.Mode == "MARKS"; var p = m.Promotion;
            c.Border(0.8f).BorderColor(m.Secondary).Padding(5).Column(col =>
            {
                col.Item().Text(m.Scope == "TERM" ? "Result" : "Final result").FontFamily(new[] { Serif, Sans }).FontSize(10).Bold().FontColor(m.Primary);
                if (marks)
                    col.Item().PaddingTop(2).Row(r =>
                    {
                        r.RelativeItem().Text(t => { t.Span(m.Scope == "TERM" ? "Total  " : "Grand total  ").FontColor(Muted); t.Span($"{Num(o.Obtained)} / {Num(o.OutOf)}").Bold(); });
                        r.RelativeItem().Text(t => { t.Span("Percentage  ").FontColor(Muted); t.Span(Pct(o.Percent)).Bold(); });
                        r.RelativeItem().Text(t => { t.Span("Grade  ").FontColor(Muted); t.Span(o.Grade ?? "-").Bold(); });
                    });
                col.Item().PaddingTop(2).Row(r =>
                {
                    r.RelativeItem().Text(t => { t.Span("Result  ").FontColor(Muted); t.Span(ResultText(o.Result, m.Mode)).Bold().FontColor(o.Result == "FAIL" ? "#B91C1C" : Ink); });
                    if (m.ShowRank && o.Rank != null) r.RelativeItem().Text(t => { t.Span("Rank  ").FontColor(Muted); t.Span(o.RankedCount != null ? $"{o.Rank} of {o.RankedCount}" : $"{o.Rank}").Bold(); });
                });
                if (o.FailedSubjects.Count > 0) col.Item().PaddingTop(2).Text(t => { t.Span("Subjects to improve  ").FontColor(Muted); t.Span(string.Join(", ", o.FailedSubjects)).FontColor("#B91C1C"); });
                if (!string.IsNullOrWhiteSpace(p.Decision))
                    col.Item().PaddingTop(2).Text(t =>
                    {
                        t.Span("Promotion  ").FontColor(Muted);
                        t.Span(p.Decision switch { "PROMOTED" => "Promoted", "NOT_PROMOTED" => "Not promoted", "DETAINED" => "Detained", var d => d ?? "" }).Bold();
                        if (!string.IsNullOrWhiteSpace(p.PromotedTo)) { t.Span("  to  ").FontColor(Muted); t.Span(p.PromotedTo).Bold(); }
                    });
                if (!string.IsNullOrWhiteSpace(p.NewSessionDate)) col.Item().PaddingTop(2).Text(t => { t.Span("New session begins on  ").FontColor(Muted); t.Span(p.NewSessionDate).Bold(); });
            });
        }
    }
}
