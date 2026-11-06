using System.Numerics;

namespace smss_api_service_layer.helper.reportcard
{
    // ======================================================================================== input
    public sealed record CalcBand(string Code, string Label, decimal MinPercent, bool IsFail);
    public sealed record CalcTerm(long TermId, int Sequence, string Name, decimal? YearWeight);
    public sealed record CalcSubject(long SchemeSubjectId, string Name, bool IncludeInTotal, int DisplayOrder);

    /// <summary>One exam subject (a cell of the scheme's matrix). Max and Weight are the EFFECTIVE values (the cell's own, else the exam's). A GRADE cell has neither.</summary>
    public sealed record CalcCell(long ExamSubjectId, long TermId, long ExamId, long SchemeSubjectId, bool IsMarks, decimal? Max, decimal? Weight, IReadOnlyList<long> SkillIds);

    public sealed class CalcScheme
    {
        public bool IsMarksMode { get; init; } = true;
        public bool WeightedYear { get; init; }
        public decimal SubjectPassPercent { get; init; } = 33m;
        public decimal OverallPassPercent { get; init; } = 33m;
        public bool Truncate { get; init; }
        public bool ShowRank { get; init; }
        /// <summary>Rank within the whole class instead of the section.</summary>
        public bool RankByClass { get; init; }
        public IReadOnlyList<CalcBand> Bands { get; init; } = Array.Empty<CalcBand>();
        public IReadOnlyList<CalcTerm> Terms { get; init; } = Array.Empty<CalcTerm>();
        public IReadOnlyList<CalcSubject> Subjects { get; init; } = Array.Empty<CalcSubject>();
        public IReadOnlyList<CalcCell> Cells { get; init; } = Array.Empty<CalcCell>();
    }

    public enum EntryKind { Entered, Absent, Exempt }
    public sealed record CalcEntry(EntryKind Kind, decimal? Marks = null, string? Grade = null);

    public sealed class CalcStudent
    {
        public string StudentId { get; init; } = "";
        public long SectionId { get; init; }
        public long ClassId { get; init; }
        /// <summary>Entries by (exam subject, skill). A MARKS cell uses a null skill. A cell with no entry at all is MISSING.</summary>
        public Dictionary<(long ExamSubjectId, long? SkillId), CalcEntry> Entries { get; init; } = new();
    }

    // ======================================================================================== output
    public static class ResultStatuses { public const string Pass = "PASS", Fail = "FAIL", Incomplete = "INCOMPLETE"; }
    public static class CellStates { public const string Entered = "ENTERED", Absent = "ABSENT", Exempt = "EXEMPT", Missing = "MISSING"; }

    public sealed class SkillOutcome { public long SkillId { get; init; } public string State { get; init; } = ""; public string? Grade { get; init; } }

    public sealed class CellOutcome
    {
        public long ExamSubjectId { get; init; }
        public long ExamId { get; init; }
        public bool IsMarks { get; init; }
        /// <summary>ENTERED, ABSENT (prints "Ab"), EXEMPT (prints "Ex") or MISSING.</summary>
        public string State { get; set; } = "";
        public decimal? Marks { get; init; }
        public decimal? Max { get; init; }
        public decimal? Weight { get; init; }
        /// <summary>What the cell adds to the subject's total (marks x weight / max), for display only.</summary>
        public decimal? Contribution { get; init; }
        public string? Grade { get; set; }
        public List<SkillOutcome> Skills { get; init; } = new();
    }

    public sealed class SubjectResult
    {
        public long SchemeSubjectId { get; init; }
        public bool Included { get; init; }
        public List<CellOutcome> Cells { get; init; } = new();
        public decimal Obtained { get; init; }
        public decimal OutOf { get; init; }
        public decimal? Percent { get; init; }
        public string? Grade { get; init; }
        public bool Incomplete { get; init; }
        public bool Failed { get; init; }
    }

