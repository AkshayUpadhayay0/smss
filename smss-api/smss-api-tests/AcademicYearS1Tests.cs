using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using smss_api_db_layer.repository;
using smss_api_service_layer.dto;
using smss_api_service_layer.service;
using Xunit;

namespace smss_api_tests
{
    /// <summary>
    /// Academic years, stage S1 (Docs/academic-year-config-design.md): one current year per school and no overlapping Active years (both in the DATABASE),
    /// close / reopen, date and deactivation guards, and the closed-year block in the services that exist today.
    /// The [DbFact] tests run only with SMSS_TEST_PG set (a scratch server) against a template that already has Docs/smss-acad-s1-years-RUN-ME.sql applied.
    /// </summary>
    public class AcademicYearS1Tests
    {
        private const string School = "ZZAY0000001", Other = "ZZAY0000002";
        private const string Active = "(select sid from lut_status where sname='Active' and stype='general status')";

        private static AcademicYearService Svc(TaxDb db) => new(new AcademicYearRepository(db.Ctx()), NullLogger<AcademicYearService>.Instance);
        private static void NewSchool(TaxDb db, string id) =>
            db.Exec($"insert into tb_schools (school_id, school_code, school_name, school_status_id) values ('{id}', '{id}-C', 'Year test {id}', {Active})");
        private static CreateAcademicYearRequest Req(string name, string from, string to) => new() { YearName = name, StartDate = DateOnly.Parse(from), EndDate = DateOnly.Parse(to) };
        private static async Task<AcademicYearResponse> Make(TaxDb db, string school, string name, string from, string to)
        {
            var r = await Svc(db).CreateAsync(school, Req(name, from, to));
            Assert.True(r.Status, r.Message);
            return (AcademicYearResponse)r.Data!;
        }
        private static AcademicYearResponse Data(smss_api_service_layer.dto.ApiResponse<object> r) { Assert.True(r.Status, r.Message); return (AcademicYearResponse)r.Data!; }

        // ---------------------------------------------------------------- pure
        [Theory]
        [InlineData(true, true, "2026-04-01", "2027-03-31", "2026-11-21", "CLOSED")]      // closed wins
        [InlineData(false, true, "2026-04-01", "2027-03-31", "2026-11-21", "CURRENT")]
        [InlineData(false, false, "2027-04-01", "2028-03-31", "2026-11-21", "UPCOMING")]
        [InlineData(false, false, "2025-04-01", "2026-03-31", "2026-11-21", "PAST")]
        [InlineData(false, false, "2026-04-01", "2027-03-31", "2026-11-21", "IN_SESSION")]
        [InlineData(false, false, "2026-04-01", "2027-03-31", "2026-04-01", "IN_SESSION")]  // first day is inside
        [InlineData(false, false, "2026-04-01", "2027-03-31", "2027-03-31", "IN_SESSION")]  // last day is inside
        public void Year_state_is_derived_from_the_flags_and_today(bool closed, bool current, string start, string end, string today, string expected) =>
            Assert.Equal(expected, AcademicYearStates.Of(closed, current, DateOnly.Parse(start), DateOnly.Parse(end), DateOnly.Parse(today)));

