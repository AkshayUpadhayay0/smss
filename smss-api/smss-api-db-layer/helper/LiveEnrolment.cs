using System;
using System.Linq;
using System.Linq.Expressions;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.helper
{
    /// <summary>
    /// THE one rule for "is this enrolment live" (Stage 8). A live enrolment is an enrolment that is Active, whose outcome is not LEFT or TRANSFERRED_OUT, and not PASSED_OUT once its year is no longer
    /// the current one. tb_students.status_id is NOT part of this rule and is never changed by leaving ("left" is derived from the enrolment outcome). Every live roster, count, attendance list, pending
    /// list and the subscription billing use this (the C# form, the EF expression or the SQL text: all three say the same thing, and a test pins them together). Old-year views and history do NOT use it.
    /// </summary>
    public static class LiveEnrolment
    {
        public const string Left = "LEFT";
        public const string TransferredOut = "TRANSFERRED_OUT";
        public const string PassedOut = "PASSED_OUT";

        /// <summary>The rule itself, for code that already holds the values. <paramref name="enrolmentActive"/> is whether the enrolment's own status is Active.</summary>
        public static bool IsLive(bool enrolmentActive, string? outcome, bool yearIsCurrent) =>
            enrolmentActive && outcome != Left && outcome != TransferredOut && !(outcome == PassedOut && !yearIsCurrent);

        /// <summary>The outcome part only (no status check): for callers that apply the Active-status test themselves.</summary>
        public static bool OutcomeIsLive(string? outcome, bool yearIsCurrent) => IsLive(true, outcome, yearIsCurrent);

        /// <summary>
        /// The outcome part as an EF expression: <c>_db.StudentEnrollments.Where(LiveEnrolment.OutcomeIsLive(_db.AcademicYears))</c>. The Active-status test stays where it already was in each query.
        /// </summary>
        public static Expression<Func<TbStudentEnrollment, bool>> OutcomeIsLive(IQueryable<TbAcademicYears> years) =>
            e => e.Outcome != Left && e.Outcome != TransferredOut
                 && !(e.Outcome == PassedOut && !years.Any(y => y.SchoolId == e.SchoolId && y.AcademicYearId == e.AcademicYearId && y.IsCurrent));

        /// <summary>
        /// For a view of a given YEAR (a roster, a count, a list by year): in the CURRENT year the live rule applies; in any other year the view is history and shows everyone ("old-year views stay unchanged").
        /// In the current year this is the same as the live outcome rule (PASSED_OUT is still live there).
        /// </summary>
        public static Expression<Func<TbStudentEnrollment, bool>> LiveForYear(IQueryable<TbAcademicYears> years) =>
            e => !years.Any(y => y.SchoolId == e.SchoolId && y.AcademicYearId == e.AcademicYearId && y.IsCurrent) || (e.Outcome != Left && e.Outcome != TransferredOut);

        public static IQueryable<TbStudentEnrollment> LiveForYear(this IQueryable<TbStudentEnrollment> q, IQueryable<TbAcademicYears> years) => q.Where(LiveForYear(years));

        /// <summary>The year-view form as code: yearIsCurrent is whether the viewed year is the current one.</summary>
        public static bool IsLiveForYear(string? outcome, bool yearIsCurrent) => !yearIsCurrent || (outcome != Left && outcome != TransferredOut);

        /// <summary>The year-view form as SQL (alias of the enrolment).</summary>
        public static string SqlForYear(string e = "e") =>
            $@"(NOT EXISTS (SELECT 1 FROM public.tb_academic_years ly WHERE ly.school_id = {e}.school_id AND ly.academic_year_id = {e}.academic_year_id AND ly.is_current)
   OR COALESCE({e}.outcome, '') NOT IN ('LEFT', 'TRANSFERRED_OUT'))";

        public static IQueryable<TbStudentEnrollment> LiveOutcome(this IQueryable<TbStudentEnrollment> q, IQueryable<TbAcademicYears> years) => q.Where(OutcomeIsLive(years));

        /// <summary>The outcome part as SQL, for raw queries. <paramref name="e"/> is the enrolment alias; it needs no year join (a correlated sub-select reads the flag).</summary>
        public static string Sql(string e = "e") =>
            $@"(COALESCE({e}.outcome, '') NOT IN ('LEFT', 'TRANSFERRED_OUT')
   AND NOT (COALESCE({e}.outcome, '') = 'PASSED_OUT' AND NOT EXISTS (SELECT 1 FROM public.tb_academic_years ly WHERE ly.school_id = {e}.school_id AND ly.academic_year_id = {e}.academic_year_id AND ly.is_current)))";
    }
}
