namespace smss_api_service_layer.helper
{
    /// <summary>tb_enquiries.enquiry_type values.</summary>
    public static class EnquiryTypes
    {
        public const string Contact = "CONTACT";
        public const string Demo = "DEMO";
        public static readonly string[] All = { Contact, Demo };
    }

    /// <summary>tb_enquiries.status values (string codes, not lut_status: that table only holds Active / Inactive).</summary>
    public static class EnquiryStatuses
    {
        public const string New = "NEW";
        public const string Contacted = "CONTACTED";
        public const string DemoScheduled = "DEMO_SCHEDULED";
        public const string Closed = "CLOSED";
        public const string Spam = "SPAM";
        public static readonly string[] All = { New, Contacted, DemoScheduled, Closed, Spam };
    }

    /// <summary>The choices the public Request Demo form offers. The Angular form lists the same values (enquiry.constants.ts); the server is the authority.</summary>
    public static class EnquiryChoices
    {
        public static readonly string[] StudentCountRanges = { "Up to 200", "201 - 500", "501 - 1,000", "1,001 - 2,500", "More than 2,500" };
        public static readonly string[] TimeSlots = { "Morning (9 AM - 12 PM)", "Afternoon (12 PM - 4 PM)", "Evening (4 PM - 7 PM)" };
    }
}
