using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.helper;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Year continuity, stage 1a (Docs/smss-yc-1a-RUN-ME.sql): the enrolment foreign keys, normalised roll numbers, the next-class chain, the master guards, the section move,
    /// and the three enrolment sites (direct entry, Confirm Admission, Excel import). [DbFact]: needs SMSS_TEST_PG and a template with the 1a script applied (smss_tpl_1a).
    /// </summary>
    public class YearContinuity1aTests
    {
        private const string A = TaxDb.SchoolA, B = TaxDb.SchoolB;
        private const long Y = TaxDb.YearA;

        private static string? Sqlstate(TaxDb db, string sql)
        {
            try { db.Exec(sql); return null; }
            catch (PostgresException ex) { return ex.SqlState + ":" + ex.ConstraintName; }
        }

        private static ClassService Classes(TaxDb db) => new(new ClassRepository(db.Ctx()), NullLogger<ClassService>.Instance);
        private static SectionService Sections(TaxDb db) => new(new SectionRepository(db.Ctx()), NullLogger<SectionService>.Instance);
        private static SubjectService Subjects(TaxDb db) => new(new SubjectRepository(db.Ctx()), NullLogger<SubjectService>.Instance);
        private static StudentOnboardingService Onboarding(TaxDb db) =>
            new(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<StudentOnboardingService>.Instance);

        private static long NewClass(TaxDb db, string school, string name, int order, long? next = null) =>
            ((ClassResponse)Classes(db).CreateAsync(school, new CreateClassRequest { ClassName = name, SequenceOrder = order, NextClassId = next }).Result.Data!).ClassId;
        private static long NewSection(TaxDb db, string school, long cls, string name) =>
            ((SectionResponse)Sections(db).CreateAsync(school, new CreateSectionRequest { ClassId = cls, SectionName = name }).Result.Data!).SectionId;

        /// <summary>A section of school A that the year does NOT offer (a master row without an offering).</summary>
        private static long UnofferedSection(TaxDb db, string name = "Z")
        {
            var cls = NewClass(db, A, "Zeta " + Guid.NewGuid().ToString("N")[..6], 40);
            db.Exec($"insert into tb_sections (school_id, class_id, section_name, status_id) values ('{A}', {cls}, '{name}', 1)");
            return db.Scalar<long>($"select max(section_id) from tb_sections where class_id={cls}");
        }

        private static DirectStudentEntryRequestDto Direct(long section, string? roll, string first = "Roll") => new()
        {
            AdmissionDate = DateTime.UtcNow.Date, AcademicYearId = Y, ClassSectionId = section, RollNumber = roll,
            FirstName = first, LastName = "Test", DateOfBirth = new DateTime(2018, 5, 5), GuardianName = "Guardian", GuardianRelationship = "Father", GuardianMobileNumber = "9876543210",
        };

        // ----------------------------------------------------------------------------------------------- database keys and roll numbers
        [DbFact]
        public void The_database_refuses_an_enrolment_in_a_section_the_year_does_not_offer_another_schools_section_or_another_schools_year()
        {
            using var db = new TaxDb();
            var enrolment = db.Scalar<long>($"select min(enrollment_id) from tb_student_enrollments where academic_year_id={Y}");
            var unoffered = UnofferedSection(db);
            var sectionOfB = db.Scalar<long>($"select min(section_id) from tb_sections where school_id='{B}'");
            var yearOfB = db.Scalar<long>($"select min(academic_year_id) from tb_academic_years where school_id='{B}'");
            Assert.Equal("23503:fk_tb_enrollments_offering", Sqlstate(db, $"update tb_student_enrollments set class_section_id={unoffered} where enrollment_id={enrolment}"));
            Assert.StartsWith("23503", Sqlstate(db, $"update tb_student_enrollments set class_section_id={sectionOfB} where enrollment_id={enrolment}"));   // another school's section
            Assert.StartsWith("23503", Sqlstate(db, $"update tb_student_enrollments set academic_year_id={yearOfB} where enrollment_id={enrolment}"));      // another school's year
            Assert.Equal(Y, db.Scalar<long>($"select academic_year_id from tb_student_enrollments where enrollment_id={enrolment}"));
        }

        [DbFact]
        public void Roll_numbers_are_unique_per_section_and_year_without_spaces_case_or_leading_zeros()
        {
            using var db = new TaxDb();
            var rows = db.Scalar<string>($"select string_agg(enrollment_id::text, ',' order by enrollment_id) from (select enrollment_id from tb_student_enrollments where academic_year_id={Y} and class_section_id = (select class_section_id from tb_student_enrollments where academic_year_id={Y} group by 1 having count(*) >= 3 order by 1 limit 1) order by 1 limit 3) x").Split(',');
            db.Exec($"update tb_student_enrollments set roll_number=null where academic_year_id={Y} and class_section_id=(select class_section_id from tb_student_enrollments where enrollment_id={rows[0]})");
            db.Exec($"update tb_student_enrollments set roll_number='7' where enrollment_id={rows[0]}");
            Assert.Equal("23505:uq_tb_enrollments_roll", Sqlstate(db, $"update tb_student_enrollments set roll_number='07' where enrollment_id={rows[1]}"));
            Assert.Equal("23505:uq_tb_enrollments_roll", Sqlstate(db, $"update tb_student_enrollments set roll_number=' 7 ' where enrollment_id={rows[1]}"));
            Assert.Equal("23505:uq_tb_enrollments_roll", Sqlstate(db, $"update tb_student_enrollments set roll_number='000' where enrollment_id={rows[1]}; update tb_student_enrollments set roll_number='0' where enrollment_id={rows[2]}"));
            db.Exec($"update tb_student_enrollments set roll_number=null where enrollment_id in ({rows[1]},{rows[2]})");
            db.Exec($"update tb_student_enrollments set roll_number='A-1' where enrollment_id={rows[1]}");
            Assert.Equal("23505:uq_tb_enrollments_roll", Sqlstate(db, $"update tb_student_enrollments set roll_number='a-1' where enrollment_id={rows[2]}"));
            Assert.Null(Sqlstate(db, $"update tb_student_enrollments set roll_number='007x' where enrollment_id={rows[2]}"));   // not purely numeric: "007x" and "7x" are different rolls
            Assert.Null(Sqlstate(db, $"update tb_student_enrollments set roll_number=null where enrollment_id={rows[2]}"));      // several enrolments without a roll are fine
        }

        [DbFact]
        public void The_same_roll_number_in_another_section_or_year_is_fine()
        {
            using var db = new TaxDb();
            var two = db.Scalar<string>($"select string_agg(enrollment_id::text, ',') from (select min(enrollment_id) enrollment_id from tb_student_enrollments where academic_year_id={Y} group by class_section_id order by 1 limit 2) x").Split(',');
            db.Exec($"update tb_student_enrollments set roll_number=null where academic_year_id={Y} and enrollment_id in (select enrollment_id from tb_student_enrollments where class_section_id in (select class_section_id from tb_student_enrollments where enrollment_id in ({two[0]},{two[1]})))");
            db.Exec($"update tb_student_enrollments set roll_number='9' where enrollment_id in ({two[0]},{two[1]})");
            Assert.Equal(2, db.Scalar<int>($"select count(*) from tb_student_enrollments where roll_number='9' and enrollment_id in ({two[0]},{two[1]})"));
        }

        [DbFact]
        public void The_C_sharp_roll_key_and_the_database_function_agree()
        {
            using var db = new TaxDb();
            foreach (var r in new[] { "1", "01", " 1 ", "0", "000", "007", "A-7", "a-7", " A-7 ", "7x", "007x", "٣", "12", "012", "1 2" })
                Assert.Equal(RollKey.Of(r), db.Scalar<string>($"select public.fn_roll_key('{r}')"));
            Assert.Null(RollKey.Of("  "));
            Assert.Null(RollKey.Of(null));
            Assert.True(RollKey.Same("01", " 1"));
            Assert.False(RollKey.Same("1", "10"));
        }

        // ----------------------------------------------------------------------------------------------- next class
        [DbFact]
        public void Next_class_rules_self_unknown_other_school_shared_and_loop()
        {
            using var db = new TaxDb();
            var x = NewClass(db, A, "ZX", 51); var y = NewClass(db, A, "ZY", 52); var z = NewClass(db, A, "ZZ", 53);
            var other = db.Scalar<long>($"select min(class_id) from tb_classes where school_id='{B}'");
            var svc = Classes(db);
            UpdateClassRequest Up(string n, int o, long? next) => new() { ClassName = n, SequenceOrder = o, NextClassId = next };

            Assert.Equal(200, svc.UpdateAsync(A, x, Up("ZX", 51, y)).Result.StatusCode);
            Assert.Equal(400, svc.UpdateAsync(A, y, Up("ZY", 52, y)).Result.StatusCode);                 // itself
            Assert.Equal(400, svc.UpdateAsync(A, y, Up("ZY", 52, 99999999)).Result.StatusCode);          // unknown
            Assert.Equal(400, svc.UpdateAsync(A, y, Up("ZY", 52, other)).Result.StatusCode);             // another school's class: not found for this school
            var shared = svc.UpdateAsync(A, z, Up("ZZ", 53, y)).Result;                                  // X already moves up to Y
            Assert.Equal(409, shared.StatusCode); Assert.Contains("ZX", shared.Message);
            Assert.Equal(200, svc.UpdateAsync(A, y, Up("ZY", 52, z)).Result.StatusCode);
            var loop = svc.UpdateAsync(A, z, Up("ZZ", 53, x)).Result;                                    // X -> Y -> Z -> X
            Assert.Equal(409, loop.StatusCode); Assert.Contains("loop", loop.Message);
            var shown = (ClassResponse)svc.GetByIdAsync(A, x).Result.Data!;
            Assert.Equal(y, shown.NextClassId); Assert.Equal("ZY", shown.NextClassName);
            Assert.Equal(400, svc.CreateAsync(A, new CreateClassRequest { ClassName = "ZNew", SequenceOrder = 54, NextClassId = other }).Result.StatusCode);
            Assert.Equal(409, svc.CreateAsync(A, new CreateClassRequest { ClassName = "ZNew", SequenceOrder = 54, NextClassId = y }).Result.StatusCode);   // Y is already X's next
        }

        [DbFact]
        public void The_database_refuses_a_self_next_class_and_two_classes_with_the_same_next_class()
        {
            using var db = new TaxDb();
            var x = NewClass(db, A, "ZX", 51); var y = NewClass(db, A, "ZY", 52); var z = NewClass(db, A, "ZZ", 53);
            Assert.StartsWith("23514", Sqlstate(db, $"update tb_classes set next_class_id={x} where class_id={x}"));
            db.Exec($"update tb_classes set next_class_id={y} where class_id={x}");
            Assert.Equal("23505:uq_tb_classes_school_next", Sqlstate(db, $"update tb_classes set next_class_id={y} where class_id={z}"));
            var otherClass = db.Scalar<long>($"select min(class_id) from tb_classes where school_id='{B}'");
            Assert.Equal("23503:fk_tb_classes_next", Sqlstate(db, $"update tb_classes set next_class_id={otherClass} where class_id={z}"));
        }

        [DbFact]
        public void A_class_that_another_class_points_to_cannot_be_deactivated_and_the_message_names_that_class()
        {
            using var db = new TaxDb();
            var x = NewClass(db, A, "ZX", 51); var y = NewClass(db, A, "ZY", 52);
            db.Exec($"update tb_classes set next_class_id={y} where class_id={x}");
            var refused = Classes(db).ToggleStatusAsync(A, y).Result;
            Assert.Equal(409, refused.StatusCode); Assert.Contains("ZX", refused.Message);
            Assert.Equal(1, db.Scalar<int>($"select status_id from tb_classes where class_id={y}"));
            db.Exec($"update tb_classes set next_class_id=null where class_id={x}");
            Assert.Equal(200, Classes(db).ToggleStatusAsync(A, y).Result.StatusCode);
            Assert.Equal(200, Classes(db).ToggleStatusAsync(A, y).Result.StatusCode);   // and it can be activated again
        }

        // ----------------------------------------------------------------------------------------------- master guards
        [DbFact]
        public void A_class_section_or_subject_in_use_in_an_open_year_cannot_be_deactivated_and_the_reason_is_given()
        {
            using var db = new TaxDb();
            db.Exec($"update tb_classes set next_class_id=null where school_id='{A}'");   // the chain guard has its own test; here the USE guard speaks
            var usedSection = db.Scalar<long>($"select class_section_id from tb_student_enrollments where academic_year_id={Y} group by 1 order by count(*) desc limit 1");
            var usedClass = db.Scalar<long>($"select class_id from tb_sections where section_id={usedSection}");
            var c = Classes(db).ToggleStatusAsync(A, usedClass).Result;
            Assert.Equal(409, c.StatusCode); Assert.Contains("enrolled student", c.Message);
            var s = Sections(db).ToggleStatusAsync(A, usedSection).Result;
            Assert.Equal(409, s.StatusCode); Assert.Contains("enrolled student", s.Message);
            var usedSubject = db.Scalar<long>($"select subject_id from tb_class_subjects where school_id='{A}' and academic_year_id={Y} and status_id=1 limit 1");
            var sub = Subjects(db).ToggleStatusAsync(A, usedSubject).Result;
            Assert.Equal(409, sub.StatusCode); Assert.Contains("can't be deactivated", sub.Message);
            // another school's id is a 404, not a hint
            Assert.Equal(404, Classes(db).ToggleStatusAsync(B, usedClass).Result.StatusCode);
            Assert.Equal(404, Sections(db).ToggleStatusAsync(B, usedSection).Result.StatusCode);
            Assert.Equal(404, Subjects(db).ToggleStatusAsync(B, usedSubject).Result.StatusCode);
        }

        [DbFact]
        public void Unused_masters_can_be_deactivated_and_a_used_one_in_a_closed_year_only_can_too()
        {
            using var db = new TaxDb();
            var cls = NewClass(db, A, "ZUnused", 60); var sec = NewSection(db, A, cls, "A");
            Assert.Equal(200, Sections(db).ToggleStatusAsync(A, sec).Result.StatusCode);
            Assert.Equal(200, Classes(db).ToggleStatusAsync(A, cls).Result.StatusCode);

            // a class whose only enrolments are in a CLOSED year may go
            var usedSection = db.Scalar<long>($"select class_section_id from tb_student_enrollments where academic_year_id={Y} group by 1 order by count(*) desc limit 1");
            var usedClass = db.Scalar<long>($"select class_id from tb_sections where section_id={usedSection}");
            db.Exec($"update tb_academic_years set is_current=false where academic_year_id={Y}");
            db.Exec($"update tb_academic_years set closed_at=now(), closed_by=120 where academic_year_id={Y}");
            db.Exec($"update tb_fee_structures set isactive=false where school_id='{A}' and academic_year_id={Y} and class_id={usedClass}");
            db.Exec($"update tb_class_subjects set status_id=2 where school_id='{A}' and academic_year_id={Y} and class_id={usedClass}");
            db.Exec($"update tb_class_teacher_assignments set status_id=2, effective_to=greatest(effective_from, current_date) where school_id='{A}' and academic_year_id={Y} and class_id={usedClass}");
            db.Exec($"update tb_timetable_entries set status_id=2 where school_id='{A}' and academic_year_id={Y} and class_id={usedClass}");
            db.Exec($"update tb_classes set next_class_id=null where school_id='{A}'");
            var secR = Sections(db).ToggleStatusAsync(A, usedSection).Result; Assert.True(secR.Status, secR.Message);
            var cls2 = Classes(db).ToggleStatusAsync(A, usedClass).Result; Assert.True(cls2.Status, cls2.Message);
        }

        // ----------------------------------------------------------------------------------------------- section move (and no hard delete of offerings)
        [DbFact]
        public void An_unused_section_moves_to_another_class_and_its_offering_rows_are_updated_in_place()
        {
            using var db = new TaxDb();
            var c1 = NewClass(db, A, "ZMove1", 61); var c2 = NewClass(db, A, "ZMove2", 62); var sec = NewSection(db, A, c1, "A");
            var before = db.Scalar<string>($"select string_agg(year_section_id::text, ',' order by year_section_id) from tb_academic_year_sections where section_id={sec}");
            Assert.False(string.IsNullOrEmpty(before));
            var r = Sections(db).UpdateAsync(A, sec, new UpdateSectionRequest { ClassId = c2, SectionName = "A" }).Result;
            Assert.True(r.Status, r.Message);
            Assert.Equal(c2, db.Scalar<long>($"select class_id from tb_sections where section_id={sec}"));
            Assert.Equal(before, db.Scalar<string>($"select string_agg(year_section_id::text, ',' order by year_section_id) from tb_academic_year_sections where section_id={sec}"));   // the same rows, none deleted
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_academic_year_sections where section_id={sec} and class_id<>{c2}"));
        }

        [DbFact]
        public void A_used_section_cannot_move_to_another_class_but_can_still_be_renamed()
        {
            using var db = new TaxDb();
            var used = db.Scalar<long>($"select class_section_id from tb_student_enrollments where academic_year_id={Y} group by 1 order by count(*) desc limit 1");
            var oldClass = db.Scalar<long>($"select class_id from tb_sections where section_id={used}");
            var name = db.Scalar<string>($"select section_name from tb_sections where section_id={used}");
            var other = NewClass(db, A, "ZOther", 63);
            var moved = Sections(db).UpdateAsync(A, used, new UpdateSectionRequest { ClassId = other, SectionName = name }).Result;
            Assert.Equal(409, moved.StatusCode); Assert.Contains("enrolment", moved.Message);
            Assert.Equal(oldClass, db.Scalar<long>($"select class_id from tb_sections where section_id={used}"));
            var renamed = Sections(db).UpdateAsync(A, used, new UpdateSectionRequest { ClassId = oldClass, SectionName = name + "x" }).Result;
            Assert.True(renamed.Status, renamed.Message);
        }

        [DbFact]
        public void A_section_cannot_move_to_a_class_that_a_year_it_is_offered_in_does_not_offer()
        {
            using var db = new TaxDb();
            var c1 = NewClass(db, A, "ZMoveA", 64); var sec = NewSection(db, A, c1, "A");
            var c2 = NewClass(db, A, "ZMoveB", 65);
            db.Exec($"update tb_academic_year_classes set status_id=2 where class_id={c2}");   // still a row: withdrawn classes stay offered-as-inactive...
            db.Exec($"delete from tb_academic_year_classes where class_id={c2}");               // ...so remove the row to model "never offered" (a test-only shortcut)
            var r = Sections(db).UpdateAsync(A, sec, new UpdateSectionRequest { ClassId = c2, SectionName = "A" }).Result;
            Assert.Equal(409, r.StatusCode); Assert.Contains("not offered", r.Message);
            Assert.Equal(c1, db.Scalar<long>($"select class_id from tb_sections where section_id={sec}"));
        }

        [Fact]
        public void No_code_path_deletes_offering_rows()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "smss-api-db-layer"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var bad = new List<string>();
            foreach (var f in Directory.EnumerateFiles(dir!.FullName, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains("smss-api-tests")))
            {
                var text = File.ReadAllText(f);
                if (System.Text.RegularExpressions.Regex.IsMatch(text, @"DELETE\s+FROM\s+(public\.)?tb_academic_year_(sections|classes)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    || System.Text.RegularExpressions.Regex.IsMatch(text, @"AcademicYear(Sections|Classes)\s*\.\s*Remove(Range)?\(")) bad.Add(Path.GetFileName(f));
            }
            Assert.True(bad.Count == 0, "Withdrawing an offering must stay a status change; hard delete in: " + string.Join(", ", bad));
        }

        // ----------------------------------------------------------------------------------------------- the three enrolment sites
        [DbFact]
        public void Direct_entry_refuses_a_section_the_year_does_not_offer_and_creates_nothing()
        {
            using var db = new TaxDb();
            var section = UnofferedSection(db);
            var before = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            var r = Onboarding(db).CreateDirectEntryAsync(A, Direct(section, null), default).Result;
            Assert.Equal(409, r.StatusCode); Assert.Contains("not offered", r.Message);
            Assert.Equal(before, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));
        }

        [DbFact]
        public void Direct_entry_refuses_a_roll_number_taken_after_normalising_and_accepts_a_free_one()
        {
            using var db = new TaxDb();
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            db.Exec($"update tb_student_enrollments set roll_number=null where academic_year_id={Y} and class_section_id={section}");
            var first = Onboarding(db).CreateDirectEntryAsync(A, Direct(section, "05", "First"), default).Result;
            Assert.True(first.Status, first.Message);
            var students = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            var second = Onboarding(db).CreateDirectEntryAsync(A, Direct(section, " 5 ", "Second"), default).Result;
            Assert.Equal(409, second.StatusCode); Assert.Contains("already taken", second.Message); Assert.Contains("leading zeros", second.Message);
            Assert.Equal(students, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));
            Assert.True(Onboarding(db).CreateDirectEntryAsync(A, Direct(section, "6", "Third"), default).Result.Status);
        }

        [DbFact]
        public async Task Two_direct_entries_taking_the_same_roll_number_at_the_same_moment_create_exactly_one_student()
        {
            using var db = new TaxDb();
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            db.Exec($"update tb_student_enrollments set roll_number=null where academic_year_id={Y} and class_section_id={section}");
            var before = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            var tasks = Enumerable.Range(0, 4).Select(i => Task.Run(() => Onboarding(db).CreateDirectEntryAsync(A, Direct(section, i % 2 == 0 ? "77" : "077", "Race" + i), default))).ToArray();
            var results = await Task.WhenAll(tasks);
            Assert.Equal(1, results.Count(r => r.Status));
            Assert.All(results.Where(r => !r.Status), r => Assert.Equal(409, r.StatusCode));
            Assert.Equal(before + 1, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_enrollments where academic_year_id={Y} and class_section_id={section} and public.fn_roll_key(roll_number)='77'"));
        }

        [DbFact]
        public void Direct_entry_into_a_section_withdrawn_from_the_year_is_refused()
        {
            using var db = new TaxDb();
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            db.Exec($"update tb_academic_year_sections set status_id=2 where academic_year_id={Y} and section_id={section}");   // withdrawal is a status change: the row stays
            var r = Onboarding(db).CreateDirectEntryAsync(A, Direct(section, null), default).Result;
            Assert.Equal(409, r.StatusCode); Assert.Contains("not offered", r.Message);
        }

        private static long Approved(TaxDb db, string first)
        {
            db.Exec($@"insert into tb_admissions (school_id, application_number, academic_year_id, applied_class_id, admission_type_id, applicant_first_name, applicant_last_name, date_of_birth,
                       guardian_name, guardian_relationship, guardian_mobile_number, application_date, status_id)
                       values ('{A}', 'ZZ1A-{first}', {Y}, (select min(class_id) from tb_classes where school_id='{A}'), (select min(admission_type_id) from lut_admission_type), '{first}', 'App', '2018-01-01',
                       'G', 'Father', '9876543210', current_date, (select sid from lut_status where sname='APPROVED'))");
            return db.Scalar<long>($"select admission_id from tb_admissions where school_id='{A}' and application_number='ZZ1A-{first}'");
        }

        private static AdmissionService Admissions(TaxDb db) =>
            new(new AdmissionRepository(db.Ctx()), Fake.Of<INotificationService>(), Fake.Of<IStudentFeeBillingService>(), NullLogger<AdmissionService>.Instance);

        [DbFact]
        public void Confirm_admission_refuses_an_unoffered_section_and_a_taken_roll_number_and_changes_nothing()
        {
            using var db = new TaxDb();
            var bad = UnofferedSection(db);
            var id = Approved(db, "Aarav");
            var students = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            var notOffered = Admissions(db).ConfirmAdmissionAsync(A, id, new ConfirmAdmissionRequestDto { ClassSectionId = bad }, default).Result;
            Assert.Equal(409, notOffered.StatusCode); Assert.Contains("not offered", notOffered.Message);

            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            db.Exec($"update tb_student_enrollments set roll_number=null where academic_year_id={Y} and class_section_id={section}");
            db.Exec($"update tb_student_enrollments set roll_number='3' where enrollment_id=(select min(enrollment_id) from tb_student_enrollments where academic_year_id={Y} and class_section_id={section})");
            var taken = Admissions(db).ConfirmAdmissionAsync(A, id, new ConfirmAdmissionRequestDto { ClassSectionId = section, RollNumber = "003" }, default).Result;
            Assert.Equal(409, taken.StatusCode); Assert.Contains("already taken", taken.Message);
            Assert.Equal(students, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));
            Assert.Equal("APPROVED", db.Scalar<string>($"select s.sname from tb_admissions a join lut_status s on s.sid=a.status_id where a.admission_id={id}"));

            var ok = Admissions(db).ConfirmAdmissionAsync(A, id, new ConfirmAdmissionRequestDto { ClassSectionId = section, RollNumber = "4" }, default).Result;
            Assert.True(ok.Status, ok.Message);
        }

        private static byte[] Sheet(params string[][] rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Students");
            var headers = new[] { "Admission number", "Full name", "Gender", "Date of birth", "Class", "Section", "Roll number", "Father name", "Father phone" };
            for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++) ws.Cell(r + 2, c + 1).Value = rows[r][c];
            using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
        }

        [DbFact]
        public void Import_flags_an_unoffered_section_a_duplicate_roll_in_the_file_and_a_roll_taken_in_the_database()
        {
            using var db = new TaxDb();
            var svc = new StudentImportService(new StudentImportRepository(db.Ctx()), NullLogger<StudentImportService>.Instance);
            var section = db.Scalar<long>($"select min(section_id) from tb_academic_year_sections where academic_year_id={Y} and status_id=1");
            db.Exec($"update tb_student_enrollments set roll_number=null where academic_year_id={Y} and class_section_id={section}");
            db.Exec($"update tb_student_enrollments set roll_number='2' where enrollment_id=(select min(enrollment_id) from tb_student_enrollments where academic_year_id={Y} and class_section_id={section})");
            var cls = db.Scalar<string>($"select c.class_name from tb_sections s join tb_classes c on c.class_id=s.class_id where s.section_id={section}");
            var sec = db.Scalar<string>($"select section_name from tb_sections where section_id={section}");
            var badSection = UnofferedSection(db, "Q");
            var badCls = db.Scalar<string>($"select c.class_name from tb_sections s join tb_classes c on c.class_id=s.class_id where s.section_id={badSection}");

            string[] Row(string name, string c, string s, string roll) => new[] { "", name, "Male", "05/05/2018", c, s, roll, "Father", "9876543210" };
            var file = Sheet(Row("Import One", cls, sec, "01"), Row("Import Two", cls, sec, "1"), Row("Import Three", cls, sec, "002"), Row("Import Four", badCls, "Q", "9"), Row("Import Five", cls, sec, "8"));
            var preview = svc.PreviewAsync(A, file, default).Result;
            Assert.True(preview.Status, preview.Message);
            var rows = preview.Data!.Rows;
            Assert.Contains(rows[0].Messages.Concat(rows[1].Messages), m => m.Contains("used more than once"));      // "01" and "1" are the same roll
            Assert.Contains(rows[2].Messages, m => m.Contains("already taken"));                                       // "002" = the database's "2"
            Assert.Contains(rows[3].Messages, m => m.Contains("not offered"));
            Assert.Equal("OK", rows[4].Status);
            Assert.False(preview.Data.Summary.CanImport);
            var students = db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'");
            Assert.False(svc.CommitAsync(A, file, default).Result.Status);   // one error stops the whole import
            Assert.Equal(students, db.Scalar<long>($"select count(*) from tb_students where school_id='{A}'"));
        }

        // ----------------------------------------------------------------------------------------------- history readers keep showing inactive masters
        [DbFact]
        public void History_readers_still_show_a_class_section_and_subject_that_were_deactivated_later()
        {
            using var db = new TaxDb();
            var enrolment = db.Scalar<long>($"select min(enrollment_id) from tb_student_enrollments where academic_year_id={Y}");
            var student = db.Scalar<string>($"select student_id from tb_student_enrollments where enrollment_id={enrolment}");
            var section = db.Scalar<long>($"select class_section_id from tb_student_enrollments where enrollment_id={enrolment}");
            var cls = db.Scalar<long>($"select class_id from tb_sections where section_id={section}");
            var className = db.Scalar<string>($"select class_name from tb_classes where class_id={cls}");
            var sectionName = db.Scalar<string>($"select section_name from tb_sections where section_id={section}");
            db.Exec($"update tb_classes set status_id=2 where class_id={cls}");
            db.Exec($"update tb_sections set status_id=2 where section_id={section}");
            db.Exec($"update tb_subjects set status_id=2 where school_id='{A}'");

            // the student's own profile and the staff view of it
            var students = new StudentService(new StudentRepository(db.Ctx()), Fake.Of<IFileStorageService>(), NullLogger<StudentService>.Instance);
            var profile = students.GetStudentProfileAsync(A, student, false, default).Result;
            Assert.True(profile.Status, profile.Message);
            Assert.Equal(className, profile.Data!.ClassName); Assert.Equal(sectionName, profile.Data.SectionName);

            // the class list of the year (the student list screen) still returns the inactive section's students
            var list = students.GetStudentsAsync(A, Y, section, null, default).Result;
            Assert.True(list.Status, list.Message); Assert.NotEmpty(list.Data!);

            // the receipt of a payment made while the class was active still prints the class
            var pay = db.Pay(student, 10m);
            if (pay.Result.Status) Assert.Equal(className, pay.Result.Data!.Student.ClassName);

            // report setup: an existing scheme still names an inactive class; ADDING an inactive class is still refused (that lookup stays active-only)
            var setup = new ReportSetupRepository(db.Ctx());
            var active = db.Scalar<int>("select sid from lut_status where sname='Active' and stype='general status'");
            Assert.Equal(className, setup.ClassNamesAnyStatusAsync(A, new[] { cls }, default).Result[cls]);
            Assert.DoesNotContain(cls, setup.ClassNamesAsync(A, new[] { cls }, active, default).Result.Keys);
            // class-wide ranking still counts the students of an inactive section
            Assert.Contains(section, new ReportResultRepository(db.Ctx()).GetClassSectionIdsAsync(A, cls, active, default).Result);
        }

        // ----------------------------------------------------------------------------------------------- Import Masters sets the chain
        [DbFact]
        public void Import_masters_links_the_catalogue_chain_without_overwriting_and_a_second_run_changes_nothing()
        {
            using var db = new TaxDb();
            var svc = new MasterImportService(new MasterImportRepository(db.Ctx()), NullLogger<MasterImportService>.Instance);
            var school = "TEST261121001";
            db.Exec($"update tb_classes set next_class_id=null where school_id='{school}'");
            db.Exec($"delete from tb_classes where school_id='{school}' and class_name in ('Class 6','Class 7')");   // not present yet in this school, so the import creates them
            var r = svc.ImportAsync(school, new ImportMastersRequest { Classes = { "Class 5", "Class 6", "Class 7" } }, default).Result;
            Assert.True(r.Status, r.Message);
            string Next(string from) => db.Scalar<string>($"select n.class_name from tb_classes c join tb_classes n on n.class_id=c.next_class_id where c.school_id='{school}' and c.class_name='{from}'");
            Assert.Equal("Class 6", Next("Class 5"));
            Assert.Equal("Class 7", Next("Class 6"));
            Assert.Null(Next("Class 7"));                                  // Class 8 is not in this school
            Assert.Equal("Class 5", Next("Class 4"));                      // the link INTO a picked class is completed too
            Assert.Null(Next("Class 3"));                                  // neither end picked: left alone
            var fingerprint = db.Scalar<string>($"select md5(string_agg(class_id::text || ':' || coalesce(next_class_id::text,'-'), ',' order by class_id)) from tb_classes where school_id='{school}'");
            Assert.True(svc.ImportAsync(school, new ImportMastersRequest { Classes = { "Class 5", "Class 6", "Class 7" } }, default).Result.Status);
            Assert.Equal(fingerprint, db.Scalar<string>($"select md5(string_agg(class_id::text || ':' || coalesce(next_class_id::text,'-'), ',' order by class_id)) from tb_classes where school_id='{school}'"));
        }
    }
}