    public sealed class TermResult
    {
        public long TermId { get; init; }
        public List<SubjectResult> Subjects { get; init; } = new();
        public decimal Obtained { get; set; }
        public decimal OutOf { get; set; }
        public decimal? Percent { get; set; }
        public string? Grade { get; set; }
        public string Status { get; set; } = ResultStatuses.Incomplete;
        public List<long> FailedSubjects { get; init; } = new();
        public int? Rank { get; set; }
        public int? RankedCount { get; set; }
        internal Rat Ratio;
    }

    public sealed class YearResult
    {
        public List<SubjectResult> Subjects { get; init; } = new();
        public decimal Obtained { get; init; }
        public decimal OutOf { get; init; }
        public decimal? Percent { get; init; }
        public string? Grade { get; init; }
        public string Status { get; set; } = ResultStatuses.Incomplete;
        public List<long> FailedSubjects { get; init; } = new();
        public int? Rank { get; set; }
        public int? RankedCount { get; set; }
        internal Rat Ratio;
    }

    public sealed class StudentResult
    {
        public string StudentId { get; init; } = "";
        public long SectionId { get; init; }
        public long ClassId { get; init; }
        public List<TermResult> Terms { get; init; } = new();
        public YearResult Year { get; init; } = new();
    }

    // ======================================================================================== exact fractions
    /// <summary>
    /// An exact fraction. The spec says "multiply before divide, no rounding until display"; with plain decimals 1/3 + 1/3 + 1/3 is 0.9999...9 and a school that truncates would lose a mark
    /// to a rounding residue. Every sum here is exact; it becomes a decimal only at the end, through the scheme's rounding mode.
    /// </summary>
    internal readonly struct Rat : IComparable<Rat>
    {
        public BigInteger N { get; }
        public BigInteger D { get; }

        public Rat(BigInteger n, BigInteger d)
        {
            if (d.IsZero) { N = BigInteger.Zero; D = BigInteger.One; return; }
            if (d.Sign < 0) { n = -n; d = -d; }
            var g = BigInteger.GreatestCommonDivisor(BigInteger.Abs(n), d);
            if (g > BigInteger.One) { n /= g; d /= g; }
            N = n; D = d;
        }

        public static Rat Zero => new(BigInteger.Zero, BigInteger.One);
        public static Rat From(decimal v)
        {
            // decimal is an unscaled 96-bit integer and a power-of-ten scale: take it apart, no floating point involved
            var bits = decimal.GetBits(v);
            var scale = (bits[3] >> 16) & 0xFF;
            var mag = (new BigInteger((uint)bits[2]) << 64) | (new BigInteger((uint)bits[1]) << 32) | new BigInteger((uint)bits[0]);
            if (bits[3] < 0) mag = -mag;
            return new Rat(mag, BigInteger.Pow(10, scale));
        }

        public bool IsZero => N.IsZero;
        public static Rat operator +(Rat a, Rat b) => new(a.N * b.D + b.N * a.D, a.D * b.D);
        public static Rat operator *(Rat a, Rat b) => new(a.N * b.N, a.D * b.D);
        public static Rat operator /(Rat a, Rat b) => new(a.N * b.D, a.D * b.N);
        public int CompareTo(Rat o) => (N * o.D).CompareTo(o.N * D);

        /// <summary>The value to two decimals by the scheme's rounding mode: TRUNCATE drops the rest, otherwise half rounds away from zero. Exact (no floating point anywhere).</summary>
        public decimal Round2(bool truncate)
        {
            var num = N * 100;
            var neg = num.Sign < 0; var abs = BigInteger.Abs(num);
            var q = truncate ? BigInteger.Divide(abs, D) : BigInteger.Divide(abs * 2 + D, D * 2);
            return (neg ? -1m : 1m) * (decimal)q / 100m;
        }
    }

