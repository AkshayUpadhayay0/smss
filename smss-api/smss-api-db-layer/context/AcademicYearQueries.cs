using Microsoft.EntityFrameworkCore;

namespace smss_api_db_layer.context
{
    // What depends on one academic year, and the first / last date any of it carries
    // (used to guard date edits and deactivation).
    public class AcademicYearUsage
    {
        public long AcademicYearId { get; set; }
        public int Enrolments { get; set; }
        public int FeeStructures { get; set; }
        public int AttendanceRows { get; set; }
        public int TimetableEntries { get; set; }
        public int ClassTeachers { get; set; }
        public int Homework { get; set; }
        public int Tests { get; set; }
        public int Events { get; set; }
        public DateOnly? MinDate { get; set; }
        public DateOnly? MaxDate { get; set; }
        public int Total => Enrolments + FeeStructures + AttendanceRows + TimetableEntries + ClassTeachers + Homework + Tests + Events;
    }

    public static class AcademicYearQueries
    {
        // One definition of "this academic year is closed" (tb_academic_years.closed_at set), shared by every repository
        // whose service must refuse writes into a closed year. Fee payments and voids do NOT use it (they stay open).
        public static Task<bool> IsYearClosedAsync(this dbContext db, string schoolId, long academicYearId, CancellationToken ct) =>
            db.AcademicYears.AsNoTracking().AnyAsync(y => y.SchoolId == schoolId && y.AcademicYearId == academicYearId && y.ClosedAt != null, ct);

        // One statement for every year of the school. Aggregated in the database; nothing is loaded to be counted.
        public static Task<List<AcademicYearUsage>> GetAcademicYearUsageAsync(this dbContext db, string schoolId, CancellationToken ct) =>
            db.Database.SqlQuery<AcademicYearUsage>($@"
SELECT y.academic_year_id AS ""AcademicYearId"",
  (SELECT count(*) FROM public.tb_student_enrollments x WHERE x.academic_year_id = y.academic_year_id)::int AS ""Enrolments"",
  (SELECT count(*) FROM public.tb_fee_structures x WHERE x.academic_year_id = y.academic_year_id)::int AS ""FeeStructures"",
  (SELECT count(*) FROM public.tb_student_attendance x WHERE x.academic_year_id = y.academic_year_id)::int AS ""AttendanceRows"",
  (SELECT count(*) FROM public.tb_timetable_entries x WHERE x.academic_year_id = y.academic_year_id)::int AS ""TimetableEntries"",
  (SELECT count(*) FROM public.tb_class_teacher_assignments x WHERE x.academic_year_id = y.academic_year_id)::int AS ""ClassTeachers"",
  (SELECT count(*) FROM public.tb_homework x WHERE x.academic_year_id = y.academic_year_id)::int AS ""Homework"",
  (SELECT count(*) FROM public.tb_class_assessments x WHERE x.academic_year_id = y.academic_year_id)::int AS ""Tests"",
  (SELECT count(*) FROM public.tb_school_events x WHERE x.academic_year_id = y.academic_year_id)::int AS ""Events"",
  (SELECT min(d) FROM (
      SELECT min(x.enrollment_date)::date AS d FROM public.tb_student_enrollments x WHERE x.academic_year_id = y.academic_year_id
      UNION ALL SELECT min(x.attendance_date) FROM public.tb_student_attendance x WHERE x.academic_year_id = y.academic_year_id
      UNION ALL SELECT min(x.start_date) FROM public.tb_school_events x WHERE x.academic_year_id = y.academic_year_id
      UNION ALL SELECT min(du.due_date) FROM public.tb_student_fee_dues du JOIN public.tb_student_fee_items i ON i.student_fee_item_id = du.student_fee_item_id WHERE i.academic_year_id = y.academic_year_id
      UNION ALL SELECT min(fd.effective_from) FROM public.tb_fee_structure_details fd JOIN public.tb_fee_structures fs ON fs.fee_structure_id = fd.fee_structure_id WHERE fs.academic_year_id = y.academic_year_id
  ) q) AS ""MinDate"",
  (SELECT max(d) FROM (
      SELECT max(x.enrollment_date)::date AS d FROM public.tb_student_enrollments x WHERE x.academic_year_id = y.academic_year_id
      UNION ALL SELECT max(x.attendance_date) FROM public.tb_student_attendance x WHERE x.academic_year_id = y.academic_year_id
      UNION ALL SELECT max(x.end_date) FROM public.tb_school_events x WHERE x.academic_year_id = y.academic_year_id
      UNION ALL SELECT max(du.due_date) FROM public.tb_student_fee_dues du JOIN public.tb_student_fee_items i ON i.student_fee_item_id = du.student_fee_item_id WHERE i.academic_year_id = y.academic_year_id
      UNION ALL SELECT max(fd.effective_from) FROM public.tb_fee_structure_details fd JOIN public.tb_fee_structures fs ON fs.fee_structure_id = fd.fee_structure_id WHERE fs.academic_year_id = y.academic_year_id
  ) q) AS ""MaxDate""
FROM public.tb_academic_years y WHERE y.school_id = {schoolId}").ToListAsync(ct);
    }
}
