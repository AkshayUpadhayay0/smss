namespace smss_api_service_layer.dto
{
    /// <summary>Names of the Admission dashboard parts that could not be loaded: the client shows "Unavailable" for them, never a wrong 0.</summary>
    public static class AdmissionDashboardParts
    {
        /// <summary>The application list: the status counts, "new this week" and the latest applications.</summary>
        public const string Applications = "applications";
        public const string Documents = "documents";
    }

    public class AdmissionStatusCountDto
    {
        /// <summary>APPLICATION_SUBMITTED / UNDER_REVIEW / APPROVED / REJECTED / ADMITTED / CANCELLED</summary>
        public string StatusName { get; set; } = null!;
        public int Count { get; set; }
    }

    public class AdmissionMissingDocumentsDto
    {
        public long AdmissionId { get; set; }
        public string ApplicationNumber { get; set; } = null!;
        public string ApplicantFullName { get; set; } = null!;
        public string StatusName { get; set; } = null!;
        public DateTime ApplicationDate { get; set; }
        public List<string> MissingDocuments { get; set; } = new();
    }

    public class AdmissionDashboardDto
    {
        public DateOnly Today { get; set; }
        /// <summary>Every status, in pipeline order, zero-filled. Empty when the applications part is unavailable.</summary>
        public List<AdmissionStatusCountDto> ByStatus { get; set; } = new();
        public int? TotalApplications { get; set; }
        /// <summary>Applications dated in the last 7 days (today and the 6 days before).</summary>
        public int? NewThisWeek { get; set; }
        /// <summary>Open applications (Submitted, Under Review, Approved) missing at least one required document.</summary>
        public int? MissingDocumentsCount { get; set; }
        /// <summary>The newest of those, at most 10.</summary>
        public List<AdmissionMissingDocumentsDto> MissingDocuments { get; set; } = new();
        /// <summary>The latest 10 applications, newest first.</summary>
        public List<DashboardAdmissionDto> RecentApplications { get; set; } = new();
        public List<string> PartsUnavailable { get; set; } = new();
    }
}
