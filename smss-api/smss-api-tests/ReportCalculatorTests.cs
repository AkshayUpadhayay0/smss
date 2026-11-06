using smss_api_service_layer.helper.reportcard;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// The report card calculation engine, checked against the twelve fixtures F1 to F12 of the approved Phase A spec, plus the edge cases that decide a child's result.
    /// Every number here can be recomputed by hand; none of it touches a database.
    /// </summary>
    public class ReportCalculatorTests
    {
        // ------------------------------------------------------------------ small builders
        private static readonly CalcBand[] Cbse =
        {
            new("E", "Needs improvement", 0m, true), new("D", "D", 33m, false), new("C2", "C2", 41m, false), new("C1", "C1", 51m, false),
            new("B2", "B2", 61m, false), new("B1", "B1", 71m, false), new("A2", "A2", 81m, false), new("A1", "A1", 91m, false),
        };

        /// <summary>A scheme with one cell per (term, subject, exam): exams[i] = (max, weight).</summary>
        private static CalcScheme Scheme(int terms, int subjects, (decimal Max, decimal Weight)[] exams, bool truncate = true, bool weighted = false, decimal[]? termWeights = null,
            bool[]? included = null, bool showRank = false, bool byClass = false, decimal pass = 33m, decimal overallPass = 33m)
        {
            var cells = new List<CalcCell>(); long es = 1;
            for (var t = 1; t <= terms; t++)
                for (var s = 1; s <= subjects; s++)
                    for (var e = 0; e < exams.Length; e++)
                        cells.Add(new CalcCell(es++, t, t * 100 + e, s, true, exams[e].Max, exams[e].Weight, Array.Empty<long>()));
            return new CalcScheme
            {
                IsMarksMode = true, WeightedYear = weighted, Truncate = truncate, ShowRank = showRank, RankByClass = byClass, SubjectPassPercent = pass, OverallPassPercent = overallPass, Bands = Cbse,
                Terms = Enumerable.Range(1, terms).Select(t => new CalcTerm(t, t, $"Term {t}", termWeights?[t - 1])).ToList(),
                Subjects = Enumerable.Range(1, subjects).Select(s => new CalcSubject(s, $"Subject {s}", included?[s - 1] ?? true, s)).ToList(),
                Cells = cells
            };
        }

        /// <summary>The exam-subject id of (term, subject, exam index) in a scheme built by Scheme().</summary>
        private static long Es(CalcScheme sc, int term, int subject, int exam) => sc.Cells.First(c => c.TermId == term && c.SchemeSubjectId == subject && c.ExamId == term * 100 + exam).ExamSubjectId;

        private static CalcStudent Student(CalcScheme sc, string id, Func<long, long, int, CalcEntry?> entry, long section = 1)
        {
            var s = new CalcStudent { StudentId = id, SectionId = section, ClassId = 1 };
            foreach (var c in sc.Cells) { var e = entry(c.TermId, c.SchemeSubjectId, (int)(c.ExamId % 100)); if (e != null) s.Entries[(c.ExamSubjectId, null)] = e; }
            return s;
        }

        private static CalcEntry M(decimal marks) => new(EntryKind.Entered, marks);
        private static StudentResult Run(CalcScheme sc, CalcStudent st) => ReportCalculator.Calculate(sc, new[] { st })[st.StudentId];

        // ------------------------------------------------------------------ F1 .. F12
        [Fact]
        public void F1_Class1_subject_9_9_78_with_weights_10_10_80_is_96_out_of_100()
        {
            var sc = Scheme(1, 1, new[] { (10m, 10m), (10m, 10m), (80m, 80m) });
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(new[] { 9m, 9m, 78m }[e])));
            var subj = r.Terms[0].Subjects[0];
            Assert.Equal(96m, subj.Obtained);
            Assert.Equal(100m, subj.OutOf);
            Assert.Equal(96.00m, subj.Percent);
        }

        [Fact]
        public void F2_Grand_total_of_two_terms_1023_of_1100_and_1037_of_1100_is_2060_of_2200()
        {
            var sc = Scheme(2, 1, new[] { (1100m, 1100m) });
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(t == 1 ? 1023m : 1037m)));
            Assert.Equal(1023m, r.Terms[0].Obtained); Assert.Equal(1100m, r.Terms[0].OutOf);
            Assert.Equal(1037m, r.Terms[1].Obtained);
            Assert.Equal(2060m, r.Year.Obtained);
            Assert.Equal(2200m, r.Year.OutOf);
        }

        [Fact]
        public void F3_93_636_is_93_63_when_truncating_and_93_64_when_rounding()
        {
            var tr = Scheme(2, 1, new[] { (1100m, 1100m) }, truncate: true);
            var hu = Scheme(2, 1, new[] { (1100m, 1100m) }, truncate: false);
            Assert.Equal(93.63m, Run(tr, Student(tr, "S", (t, s, e) => M(t == 1 ? 1023m : 1037m))).Year.Percent);
            Assert.Equal(93.64m, Run(hu, Student(hu, "S", (t, s, e) => M(t == 1 ? 1023m : 1037m))).Year.Percent);
        }

        [Fact]
        public void F4_Absent_scores_zero_keeps_its_weight_and_is_79_of_100()
        {
            var sc = Scheme(1, 1, new[] { (10m, 10m), (10m, 10m), (80m, 80m) });
            var r = Run(sc, Student(sc, "S", (t, s, e) => e == 0 ? new CalcEntry(EntryKind.Absent) : M(new[] { 0m, 9m, 70m }[e])));
            var subj = r.Terms[0].Subjects[0];
            Assert.Equal(79m, subj.Obtained); Assert.Equal(100m, subj.OutOf);
            Assert.Equal(CellStates.Absent, subj.Cells[0].State);
            Assert.Equal(0m, subj.Cells[0].Contribution);
        }

        [Fact]
        public void F5_Exempt_removes_the_subject_from_the_total_nothing_counted()
        {
            var sc = Scheme(1, 11, new[] { (100m, 100m) });           // a 1100-mark term
            var computer = 11L;
            var r = Run(sc, Student(sc, "S", (t, s, e) => s == computer ? new CalcEntry(EntryKind.Exempt) : M(90m)));
            Assert.Equal(900m, r.Terms[0].Obtained);
            Assert.Equal(1000m, r.Terms[0].OutOf);                     // out of 1000, not 1100
            Assert.Equal(90.00m, r.Terms[0].Percent);
            var c = r.Terms[0].Subjects.Single(x => x.SchemeSubjectId == computer);
            Assert.Equal(0m, c.OutOf); Assert.Null(c.Percent); Assert.False(c.Failed);
            Assert.Equal(CellStates.Exempt, c.Cells[0].State);
        }

        [Fact]
        public void F6_Fractional_7_of_8_at_weight_10_is_exactly_8_75()
        {
            var sc = Scheme(1, 1, new[] { (8m, 10m) });
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(7m)));
            Assert.Equal(8.75m, r.Terms[0].Subjects[0].Obtained);
            Assert.Equal(8.75m, r.Terms[0].Subjects[0].Cells[0].Contribution);
            Assert.Equal(87.50m, r.Terms[0].Percent);
        }

        [Fact]
        public void F7_A_subject_outside_the_total_is_shown_but_never_counted_ranked_or_failed()
        {
            var sc = Scheme(1, 3, new[] { (100m, 100m) }, included: new[] { true, true, false }, showRank: true);
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(s == 3 ? 5m : 80m)));          // Drawing (3) scores 5 of 100: would fail if it counted
            Assert.Equal(160m, r.Terms[0].Obtained); Assert.Equal(200m, r.Terms[0].OutOf);
            Assert.Equal(80.00m, r.Terms[0].Percent);
            var drawing = r.Terms[0].Subjects.Single(x => x.SchemeSubjectId == 3);
            Assert.Equal(5m, drawing.Obtained); Assert.Equal(5.00m, drawing.Percent); Assert.False(drawing.Failed);
            Assert.Equal(ResultStatuses.Pass, r.Terms[0].Status);
            Assert.Empty(r.Terms[0].FailedSubjects);
        }

        [Theory]
        [InlineData(91.00, "A1", true)]
        [InlineData(90.99, "A2", true)]
        [InlineData(90.999, "A2", true)]       // TRUNCATE: prints 90.99
        [InlineData(90.999, "A1", false)]      // HALF_UP: prints 91.00
        public void F8_The_lower_band_edge_is_inclusive_and_the_grade_comes_from_the_printed_percentage(double pct, string grade, bool truncate)
        {
            var sc = Scheme(1, 1, new[] { (1000m, 1000m) }, truncate: truncate);
            var marks = (decimal)pct * 10m;                     // out of 1000, so 90.999% is 909.99 marks
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(marks)));
            Assert.Equal(grade, r.Terms[0].Grade);
        }

        [Fact]
        public void F9_Rounding_can_change_the_pass_verdict_32_999_percent()
        {
            var tr = Scheme(1, 1, new[] { (100m, 100m) }, truncate: true);
            var hu = Scheme(1, 1, new[] { (100m, 100m) }, truncate: false);
            var failed = Run(tr, Student(tr, "S", (t, s, e) => M(32.999m)));
            var passed = Run(hu, Student(hu, "S", (t, s, e) => M(32.999m)));
            Assert.Equal(32.99m, failed.Terms[0].Percent); Assert.Equal(ResultStatuses.Fail, failed.Terms[0].Status);
            Assert.Equal(33.00m, passed.Terms[0].Percent); Assert.Equal(ResultStatuses.Pass, passed.Terms[0].Status);
        }

        [Fact]
        public void F10_Ranking_95_91_5_91_5_88_gives_1_2_2_4_and_the_incomplete_student_has_none()
        {
            var sc = Scheme(1, 1, new[] { (100m, 100m) }, showRank: true);
            var pcts = new Dictionary<string, decimal> { ["A"] = 95m, ["B"] = 91.5m, ["C"] = 91.5m, ["D"] = 88m };
            var students = pcts.Select(p => Student(sc, p.Key, (t, s, e) => M(p.Value))).ToList();
            students.Add(Student(sc, "X", (t, s, e) => null));                                       // nothing entered: INCOMPLETE
            var res = ReportCalculator.Calculate(sc, students);
            Assert.Equal(1, res["A"].Terms[0].Rank); Assert.Equal(2, res["B"].Terms[0].Rank); Assert.Equal(2, res["C"].Terms[0].Rank); Assert.Equal(4, res["D"].Terms[0].Rank);
            Assert.Null(res["X"].Terms[0].Rank);
            Assert.Equal(ResultStatuses.Incomplete, res["X"].Terms[0].Status);
            Assert.Equal(4, res["A"].Terms[0].RankedCount);
            Assert.Equal(4, res["D"].Year.Rank);
        }

        [Fact]
        public void F11_Weighted_year_with_term_weights_40_60_and_term_percentages_80_and_90_is_86()
        {
            var sc = Scheme(2, 1, new[] { (100m, 100m) }, weighted: true, termWeights: new[] { 40m, 60m });
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(t == 1 ? 80m : 90m)));
            Assert.Equal(86.00m, r.Year.Percent);
            Assert.Equal(86.00m, r.Year.Subjects[0].Percent);
            Assert.Equal(100m, r.Year.OutOf);
        }

        [Fact]
        public void F12_One_missing_cell_makes_the_student_INCOMPLETE_not_ranked_and_never_PASS_or_FAIL()
        {
            var sc = Scheme(1, 2, new[] { (50m, 50m), (50m, 50m) }, showRank: true);
            var s = Student(sc, "S", (t, sub, e) => sub == 2 && e == 1 ? null : M(45m));
            var r = ReportCalculator.Calculate(sc, new[] { s, Student(sc, "T", (t, sub, e) => M(40m)) });
            Assert.Equal(ResultStatuses.Incomplete, r["S"].Terms[0].Status);
            Assert.Equal(ResultStatuses.Incomplete, r["S"].Year.Status);
            Assert.Null(r["S"].Terms[0].Rank); Assert.Null(r["S"].Year.Rank);
            Assert.Equal(CellStates.Missing, r["S"].Terms[0].Subjects[1].Cells[1].State);
            Assert.Equal(1, r["T"].Terms[0].Rank);                       // the complete student is ranked alone
        }

        // ------------------------------------------------------------------ the rules around them
        [Fact]
        public void Three_thirds_add_up_to_exactly_the_whole_so_a_truncating_school_loses_no_mark()
        {
            // max 30, weight 10, marks 10 in each of three exams: every part is 3.333... and the total must be exactly 10.00, not 9.99
            var sc = Scheme(1, 1, new[] { (30m, 10m), (30m, 10m), (30m, 10m) }, truncate: true);
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(10m)));
            Assert.Equal(10m, r.Terms[0].Subjects[0].Obtained);
            Assert.Equal(30m, r.Terms[0].Subjects[0].OutOf);
            Assert.Equal(33.33m, r.Terms[0].Subjects[0].Percent);
        }

        [Fact]
        public void Multiply_before_divide_keeps_the_exact_value_1_over_3_of_a_weight()
        {
            var sc = Scheme(1, 1, new[] { (3m, 10m) }, truncate: true);
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(1m)));
            Assert.Equal(3.33m, r.Terms[0].Subjects[0].Obtained);       // 1 x 10 / 3 = 3.333..., shown truncated
            Assert.Equal(33.33m, r.Terms[0].Percent);                    // from the exact 10/3 out of 10, not from 3.33
        }

        [Fact]
        public void A_subject_under_its_pass_mark_fails_the_student_even_when_the_overall_is_high_and_is_listed()
        {
            var sc = Scheme(1, 3, new[] { (100m, 100m) });
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(s == 2 ? 20m : 100m)));
            Assert.Equal(73.33m, r.Terms[0].Percent);          // high enough overall (pass mark 33), yet a subject at 20% still fails the student
            Assert.Equal(ResultStatuses.Fail, r.Terms[0].Status);
            Assert.Equal(new long[] { 2 }, r.Terms[0].FailedSubjects);
        }

        [Fact]
        public void Overall_under_the_overall_pass_mark_fails_even_when_every_subject_passes()
        {
            var sc = Scheme(1, 2, new[] { (100m, 100m) }, pass: 30m, overallPass: 40m);
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(35m)));
            Assert.Empty(r.Terms[0].FailedSubjects);
            Assert.Equal(ResultStatuses.Fail, r.Terms[0].Status);
        }

        [Fact]
        public void A_year_subject_failure_uses_the_year_percentage()
        {
            var sc = Scheme(2, 1, new[] { (100m, 100m) });
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(t == 1 ? 20m : 50m)));
            Assert.Equal(ResultStatuses.Fail, r.Terms[0].Status);        // term 1 alone: 20%
            Assert.Equal(ResultStatuses.Pass, r.Terms[1].Status);
            Assert.Equal(35.00m, r.Year.Percent);
            Assert.Equal(ResultStatuses.Pass, r.Year.Status);            // year: 70 of 200 = 35%, over 33
        }

        [Fact]
        public void Rank_goes_to_PASS_students_only_a_failing_student_with_a_high_total_gets_none()
        {
            var sc = Scheme(1, 2, new[] { (100m, 100m) }, showRank: true);
            var res = ReportCalculator.Calculate(sc, new[]
            {
                Student(sc, "FAILS", (t, s, e) => M(s == 1 ? 100m : 10m)),        // 110/200 = 55% but fails subject 2
                Student(sc, "PASS1", (t, s, e) => M(60m)),
                Student(sc, "PASS2", (t, s, e) => M(50m)),
            });
            Assert.Equal(ResultStatuses.Fail, res["FAILS"].Terms[0].Status);
            Assert.Null(res["FAILS"].Terms[0].Rank);
            Assert.Equal(1, res["PASS1"].Terms[0].Rank); Assert.Equal(2, res["PASS2"].Terms[0].Rank);
        }

        [Fact]
        public void Rank_uses_the_exact_ratio_not_the_rounded_percentage()
        {
            // 91.666...% and 91.664% both print 91.66 when truncating, but the first is higher
            var sc = Scheme(1, 1, new[] { (3000m, 3000m) }, showRank: true, truncate: true);
            var res = ReportCalculator.Calculate(sc, new[] { Student(sc, "HIGH", (t, s, e) => M(2750m)), Student(sc, "LOW", (t, s, e) => M(2749.92m)) });
            Assert.Equal(res["HIGH"].Terms[0].Percent, res["LOW"].Terms[0].Percent);
            Assert.Equal(1, res["HIGH"].Terms[0].Rank); Assert.Equal(2, res["LOW"].Terms[0].Rank);
        }

        [Fact]
        public void Rank_scope_section_ranks_each_section_alone_and_class_ranks_them_together()
        {
            var sec = Scheme(1, 1, new[] { (100m, 100m) }, showRank: true, byClass: false);
            var cls = Scheme(1, 1, new[] { (100m, 100m) }, showRank: true, byClass: true);
            CalcStudent[] Build(CalcScheme sc) => new[] { Student(sc, "A1", (t, s, e) => M(90m), 1), Student(sc, "A2", (t, s, e) => M(70m), 1), Student(sc, "B1", (t, s, e) => M(80m), 2) };
            var bySection = ReportCalculator.Calculate(sec, Build(sec)); var byClass = ReportCalculator.Calculate(cls, Build(cls));
            Assert.Equal(1, bySection["B1"].Terms[0].Rank); Assert.Equal(2, bySection["A2"].Terms[0].Rank);
            Assert.Equal(2, byClass["B1"].Terms[0].Rank); Assert.Equal(3, byClass["A2"].Terms[0].Rank);
            Assert.Equal(3, byClass["A1"].Terms[0].RankedCount);
        }

        [Fact]
        public void Without_show_rank_nobody_is_ranked()
        {
            var sc = Scheme(1, 1, new[] { (100m, 100m) }, showRank: false);
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(80m)));
            Assert.Null(r.Terms[0].Rank); Assert.Null(r.Year.Rank);
        }

        [Fact]
        public void Per_exam_weights_can_differ_from_the_marks_a_20_mark_test_counting_for_10()
        {
            var sc = Scheme(1, 1, new[] { (20m, 10m), (80m, 90m) });
            var r = Run(sc, Student(sc, "S", (t, s, e) => M(new[] { 16m, 60m }[e])));
            Assert.Equal(8m + 67.5m, r.Terms[0].Subjects[0].Obtained);       // 16 x 10 / 20 = 8,  60 x 90 / 80 = 67.5
            Assert.Equal(100m, r.Terms[0].Subjects[0].OutOf);
        }

        [Fact]
        public void A_subject_that_is_exempt_in_one_term_only_counts_in_the_other_terms_of_the_year()
        {
            var sc = Scheme(2, 1, new[] { (100m, 100m) });
            var r = Run(sc, Student(sc, "S", (t, s, e) => t == 1 ? new CalcEntry(EntryKind.Exempt) : M(60m)));
            Assert.Equal(60m, r.Year.Obtained); Assert.Equal(100m, r.Year.OutOf);
            Assert.Equal(60.00m, r.Year.Percent);
            Assert.Null(r.Terms[0].Percent);
        }

        [Fact]
        public void Grade_lookup_picks_the_highest_band_whose_lower_edge_is_not_above_the_percentage()
        {
            var sc = Scheme(1, 1, new[] { (100m, 100m) });
            Assert.Equal("E", ReportCalculator.GradeFor(sc, 0m));
            Assert.Equal("E", ReportCalculator.GradeFor(sc, 32.99m));
            Assert.Equal("D", ReportCalculator.GradeFor(sc, 33.00m));
            Assert.Equal("A2", ReportCalculator.GradeFor(sc, 90.99m));
            Assert.Equal("A1", ReportCalculator.GradeFor(sc, 100m));
        }

        // ------------------------------------------------------------------ grade-entry cells
        [Fact]
        public void A_GRADE_cell_in_a_MARKS_scheme_is_display_only_never_summed_ranked_or_failed_and_never_required()
        {
            var cells = new List<CalcCell>
            {
                new(1, 1, 100, 1, true, 100m, 100m, Array.Empty<long>()),
                new(2, 1, 100, 2, false, null, null, Array.Empty<long>()),      // Art: a grade, shown on the card only
            };
            var sc = new CalcScheme { IsMarksMode = true, ShowRank = true, Bands = Cbse, Terms = new[] { new CalcTerm(1, 1, "T1", null) }, Subjects = new[] { new CalcSubject(1, "Maths", true, 1), new CalcSubject(2, "Art", true, 2) }, Cells = cells };
            var given = new CalcStudent { StudentId = "G", SectionId = 1, ClassId = 1, Entries = { [(1, null)] = M(80m), [(2, null)] = new CalcEntry(EntryKind.Entered, null, "E") } };   // E is a FAIL grade
            var absent = new CalcStudent { StudentId = "N", SectionId = 1, ClassId = 1, Entries = { [(1, null)] = M(70m) } };                                                           // no grade for Art at all
            var res = ReportCalculator.Calculate(sc, new[] { given, absent });
            Assert.Equal(80m, res["G"].Terms[0].Obtained); Assert.Equal(100m, res["G"].Terms[0].OutOf);       // the grade added nothing
            Assert.Equal(ResultStatuses.Pass, res["G"].Terms[0].Status);                                       // a failing grade code does not fail a display-only cell
            Assert.Equal("E", res["G"].Terms[0].Subjects[1].Cells[0].Grade);
            Assert.Null(res["G"].Terms[0].Subjects[1].Percent);
            Assert.Equal(ResultStatuses.Pass, res["N"].Terms[0].Status);                                       // a missing grade does not make the student INCOMPLETE
            Assert.Equal(CellStates.Missing, res["N"].Terms[0].Subjects[1].Cells[0].State);
            Assert.Equal(1, res["G"].Terms[0].Rank); Assert.Equal(2, res["N"].Terms[0].Rank);                  // ranked by the marks subject only
        }

        [Fact]
        public void A_GRADES_scheme_needs_every_skill_graded_and_a_failing_grade_fails()
        {
            var bands = new[] { new CalcBand("A", "Excellent", 0m, false), new CalcBand("C", "Needs help", 1m, true) };
            var sc = new CalcScheme
            {
                IsMarksMode = false, Bands = bands, Terms = new[] { new CalcTerm(1, 1, "T1", null) }, Subjects = new[] { new CalcSubject(1, "English", true, 1) },
                Cells = new[] { new CalcCell(1, 1, 100, 1, false, null, null, new long[] { 11, 12 }) }
            };
            CalcStudent S(string id, params (long Skill, CalcEntry E)[] e) { var st = new CalcStudent { StudentId = id, SectionId = 1, ClassId = 1 }; foreach (var (k, v) in e) st.Entries[(1, k)] = v; return st; }
            var res = ReportCalculator.Calculate(sc, new[]
            {
                S("OK", (11, new CalcEntry(EntryKind.Entered, null, "A")), (12, new CalcEntry(EntryKind.Entered, null, "A"))),
                S("HALF", (11, new CalcEntry(EntryKind.Entered, null, "A"))),
                S("BAD", (11, new CalcEntry(EntryKind.Entered, null, "A")), (12, new CalcEntry(EntryKind.Entered, null, "C"))),
                S("EX", (11, new CalcEntry(EntryKind.Exempt)), (12, new CalcEntry(EntryKind.Absent))),
            });
            Assert.Equal(ResultStatuses.Pass, res["OK"].Terms[0].Status);
            Assert.Equal(ResultStatuses.Incomplete, res["HALF"].Terms[0].Status);
            Assert.Equal(ResultStatuses.Fail, res["BAD"].Terms[0].Status);
            Assert.Equal(ResultStatuses.Pass, res["EX"].Terms[0].Status);
            Assert.Null(res["OK"].Terms[0].Percent);                       // no arithmetic in a GRADES scheme
            Assert.Null(res["OK"].Terms[0].Rank);
            Assert.Equal(2, res["OK"].Terms[0].Subjects[0].Cells[0].Skills.Count);
        }

        [Fact]
        public void Calculating_twice_gives_the_same_answer()
        {
            var sc = Scheme(2, 3, new[] { (10m, 10m), (80m, 80m) }, showRank: true);
            var students = Enumerable.Range(1, 8).Select(i => Student(sc, $"S{i}", (t, s, e) => M(Math.Round((e == 0 ? 10m : 80m) * (0.4m + i * 0.07m), 1)))).ToList();
            var a = ReportCalculator.Calculate(sc, students); var b = ReportCalculator.Calculate(sc, students);
            foreach (var id in a.Keys) { Assert.Equal(a[id].Year.Percent, b[id].Year.Percent); Assert.Equal(a[id].Year.Rank, b[id].Year.Rank); Assert.Equal(a[id].Terms[1].Obtained, b[id].Terms[1].Obtained); }
        }
    }
}