    // ======================================================================================== the engine
    /// <summary>
    /// The result engine of the report card: pure arithmetic, no database, no clock. Implements the Phase A calculation spec exactly:
    /// ABSENT scores 0 and keeps its weight, EXEMPT leaves the subject (nothing counted), a cell with no entry makes the student INCOMPLETE; subject score = sum of marks x weight / max;
    /// percentages by the scheme's rounding mode and a grade from the printed two-decimal percentage (lower band edge inclusive); a student FAILS when the overall percentage or any included
    /// subject is under its pass mark; a GRADE cell in a MARKS scheme is display-only (shown, never summed, ranked or failed); ranks go to PASS students only, by the exact ratio,
    /// competition style (1, 2, 2, 4).
    /// </summary>
    public static class ReportCalculator
    {
        public static Dictionary<string, StudentResult> Calculate(CalcScheme scheme, IReadOnlyList<CalcStudent> students)
        {
            var results = new Dictionary<string, StudentResult>();
            foreach (var s in students) results[s.StudentId] = One(scheme, s);
            if (scheme.IsMarksMode && scheme.ShowRank) Rank(scheme, students, results);
            return results;
        }

        public static string? GradeFor(CalcScheme scheme, decimal percent)
        {
            CalcBand? best = null;
            foreach (var b in scheme.Bands) if (b.MinPercent <= percent && (best == null || b.MinPercent > best.MinPercent)) best = b;
            return best?.Code;
        }

        // ------------------------------------------------------------------ one student
        private sealed class Acc { public Rat Obtained = Rat.Zero; public Rat OutOf = Rat.Zero; public bool Incomplete; public bool FailGrade; }

        private static StudentResult One(CalcScheme sc, CalcStudent st)
        {
            var res = new StudentResult { StudentId = st.StudentId, SectionId = st.SectionId, ClassId = st.ClassId };
            var perTermSubject = new Dictionary<(long Term, long Subject), (SubjectResult R, Rat Obtained, Rat OutOf)>();
            foreach (var term in sc.Terms.OrderBy(t => t.Sequence))
            {
                var tr = new TermResult { TermId = term.TermId };
                var total = new Acc(); var anyIncomplete = false; var failed = new List<long>(); var failGrade = false;
                foreach (var subj in sc.Subjects.OrderBy(s => s.DisplayOrder))
                {
                    var cells = sc.Cells.Where(c => c.TermId == term.TermId && c.SchemeSubjectId == subj.SchemeSubjectId).ToList();
                    if (cells.Count == 0) continue;
                    var (sr, ob, outOf, subjFailGrade) = Subject(sc, st, subj, cells);
                    perTermSubject[(term.TermId, subj.SchemeSubjectId)] = (sr, ob, outOf);
                    tr.Subjects.Add(sr);
                    if (sc.IsMarksMode)
                    {
                        if (subj.IncludeInTotal) { total.Obtained += ob; total.OutOf += outOf; if (sr.Incomplete) anyIncomplete = true; if (sr.Failed) failed.Add(subj.SchemeSubjectId); }
                    }
                    else { if (sr.Incomplete) anyIncomplete = true; if (subjFailGrade) failGrade = true; }
                }
                if (sc.IsMarksMode)
                {
                    var pct = Pct(total.Obtained, total.OutOf, sc.Truncate);
                    Fill(tr, total, pct, sc);
                    tr.Status = anyIncomplete ? ResultStatuses.Incomplete : (pct != null && pct < sc.OverallPassPercent) || failed.Count > 0 ? ResultStatuses.Fail : ResultStatuses.Pass;
                    tr.FailedSubjects.AddRange(failed);
                    tr.Ratio = total.OutOf.IsZero ? Rat.Zero : total.Obtained / total.OutOf;
                }
                else tr.Status = anyIncomplete ? ResultStatuses.Incomplete : failGrade ? ResultStatuses.Fail : ResultStatuses.Pass;
                res.Terms.Add(tr);
            }
            res.Year.Status = res.Terms.Any(t => t.Status == ResultStatuses.Incomplete) ? ResultStatuses.Incomplete : res.Terms.Any(t => t.Status == ResultStatuses.Fail) ? ResultStatuses.Fail : ResultStatuses.Pass;
            return Year(sc, res, perTermSubject);
        }

