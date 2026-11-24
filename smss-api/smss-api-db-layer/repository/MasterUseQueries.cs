using Microsoft.EntityFrameworkCore;
using Npgsql;
using smss_api_db_layer.context;

namespace smss_api_db_layer.repository
{
    /// <summary>
    /// "Is this class / section / subject still in use?" for the master guards. In use = referenced by live data of an OPEN academic year (closed_at is null,
    /// whether the year is Active or Inactive). Every query is school-scoped; statuses are resolved by name (never by id). Each returns short human reasons ("3 enrolled students").
    /// </summary>
    internal static class MasterUseQueries
    {
        // (label, table, section/class/subject column, extra live-row condition). Table and column names come from these constant lists, never from a caller.
        private const string ActiveSql = "(SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status' LIMIT 1)";

        private static readonly (string Label, string Table, string Where)[] ClassChecks =
        {
            ("enrolled student", "tb_student_enrollments e JOIN public.tb_sections s ON s.school_id = e.school_id AND s.section_id = e.class_section_id JOIN public.tb_academic_years y ON y.school_id = e.school_id AND y.academic_year_id = e.academic_year_id",
             "e.school_id = @s AND s.class_id = @id AND y.closed_at IS NULL AND e.status_id = " + ActiveSql),
            ("fee structure", "tb_fee_structures f JOIN public.tb_academic_years y ON y.school_id = f.school_id AND y.academic_year_id = f.academic_year_id",
             "f.school_id = @s AND f.class_id = @id AND f.isactive AND y.closed_at IS NULL"),
            ("subject mapping", "tb_class_subjects c JOIN public.tb_academic_years y ON y.school_id = c.school_id AND y.academic_year_id = c.academic_year_id",
             "c.school_id = @s AND c.class_id = @id AND c.status_id = " + ActiveSql + " AND y.closed_at IS NULL"),
            ("class teacher", "tb_class_teacher_assignments a JOIN public.tb_academic_years y ON y.school_id = a.school_id AND y.academic_year_id = a.academic_year_id",
             "a.school_id = @s AND a.class_id = @id AND a.status_id = " + ActiveSql + " AND a.effective_to IS NULL AND y.closed_at IS NULL"),
            ("timetable slot", "tb_timetable_entries t JOIN public.tb_academic_years y ON y.school_id = t.school_id AND y.academic_year_id = t.academic_year_id",
             "t.school_id = @s AND t.class_id = @id AND t.status_id = " + ActiveSql + " AND y.closed_at IS NULL"),
        };

        private static readonly (string Label, string Table, string Where)[] SectionChecks =
        {
            ("enrolled student", "tb_student_enrollments e JOIN public.tb_academic_years y ON y.school_id = e.school_id AND y.academic_year_id = e.academic_year_id",
             "e.school_id = @s AND e.class_section_id = @id AND y.closed_at IS NULL AND e.status_id = " + ActiveSql),
            ("class teacher", "tb_class_teacher_assignments a JOIN public.tb_academic_years y ON y.school_id = a.school_id AND y.academic_year_id = a.academic_year_id",
             "a.school_id = @s AND a.section_id = @id AND a.status_id = " + ActiveSql + " AND a.effective_to IS NULL AND y.closed_at IS NULL"),
            ("timetable slot", "tb_timetable_entries t JOIN public.tb_academic_years y ON y.school_id = t.school_id AND y.academic_year_id = t.academic_year_id",
             "t.school_id = @s AND t.section_id = @id AND t.status_id = " + ActiveSql + " AND y.closed_at IS NULL"),
            ("subject override", "tb_section_subject_overrides o JOIN public.tb_academic_years y ON y.school_id = o.school_id AND y.academic_year_id = o.academic_year_id",
             "o.school_id = @s AND o.section_id = @id AND o.status_id = " + ActiveSql + " AND y.closed_at IS NULL"),
        };

