namespace smss_api_service_layer.helper
{
    /// <summary>
    /// S4: which bell schedule a class follows on a day of an academic year. DERIVED, never stored:
    /// the first of (this class, this day) -> (this class, any day) -> (any class, this day) -> (any class, any day) -> the school's default schedule.
    /// A school that never assigns anything gets the default schedule everywhere, which is exactly the behaviour before schedules existed.
    /// </summary>
    public static class ScheduleResolver
    {
        public static readonly string[] DayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

        public static long? Resolve(IReadOnlyCollection<(long? ClassId, short? Day, long ScheduleId)> rows, long classId, int day, long? defaultScheduleId)
        {
            foreach (var (c, d) in new (long?, short?)[] { (classId, (short)day), (classId, null), (null, (short)day), (null, null) })
            {
                var hit = rows.FirstOrDefault(r => r.ClassId == c && r.Day == d);
                if (hit != default) return hit.ScheduleId;
            }
            return defaultScheduleId;
        }
    }
}
