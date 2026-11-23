using smss_api_db_layer.@interface;

namespace smss_api_service_layer.helper
{
    /// <summary>
    /// S3: a subject cannot be taken away from a section (or a class) in a year while something in that year uses it:
    /// timetable slots, homework, class tests, entered report-card marks, and (for the whole class) report-card exam setups.
    /// One place builds the reasons so the Academic Setup save and the Class-Subject endpoints answer identically (409 with counts).
    /// </summary>
    public static class SubjectRemovalGuard
    {
        public static string UseText(int timetable, int homework, int tests, int marks, int examSetups = 0)
        {
            var parts = new List<string>();
            if (timetable > 0) parts.Add($"{timetable} timetable slot{(timetable == 1 ? "" : "s")}");
            if (homework > 0) parts.Add($"{homework} homework");
            if (tests > 0) parts.Add($"{tests} class test{(tests == 1 ? "" : "s")}");
            if (marks > 0) parts.Add($"{marks} entered mark{(marks == 1 ? "" : "s")}");
            if (examSetups > 0) parts.Add($"{examSetups} report-card exam setup{(examSetups == 1 ? "" : "s")}");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// removed = (section, subject) pairs that stop being taught; goneFromClass = subjects no section of the class is taught any more
        /// (these also lose their report-card exam setups). Returns one line per blocked item; empty = nothing is in use.
        /// </summary>
        public static List<string> Blocks(IEnumerable<(long SectionId, long SubjectId)> removed, IReadOnlyDictionary<long, string> sectionLabel,
            IReadOnlyDictionary<long, string> subjectName, SubjectUsageSet usage, IEnumerable<long> goneFromClass)
        {
            var lines = new List<string>();
            foreach (var (sectionId, subjectId) in removed.OrderBy(r => sectionLabel.GetValueOrDefault(r.SectionId)).ThenBy(r => subjectName.GetValueOrDefault(r.SubjectId)))
            {
                var u = usage.PerSection.FirstOrDefault(x => x.SectionId == sectionId && x.SubjectId == subjectId);
                if (u == null || u.Total == 0) continue;
                lines.Add($"{subjectName.GetValueOrDefault(subjectId, "Subject")} in {sectionLabel.GetValueOrDefault(sectionId, "section")} ({UseText(u.Timetable, u.Homework, u.Tests, u.Marks)})");
            }
            foreach (var subjectId in goneFromClass.Distinct())
            {
                if (usage.ExamSetups.TryGetValue(subjectId, out var n) && n > 0)
                    lines.Add($"{subjectName.GetValueOrDefault(subjectId, "Subject")} has {UseText(0, 0, 0, 0, n)} for this class");
            }
            return lines;
        }
    }
}
