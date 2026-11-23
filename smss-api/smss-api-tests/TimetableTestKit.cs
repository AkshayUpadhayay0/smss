namespace smss_api_tests
{
    /// <summary>S6: tests that insert timetable entries by SQL must put them on a timetable. These helpers create the section's ACTIVE timetable when it is missing.</summary>
    internal static class TtKit
    {
        /// <summary>Makes sure the section has an ACTIVE timetable in the year (named "Regular timetable"), on the school's default schedule.</summary>
        public static void Ensure(TaxDb db, string school, long year, long cls, long section) =>
            db.Exec($@"insert into tb_timetables (school_id, academic_year_id, class_id, section_id, timetable_name, schedule_id, state, is_active, ever_active, first_activated_at, activated_at)
                       select '{school}', {year}, {cls}, {section}, 'Regular timetable', (select schedule_id from tb_period_schedules where school_id='{school}' and is_default), 'ACTIVE', true, true, now(), now()
                       where not exists (select 1 from tb_timetables where academic_year_id={year} and section_id={section} and is_active)");

        public const string Cols = ", timetable_id, tt_is_active";
        public static string Vals(long year, long section) => $", (select timetable_id from tb_timetables where academic_year_id={year} and section_id={section} and is_active), true";
    }
}
