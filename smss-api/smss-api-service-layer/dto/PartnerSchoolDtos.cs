namespace smss_api_service_layer.dto
{
    /// <summary>
    /// One card of the public "Our Partner Schools" section. Deliberately minimal: NO school id or code, contacts, emails, phones,
    /// status or any other internal field. Anonymous visitors see exactly these four values.
    /// </summary>
    public class PartnerSchoolDto
    {
        public string Name { get; set; } = null!;
        /// <summary>Relative path (/uploads/...) of the school's logo, or null (the page then shows the school's initials).</summary>
        public string? LogoUrl { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
    }

    /// <summary>Body of POST api/superadmin/schools/{schoolId}/website-visibility. The school comes from the route only.</summary>
    public class SetWebsiteVisibilityRequest
    {
        public bool? Show { get; set; }
    }

    public class WebsiteVisibilityDto
    {
        public string SchoolId { get; set; } = null!;
        public bool ShowOnWebsite { get; set; }
    }
}
