using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    /// <summary>
    /// S7: copy configuration from one academic year to another. Each part is written as a CANDIDATE query (one row per source row, with a Reason: null = will be created,
    /// "EXISTS" = the target already has it, anything else = skipped for that reason) and an INSERT that takes exactly the candidates without a reason. The same text is used to report and to
    /// write, so a preview (run, then rolled back by the caller) and a real run can never disagree. Nothing here updates or deletes a row; the source year is only read; masters are only referenced.
    /// </summary>
    public class YearCopyRepository : IYearCopyRepository
    {
        public const string PartClassesSections = "CLASSES_SECTIONS", PartSubjects = "SUBJECTS", PartSchedules = "SCHEDULES", PartClassTeachers = "CLASS_TEACHERS", PartTimetable = "TIMETABLE", PartFeeStructures = "FEE_STRUCTURES";

        private const string Act = "(SELECT sid FROM public.lut_status WHERE sname = 'Active' AND stype = 'general status')";
        private const string Exists = "EXISTS";

        private readonly dbContext _db;
        public YearCopyRepository(dbContext db) => _db = db;

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task LockSchoolAsync(string schoolId, CancellationToken ct)
        {
            var key = "acad-setup:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public async Task<CopyYear?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct)
        {
            var activeId = await _db.LutStatus.AsNoTracking().Where(s => s.Sname == "Active" && s.Stype == "general status").Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);
            var y = await _db.AcademicYears.AsNoTracking().Where(x => x.SchoolId == schoolId && x.AcademicYearId == academicYearId).FirstOrDefaultAsync(ct);
            return y == null ? null : new CopyYear(y.AcademicYearId, y.YearName, y.StartDate, y.ClosedAt != null, y.StatusId == activeId);
        }

        public async Task AddLogAsync(TbYearCopyLog log, CancellationToken ct) => await _db.YearCopyLogs.AddAsync(log, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
        public void ClearTracker() => _db.ChangeTracker.Clear();

        public async Task<List<CopyLogRow>> GetLogAsync(string schoolId, long targetYearId, int take, CancellationToken ct)
        {
            var rows = await (from l in _db.YearCopyLogs.AsNoTracking()
                              join y in _db.AcademicYears.AsNoTracking() on l.SourceYearId equals y.AcademicYearId
                              where l.SchoolId == schoolId && l.TargetYearId == targetYearId
                              orderby l.StartedAt descending, l.CopyId descending
                              select new { l, y.YearName, By = _db.Users.Where(u => u.UserId == l.RunBy).Select(u => u.DisplayName).FirstOrDefault() }).Take(take).ToListAsync(ct);
            return rows.Select(r => new CopyLogRow(r.l, r.YearName, r.By)).ToList();
        }

        // ================================================================== the parts
        private sealed class Cand { public string? Label { get; set; } public string? Reason { get; set; } }

        private static NpgsqlParameter[] Ps(string school, CopyYear src, CopyYear tgt, long? user, string? reason = null) => new[]
        {
            new NpgsqlParameter("school", NpgsqlDbType.Varchar) { Value = school },
            new NpgsqlParameter("src", NpgsqlDbType.Bigint) { Value = src.AcademicYearId },
            new NpgsqlParameter("tgt", NpgsqlDbType.Bigint) { Value = tgt.AcademicYearId },
            new NpgsqlParameter("usr", NpgsqlDbType.Bigint) { Value = (object?)user ?? DBNull.Value },
            new NpgsqlParameter("start", NpgsqlDbType.Date) { Value = tgt.StartDate },
            new NpgsqlParameter("why", NpgsqlDbType.Varchar) { Value = (object?)reason ?? DBNull.Value },
            new NpgsqlParameter("pct", NpgsqlDbType.Numeric) { Value = 0m },
            new NpgsqlParameter("srcname", NpgsqlDbType.Varchar) { Value = src.YearName },
            new NpgsqlParameter("tgtname", NpgsqlDbType.Varchar) { Value = tgt.YearName },
        };

        private async Task StepAsync(CopyPartOutcome o, string kind, string cand, string insertInto, string insertSelect, Func<NpgsqlParameter[]> ps, CancellationToken ct)
        {
            var rows = await _db.Database.SqlQueryRaw<Cand>($"SELECT c.label AS \"Label\", c.reason AS \"Reason\" FROM ({cand}) c", ps()).ToListAsync(ct);
            o.SkippedExisting += rows.Count(r => r.Reason == Exists);
            foreach (var r in rows.Where(r => r.Reason != null && r.Reason != Exists)) o.Skipped.Add(new CopySkip(r.Reason!, r.Label ?? ""));
            o.Created[kind] = await _db.Database.ExecuteSqlRawAsync($"INSERT INTO {insertInto} SELECT {insertSelect} FROM ({cand}) c WHERE c.reason IS NULL", ps());
        }

        public async Task<CopyPartOutcome> RunPartAsync(string part, string schoolId, CopyYear source, CopyYear target, long? userId, CancellationToken ct, decimal feeIncreasePercent = 0m)
        {
            var o = new CopyPartOutcome();
            Func<NpgsqlParameter[]> ps = () => Ps(schoolId, source, target, userId, $"Copied from {source.YearName}");
            switch (part)
            {
                case PartClassesSections:
                    await StepAsync(o, "classes", ClassesCand, "public.tb_academic_year_classes (school_id, academic_year_id, class_id, status_id, created_by)", $"@school, @tgt, c.class_id, {Act}, @usr", ps, ct);
                    await StepAsync(o, "sections", SectionsCand, "public.tb_academic_year_sections (school_id, academic_year_id, class_id, section_id, max_strength, status_id, created_by)", $"@school, @tgt, c.class_id, c.section_id, c.max_strength, {Act}, @usr", ps, ct);
                    break;
                case PartSubjects:
                    await StepAsync(o, "class subjects", MappingCand, "public.tb_class_subjects (school_id, academic_year_id, class_id, subject_id, status_id)", $"@school, @tgt, c.class_id, c.subject_id, {Act}", ps, ct);
                    await StepAsync(o, "section exceptions", OverrideCand, "public.tb_section_subject_overrides (school_id, academic_year_id, class_id, section_id, subject_id, action, status_id, created_by)", $"@school, @tgt, c.class_id, c.section_id, c.subject_id, c.action, {Act}, @usr", ps, ct);
                    break;
                case PartSchedules:
                    await StepAsync(o, "schedule assignments", SchedulesCand, "public.tb_academic_year_schedules (school_id, academic_year_id, class_id, day_of_week, schedule_id, created_by)", "@school, @tgt, c.class_id, c.day_of_week, c.schedule_id, @usr", ps, ct);
                    break;
                case PartClassTeachers:
                    await StepAsync(o, "class teachers", TeachersCand, "public.tb_class_teacher_assignments (school_id, academic_year_id, class_id, section_id, employee_id, status_id, assigned_by, effective_from, assign_reason)", $"@school, @tgt, c.class_id, c.section_id, c.employee_id, {Act}, @usr, @start, @why", ps, ct);
                    break;
                case PartTimetable:
                    await TimetablesAsync(o, schoolId, source, target, userId, ct);
                    break;
                case PartFeeStructures:
                    await FeesAsync(o, schoolId, source, target, userId, feeIncreasePercent, ct);
                    break;
                default: throw new ArgumentException("Unknown part " + part);
            }
            return o;
        }

        // ================================================================== fee structures
        // A structure is copied whole, per class, or not at all: a class that already has a fee structure in the target year is skipped (never merged into, never overwritten), so a second run creates nothing.
        // Every line starts on the target year's first day. Amounts change only by the one uniform percentage, rounded to the nearest rupee; everything else is edited on the Fee Structure screen afterwards.
        private const string FeeStructCand = $@"
SELECT s.fee_structure_id, s.class_id, s.structure_name, cl.class_name AS label,
       CASE WHEN NOT s.isactive THEN 'The fee structure is inactive'
            WHEN cl.status_id IS DISTINCT FROM {Act} THEN 'The class is inactive'
            WHEN NOT EXISTS (SELECT 1 FROM public.tb_academic_year_classes t WHERE t.academic_year_id = @tgt AND t.class_id = s.class_id AND t.status_id = {Act}) THEN 'The class is not offered in the target year'
            WHEN EXISTS (SELECT 1 FROM public.tb_fee_structures t WHERE t.school_id = @school AND t.academic_year_id = @tgt AND t.class_id = s.class_id) THEN 'EXISTS'
            WHEN NOT EXISTS (SELECT 1 FROM public.tb_fee_structure_details d JOIN public.tb_fee_types ft ON ft.fee_type_id = d.fee_type_id AND ft.school_id = @school WHERE d.fee_structure_id = s.fee_structure_id AND ft.isactive) THEN 'The structure has no line with an active fee type'
       END AS reason
FROM public.tb_fee_structures s JOIN public.tb_classes cl ON cl.class_id = s.class_id AND cl.school_id = @school
WHERE s.school_id = @school AND s.academic_year_id = @src";

        private const string NewAmount = "round(d.amount * (100 + @pct) / 100, 0)";

        private async Task FeesAsync(CopyPartOutcome o, string school, CopyYear src, CopyYear tgt, long? user, decimal pct, CancellationToken ct)
        {
            NpgsqlParameter[] Ps2()
            {
                var ps = Ps(school, src, tgt, user);
                ps.First(p => p.ParameterName == "pct").Value = pct;
                return ps;
            }
            var rows = await _db.Database.SqlQueryRaw<Cand>($"SELECT c.label AS \"Label\", c.reason AS \"Reason\" FROM ({FeeStructCand}) c", Ps2()).ToListAsync(ct);
            o.SkippedExisting += rows.Count(r => r.Reason == Exists);
            foreach (var r in rows.Where(r => r.Reason != null && r.Reason != Exists)) o.Skipped.Add(new CopySkip(r.Reason!, r.Label ?? ""));

            // lines of the structures that WILL be copied but whose fee type is inactive: left out, with the reason
            var lost = await _db.Database.SqlQueryRaw<Cand>($@"SELECT ft.fee_type_name || ' in ' || c.label AS ""Label"", 'The fee type is inactive' AS ""Reason""
                FROM ({FeeStructCand}) c JOIN public.tb_fee_structure_details d ON d.fee_structure_id = c.fee_structure_id JOIN public.tb_fee_types ft ON ft.fee_type_id = d.fee_type_id AND ft.school_id = @school
                WHERE c.reason IS NULL AND NOT ft.isactive", Ps2()).ToListAsync(ct);
            foreach (var r in lost) o.Skipped.Add(new CopySkip(r.Reason!, r.Label ?? ""));

            // what each line would become: the old amount and the new one
            var samples = await _db.Database.SqlQueryRaw<Cand>($@"SELECT c.label || ' - ' || ft.fee_type_name || ': ' || trim(to_char(d.amount, 'FM9999999990.00')) || ' -> ' || trim(to_char({NewAmount}, 'FM9999999990')) AS ""Label"", NULL::text AS ""Reason""
                FROM ({FeeStructCand}) c JOIN public.tb_fee_structure_details d ON d.fee_structure_id = c.fee_structure_id JOIN public.tb_fee_types ft ON ft.fee_type_id = d.fee_type_id AND ft.school_id = @school
                WHERE c.reason IS NULL AND ft.isactive ORDER BY c.label, ft.fee_type_name", Ps2()).ToListAsync(ct);
            o.Samples.AddRange(samples.Select(s => s.Label ?? ""));

            // the structures and their lines in ONE statement: the lines go only to the structures this very statement created
            var madeRows = await _db.Database.SqlQueryRaw<FeeCounts>($@"
WITH ins_s AS (
    INSERT INTO public.tb_fee_structures (school_id, academic_year_id, class_id, structure_name, isactive)
    SELECT @school, @tgt, c.class_id, left(replace(c.structure_name, @srcname, @tgtname), 150), true FROM ({FeeStructCand}) c WHERE c.reason IS NULL
    RETURNING fee_structure_id, class_id),
ins_d AS (
    INSERT INTO public.tb_fee_structure_details (fee_structure_id, fee_type_id, frequency_id, amount, due_day, effective_from)
    SELECT n.fee_structure_id, d.fee_type_id, d.frequency_id, {NewAmount}, d.due_day, @start
    FROM ins_s n
    JOIN public.tb_fee_structures s ON s.school_id = @school AND s.academic_year_id = @src AND s.class_id = n.class_id
    JOIN public.tb_fee_structure_details d ON d.fee_structure_id = s.fee_structure_id
    JOIN public.tb_fee_types ft ON ft.fee_type_id = d.fee_type_id AND ft.school_id = @school AND ft.isactive
    RETURNING 1)
SELECT (SELECT count(*) FROM ins_s)::int AS ""Structures"", (SELECT count(*) FROM ins_d)::int AS ""Lines""", Ps2()).ToListAsync(ct);   // (not composed with LIMIT: a data-modifying WITH must stay at the top level)
            var made = madeRows.First();
            o.Created["fee structures"] = made.Structures;
            o.Created["fee lines"] = made.Lines;
            if (pct != 0m) o.Notes[$"amounts raised by {pct:0.##} %"] = made.Lines;
        }

        private sealed class FeeCounts { public int Structures { get; set; } public int Lines { get; set; } }

        // ---- candidate queries: columns the INSERT needs, a label for the screen, and the Reason (null = create)
        private const string ClassesCand = $@"
SELECT s.class_id, c.class_name AS label,
       CASE WHEN c.status_id IS DISTINCT FROM {Act} THEN 'The class is inactive'
            WHEN EXISTS (SELECT 1 FROM public.tb_academic_year_classes t WHERE t.academic_year_id = @tgt AND t.class_id = s.class_id) THEN 'EXISTS' END AS reason
FROM public.tb_academic_year_classes s JOIN public.tb_classes c ON c.class_id = s.class_id AND c.school_id = @school
WHERE s.school_id = @school AND s.academic_year_id = @src AND s.status_id = {Act}";

        private const string SectionsCand = $@"
SELECT s.class_id, s.section_id, s.max_strength, cl.class_name || ' - ' || sec.section_name AS label,
       CASE WHEN sec.status_id IS DISTINCT FROM {Act} THEN 'The section is inactive'
            WHEN cl.status_id IS DISTINCT FROM {Act} THEN 'The class is inactive'
            WHEN NOT EXISTS (SELECT 1 FROM public.tb_academic_year_classes t WHERE t.academic_year_id = @tgt AND t.class_id = s.class_id AND t.status_id = {Act}) THEN 'The class is not offered in the target year'
            WHEN EXISTS (SELECT 1 FROM public.tb_academic_year_sections t WHERE t.academic_year_id = @tgt AND t.section_id = s.section_id) THEN 'EXISTS' END AS reason
FROM public.tb_academic_year_sections s
JOIN public.tb_sections sec ON sec.section_id = s.section_id AND sec.school_id = @school
JOIN public.tb_classes cl ON cl.class_id = s.class_id AND cl.school_id = @school
WHERE s.school_id = @school AND s.academic_year_id = @src AND s.status_id = {Act}";

        private const string MappingCand = $@"
SELECT m.class_id, m.subject_id, sub.subject_name || ' in ' || cl.class_name AS label,
       CASE WHEN sub.status_id IS DISTINCT FROM {Act} THEN 'The subject is inactive'
            WHEN NOT EXISTS (SELECT 1 FROM public.tb_academic_year_classes t WHERE t.academic_year_id = @tgt AND t.class_id = m.class_id AND t.status_id = {Act}) THEN 'The class is not offered in the target year'
            WHEN EXISTS (SELECT 1 FROM public.tb_class_subjects t WHERE t.academic_year_id = @tgt AND t.class_id = m.class_id AND t.subject_id = m.subject_id) THEN 'EXISTS' END AS reason
FROM public.tb_class_subjects m
JOIN public.tb_subjects sub ON sub.subject_id = m.subject_id AND sub.school_id = @school
JOIN public.tb_classes cl ON cl.class_id = m.class_id AND cl.school_id = @school
WHERE m.school_id = @school AND m.academic_year_id = @src AND m.status_id = {Act}";

        private const string OverrideCand = $@"
SELECT o.class_id, o.section_id, o.subject_id, o.action, sub.subject_name || ' ' || lower(o.action) || ' in ' || cl.class_name || ' - ' || sec.section_name AS label,
       CASE WHEN sub.status_id IS DISTINCT FROM {Act} THEN 'The subject is inactive'
            WHEN NOT EXISTS (SELECT 1 FROM public.tb_academic_year_sections t WHERE t.academic_year_id = @tgt AND t.section_id = o.section_id AND t.status_id = {Act}) THEN 'The section is not offered in the target year'
            WHEN o.action = 'ADD' AND EXISTS (SELECT 1 FROM public.tb_class_subjects t WHERE t.academic_year_id = @tgt AND t.class_id = o.class_id AND t.subject_id = o.subject_id AND t.status_id = {Act}) THEN 'The class already teaches this subject in the target year'
            WHEN o.action = 'REMOVE' AND NOT EXISTS (SELECT 1 FROM public.tb_class_subjects t WHERE t.academic_year_id = @tgt AND t.class_id = o.class_id AND t.subject_id = o.subject_id AND t.status_id = {Act}) THEN 'The class does not teach this subject in the target year'
            WHEN EXISTS (SELECT 1 FROM public.tb_section_subject_overrides t WHERE t.academic_year_id = @tgt AND t.section_id = o.section_id AND t.subject_id = o.subject_id) THEN 'EXISTS' END AS reason
FROM public.tb_section_subject_overrides o
JOIN public.tb_subjects sub ON sub.subject_id = o.subject_id AND sub.school_id = @school
JOIN public.tb_sections sec ON sec.section_id = o.section_id AND sec.school_id = @school
JOIN public.tb_classes cl ON cl.class_id = o.class_id AND cl.school_id = @school
WHERE o.school_id = @school AND o.academic_year_id = @src AND o.status_id = {Act}";

        private const string SchedulesCand = $@"
SELECT a.class_id, a.day_of_week, a.schedule_id,
       coalesce(cl.class_name, 'Every class') || ', ' || coalesce((ARRAY['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'])[a.day_of_week], 'every day') || ' -> ' || d.schedule_name AS label,
       CASE WHEN d.status_id IS DISTINCT FROM {Act} THEN 'The schedule is inactive'
            WHEN a.class_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM public.tb_academic_year_classes t WHERE t.academic_year_id = @tgt AND t.class_id = a.class_id AND t.status_id = {Act}) THEN 'The class is not offered in the target year'
            WHEN EXISTS (SELECT 1 FROM public.tb_academic_year_schedules t WHERE t.academic_year_id = @tgt AND coalesce(t.class_id, 0) = coalesce(a.class_id, 0) AND coalesce(t.day_of_week, 0) = coalesce(a.day_of_week, 0)) THEN 'EXISTS' END AS reason
FROM public.tb_academic_year_schedules a
JOIN public.tb_period_schedules d ON d.schedule_id = a.schedule_id AND d.school_id = @school
LEFT JOIN public.tb_classes cl ON cl.class_id = a.class_id AND cl.school_id = @school
WHERE a.school_id = @school AND a.academic_year_id = @src";

        // an eligible class teacher: the same rule the class-teacher screen uses (Active, Teaching, an employment status that still works, an Active login holding the Active Teacher role)
        private const string TeacherEligible = $@"e.status_id = {Act} AND e.employee_type = 'Teaching'
              AND NOT EXISTS (SELECT 1 FROM public.lut_employment_status es WHERE es.employment_status_id = e.employment_status_id AND es.blocks_login)
              AND EXISTS (SELECT 1 FROM public.tb_users u JOIN public.tb_user_roles ur ON ur.user_id = u.user_id JOIN public.lut_roles r ON r.role_id = ur.role_id
                          WHERE u.school_id = @school AND u.org_user_id = e.employee_id AND u.status_id = {Act} AND r.role_code = 'TEACHER' AND r.status_id = {Act})";

        private const string TeachersCand = $@"
SELECT a.class_id, a.section_id, a.employee_id, e.first_name || ' ' || e.last_name || ' for ' || cl.class_name || ' - ' || sec.section_name AS label,
       CASE WHEN NOT ({TeacherEligible}) THEN 'The teacher is no longer an active teaching employee'
            WHEN NOT EXISTS (SELECT 1 FROM public.tb_academic_year_sections t WHERE t.academic_year_id = @tgt AND t.section_id = a.section_id AND t.status_id = {Act}) THEN 'The section is not offered in the target year'
            WHEN EXISTS (SELECT 1 FROM public.tb_class_teacher_assignments t WHERE t.academic_year_id = @tgt AND t.section_id = a.section_id) THEN 'EXISTS' END AS reason
FROM public.tb_class_teacher_assignments a
JOIN public.tb_employees e ON e.employee_id = a.employee_id AND e.school_id = @school
JOIN public.tb_sections sec ON sec.section_id = a.section_id AND sec.school_id = @school
JOIN public.tb_classes cl ON cl.class_id = a.class_id AND cl.school_id = @school
WHERE a.school_id = @school AND a.academic_year_id = @src AND a.status_id = {Act}";

        // ================================================================== timetables (the slot rules need the S3 / S4 resolution, so this part is written with the entities)
        private async Task TimetablesAsync(CopyPartOutcome o, string school, CopyYear src, CopyYear tgt, long? user, CancellationToken ct)
        {
            var activeId = await _db.LutStatus.AsNoTracking().Where(s => s.Sname == "Active" && s.Stype == "general status").Select(s => s.Sid).FirstAsync(ct);
            var sources = await (from t in _db.Timetables.AsNoTracking()
                                 join c in _db.Classes.AsNoTracking() on t.ClassId equals c.ClassId
                                 join s in _db.Sections.AsNoTracking() on t.SectionId equals s.SectionId
                                 where t.SchoolId == school && t.AcademicYearId == src.AcademicYearId && t.IsActive
                                 orderby c.SequenceOrder, s.SectionName
                                 select new { t, Label = c.ClassName + " - " + s.SectionName }).ToListAsync(ct);
            var offered = (await _db.AcademicYearSections.AsNoTracking().Where(x => x.AcademicYearId == tgt.AcademicYearId && x.StatusId == activeId).Select(x => x.SectionId).ToListAsync(ct)).ToHashSet();
            var haveTimetable = (await _db.Timetables.AsNoTracking().Where(x => x.AcademicYearId == tgt.AcademicYearId).Select(x => x.SectionId).Distinct().ToListAsync(ct)).ToHashSet();
            var schedules = await _db.PeriodSchedules.AsNoTracking().Where(x => x.SchoolId == school).ToDictionaryAsync(x => x.ScheduleId, ct);
            var defaultSchedule = schedules.Values.FirstOrDefault(x => x.IsDefault)?.ScheduleId;
            var assignments = (await _db.AcademicYearSchedules.AsNoTracking().Where(x => x.AcademicYearId == tgt.AcademicYearId).ToListAsync(ct)).Select(x => (x.ClassId, x.DayOfWeek, x.ScheduleId)).ToList();
            var periods = await _db.Periods.AsNoTracking().Where(p => p.SchoolId == school).ToDictionaryAsync(p => p.PeriodId, ct);
            var eligible = (await _db.Database.SqlQueryRaw<string>($@"SELECT e.employee_id AS ""Value"" FROM public.tb_employees e WHERE e.school_id = @school AND e.status_id = {Act} AND e.employee_type = 'Teaching'
                              AND NOT EXISTS (SELECT 1 FROM public.lut_employment_status es WHERE es.employment_status_id = e.employment_status_id AND es.blocks_login)", new NpgsqlParameter("school", school)).ToListAsync(ct)).ToHashSet();
            var days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
            int created = 0, slots = 0, needs = 0;
            var now = DateTime.UtcNow;

            foreach (var item in sources)
            {
                var t = item.t;
                if (!offered.Contains(t.SectionId)) { o.Skipped.Add(new CopySkip("The section is not offered in the target year", item.Label)); continue; }
                if (haveTimetable.Contains(t.SectionId)) { o.SkippedExisting++; continue; }
                if (!schedules.TryGetValue(t.ScheduleId, out var sched) || sched.StatusId != activeId) { o.Skipped.Add(new CopySkip("The schedule is inactive", item.Label)); continue; }

                var effective = (await _db.EffectiveSubjects(school, tgt.AcademicYearId).Where(v => v.SectionId == t.SectionId).Select(v => v.SubjectId).ToListAsync(ct)).ToHashSet();
                var entries = await _db.TimetableEntries.AsNoTracking().Where(e => e.TimetableId == t.TimetableId && e.StatusId == activeId).OrderBy(e => e.DayOfWeek).ThenBy(e => e.PeriodId).ToListAsync(ct);
                var plan = new List<(TbTimetableEntry E, string? Teacher)>();
                foreach (var e in entries)
                {
                    var where = $"{item.Label}, {days[e.DayOfWeek - 1]}";
                    if (!periods.TryGetValue(e.PeriodId, out var p) || p.StatusId != activeId || p.SlotType != SlotTypes.Period)
                    { o.Skipped.Add(new CopySkip("The period is no longer available", where)); continue; }
                    var follows = ResolveSchedule(assignments, t.ClassId, e.DayOfWeek, defaultSchedule);
                    if (follows != null && follows != p.ScheduleId) { o.Skipped.Add(new CopySkip("The period is not in the schedule the class follows that day in the target year", where + $", period {p.PeriodNumber}")); continue; }
                    if (!effective.Contains(e.SubjectId)) { o.Skipped.Add(new CopySkip("The subject is not taught in this section in the target year", where + $", period {p.PeriodNumber}")); continue; }
                    plan.Add((e, e.EmployeeId != null && eligible.Contains(e.EmployeeId) ? e.EmployeeId : null));
                }
                if (entries.Count > 0 && plan.Count == 0) { o.Skipped.Add(new CopySkip("No slot could be copied", item.Label)); continue; }

                var copy = new TbTimetable
                {
                    SchoolId = school, AcademicYearId = tgt.AcademicYearId, ClassId = t.ClassId, SectionId = t.SectionId, TimetableName = t.TimetableName, ScheduleId = t.ScheduleId,
                    State = TimetableStates.Draft, IsActive = false, CreatedAt = now, UpdatedAt = now, CreatedBy = user, UpdatedBy = user
                };
                await _db.Timetables.AddAsync(copy, ct);
                await _db.SaveChangesAsync(ct);
                foreach (var (e, teacher) in plan)
                    await _db.TimetableEntries.AddAsync(new TbTimetableEntry
                    {
                        SchoolId = school, AcademicYearId = tgt.AcademicYearId, ClassId = t.ClassId, SectionId = t.SectionId, TimetableId = copy.TimetableId, TtIsActive = false,
                        DayOfWeek = e.DayOfWeek, PeriodId = e.PeriodId, SubjectId = e.SubjectId, EmployeeId = teacher, NeedsTeacher = teacher == null, RoomNo = e.RoomNo,
                        StatusId = activeId, AssignedBy = user, CreatedAt = now, UpdatedAt = now
                    }, ct);
                await _db.SaveChangesAsync(ct);
                created++; slots += plan.Count; needs += plan.Count(p => p.Teacher == null);
            }
            _db.ChangeTracker.Clear();
            o.Created["timetables (drafts)"] = created;
            o.Created["timetable slots"] = slots;
            if (needs > 0) o.Notes["slots needing a teacher"] = needs;
        }

        private static long? ResolveSchedule(List<(long? ClassId, short? Day, long ScheduleId)> rows, long classId, int day, long? defaultSchedule)
        {
            foreach (var (c, d) in new (long?, short?)[] { (classId, (short)day), (classId, null), (null, (short)day), (null, null) })
            {
                var hit = rows.FirstOrDefault(r => r.ClassId == c && r.Day == d);
                if (hit != default) return hit.ScheduleId;
            }
            return defaultSchedule;
        }
    }
}
