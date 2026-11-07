using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static smss_api_service_layer.helper.reportcard.RcKit;

namespace smss_api_service_layer.helper.reportcard
{
    /// <summary>How the shared portrait layout is dressed: the brand colours and light fills (Classic Card), or one ink colour with no filled area at all (Minimal).</summary>
    public sealed record PortraitStyle(bool Mono)
    {
        public static readonly PortraitStyle Classic = new(false), Minimal = new(true);
    }

    /// <summary>
    /// The compact A4 portrait report card: ONE page for the whole term set (one, two or three terms), subjects down the side with the exams as columns (and a Total per term), a remark per
    /// subject, the result block, the DISCIPLINE areas as conduct lines, attendance and the signature row. Used by Classic Card (brand colours, tinted header) and Minimal (black ink on white,
    /// no fills). The content block is wrapped in ScaleToFit, so 16 subjects, three terms and very long names shrink a little instead of overflowing or breaking the page.
    /// </summary>
    public static class PortraitCardLayout
    {
        public static void Render(IDocumentContainer doc, RcContext x, PortraitStyle style)
        {
            var m = x.Model; var pal = Pal(m);
            var ink = style.Mono ? "#000000" : Ink; var muted = style.Mono ? "#333333" : Muted; var rule = style.Mono ? "#000000" : Rule;
            var accent = style.Mono ? "#000000" : pal.PrimaryText; var accent2 = style.Mono ? "#000000" : pal.SecondaryText;
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(9, Unit.Millimetre);
                page.DefaultTextStyle(s => s.FontFamily(Sans).FontSize(7).FontColor(ink));
                DraftMark(page, x.Draft);
                page.Header().Column(col =>
                {
                    col.Item().Element(c => PortraitHeader(c, x, style, accent, accent2, muted));
                    col.Item().PaddingTop(3).Element(c => StudentBlockP(c, m, style, rule, muted, ink));
                    col.Item().PaddingBottom(3);
                });
                page.Content().ScaleToFit().Column(col =>
                {
                    col.Spacing(4);
                    col.Item().Element(c => Scholastic(c, m, style, accent, rule, ink, muted));
                    col.Item().Element(c => ResultBlock(c, m, style, accent, rule, muted));
                    col.Item().Element(c => ConductAndAttendance(c, m, style, accent, rule, muted));
                });
                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(4).Element(c => SignatureRow(c, x, style, muted, "Class Teacher", "Parent", "Principal"));
                    col.Item().PaddingTop(4).Row(r =>
                    {
                        if (m.ShowGradeLegend) r.RelativeItem().Element(c => LegendP(c, m, muted)); else r.RelativeItem();
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

        // ------------------------------------------------------------------ header and student
        private static void PortraitHeader(IContainer c, RcContext x, PortraitStyle style, string accent, string accent2, string muted)
        {
            var m = x.Model;
            c.BorderBottom(style.Mono ? 1f : 1.2f).BorderColor(accent).PaddingBottom(3).Row(r =>
            {
                if (x.Logo != null) r.ConstantItem(48).Height(48).Padding(2).Image(x.Logo).FitArea();
                r.RelativeItem().PaddingLeft(x.Logo != null ? 6 : 0).Column(col =>
                {
                    col.Item().Text(m.Header.SchoolName).FontFamily(new[] { Serif, Sans }).FontSize(FitSize(m.Header.SchoolName, 17f, 36, 9f)).Bold().FontColor(accent);
                    if (!string.IsNullOrWhiteSpace(m.Header.Line)) col.Item().Text(m.Header.Line).FontFamily(new[] { Serif, Sans }).FontSize(FitSize(m.Header.Line, 8.5f, 80, 6f));
                    var rec = string.Join("  ·  ", new[] { m.Header.Recognition, m.Header.Udise }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (rec.Length > 0) col.Item().Text(rec).FontSize(FitSize(rec, 7f, 100, 5.5f)).FontColor(muted);
                    if (!string.IsNullOrWhiteSpace(m.Header.AddressLine)) col.Item().Text(m.Header.AddressLine + (string.IsNullOrWhiteSpace(m.Header.Phone) ? "" : "  ·  " + m.Header.Phone)).FontSize(FitSize(m.Header.AddressLine, 7f, 110, 5.5f)).FontColor(muted);
                    col.Item().PaddingTop(1).Text(Title(m)).FontFamily(new[] { Serif, Sans }).FontSize(10).Bold().FontColor(accent2);
                });
            });
        }

        private static string Title(ReportCardModel m) => m.Scope == "TERM" && m.Terms.Count == 1 ? $"Progress Report · {m.Terms[0].Name} · {m.SessionName}" : $"Progress Report · {m.SessionName}";

        private static void StudentBlockP(IContainer c, ReportCardModel m, PortraitStyle style, string rule, string muted, string ink)
        {
            var s = m.Student;
            void KV(ColumnDescriptor col, string key, string? value, bool bold = false) =>
                col.Item().Row(r => { r.ConstantItem(62).Text(key).FontSize(6.5f).FontColor(muted); var tx = r.RelativeItem().Text(string.IsNullOrWhiteSpace(value) ? "-" : value).FontFamily(new[] { Sans }).FontSize(FitSize(value, 7.5f, 30, 6.5f)).FontColor(ink); if (bold) tx.Bold(); });
            c.Border(0.5f).BorderColor(rule).Padding(3).Row(r =>
            {
                r.RelativeItem(1.35f).Column(col => { KV(col, "Name", s.Name, true); KV(col, "Father", s.Father); KV(col, "Mother", s.Mother); KV(col, "Date of joining", s.Doj); });
                r.RelativeItem(1f).Column(col => { KV(col, "Class / Section", $"{s.ClassName} {s.SectionName}".Trim()); KV(col, "Roll No.", s.Roll); KV(col, "Admission No.", s.AdmissionNo); KV(col, "Date of birth", s.Dob); });
            });
        }

        // ------------------------------------------------------------------ the scholastic table
        private static void Scholastic(IContainer c, ReportCardModel m, PortraitStyle style, string accent, string rule, string ink, string muted)
        {
            var terms = m.Terms; var many = terms.Count > 1;
            var dense = m.Subjects.Count > 12 || terms.Count == 3;
            // a short list is drawn larger so the page is used; a long one smaller. ScaleToFit still catches the rest.
            var size = dense ? 6f : m.Subjects.Count > 9 || terms.Count == 2 && m.Subjects.Count > 7 ? 7f : 8f;
            var headFill = style.Mono ? "#FFFFFF" : Tint(m.Primary, 0.84); var zebra = style.Mono ? "#FFFFFF" : Tint(m.Primary, 0.94);
            var headLine = style.Mono ? 0.9f : 0.4f;

            // column plan (1-based): subject, then per term its exams and (when it has several) a term total, then the overall total (several terms), grade, remark
            var plan = new List<(string Kind, int Term, int Exam)>();
            plan.Add(("subject", -1, -1));
            for (var t = 0; t < terms.Count; t++)
            {
                for (var e = 0; e < terms[t].Exams.Count; e++) plan.Add(("exam", t, e));
                if (terms[t].Exams.Count > 1) plan.Add(("termtotal", t, -1));
            }
            if (many) plan.Add(("total", -1, -1));
            plan.Add(("grade", -1, -1)); plan.Add(("remark", -1, -1));

            c.Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    foreach (var p in plan)
                        switch (p.Kind)
                        {
                            case "subject": cd.RelativeColumn(3.3f); break;
                            case "exam": cd.RelativeColumn(1f); break;
                            case "termtotal": cd.RelativeColumn(1.35f); break;
                            case "total": cd.RelativeColumn(1.6f); break;
                            case "grade": cd.RelativeColumn(0.8f); break;
                            default: cd.RelativeColumn(m.Subjects.Count > 12 || terms.Count == 3 ? 2.6f : 3f); break;
                        }
                });
                IContainer Head(IContainer cc) => cc.Border(0.4f).BorderColor(rule).Background(headFill).PaddingVertical(1.5f).PaddingHorizontal(1.5f).AlignMiddle();
                IContainer Body(IContainer cc, int row) => cc.Border(0.3f).BorderColor(rule).Background(row % 2 == 0 ? zebra : "#FFFFFF").PaddingVertical(1.2f).PaddingHorizontal(1.5f).AlignMiddle();

                // header rows 1 and 2
                uint col = 1;
                foreach (var grp in plan.Select((p, i) => (p, i)))
                {
                    var p = grp.p; col = (uint)grp.i + 1;
                    if (p.Kind == "exam")
                    {
                        var e = terms[p.Term].Exams[p.Exam];
                        t.Cell().Row(2).Column(col).Element(Head).AlignCenter().Column(cc =>
                        {
                            cc.Item().AlignCenter().Text(e.Name).FontSize(size - 0.5f).Bold();
                            if (e.Max != null) cc.Item().AlignCenter().Text($"({Num(e.Max.Value)})").FontSize(size - 1.5f).FontColor(muted);
                        });
                    }
                    else if (p.Kind == "termtotal")
                        t.Cell().Row(2).Column(col).Element(Head).AlignCenter().Column(cc => { cc.Item().AlignCenter().Text("Total").FontSize(size - 0.5f).Bold(); cc.Item().AlignCenter().Text($"({Num(terms[p.Term].Exams.Sum(e => e.Weight ?? e.Max ?? 0))})").FontSize(size - 1.5f).FontColor(muted); });
                }
                // term group headings (row 1) span the term's columns
                for (var ti = 0; ti < terms.Count; ti++)
                {
                    var first = plan.FindIndex(p => p.Term == ti) + 1; var count = plan.Count(p => p.Term == ti);
                    t.Cell().Row(1).Column((uint)first).ColumnSpan((uint)count).Element(Head).AlignCenter().Text(terms[ti].Name).FontFamily(new[] { Serif, Sans }).FontSize(size + 0.5f).Bold().FontColor(accent);
                }
                t.Cell().Row(1).Column(1).RowSpan(2).Element(Head).Text("Subject").FontSize(size).Bold();
                var tcol = (uint)plan.FindIndex(p => p.Kind == "total") + 1;
                if (many) t.Cell().Row(1).Column(tcol).RowSpan(2).Element(Head).AlignCenter().Text("Grand total").FontSize(size).Bold();
                t.Cell().Row(1).Column((uint)plan.FindIndex(p => p.Kind == "grade") + 1).RowSpan(2).Element(Head).AlignCenter().Text("Grade").FontSize(size).Bold();
                t.Cell().Row(1).Column((uint)plan.FindIndex(p => p.Kind == "remark") + 1).RowSpan(2).Element(Head).Text("Remark").FontSize(size).Bold();

                // body
                for (var j = 0; j < m.Subjects.Count; j++)
                {
                    var s = m.Subjects[j]; uint row = (uint)j + 3; var shade = j;
                    foreach (var grp in plan.Select((p, i) => (p, i)))
                    {
                        var p = grp.p; var cc = (uint)grp.i + 1;
                        var st = p.Term >= 0 ? s.Terms.FirstOrDefault(y => y.TermId == terms[p.Term].TermId) : null;
                        switch (p.Kind)
                        {
                            case "subject": t.Cell().Row(row).Column(cc).Element(z => Body(z, shade)).Text(s.Name + (!s.Included ? " *" : "")).FontSize(size).FontColor(ink); break;
                            case "exam":
                                var cell = st?.Cells.FirstOrDefault(q => q.ExamId == terms[p.Term].Exams[p.Exam].ExamId);
                                t.Cell().Row(row).Column(cc).Element(z => Body(z, shade)).AlignCenter().Text(cell == null ? "" : CellText(cell)).FontSize(size).FontColor(cell?.State == CellStates.Absent && !style.Mono ? "#B91C1C" : ink); break;
                            case "termtotal":
                                t.Cell().Row(row).Column(cc).Element(z => Body(z, shade)).AlignCenter().Text(st == null ? "" : s.GradeOnly ? (st.Grade ?? "") : $"{Num(st.Obtained)}/{Num(st.OutOf)}").FontSize(size).Bold().FontColor(st?.Failed == true && !style.Mono ? "#B91C1C" : ink); break;
                            case "total":
                                t.Cell().Row(row).Column(cc).Element(z => Body(z, shade)).AlignCenter().Text(s.GradeOnly ? (s.YearGrade ?? "") : $"{Num(s.YearObtained)}/{Num(s.YearOutOf)}").FontSize(size).Bold().FontColor(s.YearFailed && !style.Mono ? "#B91C1C" : ink); break;
                            case "grade":
                                t.Cell().Row(row).Column(cc).Element(z => Body(z, shade)).AlignCenter().Text(many ? s.YearGrade ?? "" : s.Terms.FirstOrDefault()?.Grade ?? "").FontSize(size).Bold(); break;
                            default:
                                // the latest term that has a remark for this subject (the school's final comment on it)
                                var rem = s.Terms.AsEnumerable().Reverse().Select(q => q.Remark).FirstOrDefault(q => !string.IsNullOrWhiteSpace(q)) ?? "";
                                t.Cell().Row(row).Column(cc).Element(z => Body(z, shade)).Text(rem).FontSize(size - 0.5f).FontColor(muted); break;
                        }
                    }
                }
            });
        }

