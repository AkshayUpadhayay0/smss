using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>
    /// Reset a login's password (admin-initiated). <c>LoginId</c> is the target's login (org_user_id): an employee id, a student id or a school id. There is no school id here on purpose:
    /// the school always comes from the caller's token.
    /// </summary>
    public class ResetPasswordRequest
    {
        [Required(ErrorMessage = "The login id is required."), StringLength(50)] public string? LoginId { get; set; }
        [Required(ErrorMessage = "A reason is required."), StringLength(500, ErrorMessage = "The reason can be at most 500 characters.")] public string? Reason { get; set; }
    }

    /// <summary>Returned ONCE. Only its hash is stored; it is never logged and never in the audit row.</summary>
    public class PasswordResetResultDto
    {
        public string LoginId { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string TemporaryPassword { get; set; } = null!;
    }

    /// <summary>Who is asking, taken from the token by the controller (never from the request body).</summary>
    public record PasswordResetCaller(long UserId, string? SchoolId, IReadOnlyCollection<string> Roles);
}
