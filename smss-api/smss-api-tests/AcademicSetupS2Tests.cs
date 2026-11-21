using Microsoft.Extensions.Logging.Abstractions;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Academic years, stage S2: which classes and sections a school offers in each year (tb_academic_year_classes / _sections).
    /// Proves the backfill changed nobody's answers (setup checklist, school counts, section lists), that new classes / sections / imports are offered in the
    /// current year, and the rules of the Academic Setup save. [DbFact]: needs SMSS_TEST_PG and a template with the S1 and S2 RUN-ME scripts applied.
    /// </summary>
    public class AcademicSetupS2Tests
    {
        private const string Fresh = "ZZAS0000001", Other = "ZZAS0000002";
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";
        private static readonly string[] Backfilled = { "APS261013001", "GHS261103002", "LAS261103001" };

        private static void NewSchool(TaxDb db, string id) =>
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{id}', '{id}-C', 'Setup test {id}', {Active})");
        private static void NewYear(TaxDb db, string id, string name = "2026-27", string from = "2026-04-01", string to = "2027-03-31", bool current = true) =>
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{id}', '{name}', '{from}', '{to}', {(current ? "true" : "false")}, {Active})");
        private static long YearId(TaxDb db, string id, string name = "2026-27") => db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{id}' and year_name='{name}'");

        private static ClassService Classes(TaxDb db) => new(new ClassRepository(db.Ctx()), NullLogger<ClassService>.Instance);
        private static SectionService Sections(TaxDb db) => new(new SectionRepository(db.Ctx()), NullLogger<SectionService>.Instance);
        private static AcademicSetupService Setup(TaxDb db) => new(new AcademicSetupRepository(db.Ctx()), NullLogger<AcademicSetupService>.Instance);
        private static AcademicYearService Years(TaxDb db) => new(new AcademicYearRepository(db.Ctx()), NullLogger<AcademicYearService>.Instance);
        private static OnboardingService Gate(TaxDb db) => new(new OnboardingRepository(db.Ctx()), NullLogger<OnboardingService>.Instance);

        private static async Task<long> NewClass(TaxDb db, string school, string name, int order)
        {
            var r = await Classes(db).CreateAsync(school, new CreateClassRequest { ClassName = name, SequenceOrder = order });
            Assert.True(r.Status, r.Message);
            return ((ClassResponse)r.Data!).ClassId;
        }
        private static async Task<long> NewSection(TaxDb db, string school, long classId, string name)
        {
            var r = await Sections(db).CreateAsync(school, new CreateSectionRequest { ClassId = classId, SectionName = name });
            Assert.True(r.Status, r.Message);
            return ((SectionResponse)r.Data!).SectionId;
        }
        private static SaveYearOfferingRequest Req(params (long cls, long[] sections)[] classes) =>
            new() { Classes = classes.Select(c => new OfferedClassRequest { ClassId = c.cls, Sections = c.sections.Select(s => new OfferedSectionRequest { SectionId = s }).ToList() }).ToList() };

        // ---------------------------------------------------------------- the backfill changed nobody's answers
        [DbFact]
        public async Task After_the_backfill_the_checklist_the_counts_and_the_section_lists_equal_what_the_masters_gave()
        {
            using var db = new TaxDb();
            foreach (var school in Backfilled)
            {
                var year = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{school}' and is_current");
                var active = db.Scalar<int>("select sid from lut_status where sname='Active' and stype='general status'");

                // setup checklist: the classes it lists and the classes that have a section, versus the master tables (the old definition)
                var repo = new OnboardingRepository(db.Ctx());
                var classes = await repo.ActiveClassesAsync(school, year, active, default);
                var expectedClasses = db.Scalar<int>($"select count(*) from tb_classes where school_id='{school}' and status_id={active}");
                Assert.Equal(expectedClasses, classes.Count);
                var withSection = await repo.ClassIdsWithActiveSectionAsync(school, year, active, default);
                Assert.Equal(db.Scalar<int>($"select count(distinct class_id) from tb_sections where school_id='{school}' and status_id={active}"), withSection.Count);

                // school profile header counts
                var counts = await new SchoolRegistrationRepository(db.Ctx()).GetSchoolCountsAsync(school, default);
                Assert.Equal(expectedClasses, counts.Classes);
                Assert.Equal(db.Scalar<int>($"select count(*) from tb_sections where school_id='{school}' and status_id={active}"), counts.Sections);

                // attendance / class-teacher section lists (they feed the dashboards)
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                var markable = await new AttendanceRepository(db.Ctx()).GetMarkableSectionsAsync(school, year, today, null, active, default);
                Assert.Equal(counts.Sections, markable.Count);
                var assignments = await new ClassTeacherRepository(db.Ctx()).GetSectionAssignmentsAsync(school, year, active, default);
                Assert.Equal(counts.Sections, assignments.Count);

                // and the checklist as the service answers it
                var status = (await Gate(db).GetStatusAsync(school, 0, default)).Data!;
                Assert.True(status.Steps.Single(s => s.Code == "IMPORT_MASTERS_OR_CLASSES").Done);
                Assert.True(status.Steps.Single(s => s.Code == "SCHOOL_SETUP").Done);
            }
        }

        [DbFact]
        public void The_migration_left_one_offering_row_per_year_and_master()
        {
            using var db = new TaxDb();
            foreach (var school in Backfilled)
            {
                Assert.Equal(db.Scalar<int>($"select count(*) from tb_academic_years where school_id='{school}'") * db.Scalar<int>($"select count(*) from tb_classes where school_id='{school}'"),
                    db.Scalar<int>($"select count(*) from tb_academic_year_classes where school_id='{school}'"));
                Assert.Equal(db.Scalar<int>($"select count(*) from tb_academic_years where school_id='{school}'") * db.Scalar<int>($"select count(*) from tb_sections where school_id='{school}'"),
                    db.Scalar<int>($"select count(*) from tb_academic_year_sections where school_id='{school}'"));
            }
        }

        // ---------------------------------------------------------------- new things are offered in the current year
        [DbFact]
        public async Task A_new_class_and_section_are_offered_in_the_current_year_and_the_checklist_sees_them()
        {
            using var db = new TaxDb();
            NewSchool(db, Fresh);
            NewYear(db, Fresh);
            var year = YearId(db, Fresh);
            var c = await NewClass(db, Fresh, "Class 1", 1);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_academic_year_classes where academic_year_id={year} and class_id={c}"));
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_academic_year_sections where academic_year_id={year}"));
            var s = await NewSection(db, Fresh, c, "A");
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_academic_year_sections where academic_year_id={year} and section_id={s}"));
            var status = (await Gate(db).GetStatusAsync(Fresh, 0, default)).Data!;
            Assert.True(status.Steps.Single(x => x.Code == "IMPORT_MASTERS_OR_CLASSES").Done);
            Assert.True(status.Steps.Single(x => x.Code == "SCHOOL_SETUP").Done);
        }

        [DbFact]
        public async Task Without_a_current_year_nothing_is_offered_and_the_first_year_then_offers_what_exists()
        {
            using var db = new TaxDb();
            NewSchool(db, Fresh);
            var c = await NewClass(db, Fresh, "Class 1", 1);
            await NewSection(db, Fresh, c, "A");
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_academic_year_classes where school_id='{Fresh}'"));
            Assert.False((await Gate(db).GetStatusAsync(Fresh, 0, default)).Data!.Steps.Single(x => x.Code == "IMPORT_MASTERS_OR_CLASSES").Done);

            var made = await Years(db).CreateAsync(Fresh, new CreateAcademicYearRequest { YearName = "2026-27", StartDate = DateOnly.Parse("2026-04-01"), EndDate = DateOnly.Parse("2027-03-31") });
            Assert.True(made.Status, made.Message);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_academic_year_classes where school_id='{Fresh}'"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_academic_year_sections where school_id='{Fresh}'"));
            Assert.True((await Gate(db).GetStatusAsync(Fresh, 0, default)).Data!.Steps.Single(x => x.Code == "IMPORT_MASTERS_OR_CLASSES").Done);

            // a LATER year starts empty (it is set up in Academic Setup)
            var later = await Years(db).CreateAsync(Fresh, new CreateAcademicYearRequest { YearName = "2027-28", StartDate = DateOnly.Parse("2027-04-01"), EndDate = DateOnly.Parse("2028-03-31") });
            var laterId = ((AcademicYearResponse)later.Data!).AcademicYearId;
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_academic_year_classes where academic_year_id={laterId}"));
        }

        [DbFact]
        public async Task Import_Masters_offers_what_it_creates_and_the_whole_first_login_checklist_moves_on()
        {
            using var db = new TaxDb();
            NewSchool(db, Fresh);
            NewYear(db, Fresh);
            var import = new MasterImportService(new MasterImportRepository(db.Ctx()), NullLogger<MasterImportService>.Instance);
            var r = await import.ImportAsync(Fresh, new ImportMastersRequest { Classes = new() { "Class 1", "Class 2" } }, default);
            Assert.True(r.Status, r.Message);
            var year = YearId(db, Fresh);
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_academic_year_classes where academic_year_id={year}"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_academic_year_sections where academic_year_id={year}"));   // Section A for each
            var steps = (await Gate(db).GetStatusAsync(Fresh, 0, default)).Data!.Steps;
            Assert.True(steps.Single(x => x.Code == "IMPORT_MASTERS_OR_CLASSES").Done);
            Assert.True(steps.Single(x => x.Code == "SCHOOL_SETUP").Done);

            // importing again changes nothing
            await import.ImportAsync(Fresh, new ImportMastersRequest { Classes = new() { "Class 1", "Class 2" } }, default);
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_academic_year_classes where academic_year_id={year}"));
        }

        [DbFact]
        public async Task Moving_a_section_to_another_class_moves_its_offerings_with_it()
        {
            using var db = new TaxDb();
            NewSchool(db, Fresh);
            NewYear(db, Fresh);
            var year = YearId(db, Fresh);
            var c1 = await NewClass(db, Fresh, "Class 1", 1);
            var c2 = await NewClass(db, Fresh, "Class 2", 2);
            var s = await NewSection(db, Fresh, c1, "A");
            db.Exec($"update tb_academic_year_sections set max_strength = 33 where section_id = {s}");

            var moved = await Sections(db).UpdateAsync(Fresh, s, new UpdateSectionRequest { ClassId = c2, SectionName = "A" });
            Assert.True(moved.Status, moved.Message);
            Assert.Equal(c2, db.Scalar<long>($"select class_id from tb_academic_year_sections where academic_year_id={year} and section_id={s}"));
            Assert.Equal(33, db.Scalar<int>($"select max_strength from tb_academic_year_sections where academic_year_id={year} and section_id={s}"));

            // a rename without a move does not touch the offering
            Assert.True((await Sections(db).UpdateAsync(Fresh, s, new UpdateSectionRequest { ClassId = c2, SectionName = "B" })).Status);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_academic_year_sections where section_id={s}"));
        }

        [DbFact]
        public async Task The_year_filter_on_Classes_and_Sections_returns_only_what_that_year_offers()
        {
            using var db = new TaxDb();
            NewSchool(db, Fresh);
            NewYear(db, Fresh);
            var c1 = await NewClass(db, Fresh, "Class 1", 1);
            await NewClass(db, Fresh, "Class 2", 2);
            var s1 = await NewSection(db, Fresh, c1, "A");
            await Years(db).CreateAsync(Fresh, new CreateAcademicYearRequest { YearName = "2027-28", StartDate = DateOnly.Parse("2027-04-01"), EndDate = DateOnly.Parse("2028-03-31") });
            var later = YearId(db, Fresh, "2027-28");
            var current = YearId(db, Fresh);

            var all = (List<ClassResponse>)(await Classes(db).GetAllAsync(Fresh)).Data!;
            Assert.Equal(2, all.Count);
            Assert.Equal(2, ((List<ClassResponse>)(await Classes(db).GetAllAsync(Fresh, current)).Data!).Count);
            Assert.Empty((List<ClassResponse>)(await Classes(db).GetAllAsync(Fresh, later)).Data!);
            Assert.Single((List<SectionResponse>)(await Sections(db).GetAllAsync(Fresh, null, current)).Data!);
            Assert.Empty((List<SectionResponse>)(await Sections(db).GetAllAsync(Fresh, null, later)).Data!);
            // another school's year offers nothing to this school
            NewSchool(db, Other); NewYear(db, Other);
            Assert.Empty((List<ClassResponse>)(await Classes(db).GetAllAsync(Fresh, YearId(db, Other))).Data!);
            Assert.Equal(s1, ((List<SectionResponse>)(await Sections(db).GetAllAsync(Fresh, null, current)).Data!).Single().SectionId);
        }

        // ---------------------------------------------------------------- the Academic Setup save
        [DbFact]
        public async Task Saving_an_offering_adds_withdraws_sets_capacity_and_is_idempotent()
        {
            using var db = new TaxDb();
            NewSchool(db, Fresh); NewYear(db, Fresh);
            var c1 = await NewClass(db, Fresh, "Class 1", 1); var s1a = await NewSection(db, Fresh, c1, "A"); var s1b = await NewSection(db, Fresh, c1, "B");
            var c2 = await NewClass(db, Fresh, "Class 2", 2); var s2a = await NewSection(db, Fresh, c2, "A");
            await Years(db).CreateAsync(Fresh, new CreateAcademicYearRequest { YearName = "2027-28", StartDate = DateOnly.Parse("2027-04-01"), EndDate = DateOnly.Parse("2028-03-31") });
            var later = YearId(db, Fresh, "2027-28");

            var empty = (await Setup(db).GetOfferingAsync(Fresh, later, default)).Data!;
            Assert.Equal(0, empty.OfferedClasses);
            Assert.Equal(2, empty.Classes.Count);   // every master is listed, none offered

            var saved = await Setup(db).SaveOfferingAsync(Fresh, TaxDb.UserA, later,
                new SaveYearOfferingRequest { Classes = new() { new OfferedClassRequest { ClassId = c1, Sections = new() { new OfferedSectionRequest { SectionId = s1a, MaxStrength = 40 }, new OfferedSectionRequest { SectionId = s1b } } } } }, default);
            Assert.True(saved.Status, saved.Message);
            Assert.Equal(1, saved.Data!.OfferedClasses);
            Assert.Equal(2, saved.Data.OfferedSections);
            var a = saved.Data.Classes.Single(c => c.ClassId == c1).Sections.Single(s => s.SectionId == s1a);
            Assert.Equal((short?)40, a.MaxStrength);
            Assert.Equal((short?)40, a.EffectiveMaxStrength);
            Assert.False(saved.Data.Classes.Single(c => c.ClassId == c2).Offered);

            // the same request again changes nothing
            var again = await Setup(db).SaveOfferingAsync(Fresh, TaxDb.UserA, later,
                new SaveYearOfferingRequest { Classes = new() { new OfferedClassRequest { ClassId = c1, Sections = new() { new OfferedSectionRequest { SectionId = s1a, MaxStrength = 40 }, new OfferedSectionRequest { SectionId = s1b } } } } }, default);
            Assert.True(again.Status);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_academic_year_classes where academic_year_id={later}"));
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_academic_year_sections where academic_year_id={later}"));

            // withdraw section B and add class 2: B stays as an Inactive row (nothing is deleted)
            var changed = await Setup(db).SaveOfferingAsync(Fresh, TaxDb.UserA, later, Req((c1, new[] { s1a }), (c2, new[] { s2a })), default);
            Assert.True(changed.Status, changed.Message);
            Assert.Equal(2, changed.Data!.OfferedClasses);
            Assert.Equal(2, changed.Data.OfferedSections);
            Assert.Equal(3, db.Scalar<int>($"select count(*) from tb_academic_year_sections where academic_year_id={later}"));
            Assert.Equal("Inactive", db.Scalar<string>($"select sname from tb_academic_year_sections o join lut_status st on st.sid=o.status_id where o.academic_year_id={later} and o.section_id={s1b}"));
            Assert.Null(changed.Data.Classes.Single(c => c.ClassId == c1).Sections.Single(s => s.SectionId == s1a).MaxStrength);   // capacity cleared by the request

            // offering B again re-activates the same row
            Assert.True((await Setup(db).SaveOfferingAsync(Fresh, TaxDb.UserA, later, Req((c1, new[] { s1a, s1b }), (c2, new[] { s2a })), default)).Status);
            Assert.Equal(3, db.Scalar<int>($"select count(*) from tb_academic_year_sections where academic_year_id={later}"));
            // the CURRENT year was never touched by any of this
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_academic_year_classes where academic_year_id={YearId(db, Fresh)}"));
        }

        [DbFact]
        public async Task Withdrawing_something_in_use_is_refused_with_the_reasons_and_changes_nothing()
        {
            using var db = new TaxDb();
            // GHS: its current year has enrolled students, class teachers, a timetable and fee structures
            var year = TaxDb.YearA;
            var offered = (await Setup(db).GetOfferingAsync(TaxDb.SchoolA, year, default)).Data!;
            var used = offered.Classes.First(c => c.Enrolments > 0);
            var keep = Req(offered.Classes.Where(c => c.ClassId != used.ClassId && c.Offered).Select(c => (c.ClassId, c.Sections.Where(s => s.Offered).Select(s => s.SectionId).ToArray())).ToArray());

            var refused = await Setup(db).SaveOfferingAsync(TaxDb.SchoolA, TaxDb.UserA, year, keep, default);
            Assert.False(refused.Status);
            Assert.Equal(409, refused.StatusCode);
            Assert.Contains(used.ClassName, refused.Message);
            Assert.Contains("Nothing was saved", refused.Message);
            Assert.Equal(offered.OfferedClasses, (await Setup(db).GetOfferingAsync(TaxDb.SchoolA, year, default)).Data!.OfferedClasses);

            // a section with students can't be dropped either, even when its class stays
            var withStudents = used.Sections.First(s => s.Enrolments > 0);
            var dropSection = Req(offered.Classes.Where(c => c.Offered).Select(c => (c.ClassId, c.Sections.Where(s => s.Offered && s.SectionId != withStudents.SectionId).Select(s => s.SectionId).ToArray())).ToArray());
            var refused2 = await Setup(db).SaveOfferingAsync(TaxDb.SchoolA, TaxDb.UserA, year, dropSection, default);
            Assert.Equal(409, refused2.StatusCode);
            Assert.Contains("enrolled", refused2.Message);
        }

        [DbFact]
        public async Task A_closed_year_refuses_the_save_and_another_schools_ids_are_never_accepted()
        {
            using var db = new TaxDb();
            NewSchool(db, Fresh); NewYear(db, Fresh); NewSchool(db, Other); NewYear(db, Other);
            var c1 = await NewClass(db, Fresh, "Class 1", 1);
            var foreignClass = await NewClass(db, Other, "Class 1", 1);
            var foreignSection = await NewSection(db, Other, foreignClass, "A");
            var current = YearId(db, Fresh);
            await Years(db).CreateAsync(Fresh, new CreateAcademicYearRequest { YearName = "2025-26", StartDate = DateOnly.Parse("2025-04-01"), EndDate = DateOnly.Parse("2026-03-31") });
            var past = YearId(db, Fresh, "2025-26");

            Assert.Equal(404, (await Setup(db).GetOfferingAsync(Fresh, YearId(db, Other), default)).StatusCode);                         // another school's year
            Assert.Equal(404, (await Setup(db).SaveOfferingAsync(Fresh, TaxDb.UserA, YearId(db, Other), Req(), default)).StatusCode);
            Assert.Equal(400, (await Setup(db).SaveOfferingAsync(Fresh, TaxDb.UserA, current, Req((foreignClass, Array.Empty<long>())), default)).StatusCode);   // another school's class
            Assert.Equal(400, (await Setup(db).SaveOfferingAsync(Fresh, TaxDb.UserA, current, Req((c1, new[] { foreignSection })), default)).StatusCode);        // another school's section
            Assert.Equal(400, (await Setup(db).SaveOfferingAsync(Fresh, TaxDb.UserA, current, Req((c1, Array.Empty<long>()), (c1, Array.Empty<long>())), default)).StatusCode);   // twice

            var close = await Years(db).CloseAsync(Fresh, past, TaxDb.UserA, new CloseAcademicYearRequest());
            Assert.True(close.Status, close.Message);
            var closed = await Setup(db).SaveOfferingAsync(Fresh, TaxDb.UserA, past, Req((c1, Array.Empty<long>())), default);
            Assert.Equal(409, closed.StatusCode);
            Assert.Contains("closed", closed.Message);
            Assert.True((await Setup(db).GetOfferingAsync(Fresh, past, default)).Data!.IsClosed);   // reading a closed year is fine
        }

        [DbFact]
        public async Task An_inactive_class_cannot_be_newly_offered_and_a_section_needs_its_own_class()
        {
            using var db = new TaxDb();
            NewSchool(db, Fresh); NewYear(db, Fresh);
            var c1 = await NewClass(db, Fresh, "Class 1", 1); var s1 = await NewSection(db, Fresh, c1, "A");
            var c2 = await NewClass(db, Fresh, "Class 2", 2); var s2 = await NewSection(db, Fresh, c2, "A");
            await Years(db).CreateAsync(Fresh, new CreateAcademicYearRequest { YearName = "2027-28", StartDate = DateOnly.Parse("2027-04-01"), EndDate = DateOnly.Parse("2028-03-31") });
            var later = YearId(db, Fresh, "2027-28");
            db.Exec($"update tb_classes set status_id = (select sid from lut_status where sname='Inactive' and stype='general status') where class_id = {c2}");

            var inactive = await Setup(db).SaveOfferingAsync(Fresh, TaxDb.UserA, later, Req((c2, Array.Empty<long>())), default);
            Assert.Equal(400, inactive.StatusCode);
            Assert.Contains("inactive", inactive.Message);
            var wrongClass = await Setup(db).SaveOfferingAsync(Fresh, TaxDb.UserA, later, Req((c1, new[] { s2 })), default);
            Assert.Equal(400, wrongClass.StatusCode);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_academic_year_classes where academic_year_id={later}"));   // nothing was saved
            Assert.NotEqual(0, s1);
        }

        [DbFact]
        public void The_database_refuses_a_section_offered_without_its_class()
        {
            using var db = new TaxDb();
            NewSchool(db, Fresh); NewYear(db, Fresh);
            var year = YearId(db, Fresh);
            db.Exec($"insert into tb_classes (school_id, class_name, sequence_order, status_id) values ('{Fresh}', 'Class 1', 1, {Active})");
            db.Exec($"insert into tb_sections (school_id, class_id, section_name, status_id) select school_id, class_id, 'A', {Active} from tb_classes where school_id='{Fresh}'");
            var ex = Assert.Throws<Npgsql.PostgresException>(() => db.Exec(
                $"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) select school_id, {year}, class_id, section_id, {Active} from tb_sections where school_id='{Fresh}'"));
            Assert.Equal("fk_tb_ays_class", ex.ConstraintName);
            // and a school can never offer another school's class
            NewSchool(db, Other);
            db.Exec($"insert into tb_classes (school_id, class_name, sequence_order, status_id) values ('{Other}', 'Class 1', 1, {Active})");
            var cross = Assert.Throws<Npgsql.PostgresException>(() => db.Exec(
                $"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) select '{Fresh}', {year}, class_id, {Active} from tb_classes where school_id='{Other}'"));
            Assert.Equal("fk_tb_ayc_class", cross.ConstraintName);
        }
    }
}