        // ------------------------------------------------------------------ result
        private static void ResultBlock(IContainer c, ReportCardModel m, PortraitStyle style, string accent, string rule, string muted)
        {
            var o = m.Overall; var p = m.Promotion; var marks = m.Mode == "MARKS";
            var fill = style.Mono ? "#FFFFFF" : Tint(m.Primary, 0.94);
            c.Border(style.Mono ? 0.8f : 0.6f).BorderColor(style.Mono ? "#000000" : accent).Background(fill).Padding(4).Column(col =>
            {
                col.Item().Text(m.Scope == "TERM" ? "Result" : "Final result").FontFamily(new[] { Serif, Sans }).FontSize(9).Bold().FontColor(accent);
                col.Item().PaddingTop(2).Row(r =>
                {
                    if (marks)
                    {
                        r.RelativeItem().Text(t => { t.Span(m.Scope == "TERM" ? "Total  " : "Grand total  ").FontColor(muted); t.Span($"{Num(o.Obtained)} / {Num(o.OutOf)}").Bold(); });
                        r.RelativeItem().Text(t => { t.Span("Percentage  ").FontColor(muted); t.Span(Pct(o.Percent)).Bold(); });
                        r.RelativeItem(0.7f).Text(t => { t.Span("Grade  ").FontColor(muted); t.Span(o.Grade ?? "-").Bold(); });
                    }
                    r.RelativeItem(0.9f).Text(t => { t.Span("Result  ").FontColor(muted); t.Span(ResultText(o.Result, m.Mode)).Bold(); });
                });
                col.Item().PaddingTop(2).Row(r =>
                {
                    if (m.ShowRank && o.Rank != null) r.RelativeItem().Text(t => { t.Span("Position  ").FontColor(muted); t.Span(o.RankedCount != null ? $"{o.Rank} of {o.RankedCount}" : $"{o.Rank}").Bold(); });
                    if (!string.IsNullOrWhiteSpace(p.Decision))
                        r.RelativeItem(1.6f).Text(t =>
                        {
                            t.Span("Promotion  ").FontColor(muted);
                            t.Span(p.Decision switch { "PROMOTED" => "Promoted", "NOT_PROMOTED" => "Not promoted", "DETAINED" => "Detained", var d => d ?? "" }).Bold();
                            if (!string.IsNullOrWhiteSpace(p.PromotedTo)) { t.Span("  to  ").FontColor(muted); t.Span(p.PromotedTo).Bold(); }
                        });
                    if (!string.IsNullOrWhiteSpace(p.NewSessionDate)) r.RelativeItem(1.3f).Text(t => { t.Span("New session begins  ").FontColor(muted); t.Span(p.NewSessionDate).Bold(); });
                });
                if (o.FailedSubjects.Count > 0) col.Item().PaddingTop(2).Text(t => { t.Span("Subjects to improve  ").FontColor(muted); t.Span(string.Join(", ", o.FailedSubjects)); });
            });
        }

