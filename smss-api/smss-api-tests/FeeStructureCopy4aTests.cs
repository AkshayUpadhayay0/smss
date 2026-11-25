using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using smss_api.Controllers;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Stage 4a-1 (no migration): the fee structures as a part of the Year Copy. Per class a structure is copied whole or skipped with a visible reason; every line starts on the target year's first day; amounts change only
    /// by the one uniform percentage rounded to the rupee; a class that already has a structure in the target is never touched; a second run creates nothing; a preview writes nothing. [DbFact]: needs SMSS_TEST_PG.
    /// </summary>
    public class FeeStructureCopy4aTests
    {
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";
        private const string Inactive = "(select sid from lut_status where sname='Inactive' and stype='general status')";
        private static readonly string[] ClassesAndFees = { "CLASSES_SECTIONS", "FEE_STRUCTURES" };
        private static readonly string[] FeesOnly = { "FEE_STRUCTURES" };

        private static YearCopyService Copy(TaxDb db) => new(new YearCopyRepository(db.Ctx()), NullLogger<YearCopyService>.Instance);
        private static YearCopyRequest Req(decimal pct = 0, bool confirm = true, string[]? parts = null) => new() { Parts = (parts ?? ClassesAndFees).ToList(), Confirm = confirm, FeeIncreasePercent = pct };

        private sealed class World
        {
            public string School = "";
            public long Y1, Y2, C1, C2, C3, C4;
            public long Tuition, Transport, Old;
            public long Monthly, Annual, OneTime;
        }

        /// <summary>
        /// 2026-27 (source, current): Class 1 (structure: Tuition 1800 monthly due on the 10th from April, Annual 3000 and Sports 150 from 1 November), Class 2 (Tuition 500), Class 3 (an INACTIVE structure),
        /// Class 4 (a structure whose only line is an inactive fee type "Old"). 2027-28 (target) starts empty; classes are offered there by the classes-and-sections copy.
        /// </summary>
        private static async Task<World> BuildAsync(TaxDb db, string id)
        {
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{id}', '{id}-C', 'Fee copy test {id}', {Active})");
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{id}', '2026-27', '2026-04-01', '2027-03-31', true, {Active}), ('{id}', '2027-28', '2027-04-01', '2028-03-31', false, {Active})");
            var w = new World { School = id };
            w.Y1 = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}' and year_name='2026-27'");
            w.Y2 = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}' and year_name='2027-28'");
            var classes = new ClassService(new ClassRepository(db.Ctx()), NullLogger<ClassService>.Instance);
            long C(string n, int o) => ((ClassResponse)classes.CreateAsync(id, new CreateClassRequest { ClassName = n, SequenceOrder = o }).Result.Data!).ClassId;
            (w.C1, w.C2, w.C3, w.C4) = (C("Class 1", 1), C("Class 2", 2), C("Class 3", 3), C("Class 4", 4));
            w.Monthly = db.Scalar<long>("select frequency_id from lut_fee_frequency where frequency_code='MONTHLY'");
            w.Annual = db.Scalar<long>("select frequency_id from lut_fee_frequency where frequency_code='ANNUAL'");
            w.OneTime = db.Scalar<long>("select frequency_id from lut_fee_frequency where frequency_code='ONE_TIME'");
            long Type(string n, string code, bool active = true) => db.Scalar<long>($"insert into tb_fee_types (school_id, fee_type_name, fee_type_code, isactive) values ('{id}', '{n}', '{code}', {active.ToString().ToLower()}) returning fee_type_id");
            w.Tuition = Type("Tuition Fee", "TUI");
            var annual = Type("Annual Charges", "ANN");
            var sports = Type("Sports Fee", "SPO");
            w.Old = Type("Old Fee", "OLD", false);
            w.Transport = annual;
            long Structure(long cls, string name, bool active = true) => db.Scalar<long>($"insert into tb_fee_structures (school_id, academic_year_id, class_id, structure_name, isactive) values ('{id}', {w.Y1}, {cls}, '{name}', {active.ToString().ToLower()}) returning fee_structure_id");
            void Line(long s, long type, long freq, string amount, string? dueDay, string from) =>
                db.Exec($"insert into tb_fee_structure_details (fee_structure_id, fee_type_id, frequency_id, amount, due_day, effective_from) values ({s}, {type}, {freq}, {amount}, {dueDay ?? "null"}, '{from}')");
            var s1 = Structure(w.C1, "Class 1 Fee Structure 2026-27");
            Line(s1, w.Tuition, w.Monthly, "1800", "10", "2026-04-01"); Line(s1, annual, w.Annual, "3000", null, "2026-04-01"); Line(s1, sports, w.Monthly, "150", "5", "2026-11-01");
            Line(s1, w.Old, w.Annual, "999", null, "2026-04-01");   // an inactive fee type: this line is left out
            var s2 = Structure(w.C2, "Class 2 Fee Structure 2026-27");
            Line(s2, w.Tuition, w.Monthly, "500", "10", "2026-04-01"); Line(s2, annual, w.OneTime, "333", null, "2026-04-01"); Line(s2, sports, w.Monthly, "0", "5", "2026-04-01");
            var s3 = Structure(w.C3, "Class 3 Fee Structure 2026-27", active: false);
            Line(s3, w.Tuition, w.Monthly, "700", "10", "2026-04-01");
            var s4 = Structure(w.C4, "Class 4 Fee Structure 2026-27");
            Line(s4, w.Old, w.Annual, "100", null, "2026-04-01");
            await Task.CompletedTask;
            return w;
        }

        /// <summary>The usual order for a new year: the offerings first, then the fees.</summary>
        private static async Task OfferAsync(TaxDb db, World w) => Assert.True((await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(parts: new[] { "CLASSES_SECTIONS" }), default)).Status);

        private static string Fingerprint(TaxDb db, string school) => db.Scalar<string>($@"select md5(coalesce(string_agg(x, '|' order by x), '') || (select count(*)::text from tb_year_copy_log where school_id='{school}')) from (
            select s::text x from tb_fee_structures s where school_id='{school}' union all select d::text from tb_fee_structure_details d join tb_fee_structures s using(fee_structure_id) where s.school_id='{school}') z");

        private static int Created(YearCopyResultDto r, string part) => r.Parts.Single(p => p.Part == part).Created;

        // ------------------------------------------------------------------------------------------------ what is copied
        [DbFact]
        public async Task The_structures_of_the_offered_classes_are_copied_with_every_line_starting_on_the_first_day_of_the_target_year_and_the_source_is_untouched()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZFC1");
            await OfferAsync(db, w);
            var source = db.Scalar<string>($"select md5(string_agg(d::text, '|' order by d.fee_structure_detail_id)) from tb_fee_structure_details d join tb_fee_structures s using(fee_structure_id) where s.academic_year_id={w.Y1}");
            var r = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default);
            Assert.True(r.Status, r.Message);
            Assert.Equal(2, r.Data!.Parts.Single(p => p.Part == "FEE_STRUCTURES").Details.Single(d => d.Kind == "fee structures").Count);   // Class 1 and Class 2
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_fee_structures where academic_year_id={w.Y2}"));
            // Class 1: Tuition, Annual and Sports are copied; the inactive fee type's line is left out
            Assert.Equal(3, db.Scalar<int>($"select count(*) from tb_fee_structure_details d join tb_fee_structures s using(fee_structure_id) where s.academic_year_id={w.Y2} and s.class_id={w.C1}"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_fee_structure_details d join tb_fee_structures s using(fee_structure_id) where s.academic_year_id={w.Y2} and d.effective_from <> date '2027-04-01'"));   // all lines start on the target year's first day, even Sports (1 November last year)
            Assert.Equal(10, db.Scalar<int>($"select d.due_day from tb_fee_structure_details d join tb_fee_structures s using(fee_structure_id) where s.academic_year_id={w.Y2} and s.class_id={w.C1} and d.fee_type_id={w.Tuition}"));
            Assert.Equal(1800m, db.Scalar<decimal>($"select d.amount from tb_fee_structure_details d join tb_fee_structures s using(fee_structure_id) where s.academic_year_id={w.Y2} and s.class_id={w.C1} and d.fee_type_id={w.Tuition}"));
            Assert.Equal(w.Monthly, db.Scalar<long>($"select d.frequency_id from tb_fee_structure_details d join tb_fee_structures s using(fee_structure_id) where s.academic_year_id={w.Y2} and s.class_id={w.C1} and d.fee_type_id={w.Tuition}"));
            Assert.Equal("Class 1 Fee Structure 2027-28", db.Scalar<string>($"select structure_name from tb_fee_structures where academic_year_id={w.Y2} and class_id={w.C1}"));   // the year in the name follows
            Assert.True(db.Scalar<bool>($"select isactive from tb_fee_structures where academic_year_id={w.Y2} and class_id={w.C1}"));
            Assert.Equal(source, db.Scalar<string>($"select md5(string_agg(d::text, '|' order by d.fee_structure_detail_id)) from tb_fee_structure_details d join tb_fee_structures s using(fee_structure_id) where s.academic_year_id={w.Y1}"));   // the source year is exactly as it was
        }

        [DbFact]
        public async Task A_uniform_percentage_is_added_to_every_amount_and_rounded_to_the_rupee_and_the_preview_shows_old_and_new()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZFC2");
            await OfferAsync(db, w);
            var preview = await Copy(db).PreviewAsync(w.School, w.Y2, w.Y1, Req(5, false), default);
            var part = preview.Data!.Parts.Single(p => p.Part == "FEE_STRUCTURES");
            Assert.Contains("Class 1 - Tuition Fee: 1800.00 -> 1890", part.Samples);
            Assert.Contains("Class 2 - Annual Charges: 333.00 -> 350", part.Samples);   // 349.65
            Assert.Contains("Class 2 - Sports Fee: 0.00 -> 0", part.Samples);
            Assert.Contains(part.Notes, n => n.Kind.Contains("raised by 5"));
            var r = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(5), default);
            Assert.True(r.Status, r.Message);
            decimal Amount(long cls, long type) => db.Scalar<decimal>($"select d.amount from tb_fee_structure_details d join tb_fee_structures s using(fee_structure_id) where s.academic_year_id={w.Y2} and s.class_id={cls} and d.fee_type_id={type}");
            Assert.Equal(1890m, Amount(w.C1, w.Tuition));
            Assert.Equal(3150m, Amount(w.C1, w.Transport));   // 3000 x 1.05
            Assert.Equal(525m, Amount(w.C2, w.Tuition));
            Assert.Equal(350m, Amount(w.C2, w.Transport));
            Assert.Equal(158m, db.Scalar<decimal>($"select d.amount from tb_fee_structure_details d join tb_fee_structures s using(fee_structure_id) join tb_fee_types t using(fee_type_id) where s.academic_year_id={w.Y2} and s.class_id={w.C1} and t.fee_type_code='SPO'"));   // 157.5 rounds up
        }

        // ------------------------------------------------------------------------------------------------ skipped with a visible reason
        [DbFact]
        public async Task A_class_that_is_not_offered_is_inactive_or_has_an_inactive_structure_or_only_inactive_fee_types_is_skipped_with_the_reason()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZFC3");
            await OfferAsync(db, w);
            db.Exec($"delete from tb_academic_year_sections where academic_year_id={w.Y2} and class_id={w.C2}");
            db.Exec($"delete from tb_academic_year_classes where academic_year_id={w.Y2} and class_id={w.C2}");   // Class 2 is not offered in the target year
            var r = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(parts: FeesOnly), default);
            var part = r.Data!.Parts.Single(p => p.Part == "FEE_STRUCTURES");
            Assert.Equal(1, part.Details.Single(d => d.Kind == "fee structures").Count);
            string[] Reason(string why) => part.Skipped.Single(s => s.Reason == why).Examples.ToArray();
            Assert.Equal(new[] { "Class 2" }, Reason("The class is not offered in the target year"));
            Assert.Contains("Class 3", Reason("The fee structure is inactive"));
            Assert.Equal(new[] { "Class 4" }, Reason("The structure has no line with an active fee type"));
            Assert.Equal(new[] { "Old Fee in Class 1" }, Reason("The fee type is inactive"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_fee_structures where academic_year_id={w.Y2} and class_id in ({w.C2},{w.C3},{w.C4})"));
            // a class made inactive after the offerings were copied
            db.Exec($"update tb_classes set status_id={Inactive} where class_id={w.C1}");
            db.Exec($"delete from tb_fee_structures where academic_year_id={w.Y2}");
            var again = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(parts: FeesOnly), default);
            Assert.Contains("Class 1", again.Data!.Parts.Single(p => p.Part == "FEE_STRUCTURES").Skipped.Single(s => s.Reason == "The class is inactive").Examples);
        }

        // ------------------------------------------------------------------------------------------------ never overwrite, never duplicate
        [DbFact]
        public async Task A_class_that_already_has_a_structure_in_the_target_is_never_touched_and_a_second_run_creates_nothing()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZFC4");
            await OfferAsync(db, w);
            var own = db.Scalar<long>($"insert into tb_fee_structures (school_id, academic_year_id, class_id, structure_name, isactive) values ('{w.School}', {w.Y2}, {w.C1}, 'My own Class 1 fees', true) returning fee_structure_id");
            db.Exec($"insert into tb_fee_structure_details (fee_structure_id, fee_type_id, frequency_id, amount, due_day, effective_from) values ({own}, {w.Tuition}, {w.Monthly}, 2222, 7, '2027-06-01')");
            var ownBefore = db.Scalar<string>($"select md5(string_agg(x, '|' order by x)) from (select s::text x from tb_fee_structures s where fee_structure_id={own} union all select d::text from tb_fee_structure_details d where fee_structure_id={own}) z");
            var first = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(10), default);
            Assert.True(first.Status, first.Message);
            var part = first.Data!.Parts.Single(p => p.Part == "FEE_STRUCTURES");
            Assert.Equal(1, part.SkippedExisting);   // Class 1
            Assert.Equal(1, part.Details.Single(d => d.Kind == "fee structures").Count);   // only Class 2 was made
            Assert.Equal(ownBefore, db.Scalar<string>($"select md5(string_agg(x, '|' order by x)) from (select s::text x from tb_fee_structures s where fee_structure_id={own} union all select d::text from tb_fee_structure_details d where fee_structure_id={own}) z"));   // not merged, not repriced
            var before = Fingerprint(db, w.School);
            var second = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(10), default);
            Assert.Equal(0, Created(second.Data!, "FEE_STRUCTURES"));
            Assert.Equal(2, second.Data!.Parts.Single(p => p.Part == "FEE_STRUCTURES").SkippedExisting);
            Assert.Equal(before.Length, Fingerprint(db, w.School).Length);
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_fee_structures where academic_year_id={w.Y2}"));   // no duplicate
        }

        [DbFact]
        public async Task A_preview_writes_nothing_and_says_what_the_real_run_then_does()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZFC5");
            await OfferAsync(db, w);
            var before = Fingerprint(db, w.School);
            var preview = await Copy(db).PreviewAsync(w.School, w.Y2, w.Y1, Req(3, false), default);
            Assert.True(preview.Data!.Preview);
            Assert.Equal(before, Fingerprint(db, w.School));
            var real = await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(3), default);
            Assert.Equal(preview.Data.Parts.Single(p => p.Part == "FEE_STRUCTURES").Details.Select(d => (d.Kind, d.Count)), real.Data!.Parts.Single(p => p.Part == "FEE_STRUCTURES").Details.Select(d => (d.Kind, d.Count)));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_year_copy_log where school_id='{w.School}' and parts::text like '%FEE_STRUCTURES%' and status='COMPLETED'"));   // the real run is logged, the preview is not
        }

        [DbFact]
        public async Task The_increase_must_be_between_0_and_100_the_apply_needs_confirm_and_a_closed_or_same_year_is_refused()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZFC6");
            await OfferAsync(db, w);
            Assert.Equal(400, (await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(-1), default)).StatusCode);
            Assert.Equal(400, (await Copy(db).PreviewAsync(w.School, w.Y2, w.Y1, Req(101, false), default)).StatusCode);
            Assert.Equal(400, (await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(0, false), default)).StatusCode);
            Assert.Equal(400, (await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y1, w.Y1, Req(), default)).StatusCode);
            db.Exec($"update tb_academic_years set closed_at=now() where academic_year_id={w.Y2}");
            Assert.Equal(409, (await Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default)).StatusCode);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_fee_structures where academic_year_id={w.Y2}"));
        }

        [DbFact]
        public async Task Another_schools_years_are_a_404_and_its_structures_are_never_copied()
        {
            using var db = new TaxDb();
            var a = await BuildAsync(db, "ZFC7A");
            var b = await BuildAsync(db, "ZFC7B");
            await OfferAsync(db, a); await OfferAsync(db, b);
            Assert.Equal(404, (await Copy(db).ApplyAsync(a.School, TaxDb.UserA, b.Y2, b.Y1, Req(), default)).StatusCode);   // school A's caller names school B's years
            Assert.Equal(404, (await Copy(db).PreviewAsync(a.School, a.Y2, b.Y1, Req(0, false), default)).StatusCode);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_fee_structures where academic_year_id in ({a.Y2},{b.Y2})"));
            Assert.True((await Copy(db).ApplyAsync(a.School, TaxDb.UserA, a.Y2, a.Y1, Req(), default)).Status);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_fee_structures where academic_year_id={b.Y2}"));   // school B was not touched
        }

        [DbFact]
        public async Task Two_copies_at_the_same_moment_make_each_structure_once()
        {
            using var db = new TaxDb();
            var w = await BuildAsync(db, "ZFC8");
            await OfferAsync(db, w);
            var results = await Task.WhenAll(Task.Run(() => Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default)), Task.Run(() => Copy(db).ApplyAsync(w.School, TaxDb.UserA, w.Y2, w.Y1, Req(), default)));
            Assert.All(results, r => Assert.True(r.Status, r.Message));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_fee_structures where academic_year_id={w.Y2}"));
            Assert.Equal(6, db.Scalar<int>($"select count(*) from tb_fee_structure_details d join tb_fee_structures s using(fee_structure_id) where s.academic_year_id={w.Y2}"));   // Class 1: 3 lines (the inactive fee type's is left out), Class 2: 3 lines
            Assert.Equal(1, results.Count(r => Created(r.Data!, "FEE_STRUCTURES") > 0));   // one of them did the work, the other found it done
        }

        // ------------------------------------------------------------------------------------------------ who may do what
        public static IEnumerable<object[]> RoleCases() => Fake.AllRoles.Concat(new[] { "Driver" }).Select(r => new object[] { r });

        [Theory, MemberData(nameof(RoleCases))]
        public async Task The_School_Admin_applies_the_copy_and_the_School_Admin_or_Principal_previews_it_nobody_else(string role)
        {
            var svc = Fake.Of<IYearCopyService>();
            var c = Fake.WithUser(new AcademicSetupController(Fake.Of<IAcademicSetupService>(), Fake.Of<ISubjectSetupService>(), Fake.Of<IPeriodScheduleService>(), svc), Fake.User("S", 5, role));
            var body = new YearCopyRequest { Parts = new() { "FEE_STRUCTURES" }, Confirm = true, FeeIncreasePercent = 5 };
            var preview = Fake.Code(await c.CopyPreview(2, 1, body, default));
            if (role is "School Admin" or "Principal") Assert.NotEqual(403, preview); else Assert.Equal(403, preview);
            var apply = Fake.Code(await c.CopyApply(2, 1, body, default));
            if (role == "School Admin") Assert.NotEqual(403, apply); else Assert.Equal(403, apply);
            Assert.Equal(role == "School Admin", Fake.ProxyOf(svc).Calls.Contains("ApplyAsync"));
        }
    }
}
