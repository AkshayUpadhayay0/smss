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
    }
}
