using smss_api_db_layer.@interface;

namespace smss_api_service_layer.helper
{
    /// <summary>The years a management dashboard (School Admin, Principal, Accountant) deals with.</summary>
    public class DashboardYearChoice
    {
        /// <summary>The year being VIEWED; null when the school has no year at all.</summary>
        public DashboardYearRow? Viewed { get; init; }
        /// <summary>The school's current-flagged year (may differ from <see cref="Viewed"/>); null when none is flagged.</summary>
        public DashboardYearRow? Current { get; init; }
        /// <summary>The viewed year is not the school's current year: the client says so.</summary>
        public bool ViewingAnotherYear => Viewed != null && Current != null && Viewed.AcademicYearId != Current.AcademicYearId;
        /// <summary>A year was asked for and it is not this school's (a 404; ids from other schools are never confirmed).</summary>
        public bool NotFound { get; init; }
        /// <summary>No year is flagged current, so the year containing today was used.</summary>
        public bool FellBackToToday { get; init; }
    }

    /// <summary>
    /// ONE rule for which academic year a management dashboard shows: the year asked for (never another school's), else the school's CURRENT-flagged year, else (only when none is
    /// flagged) the year whose dates contain today. Operational "today" parts (marking attendance, today's classes) keep using the year containing today, not this rule.
    /// </summary>
    public static class DashboardYears
    {
        public static async Task<DashboardYearChoice> ResolveAsync(ISchoolDashboardRepository repo, string schoolId, long? requestedYearId, DateOnly today, CancellationToken ct)
        {
            var current = await repo.CurrentYearAsync(schoolId, ct);
            if (requestedYearId != null)
            {
                var asked = await repo.YearAsync(schoolId, requestedYearId.Value, ct);
                return new DashboardYearChoice { Viewed = asked, Current = current, NotFound = asked == null };
            }
            if (current != null) return new DashboardYearChoice { Viewed = current, Current = current };
            var containing = await repo.YearContainingAsync(schoolId, today, ct);
            return new DashboardYearChoice { Viewed = containing, Current = null, FellBackToToday = containing != null };
        }

        /// <summary>"Viewing 2027-28 ... The school's current year is 2026-27." or null when the viewed year is the current one.</summary>
        public static string? ViewingNote(DashboardYearChoice c) =>
            c.Viewed == null ? null
            : c.ViewingAnotherYear ? $"You are viewing {c.Viewed.YearName}{(c.Viewed.IsClosed ? " (closed, read-only)" : "")}. The school's current year is {c.Current!.YearName}."
            : c.FellBackToToday ? $"No academic year is set as current, so {c.Viewed.YearName} (the year that contains today) is shown."
            : null;
    }
}