        private static void Fill(TermResult tr, Acc total, decimal? pct, CalcScheme sc)
        {
            tr.Obtained = total.Obtained.Round2(sc.Truncate);
            tr.OutOf = total.OutOf.Round2(sc.Truncate);
            tr.Percent = pct;
            tr.Grade = pct == null ? null : GradeFor(sc, pct.Value);
        }

        private static string StateOf(CalcEntry? e) => e == null ? CellStates.Missing : e.Kind == EntryKind.Entered ? CellStates.Entered : e.Kind == EntryKind.Absent ? CellStates.Absent : CellStates.Exempt;
        private static bool IsFailGrade(CalcScheme sc, string? grade) => grade != null && sc.Bands.Any(b => b.IsFail && string.Equals(b.Code, grade, StringComparison.OrdinalIgnoreCase));

        private static decimal? Pct(Rat x, Rat y, bool truncate) => y.IsZero ? null : (x * Rat.From(100m) / y).Round2(truncate);

        /// <summary>One subject in one term. Returns the subject result, its exact obtained / out-of, and whether a grade-given cell was a failing grade (GRADES mode).</summary>
        private static (SubjectResult R, Rat Obtained, Rat OutOf, bool FailGrade) Subject(CalcScheme sc, CalcStudent st, CalcSubject subj, List<CalcCell> cells)
        {
            var outcomes = new List<CellOutcome>(); var obtained = Rat.Zero; var outOf = Rat.Zero; var incomplete = false; var failGrade = false;
            foreach (var cell in cells)
            {
                if (cell.IsMarks && sc.IsMarksMode)
                {
                    st.Entries.TryGetValue((cell.ExamSubjectId, null), out var e);
                    var max = cell.Max ?? 0m; var weight = cell.Weight ?? 0m;
                    if (e == null) { incomplete = true; outcomes.Add(new CellOutcome { ExamSubjectId = cell.ExamSubjectId, ExamId = cell.ExamId, IsMarks = true, State = CellStates.Missing, Max = cell.Max, Weight = cell.Weight }); continue; }
                    switch (e.Kind)
                    {
                        case EntryKind.Entered:
                        {
                            var m = e.Marks ?? 0m;
                            var part = max == 0m ? Rat.Zero : Rat.From(m) * Rat.From(weight) / Rat.From(max);     // multiply before divide, exact
                            obtained += part; outOf += Rat.From(weight);
                            outcomes.Add(new CellOutcome { ExamSubjectId = cell.ExamSubjectId, ExamId = cell.ExamId, IsMarks = true, State = CellStates.Entered, Marks = m, Max = cell.Max, Weight = cell.Weight, Contribution = part.Round2(sc.Truncate) });
                            break;
                        }
                        case EntryKind.Absent:
                            outOf += Rat.From(weight);                                                          // scores 0, keeps its weight
                            outcomes.Add(new CellOutcome { ExamSubjectId = cell.ExamSubjectId, ExamId = cell.ExamId, IsMarks = true, State = CellStates.Absent, Max = cell.Max, Weight = cell.Weight, Contribution = 0m });
                            break;
                        default:                                                                                // exempt: nothing counted
                            outcomes.Add(new CellOutcome { ExamSubjectId = cell.ExamSubjectId, ExamId = cell.ExamId, IsMarks = true, State = CellStates.Exempt, Max = cell.Max, Weight = cell.Weight });
                            break;
                    }
                }
                else
                {
                    // a GRADE cell: in a MARKS scheme it is display-only (never summed, never required, never fails); in a GRADES scheme it must be filled (every skill) and a failing grade fails
                    var o = new CellOutcome { ExamSubjectId = cell.ExamSubjectId, ExamId = cell.ExamId, IsMarks = false };
                    var states = new List<string>();
                    if (cell.SkillIds.Count == 0)
                    {
                        st.Entries.TryGetValue((cell.ExamSubjectId, null), out var e);
                        o.State = StateOf(e); o.Grade = e?.Grade; states.Add(o.State);
                        if (e?.Kind == EntryKind.Entered && IsFailGrade(sc, e.Grade)) failGrade = true;
                    }
                    else
                    {
                        foreach (var skillId in cell.SkillIds)
                        {
                            st.Entries.TryGetValue((cell.ExamSubjectId, skillId), out var e);
                            var state = StateOf(e); states.Add(state);
                            o.Skills.Add(new SkillOutcome { SkillId = skillId, State = state, Grade = e?.Grade });
                            if (e?.Kind == EntryKind.Entered && IsFailGrade(sc, e.Grade)) failGrade = true;
                        }
                        o.State = states.Any(s => s == CellStates.Missing) ? CellStates.Missing : states.Any(s => s == CellStates.Entered) ? CellStates.Entered : states.All(s => s == CellStates.Absent) ? CellStates.Absent : CellStates.Exempt;
                    }
                    outcomes.Add(o);
                    if (!sc.IsMarksMode && states.Any(s => s == CellStates.Missing)) incomplete = true;
                }
            }
            decimal? pct = sc.IsMarksMode ? Pct(obtained, outOf, sc.Truncate) : null;
            var failed = sc.IsMarksMode && subj.IncludeInTotal && pct != null && pct < sc.SubjectPassPercent;
            var sr = new SubjectResult
            {
                SchemeSubjectId = subj.SchemeSubjectId, Included = subj.IncludeInTotal, Cells = outcomes, Obtained = obtained.Round2(sc.Truncate), OutOf = outOf.Round2(sc.Truncate), Percent = pct,
                Grade = pct == null ? null : GradeFor(sc, pct.Value), Incomplete = incomplete, Failed = failed
            };
            return (sr, obtained, outOf, failGrade);
        }