        // ---------------------------------------------------------------- the database rules
        [DbFact]
        public void Database_allows_one_current_year_and_no_overlapping_active_years()
        {
            using var db = new TaxDb();
            NewSchool(db, School);
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{School}', 'A', '2026-04-01', '2027-03-31', true, {Active})");

            var second = Assert.Throws<PostgresException>(() => db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{School}', 'B', '2028-04-01', '2029-03-31', true, {Active})"));
            Assert.Equal("uq_tb_ay_one_current", second.ConstraintName);

            var overlap = Assert.Throws<PostgresException>(() => db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{School}', 'C', '2027-03-31', '2028-03-30', false, {Active})"));
            Assert.Equal("ex_tb_ay_no_overlap", overlap.ConstraintName);   // inclusive: a year may not start on the day another ends

            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{School}', 'D', '2027-04-01', '2028-03-31', false, {Active})");   // the next day is fine
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{School}', 'E', '2026-06-01', '2026-09-30', false, (select sid from lut_status where sname='Inactive' and stype='general status'))");   // a retired year may overlap

            var closedCurrent = Assert.Throws<PostgresException>(() => db.Exec($"update tb_academic_years set closed_at = now() where school_id='{School}' and year_name='A'"));
            Assert.Equal("chk_tb_ay_closed_not_current", closedCurrent.ConstraintName);

            NewSchool(db, Other);   // another school is a separate world: the same dates and its own current year are fine
            db.Exec($"insert into tb_academic_years (school_id, year_name, start_date, end_date, is_current, status_id) values ('{Other}', 'A', '2026-04-01', '2027-03-31', true, {Active})");
        }

        // ---------------------------------------------------------------- the service rules
        [DbFact]
        public async Task Create_refuses_an_overlap_names_the_other_year_and_allows_adjacent_years()
        {
            using var db = new TaxDb();
            NewSchool(db, School);
            var first = await Make(db, School, "2026-27", "2026-04-01", "2027-03-31");
            Assert.True(first.IsCurrent);
            Assert.Equal("CURRENT", first.State);

            var clash = await Svc(db).CreateAsync(School, Req("2027-28", "2027-03-31", "2028-03-30"));
            Assert.False(clash.Status);
            Assert.Equal(409, clash.StatusCode);
            Assert.Contains("2026-27", clash.Message);

            var next = await Make(db, School, "2027-28", "2027-04-01", "2028-03-31");
            Assert.False(next.IsCurrent);
            Assert.Equal("UPCOMING", next.State);
            var prev = await Make(db, School, "2025-26", "2025-04-01", "2026-03-31");
            Assert.Equal("PAST", prev.State);

            // another school's years never matter
            NewSchool(db, Other);
            await Make(db, Other, "2026-27", "2026-04-01", "2027-03-31");
        }

        [DbFact]
        public async Task A_retired_year_may_overlap_but_cannot_be_reactivated_into_an_overlap()
        {
            using var db = new TaxDb();
            NewSchool(db, School);
            await Make(db, School, "2026-27", "2026-04-01", "2027-03-31");
            var spare = await Make(db, School, "2027-28", "2027-04-01", "2028-03-31");
            Assert.True(Data(await Svc(db).ToggleStatusAsync(School, spare.AcademicYearId)).StatusName == "Inactive");

            await Make(db, School, "2027-summer", "2027-05-01", "2027-07-31");     // the inactive year no longer blocks these dates
            var again = await Svc(db).ToggleStatusAsync(School, spare.AcademicYearId);
            Assert.Equal(409, again.StatusCode);
            Assert.Contains("overlap", again.Message);
        }

        [DbFact]
        public async Task Closing_and_reopening_follow_the_rules()
        {
            using var db = new TaxDb();
            NewSchool(db, School);
            var cur = await Make(db, School, "2026-27", "2026-04-01", "2027-03-31");
            var past = await Make(db, School, "2025-26", "2025-04-01", "2026-03-31");

            var closeCurrent = await Svc(db).CloseAsync(School, cur.AcademicYearId, TaxDb.UserA, new CloseAcademicYearRequest());
            Assert.Equal(409, closeCurrent.StatusCode);
            Assert.Contains("current", closeCurrent.Message);

            var closed = Data(await Svc(db).CloseAsync(School, past.AcademicYearId, TaxDb.UserA, new CloseAcademicYearRequest { Note = "  audited  " }));
            Assert.True(closed.IsClosed);
            Assert.Equal("CLOSED", closed.State);
            Assert.Equal("audited", closed.CloseNote);
            Assert.NotNull(closed.ClosedAt);
            Assert.Equal(TaxDb.UserA, db.Scalar<long>($"select closed_by from tb_academic_years where academic_year_id={past.AcademicYearId}"));

            Assert.Equal(409, (await Svc(db).CloseAsync(School, past.AcademicYearId, TaxDb.UserA, new CloseAcademicYearRequest())).StatusCode);          // already closed
            Assert.Equal(409, (await Svc(db).SetCurrentAsync(School, past.AcademicYearId)).StatusCode);                                            // a closed year can't become current
            var dates = await Svc(db).UpdateAsync(School, past.AcademicYearId, new UpdateAcademicYearRequest { YearName = "2025-26", StartDate = DateOnly.Parse("2025-04-02"), EndDate = DateOnly.Parse("2026-03-31") });
            Assert.Equal(409, dates.StatusCode);                                                                                                  // dates are frozen while closed
            Assert.True((await Svc(db).UpdateAsync(School, past.AcademicYearId, new UpdateAcademicYearRequest { YearName = "2025-2026", StartDate = DateOnly.Parse("2025-04-01"), EndDate = DateOnly.Parse("2026-03-31") })).Status);   // a name edit is fine

            Assert.Equal(404, (await Svc(db).CloseAsync(Other, past.AcademicYearId, TaxDb.UserA, new CloseAcademicYearRequest())).StatusCode);              // another school's year
            Assert.Equal(404, (await Svc(db).ReopenAsync(Other, past.AcademicYearId)).StatusCode);

            var reopened = Data(await Svc(db).ReopenAsync(School, past.AcademicYearId));
            Assert.False(reopened.IsClosed);
            Assert.Equal("PAST", reopened.State);
            Assert.Null(reopened.CloseNote);
            Assert.Equal(409, (await Svc(db).ReopenAsync(School, past.AcademicYearId)).StatusCode);                                               // not closed any more
        }

        [DbFact]
        public async Task Set_current_swaps_the_one_current_year_without_tripping_the_index()
        {
            using var db = new TaxDb();
            NewSchool(db, School);
            var a = await Make(db, School, "2026-27", "2026-04-01", "2027-03-31");
            var b = await Make(db, School, "2027-28", "2027-04-01", "2028-03-31");
            Data(await Svc(db).SetCurrentAsync(School, b.AcademicYearId));
            Assert.Equal(1, db.Scalar<int>($"select count(*) from tb_academic_years where school_id='{School}' and is_current"));
            Assert.True(db.Scalar<bool>($"select is_current from tb_academic_years where academic_year_id={b.AcademicYearId}"));
            Data(await Svc(db).SetCurrentAsync(School, a.AcademicYearId));
            Assert.True(db.Scalar<bool>($"select is_current from tb_academic_years where academic_year_id={a.AcademicYearId}"));
        }

        [DbFact]
        public async Task Dates_cannot_leave_out_records_and_a_year_with_data_cannot_be_deactivated()
        {
            using var db = new TaxDb();
            NewSchool(db, School);
            await Make(db, School, "2026-27", "2026-04-01", "2027-03-31");
            var past = await Make(db, School, "2025-26", "2025-04-01", "2026-03-31");
            db.Exec($"insert into tb_school_events (school_id, academic_year_id, event_title, start_date, end_date, event_category_id, created_by, status_id) " +
                    $"values ('{School}', {past.AcademicYearId}, 'Annual day', '2025-06-10', '2025-06-12', (select min(event_category_id) from lut_event_category), {TaxDb.UserA}, {Active})");

            var u = (await Svc(db).GetAllAsync(School)).Data as List<AcademicYearResponse>;
            Assert.Equal(1, u!.Single(x => x.AcademicYearId == past.AcademicYearId).Usage.Events);

            UpdateAcademicYearRequest Range(string from, string to) => new() { YearName = "2025-26", StartDate = DateOnly.Parse(from), EndDate = DateOnly.Parse(to) };
            var late = await Svc(db).UpdateAsync(School, past.AcademicYearId, Range("2025-06-11", "2026-03-31"));
            Assert.Equal(409, late.StatusCode);
            Assert.Contains("1 calendar event", late.Message);
            Assert.Equal(409, (await Svc(db).UpdateAsync(School, past.AcademicYearId, Range("2025-04-01", "2025-06-11"))).StatusCode);
            Assert.True((await Svc(db).UpdateAsync(School, past.AcademicYearId, Range("2025-06-10", "2025-06-12"))).Status);   // exactly the event's days is fine
            Assert.True((await Svc(db).UpdateAsync(School, past.AcademicYearId, Range("2025-04-01", "2026-03-31"))).Status);

            var off = await Svc(db).ToggleStatusAsync(School, past.AcademicYearId);
            Assert.Equal(409, off.StatusCode);
            Assert.Contains("already has data", off.Message);

            var empty = await Make(db, School, "2030-31", "2030-04-01", "2031-03-31");
            Assert.Equal("Inactive", Data(await Svc(db).ToggleStatusAsync(School, empty.AcademicYearId)).StatusName);   // no data: can be retired
        }

        // ---------------------------------------------------------------- the closed-year block
        [DbFact]
        public async Task A_closed_year_refuses_class_teacher_timetable_and_attendance_writes_but_fee_payments_still_work()
        {
            using var db = new TaxDb();
            NewSchool(db, School);
            // today (2026-11-21 in the tests' clock is whatever "today" is): the year covering today is made NON-current so it can be closed
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
            var covering = await Make(db, School, "covering", today.AddMonths(-3).ToString("yyyy-MM-dd"), today.AddMonths(6).ToString("yyyy-MM-dd"));
            var later = await Make(db, School, "later", today.AddMonths(7).ToString("yyyy-MM-dd"), today.AddMonths(19).ToString("yyyy-MM-dd"));
            Data(await Svc(db).SetCurrentAsync(School, later.AcademicYearId));
            db.Exec($"insert into tb_classes (school_id, class_name, sequence_order, status_id) values ('{School}', 'Class 1', 1, {Active})");
            db.Exec($"insert into tb_sections (school_id, class_id, section_name, status_id) select school_id, class_id, 'A', {Active} from tb_classes where school_id='{School}'");
            var section = db.Scalar<long>($"select section_id from tb_sections where school_id='{School}'");

            var ctx = db.Ctx();
            var teachers = new ClassTeacherService(new ClassTeacherRepository(db.Ctx()), NullLogger<ClassTeacherService>.Instance);
            var attendance = new AttendanceService(new AttendanceRepository(db.Ctx()), teachers, NullLogger<AttendanceService>.Instance);
            var timetable = new TimetableService(new TimetableRepository(db.Ctx()), NullLogger<TimetableService>.Instance);
            AssignClassTeacherRequest Ct() => new() { AcademicYearId = covering.AcademicYearId, SectionId = section, EmployeeId = "NOBODY" };
            AssignTimetableRequest Tt() => new() { AcademicYearId = covering.AcademicYearId, SectionId = section, DayOfWeek = 1, PeriodId = 1, SubjectId = 1, EmployeeId = "NOBODY" };
            SubmitAttendanceRequest At() => new() { SectionId = section, Date = today, Records = new() { new AttendanceRecordRequest { StudentId = "X", AttendanceStatusId = 1 } } };

            // open year: these get past the closed check and fail later for their own reasons (never with the closed message)
            Assert.DoesNotContain("closed", (await teachers.AssignAsync(School, 1, Ct(), default)).Message);
            Assert.DoesNotContain("closed", (await timetable.AssignAsync(School, 1, Tt(), default)).Message);
            Assert.DoesNotContain("closed", (await attendance.SubmitAsync(School, 1, true, At(), default)).Message);

            Data(await Svc(db).CloseAsync(School, covering.AcademicYearId, TaxDb.UserA, new CloseAcademicYearRequest()));

            foreach (var (name, r) in new (string, (bool Status, int Code, string Message))[]
            {
                ("class teacher assign", await Pack(teachers.AssignAsync(School, 1, Ct(), default))),
                ("class teacher unassign", await Pack(teachers.UnassignAsync(School, TaxDb.UserA, new UnassignClassTeacherRequest { AcademicYearId = covering.AcademicYearId, SectionId = section }, default))),
                ("timetable assign", await Pack(timetable.AssignAsync(School, 1, Tt(), default))),
                ("timetable unassign", await Pack(timetable.UnassignAsync(School, new UnassignTimetableRequest { AcademicYearId = covering.AcademicYearId, SectionId = section, DayOfWeek = 1, PeriodId = 1 }, default))),
                ("attendance submit", await Pack(attendance.SubmitAsync(School, 1, true, At(), default))),
            })
            {
                Assert.False(r.Status, name);
                Assert.Equal(409, r.Code);
                Assert.Contains("closed", r.Message);
            }

            // the same services keep working for the OTHER (open) year of the school: only the closed one is blocked
            var open = await teachers.AssignAsync(School, 1, new AssignClassTeacherRequest { AcademicYearId = later.AcademicYearId, SectionId = section, EmployeeId = "NOBODY" }, default);
            Assert.DoesNotContain("closed", open.Message);

            // reads of a closed year are open
            Assert.True((await teachers.ListAsync(School, covering.AcademicYearId, default)).Status);

            // fee payments are never blocked by a closed year: GHS year 41 has dues; make another year current, close 41, pay
            var ghs = new AcademicYearService(new AcademicYearRepository(db.Ctx()), NullLogger<AcademicYearService>.Instance);
            var y2 = await ghs.CreateAsync(TaxDb.SchoolA, Req("2027-28", "2027-04-01", "2028-03-31"));
            Assert.True(y2.Status, y2.Message);
            Data(await ghs.SetCurrentAsync(TaxDb.SchoolA, ((AcademicYearResponse)y2.Data!).AcademicYearId));
            Data(await ghs.CloseAsync(TaxDb.SchoolA, TaxDb.YearA, TaxDb.UserA, new CloseAcademicYearRequest()));
            var paid = await db.Pay("GHS001ST001", 100);
            Assert.True(paid.Status, paid.Message);
            var receipt = await db.Payments().GetReceiptAsync(TaxDb.SchoolA, paid.Data!.PaymentId, default);
            Assert.True(receipt.Status, receipt.Message);
            var voided = await db.Payments().VoidPaymentAsync(TaxDb.SchoolA, paid.Data!.PaymentId, TaxDb.UserA, new VoidPaymentRequest { VoidReason = "closed-year check" }, default);
            Assert.True(voided.Status, voided.Message);
        }

        private static async Task<(bool, int, string)> Pack<T>(Task<smss_api_service_layer.dto.ApiResponse<T>> t) { var r = await t; return (r.Status, r.StatusCode, r.Message); }
    }
}
