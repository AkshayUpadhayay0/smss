namespace smss_api_service_layer.helper
{
    /// <summary>The pure rules of Record leaving and of the student login after leaving (unit-tested; nothing here touches the database).</summary>
    public static class LeavingRules
    {
        public const string Left = "LEFT";
        public const string TransferredOut = "TRANSFERRED_OUT";
        public const string PassedOut = "PASSED_OUT";
        public const int MinReason = 10;

        public static bool IsLeavingType(string? type) => type is Left or TransferredOut;

        /// <summary>Problems with the date: not in the future (school time) and not before the enrolment began. Empty = fine.</summary>
        public static string? DateProblem(DateOnly leavingDate, DateOnly today, DateOnly enrollmentDate) =>
            leavingDate > today ? "The leaving date cannot be in the future."
            : leavingDate < enrollmentDate ? $"The leaving date cannot be before the enrolment date ({enrollmentDate:dd MMM yyyy})."
            : null;

        /// <summary>The override needs the tick and a reason of at least 10 characters (the same rule as promotion).</summary>
        public static string? OverrideProblem(decimal outstanding, bool overrideDues, string? overrideReason) =>
            outstanding <= 0 ? null
            : !overrideDues ? $"There are unpaid dues of {outstanding:0.##} up to the leaving date. Tick the override and give a reason to record the leaving anyway."
            : (overrideReason?.Trim().Length ?? 0) < MinReason ? $"A reason of at least {MinReason} characters is needed to override the dues."
            : null;

        /// <summary>
        /// Whether a student login must be refused: the student is Inactive, or the latest enrolment ended with LEFT / TRANSFERRED_OUT, or with PASSED_OUT once its year is no longer current
        /// (until then a passed-out student signs in as normal). The same rule runs at sign-in and at refresh.
        /// </summary>
        public static bool StudentLoginBlocked(bool studentActive, string? outcome, bool yearIsCurrent) =>
            !studentActive || outcome is Left or TransferredOut || (outcome == PassedOut && !yearIsCurrent);

        public const string LoginBlockedMessage = "Your account is no longer active. Please contact your school.";
    }
}
