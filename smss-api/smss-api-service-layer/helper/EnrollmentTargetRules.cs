using smss_api_db_layer.@interface;

namespace smss_api_service_layer.helper
{
    /// <summary>The words (and status codes) for a refused enrolment target: a section the year does not offer, or a roll number that is taken. One place, used by Confirm Admission, direct entry and the import.</summary>
    public static class EnrollmentTargetRules
    {
        public const string RollConstraint = "uq_tb_enrollments_roll";
        public const string OfferingConstraint = "fk_tb_enrollments_offering";

        /// <summary>Null when the enrolment may go ahead (also for a null check, which only a test fake returns).</summary>
        public static (int Code, string Message)? Refusal(EnrollmentTargetCheck? c, string? rollNumber)
        {
            if (c == null) return null;
            if (!c.Offered) return (409, NotOffered(c.SectionLabel, c.YearName));
            if (c.RollTakenByStudentId != null) return (409, RollTaken(rollNumber, c.SectionLabel, c.YearName, c.RollTakenByStudentId));
            return null;
        }

        public static string NotOffered(string section, string year) => $"{section} is not offered in {year}. Offer it in Academic Setup first, or choose another section.";

        public static string RollTaken(string? roll, string section, string year, string? byStudent = null) =>
            $"Roll number {roll?.Trim()} is already taken in {section} for {year}{(byStudent == null ? "" : $" (student {byStudent})")}. Roll numbers are compared without spaces, capital letters or leading zeros (01 is the same as 1).";
    }
}
