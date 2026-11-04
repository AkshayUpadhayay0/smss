namespace smss_api_service_layer.dto
{
    /// <summary>A student named in a bulk ID-card report.</summary>
    public class IdCardStudentRefDto
    {
        public string StudentId { get; set; } = null!;
        public string AdmissionNumber { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? RollNumber { get; set; }
        /// <summary>Only on excluded students: why no card was made.</summary>
        public string? Reason { get; set; }
    }

    /// <summary>
    /// Result of the section-wise bulk run. The PDF travels inside the standard response (base64) so the report lists can come back with it.
    /// Nothing is silently dropped: every student of the section is either printed or listed under excluded.
    /// </summary>
    public class IdCardBulkResultDto
    {
        public string FileName { get; set; } = null!;
        public string ContentType { get; set; } = "application/pdf";
        /// <summary>Null when no student qualified (nothing to print) - see excluded.</summary>
        public string? PdfBase64 { get; set; }
        public int CardCount { get; set; }
        public int SheetCount { get; set; }
        /// <summary>The most students one request will print.</summary>
        public int MaxCardsPerRequest { get; set; }
        public string AcademicYearName { get; set; } = null!;
        /// <summary>Printed with the initials placeholder because they have no (readable) photo.</summary>
        public List<IdCardStudentRefDto> NoPhoto { get; set; } = new();
        /// <summary>Left out of the PDF (inactive student, or no active enrollment this year), each with the reason.</summary>
        public List<IdCardStudentRefDto> Excluded { get; set; } = new();
    }

    /// <summary>A rendered single-student card: the PDF bytes plus a file name.</summary>
    public class IdCardFileDto
    {
        public string FileName { get; set; } = null!;
        public byte[] Content { get; set; } = Array.Empty<byte>();
    }
}