        private static readonly (string Label, string Table, string Where)[] SubjectChecks =
        {
            ("class mapping", "tb_class_subjects c JOIN public.tb_academic_years y ON y.school_id = c.school_id AND y.academic_year_id = c.academic_year_id",
             "c.school_id = @s AND c.subject_id = @id AND c.status_id = " + ActiveSql + " AND y.closed_at IS NULL"),
            ("section override", "tb_section_subject_overrides o JOIN public.tb_academic_years y ON y.school_id = o.school_id AND y.academic_year_id = o.academic_year_id",
             "o.school_id = @s AND o.subject_id = @id AND o.status_id = " + ActiveSql + " AND y.closed_at IS NULL"),
            ("timetable slot", "tb_timetable_entries t JOIN public.tb_academic_years y ON y.school_id = t.school_id AND y.academic_year_id = t.academic_year_id",
             "t.school_id = @s AND t.subject_id = @id AND t.status_id = " + ActiveSql + " AND y.closed_at IS NULL"),
        };

        public static Task<List<string>> ClassAsync(dbContext db, string schoolId, long classId, CancellationToken ct = default) => RunAsync(db, ClassChecks, schoolId, classId, ct);
        public static Task<List<string>> SectionAsync(dbContext db, string schoolId, long sectionId, CancellationToken ct = default) => RunAsync(db, SectionChecks, schoolId, sectionId, ct);
        public static Task<List<string>> SubjectAsync(dbContext db, string schoolId, long subjectId, CancellationToken ct = default) => RunAsync(db, SubjectChecks, schoolId, subjectId, ct);

        public static async Task<List<string>> SectionHistoryAsync(dbContext db, string schoolId, long sectionId, CancellationToken ct = default)
        {
            var reasons = new List<string>();
            foreach (var (label, table, col) in HistoryColumns())
            {
                var n = await Count(db, $"SELECT count(*)::int AS \"Value\" FROM public.{table} WHERE school_id = @s AND {col} = @id", schoolId, sectionId, ct);
                if (n > 0) reasons.Add(Plural(n, label));
            }
            return reasons;
        }

        private static IEnumerable<(string Label, string Table, string Col)> HistoryColumns() => new (string, string, string)[]
        {
            ("enrolment", "tb_student_enrollments", "class_section_id"), ("attendance record", "tb_student_attendance", "section_id"), ("assessment", "tb_class_assessments", "section_id"),
            ("homework", "tb_homework", "section_id"), ("class teacher record", "tb_class_teacher_assignments", "section_id"), ("timetable", "tb_timetables", "section_id"),
            ("timetable slot", "tb_timetable_entries", "section_id"), ("subject override", "tb_section_subject_overrides", "section_id"),
            ("report-card setup row", "tb_report_term_section", "section_id"), ("report mark", "tb_report_marks", "section_id"), ("report entry status", "tb_report_entry_status", "section_id"),
            ("published report card", "tb_report_publications", "section_id"), ("report result", "tb_report_year_student", "section_id"),
        };

        private static async Task<List<string>> RunAsync(dbContext db, (string Label, string Table, string Where)[] checks, string schoolId, long id, CancellationToken ct)
        {
            var reasons = new List<string>();
            foreach (var (label, table, where) in checks)
            {
                var n = await Count(db, $"SELECT count(*)::int AS \"Value\" FROM public.{table} WHERE {where}", schoolId, id, ct);
                if (n > 0) reasons.Add(Plural(n, label));
            }
            return reasons;
        }

        private static string Plural(int n, string label) => $"{n} {label}{(n == 1 ? "" : "s")}";

        private static Task<int> Count(dbContext db, string sql, string schoolId, long id, CancellationToken ct) =>
            db.Database.SqlQueryRaw<int>(sql, new NpgsqlParameter("s", schoolId), new NpgsqlParameter("id", id)).SingleAsync(ct);
    }
}
