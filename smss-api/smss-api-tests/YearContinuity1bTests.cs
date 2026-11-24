using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Year continuity, stage 1b (Docs/smss-yc-1b-RUN-ME.sql): receipts use the year of the payment's installments and a fee-type name snapshot, concessions are per year, the profile lists
    /// every enrolment, and NextClassId on update (omitted = unchanged, null = clear). [DbFact]: needs SMSS_TEST_PG and a template with the 1a and 1b scripts applied (smss_tpl_1b).
    /// </summary>
    public class YearContinuity1bTests
    {
        private const string A = TaxDb.SchoolA;
        private const long Y1 = TaxDb.YearA;
        private const string S = "GHS001ST001";

        private sealed record World(long Y2, long Due1, long Due2, string Class1, string Class2, long Section1, long Section2);

        /// <summary>
        /// Student GHS001ST001 is in year 1 (2026-27) in his section. A second year 2027-28 offers ANOTHER class and section, enrols him there and gives him one installment
        /// (due 5 Apr 2027, 500) that belongs to that year only.
        /// </summary>
        private static World Build(TaxDb db)
        {
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{A}', '2027-28', '2027-04-01', '2028-03-31', false, 1)");
            var y2 = db.Scalar<long>($"select academic_year_id from tb_academic_years where school_id='{A}' and year_name='2027-28'");
            var s1 = db.Scalar<long>($"select class_section_id from tb_student_enrollments where student_id='{S}' and academic_year_id={Y1}");
            var s2 = db.Scalar<long>($"select min(section_id) from tb_sections where school_id='{A}' and class_id <> (select class_id from tb_sections where section_id={s1}) and status_id=1");
            var c1 = db.Scalar<string>($"select c.class_name from tb_sections s join tb_classes c on c.class_id=s.class_id where s.section_id={s1}");
            var c2 = db.Scalar<string>($"select c.class_name from tb_sections s join tb_classes c on c.class_id=s.class_id where s.section_id={s2}");
            db.Exec($"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) select '{A}', {y2}, class_id, 1 from tb_sections where section_id={s2}");
            db.Exec($"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) select '{A}', {y2}, class_id, section_id, 1 from tb_sections where section_id={s2}");
            db.Exec($@"insert into tb_student_enrollments (school_id, student_id, academic_year_id, class_section_id, enrollment_date, admission_type_id, status_id, roll_number)
                       select '{A}', '{S}', {y2}, {s2}, '2027-04-01', admission_type_id, 1, '9' from tb_student_enrollments where student_id='{S}' and academic_year_id={Y1}");
            db.Exec($@"insert into tb_student_fee_items (student_id, school_id, academic_year_id, fee_type_id, frequency_id, base_amount, net_amount)
                       select '{S}', '{A}', {y2}, fee_type_id, frequency_id, 500, 500 from tb_student_fee_items where student_id='{S}' and academic_year_id={Y1} order by student_fee_item_id limit 1");
            db.Exec($@"insert into tb_student_fee_dues (student_fee_item_id, student_id, school_id, due_date, amount_due, amount_paid)
                       values ((select max(student_fee_item_id) from tb_student_fee_items where student_id='{S}' and academic_year_id={y2}), '{S}', '{A}', '2027-04-05', 500, 0)");
            var d1 = db.Scalar<long>($"select min(d.fee_due_id) from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where d.student_id='{S}' and i.academic_year_id={Y1} and d.amount_due>d.amount_paid");
            var d2 = db.Scalar<long>($"select max(fee_due_id) from tb_student_fee_dues where student_id='{S}' and due_date='2027-04-05'");
            return new World(y2, d1, d2, c1, c2, s1, s2);
        }

        private static List<PaymentAllocationRequest> Alloc(params (long due, decimal amount)[] a) => a.Select(x => new PaymentAllocationRequest { FeeDueId = x.due, Amount = x.amount }).ToList();

        // ------------------------------------------------------------------------------------------------ receipts
        [DbFact]
        public async Task A_receipt_shows_the_students_class_in_the_year_of_the_payment_not_the_current_year()
        {
            using var db = new TaxDb();
            var w = Build(db);
            Assert.NotEqual(w.Class1, w.Class2);
            var paid = await db.Pay(S, 200, allocations: Alloc((w.Due2, 200)));
            Assert.True(paid.Status, paid.Message);
            var r = paid.Data!;
            Assert.Equal(w.Class2, r.Student.ClassName);                  // his class IN 2027-28
            Assert.Equal("2027-28", r.Student.AcademicYearName);
            Assert.Equal(new[] { "2027-28" }, r.Student.AcademicYearNames);
            Assert.All(r.Lines, l => Assert.Equal("2027-28", l.AcademicYearName));

            // a payment of a year-1 installment still shows year 1's class, although year 1 is the current year here and year 2 is just another year
            var back = await db.Pay(S, 100, allocations: Alloc((w.Due1, 100)));
            Assert.Equal(w.Class1, back.Data!.Student.ClassName);
            Assert.Equal("2026-27", back.Data.Student.AcademicYearName);

            // the receipt read again later, and by receipt id, says the same
            var again = await db.Payments().GetReceiptAsync(A, r.PaymentId, default);
            Assert.Equal(w.Class2, again.Data!.Student.ClassName);
        }

        [DbFact]
        public async Task A_payment_that_spans_years_names_the_latest_year_and_every_year_on_its_lines()
        {
            using var db = new TaxDb();
            var w = Build(db);
            var paid = await db.Pay(S, 300, allocations: Alloc((w.Due1, 100), (w.Due2, 200)));
            Assert.True(paid.Status, paid.Message);
            var r = paid.Data!;
            Assert.Equal("2027-28", r.Student.AcademicYearName);
            Assert.Equal(w.Class2, r.Student.ClassName);
            Assert.Equal(new[] { "2026-27", "2027-28" }, r.Student.AcademicYearNames);
            Assert.Equal(new[] { "2026-27", "2027-28" }, r.Lines.Select(l => l.AcademicYearName).OrderBy(x => x));
        }

        [DbFact]
        public async Task A_receipt_keeps_the_fee_type_name_it_was_paid_under_after_the_fee_type_is_renamed()
        {
            using var db = new TaxDb();
            var w = Build(db);
            var first = (await db.Pay(S, 100, allocations: Alloc((w.Due1, 100)))).Data!;
            var original = first.Lines.Single().FeeTypeName;
            Assert.False(string.IsNullOrWhiteSpace(original));
            var typeId = db.Scalar<long>($"select i.fee_type_id from tb_student_fee_dues d join tb_student_fee_items i on i.student_fee_item_id=d.student_fee_item_id where d.fee_due_id={w.Due1}");
            db.Exec($"update tb_fee_types set fee_type_name='Renamed Head' where fee_type_id={typeId}");

            var reread = (await db.Payments().GetReceiptAsync(A, first.PaymentId, default)).Data!;
            Assert.Equal(original, reread.Lines.Single().FeeTypeName);                 // the old receipt is not rewritten
            var second = (await db.Pay(S, 50, allocations: Alloc((w.Due1, 50)))).Data!;
            Assert.Equal("Renamed Head", second.Lines.Single().FeeTypeName);           // a new payment is taken under the new name
            Assert.Equal(original, db.Scalar<string>($"select fee_type_name_snapshot from tb_fee_payment_allocations where payment_id={first.PaymentId}"));
        }

        [DbFact]
        public void Every_existing_payment_line_has_a_fee_type_name_and_the_database_refuses_one_without()
        {
            using var db = new TaxDb();
            Assert.Equal(0, db.Scalar<int>("select count(*) from tb_fee_payment_allocations where fee_type_name_snapshot is null or btrim(fee_type_name_snapshot) = ''"));
            var ex = Assert.Throws<PostgresException>(() => db.Exec($@"insert into tb_fee_payment_allocations (payment_id, fee_due_id, allocated_amount, concession_amount, fee_type_name_snapshot)
                       select payment_id, fee_due_id, 1, 0, '   ' from tb_fee_payment_allocations limit 1"));
            Assert.Equal("23514", ex.SqlState);
        }

        // ------------------------------------------------------------------------------------------------ concessions per year
        [DbFact]
        public void A_concession_is_unique_per_student_concession_and_year_so_the_same_one_can_be_given_in_another_year()
        {
            using var db = new TaxDb();
            var w = Build(db);
            db.Exec($"insert into tb_student_fee_concessions (student_id, concession_id, approved_by, academic_year_id) values ('{S}', 5, {TaxDb.UserA}, {Y1})");
            db.Exec($"insert into tb_student_fee_concessions (student_id, concession_id, approved_by, academic_year_id) values ('{S}', 5, {TaxDb.UserA}, {w.Y2})");   // another year: fine
            var ex = Assert.Throws<PostgresException>(() => db.Exec($"insert into tb_student_fee_concessions (student_id, concession_id, approved_by, academic_year_id) values ('{S}', 5, {TaxDb.UserA}, {Y1})"));
            Assert.Equal("23505", ex.SqlState);
        }

        [DbFact]
        public async Task A_concession_given_for_one_year_does_not_apply_to_another_years_fees()
        {
            using var db = new TaxDb();
            var w = Build(db);
            db.Exec($"insert into tb_student_fee_concessions (student_id, concession_id, approved_by, academic_year_id) values ('{S}', 5, {TaxDb.UserA}, {Y1})");
            var repo = new StudentFeeRepository(db.Ctx());
            Assert.Single(await repo.GetAppliedConcessionsAsync(A, S, Y1, default));
            Assert.Empty(await repo.GetAppliedConcessionsAsync(A, S, w.Y2, default));
        }

        [DbFact]
        public async Task Choosing_a_concession_at_payment_is_a_duplicate_only_for_the_year_of_the_installments_being_paid()
        {
            using var db = new TaxDb();
            var w = Build(db);
            db.Exec($"insert into tb_fee_concessions (school_id, concession_name, concession_code, concession_type, value, applicable_fee_type_id) values ('{A}', 'Year Test', 'ZZYT', 'FIXED', 10, null)");
            var concession = db.Scalar<long>($"select concession_id from tb_fee_concessions where concession_code='ZZYT'");
            db.Exec($"insert into tb_student_fee_concessions (student_id, concession_id, approved_by, academic_year_id) values ('{S}', {concession}, {TaxDb.UserA}, {Y1})");

            Task<ApiResponse<FeeReceiptDto>> Pay(long due, decimal amount) => db.Payments().RecordPaymentAsync(A, S, TaxDb.UserA,
                new RecordPaymentRequest { Amount = amount, PaymentMode = "Cash", ConcessionId = concession, ConcessionReason = "test", Allocations = Alloc((due, amount)) }, default);

            var sameYear = await Pay(w.Due1, 100);                    // assigned for 2026-27, paying a 2026-27 installment: would discount twice
            Assert.Equal(409, sameYear.StatusCode);
            var otherYear = await Pay(w.Due2, 100);                   // paying a 2027-28 installment: the 2026-27 assignment does not apply
            Assert.True(otherYear.Status, otherYear.Message);
            Assert.Equal(10m, otherYear.Data!.ConcessionAmount);
        }

        [DbFact]
        public async Task Removing_a_concession_names_the_year_when_the_student_has_it_in_several()
        {
            using var db = new TaxDb();
            var w = Build(db);
            db.Exec($"insert into tb_student_fee_concessions (student_id, concession_id, approved_by, academic_year_id) values ('{S}', 5, {TaxDb.UserA}, {Y1}), ('{S}', 5, {TaxDb.UserA}, {w.Y2})");
            var c = Services.Fees(db);
            Assert.Equal(400, (await c.RemoveConcessionAsync(A, S, 5, null, default)).StatusCode);          // which year?
            Assert.Equal(404, (await c.RemoveConcessionAsync(A, S, 5, 99999999, default)).StatusCode);       // not that year
            Assert.Equal(404, (await c.RemoveConcessionAsync("APS261013001", S, 5, Y1, default)).StatusCode); // another school's student
            var removed = await c.RemoveConcessionAsync(A, S, 5, w.Y2, default);
            Assert.True(removed.Status, removed.Message);
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_student_fee_concessions where student_id='{S}' and concession_id=5"));
            Assert.Equal(Y1, db.Scalar<long>($"select academic_year_id from tb_student_fee_concessions where student_id='{S}' and concession_id=5"));
            // with one year left the year is not needed
            Assert.True((await c.RemoveConcessionAsync(A, S, 5, null, default)).Status);
        }

        // ------------------------------------------------------------------------------------------------ profile
        [DbFact]
        public async Task The_profile_lists_every_enrolment_newest_year_first()
        {
            using var db = new TaxDb();
            var w = Build(db);
            var students = new StudentService(new StudentRepository(db.Ctx()), Fake.Of<IFileStorageService>(), NullLogger<StudentService>.Instance);
            var p = (await students.GetStudentProfileAsync(A, S, false, default)).Data!;
            Assert.Equal(2, p.Enrollments.Count);
            Assert.Equal(new[] { "2027-28", "2026-27" }, p.Enrollments.Select(e => e.AcademicYearName));
            Assert.Equal(w.Class2, p.Enrollments[0].ClassName);
            Assert.Equal("9", p.Enrollments[0].RollNumber);
            Assert.True(p.Enrollments[1].IsCurrentYear);
            Assert.False(p.Enrollments[0].YearIsClosed);
            // the single-enrolment fields are still the current year's
            Assert.Equal("2026-27", p.AcademicYearName);
            Assert.Equal(w.Class1, p.ClassName);
            Assert.Equal(404, (await students.GetStudentProfileAsync("APS261013001", S, false, default)).StatusCode);   // another school's student
        }

        // ------------------------------------------------------------------------------------------------ migration
        [DbFact]
        public void The_migration_refuses_a_concession_whose_year_cannot_be_told_and_changes_nothing()
        {
            // a scratch copy of the template BEFORE 1b: the ambiguity is created, then the script is run against it
            var admin = new NpgsqlConnectionStringBuilder(TaxDb.Server!) { Database = "postgres", Pooling = false };
            var name = "smss_t_mig1b_" + Guid.NewGuid().ToString("N")[..8];
            using (var c = new NpgsqlConnection(admin.ConnectionString)) { c.Open(); new NpgsqlCommand($"CREATE DATABASE \"{name}\" TEMPLATE smss_tpl_1a", c) { CommandTimeout = 300 }.ExecuteNonQuery(); }
            try
            {
                var conn = new NpgsqlConnectionStringBuilder(TaxDb.Server!) { Database = name, Pooling = false }.ConnectionString;
                using var db = new NpgsqlConnection(conn); db.Open();
                void Exec(string sql) { using var cmd = new NpgsqlCommand(sql, db); cmd.ExecuteNonQuery(); }
                // GHS001ST052 has a concession and one enrolment; give it a second enrolment in another year: now its year is ambiguous
                Exec("insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('GHS261103002', '2030-31', '2030-04-01', '2031-03-31', false, 1)");
                Exec(@"insert into tb_academic_year_classes (school_id, academic_year_id, class_id, status_id) select 'GHS261103002', (select max(academic_year_id) from tb_academic_years where school_id='GHS261103002'), s.class_id, 1 from tb_student_enrollments e join tb_sections s on s.section_id=e.class_section_id where e.student_id='GHS001ST052'");
                Exec(@"insert into tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, status_id) select 'GHS261103002', (select max(academic_year_id) from tb_academic_years where school_id='GHS261103002'), s.class_id, s.section_id, 1 from tb_student_enrollments e join tb_sections s on s.section_id=e.class_section_id where e.student_id='GHS001ST052'");
                Exec(@"insert into tb_student_enrollments (school_id, student_id, academic_year_id, class_section_id, enrollment_date, admission_type_id, status_id) select school_id, student_id, (select max(academic_year_id) from tb_academic_years where school_id='GHS261103002'), class_section_id, current_date, admission_type_id, 1 from tb_student_enrollments where student_id='GHS001ST052'");

                var script = File.ReadAllText(Path.Combine(RepoDocs(), "smss-yc-1b-RUN-ME.sql"));
                var ex = Assert.Throws<PostgresException>(() => Exec(script.Replace("\r", "")));
                Assert.Contains("PRE-CHECK FAILED", ex.MessageText);
                Assert.Contains("GHS001ST052", ex.MessageText);
                Exec("ROLLBACK");   // the script opened its own transaction; psql would end it the same way
                using var check = new NpgsqlCommand("select count(*) from information_schema.columns where table_name='tb_student_fee_concessions' and column_name='academic_year_id'", db);
                Assert.Equal(0L, check.ExecuteScalar());          // nothing was changed
            }
            finally
            {
                NpgsqlConnection.ClearAllPools();
                using var c = new NpgsqlConnection(admin.ConnectionString); c.Open();
                new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", c).ExecuteNonQuery();
            }
        }

        private static string RepoDocs()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Docs"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "Docs");
        }

        // ------------------------------------------------------------------------------------------------ next class on update
        private static ClassService Classes(TaxDb db) => new(new ClassRepository(db.Ctx()), NullLogger<ClassService>.Instance);

        [DbFact]
        public void On_update_an_omitted_next_class_is_unchanged_and_an_explicit_null_clears_it()
        {
            using var db = new TaxDb();
            var svc = Classes(db);
            long New(string n, int o) => ((ClassResponse)svc.CreateAsync(A, new CreateClassRequest { ClassName = n, SequenceOrder = o }).Result.Data!).ClassId;
            var x = New("ZN1", 71); var y = New("ZN2", 72);

            // the way the API receives it: JSON, where a property is either present (null included) or absent
            UpdateClassRequest Json(string body) => JsonSerializer.Deserialize<UpdateClassRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.True(svc.UpdateAsync(A, x, Json($"{{\"className\":\"ZN1\",\"sequenceOrder\":71,\"nextClassId\":{y}}}")).Result.Status);
            Assert.Equal(y, db.Scalar<long>($"select next_class_id from tb_classes where class_id={x}"));

            var renamed = svc.UpdateAsync(A, x, Json("{\"className\":\"ZN1b\",\"sequenceOrder\":71}")).Result;     // next class not mentioned: unchanged
            Assert.True(renamed.Status, renamed.Message);
            Assert.Equal(y, db.Scalar<long>($"select next_class_id from tb_classes where class_id={x}"));
            Assert.Equal(y, ((ClassResponse)renamed.Data!).NextClassId);

            var cleared = svc.UpdateAsync(A, x, Json("{\"className\":\"ZN1b\",\"sequenceOrder\":71,\"nextClassId\":null}")).Result;   // explicit null: cleared
            Assert.True(cleared.Status, cleared.Message);
            Assert.Equal(0, db.Scalar<int>($"select count(*) from tb_classes where class_id={x} and next_class_id is not null"));
            Assert.Null(((ClassResponse)cleared.Data!).NextClassId);
        }
    }

    /// <summary>Service builders the 1b tests share.</summary>
    internal static class Services
    {
        public static StudentFeeService Fees(TaxDb db)
        {
            var c = db.Ctx();
            var billing = new StudentFeeBillingService(new StudentFeeBillingRepository(c), new StudentFeeRepository(c), NullLogger<StudentFeeBillingService>.Instance);
            return new StudentFeeService(new StudentFeeRepository(c), billing, NullLogger<StudentFeeService>.Instance);
        }
    }
}
