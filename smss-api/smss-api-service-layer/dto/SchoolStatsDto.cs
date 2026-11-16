namespace smss_api_service_layer.dto
{
    /// <summary>Head counts shown on the School Profile header (Active records only).</summary>
    public class SchoolStatsResponse
    {
        public int TotalStudents { get; set; }
        public int TotalEmployees { get; set; }
        public int TotalClasses { get; set; }
        public int TotalSections { get; set; }
    }
}
