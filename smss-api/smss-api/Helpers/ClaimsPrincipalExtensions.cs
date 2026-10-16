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
        public static bool CanManageFees(this ClaimsPrincipal user) =>
            user.FindAll(ClaimTypes.Role).Any(r =>
                string.Equals(r.Value?.Trim(), RoleNames.SchoolAdmin, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.Value?.Trim(), RoleNames.Accountant, StringComparison.OrdinalIgnoreCase));
    }
}
