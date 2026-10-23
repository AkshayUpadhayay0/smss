using smss_api_service_layer.helper;
using System.Security.Claims;

namespace smss_api.Helpers
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// The school the authenticated user belongs to, from the JWT "school_id" claim. This is the ONLY way
        /// tenant-scoped endpoints learn their school: never accept a school id from the client.
        /// Null when the account is not linked to a school (services answer that with a 400).
        /// </summary>
        public static string? GetSchoolId(this ClaimsPrincipal user) => user.FindFirst("school_id")?.Value;

        /// <summary>
        /// Whether the caller may manage fees (void a payment): a School Admin or an Accountant. The token carries role NAMES (the project has
        /// no permission system yet, so this stands in for a "Fees:Manage" permission). Keep this the ONE place that decides it.
        /// </summary>
        public static bool CanManageFees(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.SchoolAdmin, RoleNames.Accountant);

        /// <summary>
        /// Whether the caller may SEE fee figures (the dashboard and reports): everyone who can manage fees, plus the Principal, who is
        /// view-only. This is the first view-vs-manage split in the project, deliberately just this pair: CanViewFees (read) and CanManageFees (change).
        /// </summary>
        /// <summary>
        /// Whether the caller may CONFIGURE fees (fee types, structures, concessions, and how one student is billed: assigning a structure,
        /// concessions, per-item frequency / opt-out): School Admin only. The Accountant collects and voids payments (CanManageFees) but
        /// does not change what is charged, matching the frontend route rules where all of Fee Setup is School Admin only.
        /// </summary>
        /// <summary>School Admin only (the employee module, ...). Keep role-name matching in this one file.</summary>
        public static bool IsSchoolAdmin(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.SchoolAdmin);

        /// <summary>
        /// Whether the caller may create / change / delete calendar events: a School Admin or a Principal. Reading the calendar needs no role
        /// (every role of the school sees every event). Kept separate from IsSchoolAdmin / CanManageFees so each permission says what it is for.
        /// </summary>
        public static bool CanManageEvents(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.SchoolAdmin, RoleNames.Principal);

        /// <summary>
        /// Whether the caller may approve / reject EMPLOYEE leave and see every employee's requests: a School Admin or a Principal. Student leave is decided
        /// by the student's class teacher or a School Admin instead (checked against the Class-Teacher module in the service), so it does not use this.
        /// </summary>
        public static bool CanApproveLeave(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.SchoolAdmin, RoleNames.Principal);

        /// <summary>
        /// Whether the caller may publish homework: a School Admin or a Teacher. Whether a TEACHER may do it for a given section + subject is a per-request
        /// check against the Timetable (ITimetableService.IsTeachingSectionSubjectAsync) in the service; a School Admin bypasses it.
        /// </summary>
        public static bool CanManageHomework(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.SchoolAdmin, RoleNames.Teacher);

        /// <summary>Whether the caller may record internal tests (class tests): a School Admin or a Teacher, exactly like homework. Whether a TEACHER may do it for a given section + subject is the same per-request Timetable check.</summary>
        public static bool CanManageAssessments(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.SchoolAdmin, RoleNames.Teacher);

        /// <summary>A student's login (reads their own section's homework).</summary>
        public static bool IsStudent(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.Student);

        /// <summary>
        /// Admission applications: who may take them in and work them (create, edit while undecided, move to review, withdraw, documents, every read, hand an
        /// approved one on for student creation): School Admin, Principal or Admission Staff. Deciding is a separate, narrower permission (CanDecideAdmission).
        /// </summary>
        public static bool CanManageApplications(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.SchoolAdmin, RoleNames.Principal, RoleNames.AdmissionStaff);

        /// <summary>Approving or rejecting an application: School Admin or Principal ONLY. Admission Staff can prepare an application but never decide it.</summary>
        public static bool CanDecideAdmission(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.SchoolAdmin, RoleNames.Principal);

        /// <summary>
        /// A Student or Guardian login. These accounts must never read another student's record or change anything about students, so the student endpoints
        /// refuse them outright (the staff roles, including a custom role, keep the read access they had).
        /// </summary>
        public static bool IsStudentOrGuardianLogin(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.Student, RoleNames.Guardian);

        public static bool CanConfigureFees(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.SchoolAdmin);

        /// <summary>
        /// An Accountant who is not also a School Admin: gets the limited student view (header + class/section, no personal details,
        /// guardian contacts or credential actions). Only this role is narrowed: what every other role could already do is unchanged.
        /// </summary>
        public static bool IsAccountantOnly(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.Accountant) && !HasAnyRole(user, RoleNames.SchoolAdmin);

        public static bool CanViewFees(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.SchoolAdmin, RoleNames.Accountant, RoleNames.Principal);

        private static bool HasAnyRole(ClaimsPrincipal user, params string[] roles) =>
            user.FindAll(ClaimTypes.Role).Any(r => roles.Any(want => string.Equals(r.Value?.Trim(), want, StringComparison.OrdinalIgnoreCase)));
    }
}
