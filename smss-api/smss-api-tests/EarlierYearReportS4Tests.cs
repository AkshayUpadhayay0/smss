using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// "Outstanding from earlier years", step 4 (reports): the class fee report gets a SEPARATE "Earlier years" column (never added to the year's own figures), and a student who left but still owes
    /// money stays on the report flagged Left (until everything is paid). [DbFact]: needs SMSS_TEST_PG (no migration).
    /// </summary>
    public class EarlierYearReportS4Tests
    {
        [DbFact]
        public async Task The_class_report_has_a_separate_earlier_years_column_and_a_left_student_with_dues_stays_flagged()
        {
            using var db = new TaxDb(); var w = EarlierYearsKit.Build(db, "ZEI");
            var r = (await db.Reports().GetClassReportAsync("ZEI", w.C1, w.Y2, null, true, default)).Data!;
            var repeated = r.Students.Single(x => x.StudentId == w.R);
            Assert.Equal((1000m, 500m, false), (repeated.Outstanding, repeated.EarlierYears, repeated.Left));      // the year's own figure is untouched
            var fresh = r.Students.Single(x => x.StudentId == w.N);
            Assert.Equal((0m, false), (fresh.EarlierYears, fresh.Left));
            var left = r.Students.Single(x => x.StudentId == w.L);                                                  // left in 2026-27 but still owes 400 + 200: listed, flagged
            Assert.True(left.Left); Assert.Equal((400m, 200m), (left.Outstanding, left.EarlierYears));
            Assert.DoesNotContain(r.Students, x => x.StudentId == w.P);                                             // ZP is in Class 3 this year
            Assert.Equal(700m, r.TotalEarlierYears);
            Assert.Equal(2400m, r.TotalOutstanding);                                                                // earlier years are NOT in the year's total

            // once everything is paid the left student drops off (they are not counted in the class any more)
            db.Exec($"update tb_student_fee_dues set amount_paid = amount_due where student_id = '{w.L}'");
            var after = (await db.Reports().GetClassReportAsync("ZEI", w.C1, w.Y2, null, true, default)).Data!;
            Assert.DoesNotContain(after.Students, x => x.StudentId == w.L);
        }

        [DbFact]
        public async Task A_past_year_report_lists_the_class_as_it_was_and_another_schools_class_is_not_found()
        {
            using var db = new TaxDb(); var w = EarlierYearsKit.Build(db, "ZEJ"); var other = EarlierYearsKit.Build(db, "ZEK", "K");
            var past = (await db.Reports().GetClassReportAsync("ZEJ", w.C1, w.Y1, null, true, default)).Data!;
            Assert.Equal(new[] { w.L, w.P, w.R }.OrderBy(x => x), past.Students.Select(s => s.StudentId).OrderBy(x => x));   // history: everyone enrolled then
            Assert.All(past.Students, s => { Assert.False(s.Left); Assert.Equal(0m, s.EarlierYears); });                      // nothing earlier than the first year
            Assert.Equal(404, (await db.Reports().GetClassReportAsync("ZEJ", other.C1, w.Y1, null, true, default)).StatusCode);
        }
    }
}
