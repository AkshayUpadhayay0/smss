namespace smss_api_db_layer.@interface
{
    /// <summary>The public projection of a listed school (see PartnerSchoolDto): nothing else is ever selected.</summary>
    public record PartnerSchoolRow(string Name, string? LogoUrl, string? City, string? State);

    public interface IPartnerSchoolRepository
    {
        /// <summary>Schools that are Active (lut_status, matched by name and type) AND opted in, by name, at most <paramref name="max"/>. Read-only projection.</summary>
        Task<List<PartnerSchoolRow>> GetListedAsync(string activeName, string statusType, int max, CancellationToken ct);

        /// <summary>
        /// True for the platform's own school row (the one SMSS Admin / the Super Admin belongs to). Identified structurally, never by id: a school
        /// with at least one login that holds the SUPER_ADMIN role (lut_roles.role_code). No tenant school ever has such a login.
        /// </summary>
        Task<bool> IsPlatformSchoolAsync(string schoolId, CancellationToken ct);

        /// <summary>Sets the flag and updated_at. False when the school does not exist.</summary>
        Task<bool> SetShowOnWebsiteAsync(string schoolId, bool show, DateTime nowUtc, CancellationToken ct);
    }
}
