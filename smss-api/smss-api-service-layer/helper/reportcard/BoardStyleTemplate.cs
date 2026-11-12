using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static smss_api_service_layer.helper.reportcard.RcKit;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>
    /// The board-style A4 portrait report. A normal class (7 to 10 subjects, one to three terms) fits ONE page, with or without a photo: Part 1 Scholastic (one compact table per term: subject, every
    /// exam with its maximum in the header, a term total and the grade), Part 2 Co-scholastic and Part 3 Discipline side by side, ONE merged result block (totals, percentage, grade, result,
    /// position, promotion and the new session date), then attendance, conduct and the class teacher's remarks, and the signature row at the foot. In GRADES mode only grades are shown.
    /// Nothing is scaled down: when there really are too many subjects the card continues on a further page that repeats a compact one-line header, and the blocks are never split.
    /// Board Style draws it in the school's colours; Minimal draws the same layout in black ink with no fills (the grades variant of Minimal).
    /// </summary>
    public static class BoardLayout
    {
        private const float Font = 7f;

        public static void Render(IDocumentContainer doc, RcContext x, PortraitStyle style)
        {
            var m = x.Model; var pal = Pal(m);
            var ink = style.Mono ? "#000000" : Ink; var muted = style.Mono ? "#333333" : Muted; var rule = style.Mono ? "#000000" : Rule;
            var accent = style.Mono ? "#000000" : pal.PrimaryText; var accent2 = style.Mono ? "#000000" : pal.SecondaryText;
            var photo = m.ShowPhoto && !style.Mono;       // Minimal never draws a photo: it is the ink-saving design
            // A long list (subjects x terms rows) is fitted with tighter margins and spacing, never with a font below RcKit.MinFontPt
            var dense = m.Subjects.Count * Math.Max(1, m.Terms.Count) > 20;
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(8, Unit.Millimetre);
                page.MarginVertical(dense ? 5 : 8, Unit.Millimetre);
                page.DefaultTextStyle(s => s.FontFamily(Sans).FontSize(Font).FontColor(ink));
                DraftMark(page, x.Draft);
                page.Header().Column(h =>
                {
                    // the full header is printed once; every later page repeats one compact line
                    h.Item().ShowOnce().Column(first =>
                    {
                        first.Item().Element(c => PortraitCardLayout.PortraitHeader(c, x, style, accent, accent2, muted));
                        first.Item().PaddingTop(3).Row(r =>
                        {
                            r.RelativeItem().Element(c => PortraitCardLayout.StudentBlockP(c, m, style, rule, muted, ink));
                            if (photo) r.ConstantItem(48).PaddingLeft(4).Element(c => PhotoSlot(c, x, 44, 52));
                        });
                        first.Item().PaddingBottom(3);
                    });
                    h.Item().SkipOnce().Element(c => CompactHeader(c, m, style, accent, muted));
                });
                page.Content().Column(col =>
                {
                    col.Spacing(dense ? 1.5f : 3);
                    if (!dense || m.Subjects.Count * Math.Max(1, m.Terms.Count) <= 28)   // a very long list drops this label to keep the card on one page
                        col.Item().Text("Part 1 · Scholastic areas").FontFamily(new[] { Serif, Sans }).FontSize(9).Bold().FontColor(accent);
                    foreach (var term in m.Terms) col.Item().Element(c => TermTable(c, m, style, term, accent, rule, muted));
                    col.Item().ShowEntire().Element(c => Activities(c, m, style, accent, rule, muted));
                    col.Item().ShowEntire().Element(c => ResultBlock(c, m, style, accent, rule, muted));
                    col.Item().ShowEntire().Element(c => AttendanceAndRemarks(c, m, muted));
                });
                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(3).Element(c => PortraitCardLayout.SignatureRow(c, x, style, muted, "Class Teacher", "Parent", "Principal"));
                    col.Item().PaddingTop(3).Row(r =>
                    {
                        if (m.ShowGradeLegend) r.RelativeItem().Element(c => PortraitCardLayout.LegendP(c, m, muted)); else r.RelativeItem();
                        r.ConstantItem(150).AlignRight().Text(t =>
                        {
                            t.DefaultTextStyle(s => s.FontSize(7).FontColor(muted));
                            t.Span(x.Draft ? "DRAFT preview - not an official copy" : $"Version {m.Version}" + (m.PublishedAt != null ? $" · {m.PublishedAt}" : ""));
                        });
                    });
                    FooterText(col, m);
                });
            });
        }

        /// <summary>The one-line header of a continuation page: school, report title and who the card is for.</summary>
        private static void CompactHeader(IContainer c, ReportCardModel m, PortraitStyle style, string accent, string muted)
        {
            var s = m.Student;
            c.BorderBottom(style.Mono ? 1f : 0.8f).BorderColor(accent).PaddingBottom(2).PaddingTop(2).Row(r =>
            {
                r.RelativeItem().Text(m.Header.SchoolName).FontFamily(new[] { Serif, Sans }).FontSize(FitSize(m.Header.SchoolName, 10f, 40, 7f)).Bold().FontColor(accent);
                r.AutoItem().PaddingRight(8).Text($"Progress Report · {m.SessionName}").FontSize(7.5f).FontColor(muted);
                r.AutoItem().Text(t =>
                {
                    t.Span(string.IsNullOrWhiteSpace(s.Name) ? "-" : s.Name).Bold().FontSize(FitSize(s.Name, 8f, 28, 7f));
                    t.Span($"   {s.ClassName} {s.SectionName}".TrimEnd() + (string.IsNullOrWhiteSpace(s.Roll) ? "" : $" · Roll {s.Roll}")).FontColor(muted).FontSize(7.5f);
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

        private static void TermTable(IContainer c, ReportCardModel m, PortraitStyle style, RcTerm term, string accent, string rule, string muted)
        {
            var marks = m.Mode == "MARKS"; var many = term.Exams.Count > 1;
            var termIndex = m.Terms.IndexOf(term);
            var subjects = m.Subjects.ToList();
            var headFill = style.Mono ? "#FFFFFF" : Tint(m.Primary, 0.84); var zebra = style.Mono ? "#FFFFFF" : Tint(m.Primary, 0.95);
            // the more subject rows the card has to carry (subjects x terms), the tighter the rows: a ten-subject, three-term card still fits one page
            var rowCount = subjects.Count * Math.Max(1, m.Terms.Count);
            var size = Font;   // never below RcKit.MinFontPt: a long subject list is fitted by spacing, not by a smaller font
            var vpad = rowCount <= 20 ? 0.8f : rowCount <= 28 ? 0.4f : 0f;
            var hpad = rowCount <= 20 ? 1f : 0.5f;
            var sum = m.TermSummaries.FirstOrDefault(q => q.TermId == term.TermId);
            c.Column(col =>
            {
                col.Item().PaddingBottom(rowCount <= 20 ? 1f : 0f).Text(term.Name).FontFamily(new[] { Serif, Sans }).FontSize(8).Bold().FontColor(accent);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(3.4f);
                        foreach (var _ in term.Exams) cd.RelativeColumn(1f);
                        if (marks && many) cd.RelativeColumn(1.4f);
                        cd.ConstantColumn(30);
                    });
                    IContainer Head(IContainer e) => e.Border(style.Mono ? 0.8f : 0.4f).BorderColor(rule).Fill(headFill).PaddingVertical(hpad).PaddingHorizontal(2).AlignMiddle();
                    IContainer Body(IContainer e, bool z) => e.Border(style.Mono ? 0.6f : 0.3f).BorderColor(rule).Fill(z ? zebra : "#FFFFFF").PaddingVertical(vpad).PaddingHorizontal(2).AlignMiddle();
                    t.Header(h =>
                    {
                        h.Cell().Element(Head).Text("Subject").FontSize(size).Bold();
                        foreach (var e in term.Exams)
                            h.Cell().Element(Head).AlignCenter().Text(tx =>
                            {
                                tx.AlignCenter(); tx.Span(e.Name).FontSize(RcKit.Pt(size - 0.3f)).Bold();
                                if (marks && e.Max != null) tx.Span($" ({Num(e.Max.Value)})").FontSize(RcKit.Pt(size - 1.5f)).FontColor(muted);
                            });
                        if (marks && many) h.Cell().Element(Head).AlignCenter().Text(tx => { tx.AlignCenter(); tx.Span("Total").FontSize(RcKit.Pt(size - 0.3f)).Bold(); tx.Span($" ({Num(term.Exams.Sum(e => e.Weight ?? e.Max ?? 0))})").FontSize(RcKit.Pt(size - 1.5f)).FontColor(muted); });
                        h.Cell().Element(Head).AlignCenter().Text("Grade").FontSize(RcKit.Pt(size - 0.3f)).Bold();
                    });
                    for (var i = 0; i < subjects.Count; i++)
                    {
                        var s = subjects[i]; var z = i % 2 == 1; var st = termIndex >= 0 && termIndex < s.Terms.Count ? s.Terms[termIndex] : null;
                        t.Cell().Element(e => Body(e, z)).Text(s.Name + (marks && !s.Included ? " *" : "")).FontSize(FitSize(s.Name, size, 34, 7f));
                        foreach (var e in term.Exams)
                        {
                            var cell = st?.Cells.FirstOrDefault(q => q.ExamId == e.ExamId);
                            t.Cell().Element(q => Body(q, z)).AlignCenter().Text(cell == null ? "" : marks ? CellText(cell) : ExamGrade(cell)).FontSize(size);
                        }
                        if (marks && many) t.Cell().Element(e => Body(e, z)).AlignCenter().Text(st == null ? "-" : $"{Num(st.Obtained)}/{Num(st.OutOf)}").FontSize(size).Bold();
                        t.Cell().Element(e => Body(e, z)).AlignCenter().Text(string.IsNullOrWhiteSpace(st?.Grade) ? "-" : st!.Grade).FontSize(size).Bold();
                    }
                    if (marks && sum != null && sum.OutOf > 0)
                    {
                        t.Cell().ColumnSpan((uint)(1 + term.Exams.Count + (many ? 1 : 0))).Element(e => Body(e, false)).AlignRight().Text($"{term.Name} total  {Num(sum.Obtained)} / {Num(sum.OutOf)}  ·  {Pct(sum.Percent)}").FontSize(size).Bold();
                        t.Cell().Element(e => Body(e, false)).AlignCenter().Text(sum.Grade ?? "-").FontSize(size).Bold();
                    }
                });
                if (marks && m.Subjects.Any(s => !s.Included) && termIndex == m.Terms.Count - 1) col.Item().PaddingTop(1).Text("* not counted in the total").FontSize(7f).FontColor(muted);
            });
        }

        /// <summary>Part 2 (co-scholastic) and Part 3 (discipline) side by side; with only one of them, that one takes the whole width.</summary>
        private static void Activities(IContainer c, ReportCardModel m, PortraitStyle style, string accent, string rule, string muted)
        {
            var apad = m.Subjects.Count * Math.Max(1, m.Terms.Count) > 20 ? 0.3f : 0.8f;
            var sums = m.Terms.Select(t => (Term: t, Sum: m.TermSummaries.FirstOrDefault(s => s.TermId == t.TermId))).ToList();
            List<string> Areas(string category) => sums.SelectMany(q => q.Sum?.Activities ?? new List<RcActivity>()).Where(a => a.Category == category).Select(a => a.Area).Distinct().ToList();
            var co = Areas("CO_SCHOLASTIC"); var dis = Areas("DISCIPLINE");
            if (co.Count == 0 && dis.Count == 0) return;
            var withLabel = m.Terms.Count <= 2;
            void Block(IContainer cc, string category, string title, List<string> areas) =>
                cc.Column(col =>
                {
                    col.Item().PaddingBottom(1).Text(title).FontFamily(new[] { Serif, Sans }).FontSize(8).Bold().FontColor(accent);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(2.2f); foreach (var _ in sums) cd.RelativeColumn(withLabel ? 2f : 1f); });
                        t.Header(h =>
                        {
                            h.Cell().BorderBottom(style.Mono ? 0.9f : 0.6f).BorderColor(accent).PaddingBottom(1).Text("Area").FontSize(Font).Bold();
                            foreach (var q in sums) h.Cell().BorderBottom(style.Mono ? 0.9f : 0.6f).BorderColor(accent).PaddingBottom(1).AlignCenter().Text(q.Term.Name).FontSize(RcKit.Pt(Font - 0.5f)).Bold().FontColor(muted);
                        });
                        foreach (var area in areas)
                        {
                            t.Cell().BorderBottom(0.25f).BorderColor(rule).PaddingVertical(apad).Text(area).FontSize(Font);
                            foreach (var q in sums)
                            {
                                var a = q.Sum?.Activities.FirstOrDefault(z => z.Category == category && z.Area == area);
                                var text = a == null ? "-" : withLabel && !string.IsNullOrWhiteSpace(a.GradeLabel) ? $"{a.Grade} ({a.GradeLabel})" : a.Grade;
                                t.Cell().BorderBottom(0.25f).BorderColor(rule).PaddingVertical(apad).AlignCenter().Text(text).FontSize(RcKit.Pt(Font - 0.5f));
                            }
                        }
                    });
                });
            c.Row(r =>
            {
                r.Spacing(10);
                if (co.Count > 0) r.RelativeItem().Element(cc => Block(cc, "CO_SCHOLASTIC", "Part 2 · Co-scholastic activities", co));
                if (dis.Count > 0) r.RelativeItem().Element(cc => Block(cc, "DISCIPLINE", "Part 3 · Discipline", dis));
            });
        }

        /// <summary>ONE result block: totals, percentage, grade, result and position (MARKS), then promotion and the new session date. It replaces the old separate "Whole year" bar and Result block.</summary>
        private static void ResultBlock(IContainer c, ReportCardModel m, PortraitStyle style, string accent, string rule, string muted)
        {
            var o = m.Overall; var p = m.Promotion; var marks = m.Mode == "MARKS";
            c.Border(style.Mono ? 1f : 0.6f).BorderColor(rule).Fill(style.Mono ? "#FFFFFF" : Tint(m.Primary, 0.94)).Padding(4).Column(col =>
            {
                col.Item().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8));
                    t.Span(m.Terms.Count > 1 && marks ? "Whole year   " : "Result   ").FontFamily(new[] { Serif, Sans }).Bold().FontColor(accent);
                    if (marks)
                    {
                        t.Span("Total ").FontColor(muted); t.Span($"{Num(o.Obtained)} / {Num(o.OutOf)}").Bold();
                        t.Span("   Percentage ").FontColor(muted); t.Span(Pct(o.Percent)).Bold();
                        t.Span("   Grade ").FontColor(muted); t.Span(o.Grade ?? "-").Bold(); t.Span("   ");
                    }
                    t.Span("Result ").FontColor(muted); t.Span(ResultText(o.Result, m.Mode)).Bold();
                    if (marks && m.ShowRank && o.Rank != null) { t.Span("   Position ").FontColor(muted); t.Span($"{o.Rank} of {o.RankedCount}").Bold(); }
                });
                if (marks && o.FailedSubjects.Count > 0) col.Item().PaddingTop(1).Text($"Subjects to improve: {string.Join(", ", o.FailedSubjects)}").FontSize(7.5f);
                if (!string.IsNullOrWhiteSpace(p.Decision) || !string.IsNullOrWhiteSpace(p.NewSessionDate))
                    col.Item().PaddingTop(1).Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8));
                        t.Span("Promotion ").FontColor(muted);
                        t.Span(p.Decision switch { "PROMOTED" => $"Promoted to {p.PromotedTo}", "NOT_PROMOTED" => "Not promoted", "DETAINED" => "Detained", var d => d ?? "-" }).Bold();
                        if (!string.IsNullOrWhiteSpace(p.NewSessionDate)) { t.Span("   New session begins ").FontColor(muted); t.Span(p.NewSessionDate).Bold(); }
                    });
            });
        }

        /// <summary>Attendance, conduct, rank and the class teacher's remarks, kept together just above the signature row.</summary>
        private static void AttendanceAndRemarks(IContainer c, ReportCardModel m, string muted)
        {
            var sums = m.Terms.Select(t => (Term: t, Sum: m.TermSummaries.FirstOrDefault(s => s.TermId == t.TermId))).ToList();
            var att = m.ShowAttendance ? sums.Where(q => q.Sum?.WorkingDays != null && q.Sum.PresentDays != null).ToList() : new();
            var remarks = sums.Where(q => !string.IsNullOrWhiteSpace(q.Sum?.Remark)).ToList();
            var extra = sums.Where(q => !string.IsNullOrWhiteSpace(q.Sum?.Conduct) || !string.IsNullOrWhiteSpace(q.Sum?.RankText)).ToList();
            if (att.Count == 0 && remarks.Count == 0 && extra.Count == 0) return;
            c.Column(col =>
            {
                col.Spacing(1.5f);
                if (att.Count > 0)
                    col.Item().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(Font + 0.5f));
                        t.Span("Attendance   ").FontColor(muted).Bold();
                        for (var i = 0; i < att.Count; i++)
                        {
                            var q = att[i]; var pct = q.Sum!.WorkingDays > 0 ? Math.Round(100.0 * q.Sum.PresentDays!.Value / q.Sum.WorkingDays!.Value, 1) : 0;
                            t.Span($"{q.Term.Name}: ").FontColor(muted); t.Span($"{q.Sum.PresentDays} of {q.Sum.WorkingDays} days ({pct}%)").Bold();
                            if (i < att.Count - 1) t.Span("    ·    ");
                        }
                    });
                if (extra.Count > 0)
                    col.Item().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(Font + 0.5f));
                        t.Span("Conduct   ").FontColor(muted).Bold();
                        for (var i = 0; i < extra.Count; i++)
                        {
                            var q = extra[i]; t.Span($"{q.Term.Name}: ").FontColor(muted);
                            t.Span(string.Join(" · ", new[] { q.Sum!.Conduct, string.IsNullOrWhiteSpace(q.Sum.RankText) ? null : "rank " + q.Sum.RankText }.Where(v => !string.IsNullOrWhiteSpace(v)))).Bold();
                            if (i < extra.Count - 1) t.Span("    ·    ");
                        }
                    });
                foreach (var q in remarks)
                    col.Item().Text(t => { t.DefaultTextStyle(s => s.FontSize(Font + 0.5f)); t.Span($"{q.Term.Name} · class teacher's remark   ").FontColor(muted).Bold(); t.Span(q.Sum!.Remark!); });
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