        // ------------------------------------------------------------------ the year
        private static StudentResult Year(CalcScheme sc, StudentResult res, Dictionary<(long Term, long Subject), (SubjectResult R, Rat Obtained, Rat OutOf)> perTermSubject)
        {
            var y = res.Year;
            if (!sc.IsMarksMode) return res;
            var yearTotal = new Acc(); var failed = new List<long>(); var anyIncomplete = false;
            foreach (var subj in sc.Subjects.OrderBy(s => s.DisplayOrder))
            {
                var parts = sc.Terms.Where(t => perTermSubject.ContainsKey((t.TermId, subj.SchemeSubjectId))).Select(t => (Term: t, Part: perTermSubject[(t.TermId, subj.SchemeSubjectId)])).ToList();
                if (parts.Count == 0) continue;
                Rat obtained, outOf;
                if (sc.WeightedYear)
                {
                    // sum(termWeight x subject%) / sum(termWeight), reported out of 100; a term where the subject counts nothing (all exempt) takes no weight
                    Rat num = Rat.Zero, wsum = Rat.Zero;
                    foreach (var (term, p) in parts) { if (p.OutOf.IsZero) continue; var w = Rat.From(term.YearWeight ?? 1m); num += w * p.Obtained * Rat.From(100m) / p.OutOf; wsum += w; }
                    obtained = wsum.IsZero ? Rat.Zero : num / wsum; outOf = wsum.IsZero ? Rat.Zero : Rat.From(100m);
                }
                else { obtained = Rat.Zero; outOf = Rat.Zero; foreach (var (_, p) in parts) { obtained += p.Obtained; outOf += p.OutOf; } }
                var incomplete = parts.Any(p => p.Part.R.Incomplete);
                var pct = Pct(obtained, outOf, sc.Truncate);
                var fail = subj.IncludeInTotal && pct != null && pct < sc.SubjectPassPercent;
                y.Subjects.Add(new SubjectResult
                {
                    SchemeSubjectId = subj.SchemeSubjectId, Included = subj.IncludeInTotal, Cells = new List<CellOutcome>(), Obtained = obtained.Round2(sc.Truncate), OutOf = outOf.Round2(sc.Truncate), Percent = pct,
                    Grade = pct == null ? null : GradeFor(sc, pct.Value), Incomplete = incomplete, Failed = fail
                });
                if (subj.IncludeInTotal) { if (incomplete) anyIncomplete = true; if (fail) failed.Add(subj.SchemeSubjectId); }
            }
            Rat tObt, tOut;
            if (sc.WeightedYear)
            {
                Rat num = Rat.Zero, wsum = Rat.Zero;
                foreach (var t in res.Terms)
                {
                    var term = sc.Terms.First(x => x.TermId == t.TermId);
                    var tOutOf = TermOutOf(sc, perTermSubject, t.TermId); if (tOutOf.OutOf.IsZero) continue;
                    var w = Rat.From(term.YearWeight ?? 1m); num += w * tOutOf.Obtained * Rat.From(100m) / tOutOf.OutOf; wsum += w;
                }
                tObt = wsum.IsZero ? Rat.Zero : num / wsum; tOut = wsum.IsZero ? Rat.Zero : Rat.From(100m);
            }
            else { tObt = Rat.Zero; tOut = Rat.Zero; foreach (var t in res.Terms) { var a = TermOutOf(sc, perTermSubject, t.TermId); tObt += a.Obtained; tOut += a.OutOf; } }
            var overall = Pct(tObt, tOut, sc.Truncate);
            var yr = new YearResult
            {
                Subjects = y.Subjects, Obtained = tObt.Round2(sc.Truncate), OutOf = tOut.Round2(sc.Truncate), Percent = overall, Grade = overall == null ? null : GradeFor(sc, overall.Value), FailedSubjects = failed,
                Status = anyIncomplete ? ResultStatuses.Incomplete : (overall != null && overall < sc.OverallPassPercent) || failed.Count > 0 ? ResultStatuses.Fail : ResultStatuses.Pass,
                Ratio = tOut.IsZero ? Rat.Zero : tObt / tOut
            };
            return new StudentResult { StudentId = res.StudentId, SectionId = res.SectionId, ClassId = res.ClassId, Terms = res.Terms, Year = yr };
        }

