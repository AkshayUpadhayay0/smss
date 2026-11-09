namespace smss_api_service_layer.dto
{
    /// <summary>
    /// The public Contact Us / Request Demo form. Validation lives in EnquiryService (it depends on the enquiry type), and mirrors the Angular form.
    /// <see cref="Website"/> is the honeypot: real visitors never see or fill it.
    /// </summary>
    public class CreateEnquiryRequest
    {
        public string? EnquiryType { get; set; }
        public string? FullName { get; set; }
        public string? Designation { get; set; }
        public string? SchoolName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? City { get; set; }
        public string? StudentCountRange { get; set; }
        public string? Subject { get; set; }
        public string? Message { get; set; }
        public DateOnly? PreferredDemoDate { get; set; }
        public string? PreferredTimeSlot { get; set; }
        public bool ConsentGiven { get; set; }
        public string? Website { get; set; }
    }

    public class EnquiryListItemDto
    {
        public long EnquiryId { get; set; }
        public string EnquiryType { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? SchoolName { get; set; }
        public string Email { get; set; } = null!;
        public string? Phone { get; set; }
        public string? City { get; set; }
        public string? Subject { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
    }

    public class EnquiryListDto
    {
        public List<EnquiryListItemDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class EnquiryDetailDto : EnquiryListItemDto
    {
        public string? Designation { get; set; }
        public string? StudentCountRange { get; set; }
        public string? Message { get; set; }
        public DateOnly? PreferredDemoDate { get; set; }
        public string? PreferredTimeSlot { get; set; }
        public bool ConsentGiven { get; set; }
        public string? AdminNotes { get; set; }
        public string? HandledByName { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    /// <summary>Who handled it is never in here: the API takes it from the signed-in user's token.</summary>
    public class UpdateEnquiryStatusRequest
    {
        public string? Status { get; set; }
        /// <summary>Null leaves the notes as they are; a value (even blank) replaces them.</summary>
        public string? AdminNotes { get; set; }
    }

    public class EnquirySummaryDto
    {
        public int New { get; set; }
        public int Contacted { get; set; }
        public int DemoScheduled { get; set; }
        public int NewDemo { get; set; }
        public int NewContact { get; set; }
        public int TotalThisMonth { get; set; }
    }
}
