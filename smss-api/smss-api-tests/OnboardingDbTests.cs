using Microsoft.Extensions.Logging.Abstractions;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// The setup checklist against a real PostgreSQL (the scratch server, see TaxTestKit): proves the EF queries translate and that the answer agrees with plain SQL.
    /// Runs only with SMSS_TEST_PG set; otherwise reported as skipped.
    /// </summary>
    public class OnboardingDbTests
    {
        private const string Fresh = "ZZONB0000001", OtherFresh = "ZZONB0000002";

        private static OnboardingService Svc(TaxDb db) => new(new OnboardingRepository(db.Ctx()), NullLogger<OnboardingService>.Instance);
        private static async Task<OnboardingStatusDto> Status(TaxDb db, string school, long user = 0) => (await Svc(db).GetStatusAsync(school, user, default)).Data!;
        private static OnboardingStepDto Step(OnboardingStatusDto d, string code) => d.Steps.Single(x => x.Code == code);
        /// <summary>The first undone step after the password (these tests have no login row for the fresh school).</summary>
        private static string NextAfterPassword(OnboardingStatusDto d) => d.Steps.First(s => !s.Done && s.Code != "PASSWORD_CHANGED").Code;

        /// <summary>The checklist reads what the CURRENT YEAR offers: a class or section made by raw SQL is only 'there' once it is offered.</summary>
        private static void OfferClasses(TaxDb db, string id) =>
            db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) select c.school_id, y.academic_year_id, c.class_id, c.status_id from tb_classes c join tb_academic_years y on y.school_id = c.school_id and y.is_current where c.school_id = '{id}' on conflict do nothing");
        private static void OfferSections(TaxDb db, string id) =>
            db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) select s.school_id, y.academic_year_id, s.class_id, s.section_id, s.status_id from tb_sections s join tb_academic_years y on y.school_id = s.school_id and y.is_current where s.school_id = '{id}' on conflict do nothing");

        private static void NewSchool(TaxDb db, string id) =>
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{id}', '{id}-C', 'Onboarding test {id}', (select sid from lut_status where sname='Active' and stype='general status'))");

        [DbFact]
        public async Task Fresh_school_walks_through_every_step_and_agrees_with_SQL()
        {
            using var db = new TaxDb();
            NewSchool(db, Fresh); NewSchool(db, OtherFresh);
            const string active = "(select sid from lut_status where sname='Active' and stype='general status')";

            var d = await Status(db, Fresh);
            Assert.False(d.IsComplete);
            Assert.Equal("ACADEMIC_YEAR", NextAfterPassword(d));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_academic_years where school_id='{Fresh}'"));

            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{Fresh}', '2026-27', '2026-04-01', '2027-03-31', true, {active})");
            d = await Status(db, Fresh);
            Assert.True(Step(d, "ACADEMIC_YEAR").Done);
            Assert.Equal("IMPORT_MASTERS_OR_CLASSES", NextAfterPassword(d));

            db.Exec($"insert into tb_classes (school_id, class_name, sequence_order, status_id) values ('{Fresh}', 'Class 1', 1, {active}), ('{Fresh}', 'Class 2', 2, {active})");
            OfferClasses(db, Fresh);
            d = await Status(db, Fresh);
            Assert.True(Step(d, "IMPORT_MASTERS_OR_CLASSES").Done);
            Assert.Equal("SCHOOL_SETUP", NextAfterPassword(d));
            Assert.Contains("Class 1", Step(d, "SCHOOL_SETUP").Detail);

            db.Exec($"insert into tb_sections (school_id, class_id, section_name, status_id) select school_id, class_id, 'A', {active} from tb_classes where school_id='{Fresh}'");
            OfferSections(db, Fresh);
            d = await Status(db, Fresh);
            Assert.True(Step(d, "SCHOOL_SETUP").Done);
            Assert.Equal("FEE_TYPES", NextAfterPassword(d));

            db.Exec($"insert into tb_fee_types (school_id, fee_type_name, fee_type_code, is_optional, isactive) values ('{Fresh}', 'Tuition', 'TUI', false, true)");
            d = await Status(db, Fresh);
            Assert.True(Step(d, "FEE_TYPES").Done);
            Assert.Equal("FEE_STRUCTURE", NextAfterPassword(d));
            Assert.Contains("Class 1", Step(d, "FEE_STRUCTURE").Detail);

            // a structure with no line does not count; with a line it does
            db.Exec($"insert into tb_fee_structures (school_id, academic_year_id, class_id, structure_name, isactive) select '{Fresh}', (select academic_year_id from tb_academic_years where school_id='{Fresh}'), class_id, 'S ' || class_name, true from tb_classes where school_id='{Fresh}'");
            Assert.False(Step(await Status(db, Fresh), "FEE_STRUCTURE").Done);
            db.Exec($@"insert into tb_fee_structure_details (fee_structure_id, fee_type_id, frequency_id, amount, effective_from)
                       select fs.fee_structure_id, (select fee_type_id from tb_fee_types where school_id='{Fresh}'), (select min(frequency_id) from lut_fee_frequency), 1000, '2026-04-01'
                       from tb_fee_structures fs where fs.school_id='{Fresh}' and fs.structure_name like '%Class 1'");
            d = await Status(db, Fresh);
            Assert.False(Step(d, "FEE_STRUCTURE").Done);
            Assert.Contains("Class 2", Step(d, "FEE_STRUCTURE").Detail);
            Assert.DoesNotContain("Class 1", Step(d, "FEE_STRUCTURE").Detail);
            db.Exec($@"insert into tb_fee_structure_details (fee_structure_id, fee_type_id, frequency_id, amount, effective_from)
                       select fs.fee_structure_id, (select fee_type_id from tb_fee_types where school_id='{Fresh}'), (select min(frequency_id) from lut_fee_frequency), 1000, '2026-04-01'
                       from tb_fee_structures fs where fs.school_id='{Fresh}' and fs.structure_name like '%Class 2'");
            d = await Status(db, Fresh);
            Assert.All(d.Steps.Where(s => s.Code != "PASSWORD_CHANGED"), s => Assert.True(s.Done));

            // SQL cross-check of the same fact: every active class has an active structure with a line in the current year
            Assert.Equal(0, db.Scalar<int>($@"select count(*) from tb_classes c where c.school_id='{Fresh}' and c.status_id={active}
                and not exists (select 1 from tb_fee_structures f join tb_fee_structure_details x using (fee_structure_id)
                                where f.school_id=c.school_id and f.class_id=c.class_id and f.isactive
                                and f.academic_year_id=(select academic_year_id from tb_academic_years where school_id='{Fresh}' and is_current))"));

            // tenant isolation: the other fresh school did not move
            var other = await Status(db, OtherFresh);
            Assert.False(other.IsComplete);
            Assert.Equal("ACADEMIC_YEAR", NextAfterPassword(other));
        }

        [DbFact]
        public async Task Schools_with_active_students_are_grandfathered_and_a_school_without_is_not()
        {
            using var db = new TaxDb();
            var d = await Status(db, TaxDb.SchoolA, TaxDb.UserA);
            Assert.True(db.Scalar<int>($"select count(*) from tb_students where school_id='{TaxDb.SchoolA}' and status_id=(select sid from lut_status where sname='Active' and stype='general status')") > 0);
            Assert.True(d.IsComplete);
            Assert.Null(d.NextStep);

            // deactivate every student of that school: it is no longer grandfathered, and what the steps say is what the data says
            db.Exec($"update tb_students set status_id=(select sid from lut_status where sname='Inactive' and stype='general status') where school_id='{TaxDb.SchoolA}'");
            var after = await Status(db, TaxDb.SchoolA, TaxDb.UserA);
            var allStepsDone = after.Steps.All(s => s.Done);
            Assert.Equal(allStepsDone, after.IsComplete);
        }

        [DbFact]
        public async Task The_gate_answers_409_for_an_empty_school_and_nothing_for_a_grandfathered_one()
        {
            using var db = new TaxDb();
            NewSchool(db, Fresh);
            var blocked = await Svc(db).CheckSetupAsync(Fresh, default);
            Assert.Equal(409, blocked!.StatusCode);
            Assert.Equal("SETUP_INCOMPLETE", blocked.Data!.Code);
            Assert.Null(await Svc(db).CheckSetupAsync(TaxDb.SchoolA, default));
        }
    }
}
