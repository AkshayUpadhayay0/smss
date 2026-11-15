namespace smss_api_service_layer.dto
{
    /// <summary>The own-school read for roles that do not manage the school: name, logo, address and the school's own contact details. Deliberately NO tax / registration numbers (GSTIN, PAN), status, subscription or contact persons.</summary>
    public class SchoolPublicProfileResponse
    {
        public string SchoolCode { get; set; } = null!;
        public string SchoolName { get; set; } = null!;
        public string? SchoolShortName { get; set; }
        public string? LogoUrl { get; set; }
        public int? CountryId { get; set; }
        public int? StateId { get; set; }
        public int? DistrictId { get; set; }
        public int? CityId { get; set; }
        public string? AddressLine1 { get; set; }
        public string? AddressLine2 { get; set; }
        public string? Pincode { get; set; }
        public string? Email { get; set; }
        public string? MobileNumber { get; set; }
        public string? Website { get; set; }
    }
}
