using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>A guardian without an account who is linked to an Active student: the candidates for bulk creation (the service decides who is complete).</summary>
    public sealed record AccountlessGuardianRow(long GuardianId, string Name, string? WhatsApp, string? Email, DateTime? ConsentGivenAt);
    /// <summary>An account found by a registered contact (forgot id or password): guardians of any school, so the caller must treat every row as the same person's.</summary>
    public sealed record AccountContactRow(long GuardianId, string SchoolId, string SchoolName, long UserId, string Name, string LoginId, string? WhatsApp, string? Email);
    /// <summary>A child linked to a guardian with portal access ON (what the family screen lists).</summary>
    public sealed record LinkedChildRow(string StudentId, string FirstName, string? MiddleName, string? LastName, string AdmissionNumber, int StatusId, string? ProfilePhotoStored, bool IsPrimary);
    /// <summary>The guardian behind a login (for the token claim and the "can this guardian sign in" check).</summary>
    public sealed record GuardianLoginInfo(long GuardianId, string SchoolId, int StatusId, long? MergedIntoGuardianId);

    /// <summary>What the parent-account service needs beyond IGuardianRepository / IMessagingRepository. Every school-owned read takes the school id; the cross-school reads are named for what they are.</summary>
    public interface IParentAccountRepository
    {
        Task<TbUsers?> GetUserAsync(long userId, bool tracked, CancellationToken ct);
        Task AddUserAsync(TbUsers user, CancellationToken ct);
        Task<long?> GetRoleIdByNameAsync(string roleName, CancellationToken ct);
        Task<string?> GetSchoolNameAsync(string schoolId, CancellationToken ct);
        /// <summary>The student's name (this school's student only).</summary>
        Task<string?> GetStudentNameAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>The name of the guardian's primary child (else any linked Active child), for the message text.</summary>
        Task<string?> GetPrimaryChildNameAsync(string schoolId, long guardianId, int activeStudentStatusId, CancellationToken ct);
        /// <summary>The student's primary guardian (not tracked), or null.</summary>
        Task<TbGuardian?> GetPrimaryGuardianOfStudentAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>True when the guardian is linked to the student.</summary>
        Task<bool> IsLinkedAsync(string schoolId, long guardianId, string studentId, CancellationToken ct);
        Task<List<AccountlessGuardianRow>> ListAccountlessGuardiansAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<List<AccountContactRow>> FindAccountsByContactAsync(string? whatsAppNumber, string? emailLower, int activeStatusId, CancellationToken ct);
        Task<GuardianLoginInfo?> GetGuardianByUserIdAsync(long userId, CancellationToken ct);
        /// <summary>The children linked to the guardian with portal_access on (this school's students only), primary link first.</summary>
        Task<List<LinkedChildRow>> ListLinkedChildrenAsync(string schoolId, long guardianId, CancellationToken ct);
        /// <summary>True only when the student is this school's AND linked to the guardian AND portal_access is on for that link.</summary>
        Task<bool> IsChildLinkedAsync(string schoolId, long guardianId, string studentId, CancellationToken ct);
        /// <summary>The outcome (PROMOTED, REPEATED, PASSED_OUT, TRANSFERRED_OUT, LEFT) of the student's most recent enrolment, or null when none was recorded.</summary>
        Task<string?> GetLatestOutcomeAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>What decides whether a STUDENT login may sign in: the student's status and the outcome of the latest enrolment with whether its year is current. Null when there is no such student in the school.</summary>
        Task<StudentLoginState?> GetStudentLoginStateAsync(string schoolId, string studentId, CancellationToken ct);
        Task<List<TbMessageDelivery>> GetRecentDeliveriesAsync(string schoolId, long guardianId, int take, CancellationToken ct);
    }

    public class StudentLoginState
    {
        public bool StudentActive { get; set; }
        /// <summary>Outcome of the latest enrolment (LEFT, TRANSFERRED_OUT, PASSED_OUT, ...), or null (an open enrolment, or none).</summary>
        public string? Outcome { get; set; }
        public bool YearIsCurrent { get; set; }
    }
}