        private static (Rat Obtained, Rat OutOf) TermOutOf(CalcScheme sc, Dictionary<(long Term, long Subject), (SubjectResult R, Rat Obtained, Rat OutOf)> map, long termId)
        {
            Rat o = Rat.Zero, d = Rat.Zero;
            foreach (var subj in sc.Subjects) if (subj.IncludeInTotal && map.TryGetValue((termId, subj.SchemeSubjectId), out var p)) { o += p.Obtained; d += p.OutOf; }
            return (o, d);
        }

        // ------------------------------------------------------------------ rank: PASS students only, exact ratio, competition ranking
        private static void Rank(CalcScheme sc, IReadOnlyList<CalcStudent> students, Dictionary<string, StudentResult> results)
        {
            var groups = students.GroupBy(s => sc.RankByClass ? s.ClassId : s.SectionId);
            foreach (var g in groups)
            {
                foreach (var termId in sc.Terms.Select(t => t.TermId))
                    Assign(g.Select(s => results[s.StudentId]).Select(r => (Status: r.Terms.First(t => t.TermId == termId).Status, Ratio: r.Terms.First(t => t.TermId == termId).Ratio, Set: (Action<int, int>)((rank, n) => { var t = r.Terms.First(x => x.TermId == termId); t.Rank = rank; t.RankedCount = n; }))).ToList());
                Assign(g.Select(s => results[s.StudentId]).Select(r => (Status: r.Year.Status, Ratio: r.Year.Ratio, Set: (Action<int, int>)((rank, n) => { r.Year.Rank = rank; r.Year.RankedCount = n; }))).ToList());
            }
        }

        private static void Assign(List<(string Status, Rat Ratio, Action<int, int> Set)> pool)
        {
            var passed = pool.Where(p => p.Status == ResultStatuses.Pass).OrderByDescending(p => p.Ratio, Comparer<Rat>.Create((a, b) => a.CompareTo(b))).ToList();
            var position = 0; Rat? previous = null; var rank = 0;
            foreach (var p in passed)
            {
                position++;
                if (previous == null || p.Ratio.CompareTo(previous.Value) != 0) rank = position;       // 1, 2, 2, 4
                previous = p.Ratio;
                p.Set(rank, passed.Count);
            }
        }
    }
}
