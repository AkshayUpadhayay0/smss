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
