namespace smss_api_service_layer.dto
{
    // The small types more than one role's dashboard shares (the School Admin, Principal and Admission dashboards).
    public class AttendanceTrendPointDto
    {
        public DateOnly Date { get; set; }
        public int Marked { get; set; }
        public int Present { get; set; }
        /// <summary>Present / marked x 100, one decimal; null for a day nothing was marked (a Sunday, a holiday, or today before marking).</summary>
        public decimal? PresentPercent { get; set; }
    }

    public class DashboardNoticeDto
    {
        public long NoticeId { get; set; }
        public string Title { get; set; } = null!;
        public string Audience { get; set; } = null!;
        public string? CreatedByName { get; set; }
        public DateTime PublishedAt { get; set; }
    }

    public class DashboardAdmissionDto
    {
        public long AdmissionId { get; set; }
        public string ApplicationNumber { get; set; } = null!;
        public string ApplicantFullName { get; set; } = null!;
        public string? AppliedClassName { get; set; }
        /// <summary>The admission status code, e.g. APPLICATION_SUBMITTED (shown as Application added) or ADMITTED (Confirmed).</summary>
        public string StatusName { get; set; } = null!;
        public DateTime ApplicationDate { get; set; }
    }
}
