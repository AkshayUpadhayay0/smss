using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static smss_api_service_layer.helper.reportcard.RcKit;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>
    /// The board-style A4 portrait report: Part 1 Scholastic areas (one table per term: subject, every exam with its maximum in the header, a term total and the grade), Part 2 Co-Scholastic
    /// activities with grades, Part 3 Discipline, attendance, the teacher's remarks, the result and promotion, the grade legend and the signature row. In GRADES mode only grades are shown (no
    /// marks, totals or percentages). The page is NOT scaled: everything flows onto a further page when it does not fit. Board Style draws it in the school's colours; Minimal draws the same
    /// layout in black ink with no fills (the grades variant of Minimal).
    /// </summary>
    public static class BoardLayout
    {
        public static void Render(IDocumentContainer doc, RcContext x, PortraitStyle style)
        {
            var m = x.Model; var pal = Pal(m);
            var photo = m.ShowPhoto && !style.Mono;       // Minimal never draws a photo: it is the ink-saving design
            var ink = style.Mono ? "#000000" : Ink; var muted = style.Mono ? "#333333" : Muted; var rule = style.Mono ? "#000000" : Rule;
            var accent = style.Mono ? "#000000" : pal.PrimaryText; var accent2 = style.Mono ? "#000000" : pal.SecondaryText;
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(9, Unit.Millimetre);
                page.DefaultTextStyle(s => s.FontFamily(Sans).FontSize(8).FontColor(ink));
                DraftMark(page, x.Draft);
                page.Header().Column(col =>
                {
                    col.Item().Element(c => PortraitCardLayout.PortraitHeader(c, x, style, accent, accent2, muted));
                    col.Item().PaddingTop(3).Row(r =>
                    {
                        r.RelativeItem().Element(c => PortraitCardLayout.StudentBlockP(c, m, style, rule, muted, ink));
                        if (photo) r.ConstantItem(62).PaddingLeft(5).Element(c => PhotoSlot(c, x, 57, 66));
                    });
                    col.Item().PaddingBottom(3);
                });
                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Text("Part 1 · Scholastic areas").FontFamily(new[] { Serif, Sans }).FontSize(10).Bold().FontColor(accent);
                    foreach (var term in m.Terms)
                        col.Item().Element(c => TermTable(c, m, style, term, accent, rule, ink, muted));
                    if (m.Mode == "MARKS" && m.Terms.Count > 1) col.Item().Element(c => YearLine(c, m, style, accent, rule, muted));
                    if (m.Mode == "MARKS" && m.Subjects.Any(s => !s.Included)) col.Item().Text("* not counted in the total").FontSize(6.5f).FontColor(muted);
                    col.Item().Element(c => ActivitiesTable(c, m, style, "CO_SCHOLASTIC", "Part 2 · Co-scholastic activities", accent, rule, muted));
                    col.Item().Element(c => ActivitiesTable(c, m, style, "DISCIPLINE", "Part 3 · Discipline", accent, rule, muted));
                    col.Item().Element(c => AttendanceAndRemarks(c, m, style, accent, rule, muted));
                    col.Item().Element(c => ResultAndPromotion(c, m, style, accent, rule, muted));
                });
                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(4).Element(c => PortraitCardLayout.SignatureRow(c, x, style, muted, "Class Teacher", "Parent", "Principal"));
                    col.Item().PaddingTop(4).Row(r =>
                    {
                        if (m.ShowGradeLegend) r.RelativeItem().Element(c => PortraitCardLayout.LegendP(c, m, muted)); else r.RelativeItem();
                        r.ConstantItem(150).AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(6).FontColor(muted));
                            t.Span(x.Draft ? "DRAFT preview - not an official copy" : $"Version {m.Version}" + (m.PublishedAt != null ? $" · {m.PublishedAt}" : ""));
                        });
                    });
                    FooterText(col, m);
                });
            });
        }

        /// <summary>The grade shown for one exam in GRADES mode: the cell's own grade, or the most frequent skill grade.</summary>
        internal static string ExamGrade(RcCell? c)
        {
            if (c == null) return "";
            if (c.State == CellStates.Absent) return "Ab";
            if (c.State == CellStates.Exempt) return "Ex";
            if (c.Skills.Count > 0)
            {
                var g = c.Skills.Where(k => !string.IsNullOrWhiteSpace(k.Grade)).GroupBy(k => k.Grade).OrderByDescending(k => k.Count()).ThenBy(k => k.Key).FirstOrDefault();
                return g?.Key ?? "-";
            }
            return c.Grade ?? "-";
        }

        private static void TermTable(IContainer c, ReportCardModel m, PortraitStyle style, RcTerm term, string accent, string rule, string ink, string muted)
        {
            var marks = m.Mode == "MARKS"; var many = term.Exams.Count > 1;
            var termIndex = m.Terms.IndexOf(term);
            var subjects = m.Subjects.ToList();
            var headFill = style.Mono ? "#FFFFFF" : Tint(m.Primary, 0.84); var zebra = style.Mono ? "#FFFFFF" : Tint(m.Primary, 0.95);
            var size = subjects.Count > 12 ? 7f : 8f;
            c.Column(col =>
            {
                col.Item().PaddingBottom(2).Text(term.Name).FontFamily(new[] { Serif, Sans }).FontSize(9).Bold().FontColor(accent);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(3.2f);
                        foreach (var _ in term.Exams) cd.RelativeColumn(1f);
                        if (marks && many) cd.RelativeColumn(1.3f);
                        cd.ConstantColumn(34);
                    });
                    IContainer Head(IContainer e) => e.Border(style.Mono ? 0.8f : 0.4f).BorderColor(rule).Background(headFill).PaddingVertical(2).PaddingHorizontal(2).AlignMiddle();
                    IContainer Body(IContainer e, bool z) => e.Border(style.Mono ? 0.6f : 0.3f).BorderColor(rule).Background(z ? zebra : Colors.White).PaddingVertical(1.5f).PaddingHorizontal(2).AlignMiddle();
                    t.Header(h =>
                    {
                        h.Cell().Element(Head).Text("Subject").FontSize(size).Bold();
                        foreach (var e in term.Exams)
                            h.Cell().Element(Head).AlignCenter().Text(tx =>
                            {
                                tx.AlignCenter(); tx.Span(e.Name).FontSize(size - 0.5f).Bold();
                                if (marks && e.Max != null) tx.Span($"\n({Num(e.Max.Value)})").FontSize(size - 1.5f).FontColor(muted);
                            });
                        if (marks && many) h.Cell().Element(Head).AlignCenter().Text(tx => { tx.AlignCenter(); tx.Span("Total").FontSize(size - 0.5f).Bold(); tx.Span($"\n({Num(term.Exams.Sum(e => e.Weight ?? e.Max ?? 0))})").FontSize(size - 1.5f).FontColor(muted); });
                        h.Cell().Element(Head).AlignCenter().Text("Grade").FontSize(size - 0.5f).Bold();
                    });
                    for (var i = 0; i < subjects.Count; i++)
                    {
                        var s = subjects[i]; var z = i % 2 == 1; var st = termIndex >= 0 && termIndex < s.Terms.Count ? s.Terms[termIndex] : null;
                        t.Cell().Element(e => Body(e, z)).Text(s.Name + (marks && !s.Included ? " *" : "")).FontSize(FitSize(s.Name, size, 30, 6.5f));
                        foreach (var e in term.Exams)
                        {
                            var cell = st?.Cells.FirstOrDefault(q => q.ExamId == e.ExamId);
                            t.Cell().Element(q => Body(q, z)).AlignCenter().Text(cell == null ? "" : marks ? CellText(cell) : ExamGrade(cell)).FontSize(size);
                        }
                        if (marks && many) t.Cell().Element(e => Body(e, z)).AlignCenter().Text(st == null ? "-" : $"{Num(st.Obtained)}/{Num(st.OutOf)}").FontSize(size).Bold();
                        t.Cell().Element(e => Body(e, z)).AlignCenter().Text(string.IsNullOrWhiteSpace(st?.Grade) ? "-" : st!.Grade).FontSize(size).Bold();
                    }
                    var sum = m.TermSummaries.FirstOrDefault(q => q.TermId == term.TermId);
                    if (marks && sum != null && sum.OutOf > 0)
                    {
                        t.Cell().ColumnSpan((uint)(1 + term.Exams.Count + (many ? 1 : 0))).Element(e => Body(e, false)).AlignRight().Text($"{term.Name} total  {Num(sum.Obtained)} / {Num(sum.OutOf)}  ·  {Pct(sum.Percent)}").FontSize(size).Bold();
                        t.Cell().Element(e => Body(e, false)).AlignCenter().Text(sum.Grade ?? "-").FontSize(size).Bold();
                    }
                });
            });
        }

        private static void YearLine(IContainer c, ReportCardModel m, PortraitStyle style, string accent, string rule, string muted)
        {
            var o = m.Overall;
            c.Border(style.Mono ? 1f : 0.5f).BorderColor(rule).Background(style.Mono ? "#FFFFFF" : Tint(m.Secondary, 0.88)).Padding(4).Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8.5f));
                t.Span("Whole year  ").Bold().FontColor(accent);
                t.Span($"{Num(o.Obtained)} / {Num(o.OutOf)}  ·  {Pct(o.Percent)}  ·  Grade {o.Grade ?? "-"}").Bold();
                if (m.ShowRank && o.Rank != null) t.Span($"  ·  Position {o.Rank} of {o.RankedCount}").FontColor(muted);
            });
        }

        private static void ActivitiesTable(IContainer c, ReportCardModel m, PortraitStyle style, string category, string title, string accent, string rule, string muted)
        {
            var sums = m.Terms.Select(t => (Term: t, Sum: m.TermSummaries.FirstOrDefault(s => s.TermId == t.TermId))).ToList();
            var areas = sums.SelectMany(q => q.Sum?.Activities ?? new List<RcActivity>()).Where(a => a.Category == category).Select(a => a.Area).Distinct().ToList();
            if (areas.Count == 0) return;
            c.Column(col =>
            {
                col.Item().PaddingBottom(2).Text(title).FontFamily(new[] { Serif, Sans }).FontSize(10).Bold().FontColor(accent);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd => { cd.RelativeColumn(3); foreach (var _ in sums) cd.RelativeColumn(1.4f); });
                    t.Header(h =>
                    {
                        h.Cell().BorderBottom(style.Mono ? 0.9f : 0.6f).BorderColor(accent).PaddingBottom(1).Text("Area").FontSize(8).Bold();
                        foreach (var q in sums) h.Cell().BorderBottom(style.Mono ? 0.9f : 0.6f).BorderColor(accent).PaddingBottom(1).AlignCenter().Text(q.Term.Name).FontSize(7.5f).Bold().FontColor(muted);
                    });
                    foreach (var area in areas)
                    {
                        t.Cell().BorderBottom(0.25f).BorderColor(rule).PaddingVertical(1.5f).Text(area).FontSize(8);
                        foreach (var q in sums)
                        {
                            var a = q.Sum?.Activities.FirstOrDefault(z => z.Category == category && z.Area == area);
                            t.Cell().BorderBottom(0.25f).BorderColor(rule).PaddingVertical(1.5f).AlignCenter().Text(a == null ? "-" : string.IsNullOrWhiteSpace(a.GradeLabel) ? a.Grade : $"{a.Grade} ({a.GradeLabel})").FontSize(7.5f);
                        }
                    }
                });
            });
        }

        private static void AttendanceAndRemarks(IContainer c, ReportCardModel m, PortraitStyle style, string accent, string rule, string muted)
        {
            var sums = m.Terms.Select(t => (Term: t, Sum: m.TermSummaries.FirstOrDefault(s => s.TermId == t.TermId))).ToList();
            var att = m.ShowAttendance ? sums.Where(q => q.Sum?.WorkingDays != null && q.Sum.PresentDays != null).ToList() : new();
            var remarks = sums.Where(q => !string.IsNullOrWhiteSpace(q.Sum?.Remark)).ToList();
            var extra = sums.Where(q => !string.IsNullOrWhiteSpace(q.Sum?.Conduct) || !string.IsNullOrWhiteSpace(q.Sum?.RankText)).ToList();
            if (att.Count == 0 && remarks.Count == 0 && extra.Count == 0) return;
            c.Column(col =>
            {
                col.Spacing(3);
                if (att.Count > 0)
                    col.Item().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8));
                        t.Span("Attendance   ").FontColor(muted);
                        for (var i = 0; i < att.Count; i++)
                        {
                            var q = att[i]; var pct = q.Sum!.WorkingDays > 0 ? Math.Round(100.0 * q.Sum.PresentDays!.Value / q.Sum.WorkingDays!.Value, 1) : 0;
                            t.Span($"{q.Term.Name}: ").FontColor(muted); t.Span($"{q.Sum.PresentDays} of {q.Sum.WorkingDays} days ({pct}%)").Bold();
                            if (i < att.Count - 1) t.Span("    ·    ");
                        }
                    });
                foreach (var q in extra)
                    col.Item().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8));
                        t.Span($"{q.Term.Name}   ").FontColor(muted);
                        if (!string.IsNullOrWhiteSpace(q.Sum!.Conduct)) { t.Span("Conduct: ").FontColor(muted); t.Span(q.Sum.Conduct!).Bold(); t.Span("    "); }
                        if (!string.IsNullOrWhiteSpace(q.Sum.RankText)) { t.Span("Rank: ").FontColor(muted); t.Span(q.Sum.RankText!).Bold(); }
                    });
                foreach (var q in remarks)
                    col.Item().Text(t => { t.DefaultTextStyle(s => s.FontSize(8)); t.Span($"{q.Term.Name} · class teacher's remark   ").FontColor(muted); t.Span(q.Sum!.Remark!); });
            });
        }

        private static void ResultAndPromotion(IContainer c, ReportCardModel m, PortraitStyle style, string accent, string rule, string muted)
        {
            var o = m.Overall; var p = m.Promotion; var marks = m.Mode == "MARKS";
            c.Border(style.Mono ? 1f : 0.6f).BorderColor(rule).Background(style.Mono ? "#FFFFFF" : Tint(m.Primary, 0.94)).Padding(5).Column(col =>
            {
                col.Item().Text("Result").FontFamily(new[] { Serif, Sans }).FontSize(10).Bold().FontColor(accent);
                col.Item().PaddingTop(2).Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8.5f));
                    if (marks) { t.Span("Total ").FontColor(muted); t.Span($"{Num(o.Obtained)} / {Num(o.OutOf)}").Bold(); t.Span("   Percentage ").FontColor(muted); t.Span(Pct(o.Percent)).Bold(); t.Span("   Grade ").FontColor(muted); t.Span(o.Grade ?? "-").Bold(); t.Span("   "); }
                    t.Span("Result ").FontColor(muted); t.Span(ResultText(o.Result, m.Mode)).Bold();
                    if (marks && m.ShowRank && o.Rank != null) { t.Span("   Position ").FontColor(muted); t.Span($"{o.Rank} of {o.RankedCount}").Bold(); }
                });
                if (marks && o.FailedSubjects.Count > 0) col.Item().PaddingTop(2).Text($"Subjects to improve: {string.Join(", ", o.FailedSubjects)}").FontSize(8);
                if (!string.IsNullOrWhiteSpace(p.Decision) || !string.IsNullOrWhiteSpace(p.NewSessionDate))
                    col.Item().PaddingTop(2).Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8.5f));
                        t.Span("Promotion ").FontColor(muted);
                        t.Span(p.Decision switch { "PROMOTED" => $"Promoted to {p.PromotedTo}", "NOT_PROMOTED" => "Not promoted", "DETAINED" => "Detained", var d => d ?? "-" }).Bold();
                        if (!string.IsNullOrWhiteSpace(p.NewSessionDate)) { t.Span("   New session begins ").FontColor(muted); t.Span(p.NewSessionDate).Bold(); }
                    });
            });
        }
    }

    /// <summary>Board Style (MARKS and GRADES): the board-style A4 portrait report in the school's colours.</summary>
    public sealed class BoardStyleTemplate : IReportCardTemplate
    {
        public string Code => "board-style";
        public string Name => "Board Style";
        public IReadOnlyList<string> SupportedModes { get; } = new[] { "MARKS", "GRADES" };
        public string Orientation => "portrait";
        public void Render(IDocumentContainer doc, RcContext ctx) => BoardLayout.Render(doc, ctx, PortraitStyle.Classic);
    }
}
