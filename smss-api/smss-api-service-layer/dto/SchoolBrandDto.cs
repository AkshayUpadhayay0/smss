namespace smss_api_service_layer.dto
{
    /// <summary>What every role of a school may see of it for the avatar: name and the (public) logo path. Nothing else.</summary>
    public class SchoolBrandResponse
    {
        public string SchoolName { get; set; } = null!;
        public string? LogoUrl { get; set; }
    }
}
