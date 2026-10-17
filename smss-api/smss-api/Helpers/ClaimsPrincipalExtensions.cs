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
        public static bool CanViewFees(this ClaimsPrincipal user) => HasAnyRole(user, RoleNames.SchoolAdmin, RoleNames.Accountant, RoleNames.Principal);

        private static bool HasAnyRole(ClaimsPrincipal user, params string[] roles) =>
            user.FindAll(ClaimTypes.Role).Any(r => roles.Any(want => string.Equals(r.Value?.Trim(), want, StringComparison.OrdinalIgnoreCase)));
    }
}
