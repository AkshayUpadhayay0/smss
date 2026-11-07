using smss_api_service_layer.helper.idcard;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static smss_api_service_layer.helper.reportcard.RcKit;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>
    /// Skill Grid (GRADES, A4 landscape): the early-years matrix. Exams are the rows (grouped under their term) and every subject is a group of columns, one column per skill (Reading, Writing,
    /// Oral, ...). Under the matrix: one panel per term with the teacher's remark, the activity grades (Discipline, Cleanliness, ...), the rank text, attendance (present of working days) and
    /// conduct; then promotion, the signature row and the grade key taken from the scheme's own scale. Up to about 6 exams and 5 subjects of 5 skills fit one table; a wider matrix is split
    /// into several tables one under the other, and everything flows onto a further page instead of overflowing.
    /// </summary>
    public sealed class SkillGridTemplate : IReportCardTemplate
    {
        public string Code => "skill-grid";
        public string Name => "Skill Grid";
        public IReadOnlyList<string> SupportedModes { get; } = new[] { "GRADES" };
        public string Orientation => "landscape";
        private const int MaxSkillColumnsPerTable = 24;

        public void Render(IDocumentContainer doc, RcContext x)
        {
            var m = x.Model; var pal = Pal(m);
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(8, Unit.Millimetre);
                page.DefaultTextStyle(s => s.FontFamily(Sans).FontSize(7).FontColor(Ink));
                DraftMark(page, x.Draft);
                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Element(c => RcKit.Header(c, x, m.Scope == "TERM" && m.Terms.Count == 1 ? $"Progress Report · {m.Terms[0].Name} · {m.SessionName}" : $"Progress Report · {m.SessionName}"));
                        col.Item().PaddingTop(3).Element(c => StudentBlock(c, m));
                        col.Item().PaddingBottom(3);
                    });
                    if (m.ShowPhoto) row.ConstantItem(72).PaddingLeft(6).Element(c => PhotoSlot(c, x, 66, 82));
                });
                page.Content().Column(col =>
                {
                    col.Spacing(5);
                    foreach (var chunk in Chunks(m)) col.Item().Element(c => Matrix(c, m, pal, chunk));
                    col.Item().Element(c => TermPanels(c, m, pal));
                    col.Item().Element(c => Promotion(c, m, pal));
                });
                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(4).Element(c => SignatureRow(c, x, "Class Teacher", "Parent", "Principal"));
                    col.Item().PaddingTop(4).Row(r =>
                    {
                        if (m.ShowGradeLegend) r.RelativeItem().Element(c => GradeLegend(c, m)); else r.RelativeItem();
                        r.ConstantItem(170).AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(6).FontColor(Muted));
                            t.Span(x.Draft ? "DRAFT preview - not an official copy" : $"Version {m.Version}" + (m.PublishedAt != null ? $" · {m.PublishedAt}" : ""));
                        });
                    });
                    FooterText(col, m);
                });
            });
        }

        /// <summary>One subject's columns: its skill names, or a single "Grade" column for a subject graded as a whole.</summary>
        private sealed record SubjectCols(RcSubject Subject, List<string> Skills);

        private static List<SubjectCols> Columns(ReportCardModel m) =>
            m.Subjects.Select(s =>
            {
                var names = s.Terms.SelectMany(t => t.Cells).SelectMany(c => c.Skills).Select(k => k.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
                return new SubjectCols(s, names.Count > 0 ? names : new List<string> { "Grade" });
            }).ToList();

        /// <summary>The subjects split into tables so no table has more than MaxSkillColumnsPerTable skill columns (a lone wide subject still gets a table of its own).</summary>
        private static List<List<SubjectCols>> Chunks(ReportCardModel m)
        {
            var result = new List<List<SubjectCols>>(); var cur = new List<SubjectCols>(); var width = 0;
            foreach (var s in Columns(m))
            {
                if (cur.Count > 0 && width + s.Skills.Count > MaxSkillColumnsPerTable) { result.Add(cur); cur = new(); width = 0; }
                cur.Add(s); width += s.Skills.Count;
            }
            if (cur.Count > 0) result.Add(cur);
            return result;
        }

        private static string GradeText(RcSubject s, int termIndex, RcExam exam, string skill)
        {
            if (termIndex >= s.Terms.Count) return "-";
            var cell = s.Terms[termIndex].Cells.FirstOrDefault(c => c.ExamId == exam.ExamId);
            if (cell == null) return "";
            if (cell.State == CellStates.Absent) return "Ab";
            if (cell.State == CellStates.Exempt) return "Ex";
            if (cell.Skills.Count > 0) return cell.Skills.FirstOrDefault(k => k.Name == skill)?.Grade ?? "-";
            return cell.Grade ?? "-";
        }

        private static void Matrix(IContainer c, ReportCardModel m, IdCardPalette pal, List<SubjectCols> subjects)
        {
            var total = subjects.Sum(s => s.Skills.Count);
            var size = total > 18 ? 6.5f : 7.5f;
            var headFill = Tint(m.Primary, 0.84); var termFill = Tint(m.Secondary, 0.8); var zebra = Tint(m.Primary, 0.95);
            c.Table(t =>
            {
                t.ColumnsDefinition(cd => { cd.ConstantColumn(58); for (var i = 0; i < total; i++) cd.RelativeColumn(1f); });
                t.Header(h =>
                {
                    h.Cell().RowSpan(2).Element(e => HeadCell(e, headFill)).Text("Exam").FontSize(size).Bold().FontColor(pal.PrimaryText);
                    foreach (var s in subjects)
                        h.Cell().ColumnSpan((uint)s.Skills.Count).Element(e => HeadCell(e, headFill)).AlignCenter().Text(s.Subject.Name).FontFamily(new[] { Serif, Sans }).FontSize(FitSize(s.Subject.Name, size + 0.5f, 6 * s.Skills.Count, 5.5f)).Bold().FontColor(pal.PrimaryText);
                    foreach (var s in subjects)
                        foreach (var k in s.Skills)
                            h.Cell().Element(e => HeadCell(e, headFill)).AlignCenter().Text(k).FontSize(FitSize(k, 5.8f, 8, 4.4f)).FontColor(Ink);
                });
                for (var ti = 0; ti < m.Terms.Count; ti++)
                {
                    var term = m.Terms[ti];
                    t.Cell().ColumnSpan((uint)(1 + total)).Element(e => BodyCell(e, true, termFill)).Text(term.Name).FontSize(size).Bold().FontColor(pal.SecondaryText);
                    for (var ei = 0; ei < term.Exams.Count; ei++)
                    {
                        var exam = term.Exams[ei]; var z = ei % 2 == 1;
                        t.Cell().Element(e => BodyCell(e, z, zebra)).Text(exam.Name).FontSize(size).FontColor(Ink);
                        foreach (var s in subjects)
                            foreach (var k in s.Skills)
                                t.Cell().Element(e => BodyCell(e, z, zebra)).AlignCenter().Text(GradeText(s.Subject, ti, exam, k)).FontSize(size).Bold();
                    }
                }
            });
        }

        // ------------------------------------------------------------------ one panel per term
        private static void TermPanels(IContainer c, ReportCardModel m, IdCardPalette pal)
        {
            if (m.TermSummaries.Count == 0) return;
            c.Row(r =>
            {
                r.Spacing(6);
                foreach (var ts in m.TermSummaries)
                {
                    var term = m.Terms.FirstOrDefault(t => t.TermId == ts.TermId);
                    r.RelativeItem().Border(0.5f).BorderColor(Rule).Column(col =>
                    {
                        col.Item().Background(Tint(m.Primary, 0.84)).Padding(3).Text(term?.Name ?? "Term").FontFamily(new[] { Serif, Sans }).FontSize(8).Bold().FontColor(pal.PrimaryText);
                        col.Item().Padding(3).Column(p =>
                        {
                            p.Spacing(2);
                            p.Item().Text(t =>
                            {
                                t.DefaultTextStyle(s => s.FontSize(7));
                                t.Span("Teacher's remark: ").FontColor(Muted);
                                t.Span(string.IsNullOrWhiteSpace(ts.Remark) ? "-" : ts.Remark);
                            });
                            var acts = ts.Activities.Where(a => a.Category == "DISCIPLINE" || a.Category == "CO_SCHOLASTIC").ToList();
                            if (acts.Count > 0)
                                p.Item().Text(t =>
                                {
                                    t.DefaultTextStyle(s => s.FontSize(7));
                                    for (var i = 0; i < acts.Count; i++)
                                    {
                                        t.Span(acts[i].Area + ": ").FontColor(Muted); t.Span(acts[i].Grade).Bold();
                                        if (i < acts.Count - 1) t.Span("   ");
                                    }
                                });
                            if (!string.IsNullOrWhiteSpace(ts.RankText)) p.Item().Text(t => { t.DefaultTextStyle(s => s.FontSize(7)); t.Span("Rank: ").FontColor(Muted); t.Span(ts.RankText).Bold(); });
                            if (!string.IsNullOrWhiteSpace(ts.Conduct)) p.Item().Text(t => { t.DefaultTextStyle(s => s.FontSize(7)); t.Span("Conduct: ").FontColor(Muted); t.Span(ts.Conduct).Bold(); });
                            if (m.ShowAttendance && ts.WorkingDays != null)
                                p.Item().Text(t =>
                                {
                                    t.DefaultTextStyle(s => s.FontSize(7)); t.Span("Attendance: ").FontColor(Muted);
                                    t.Span($"{ts.PresentDays?.ToString() ?? "-"} of {ts.WorkingDays} days").Bold();
                                    if (ts.PresentDays != null && ts.WorkingDays > 0) t.Span($" ({Math.Round(100.0 * ts.PresentDays.Value / ts.WorkingDays.Value, 1)}%)").FontColor(Muted);
                                });
                        });
                    });
                }
            });
        }

        private static void Promotion(IContainer c, ReportCardModel m, IdCardPalette pal)
        {
            var p = m.Promotion;
            if (string.IsNullOrWhiteSpace(p.Decision) && string.IsNullOrWhiteSpace(p.NewSessionDate)) return;
            c.Border(0.5f).BorderColor(Rule).Background(Tint(m.Secondary, 0.88)).Padding(4).Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8));
                t.Span("Result: ").FontColor(Muted);
                t.Span(p.Decision == "PROMOTED" ? $"Promoted to {p.PromotedTo}" : p.Decision == "NOT_PROMOTED" ? "Not promoted" : p.Decision ?? "-").Bold().FontColor(pal.PrimaryText);
                if (!string.IsNullOrWhiteSpace(p.NewSessionDate)) { t.Span("      New session begins: ").FontColor(Muted); t.Span(p.NewSessionDate).Bold(); }
            });
        }
    }
}