        // ------------------------------------------------------------------ conduct (the DISCIPLINE areas), attendance, the teacher's remark
        private static void ConductAndAttendance(IContainer c, ReportCardModel m, PortraitStyle style, string accent, string rule, string muted)
        {
            var terms = m.Terms; var sums = terms.Select(t => (Term: t, Sum: m.TermSummaries.FirstOrDefault(s => s.TermId == t.TermId))).ToList();
            var areas = sums.SelectMany(x => x.Sum?.Activities ?? new List<RcActivity>()).Where(a => a.Category == "DISCIPLINE").Select(a => a.Area).Distinct().ToList();
            var withAtt = m.ShowAttendance ? sums.Where(x => x.Sum?.WorkingDays != null && x.Sum.PresentDays != null).ToList() : new();
            var remarks = sums.Where(x => !string.IsNullOrWhiteSpace(x.Sum?.Remark)).ToList();
            if (areas.Count == 0 && withAtt.Count == 0 && remarks.Count == 0) return;
            c.Column(col =>
            {
                col.Spacing(3);
                if (areas.Count > 0)
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cd => { cd.RelativeColumn(3); foreach (var _ in terms) cd.RelativeColumn(1.4f); });
                        t.Header(h =>
                        {
                            h.Cell().BorderBottom(0.6f).BorderColor(accent).PaddingBottom(1).Text("Conduct").FontFamily(new[] { Serif, Sans }).FontSize(8).Bold().FontColor(accent);
                            foreach (var x in terms) h.Cell().BorderBottom(0.6f).BorderColor(accent).PaddingBottom(1).AlignCenter().Text(x.Name).FontSize(6.5f).Bold().FontColor(muted);
                        });
                        foreach (var area in areas)
                        {
                            t.Cell().BorderBottom(0.25f).BorderColor(rule).PaddingVertical(1).Text(area).FontSize(7);
                            foreach (var x in sums)
                            {
                                var a = x.Sum?.Activities.FirstOrDefault(q => q.Category == "DISCIPLINE" && q.Area == area);
                                t.Cell().BorderBottom(0.25f).BorderColor(rule).PaddingVertical(1).AlignCenter().Text(a == null ? "-" : string.IsNullOrWhiteSpace(a.GradeLabel) ? a.Grade : $"{a.Grade} ({a.GradeLabel})").FontSize(6.5f);
                            }
                        }
                    });
                if (withAtt.Count > 0)
                    col.Item().Text(t =>
                    {
                        t.Span("Attendance  ").FontColor(muted).FontSize(7);
                        for (var i = 0; i < withAtt.Count; i++)
                        {
                            var s = withAtt[i].Sum!; var pctv = s.WorkingDays > 0 ? $" ({(s.PresentDays!.Value * 100m / s.WorkingDays!.Value).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}%)" : "";
                            t.Span($"{(terms.Count > 1 ? withAtt[i].Term.Name + ": " : "")}{s.PresentDays} of {s.WorkingDays} days{pctv}").Bold().FontSize(7);
                            if (i < withAtt.Count - 1) t.Span("    ·    ").FontColor(muted);
                        }
                    });
                if (remarks.Count > 0)
                {
                    var last = remarks[^1];
                    col.Item().Text(t => { t.Span("Class teacher's remark  ").FontColor(muted).FontSize(7); t.Span(last.Sum!.Remark!).FontSize(7); });
                }
            });
        }

        // ------------------------------------------------------------------ signatures and legend
        private static void SignatureRow(IContainer c, RcContext x, PortraitStyle style, string muted, params string[] who)
        {
            c.Row(r =>
            {
                foreach (var w in who)
                    r.RelativeItem().PaddingHorizontal(12).Column(col =>
                    {
                        col.Item().Height(26).AlignBottom().Element(e => { if (w == "Principal" && x.Signature != null) e.Image(x.Signature).FitArea(); });
                        col.Item().LineHorizontal(style.Mono ? 0.8f : 0.6f).LineColor(style.Mono ? "#000000" : muted);
                        col.Item().AlignCenter().Text(w).FontSize(7.5f).FontColor(muted);
                    });
            });
        }

        private static void LegendP(IContainer c, ReportCardModel m, string muted)
        {
            if (m.Legend.Count == 0) return;
            c.Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(6).FontColor(muted));
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
    }

    /// <summary>Classic Card: compact A4 portrait in the school's colours.</summary>
    public sealed class ClassicCardTemplate : IReportCardTemplate
    {
        public string Code => "classic-card";
        public string Name => "Classic Card";
        public IReadOnlyList<string> SupportedModes { get; } = new[] { "MARKS" };
        public string Orientation => "portrait";
        public void Render(IDocumentContainer doc, RcContext ctx) => PortraitCardLayout.Render(doc, ctx, PortraitStyle.Classic);
    }

    /// <summary>Minimal: the same A4 portrait content in black ink with no filled areas, for plain-paper photocopying.</summary>
    public sealed class MinimalBwTemplate : IReportCardTemplate
    {
        public string Code => "minimal-bw";
        public string Name => "Minimal (ink-saving)";
        public IReadOnlyList<string> SupportedModes { get; } = new[] { "MARKS" };
        public string Orientation => "portrait";
        public void Render(IDocumentContainer doc, RcContext ctx) => PortraitCardLayout.Render(doc, ctx, PortraitStyle.Minimal);
    }
}
