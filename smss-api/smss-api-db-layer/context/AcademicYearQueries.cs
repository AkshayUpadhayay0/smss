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

        // ---- S2 offerings: which classes / sections exist in a year. The masters (tb_classes, tb_sections) never change here. ----

        /// <summary>A new class is offered in the school's CURRENT, open year (so Import Masters, the setup checklist and the dashboards see it at once). No current year: nothing to do.</summary>
        public static Task<int> OfferClassInCurrentYearAsync(this dbContext db, string schoolId, long classId, CancellationToken ct) =>
            db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO public.tb_academic_year_classes (school_id, academic_year_id, class_id, status_id)
SELECT y.school_id, y.academic_year_id, c.class_id, coalesce(c.status_id, (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status'))
FROM public.tb_academic_years y JOIN public.tb_classes c ON c.school_id = y.school_id AND c.class_id = {classId}
WHERE y.school_id = {schoolId} AND y.is_current AND y.closed_at IS NULL
ON CONFLICT (academic_year_id, class_id) DO NOTHING", ct);

        /// <summary>A new section follows its class: offered in the current, open year when its class is offered there.</summary>
        public static Task<int> OfferSectionInCurrentYearAsync(this dbContext db, string schoolId, long sectionId, CancellationToken ct) =>
            db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO public.tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, max_strength, status_id)
SELECT y.school_id, y.academic_year_id, s.class_id, s.section_id, NULL, coalesce(s.status_id, (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status'))
FROM public.tb_academic_years y
JOIN public.tb_sections s ON s.school_id = y.school_id AND s.section_id = {sectionId}
JOIN public.tb_academic_year_classes oc ON oc.academic_year_id = y.academic_year_id AND oc.class_id = s.class_id
WHERE y.school_id = {schoolId} AND y.is_current AND y.closed_at IS NULL
ON CONFLICT (academic_year_id, section_id) DO NOTHING", ct);

        /// <summary>Import Masters: the classes it just created or picked, and all their active sections, are offered in the current, open year.</summary>
        public static async Task OfferClassesWithSectionsInCurrentYearAsync(this dbContext db, string schoolId, long[] classIds, CancellationToken ct)
        {
            if (classIds.Length == 0) return;
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO public.tb_academic_year_classes (school_id, academic_year_id, class_id, status_id)
SELECT y.school_id, y.academic_year_id, c.class_id, coalesce(c.status_id, (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status'))
FROM public.tb_academic_years y JOIN public.tb_classes c ON c.school_id = y.school_id AND c.class_id = ANY({classIds})
WHERE y.school_id = {schoolId} AND y.is_current AND y.closed_at IS NULL
ON CONFLICT (academic_year_id, class_id) DO NOTHING", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO public.tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, max_strength, status_id)
SELECT y.school_id, y.academic_year_id, s.class_id, s.section_id, NULL, coalesce(s.status_id, (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status'))
FROM public.tb_academic_years y
JOIN public.tb_sections s ON s.school_id = y.school_id AND s.class_id = ANY({classIds})
JOIN public.tb_academic_year_classes oc ON oc.academic_year_id = y.academic_year_id AND oc.class_id = s.class_id
WHERE y.school_id = {schoolId} AND y.is_current AND y.closed_at IS NULL
ON CONFLICT (academic_year_id, section_id) DO NOTHING", ct);
        }

        /// <summary>The first year of a school offers every class and section that already exists (the old meaning: masters apply to every year).</summary>
        public static async Task OfferAllInYearAsync(this dbContext db, string schoolId, long academicYearId, CancellationToken ct)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO public.tb_academic_year_classes (school_id, academic_year_id, class_id, status_id)
SELECT c.school_id, {academicYearId}, c.class_id, coalesce(c.status_id, (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status'))
FROM public.tb_classes c WHERE c.school_id = {schoolId}
ON CONFLICT (academic_year_id, class_id) DO NOTHING", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO public.tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, max_strength, status_id)
SELECT s.school_id, {academicYearId}, s.class_id, s.section_id, NULL, coalesce(s.status_id, (SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status'))
FROM public.tb_sections s WHERE s.school_id = {schoolId}
ON CONFLICT (academic_year_id, section_id) DO NOTHING", ct);
        }

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
