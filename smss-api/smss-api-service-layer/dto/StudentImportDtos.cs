namespace smss_api_service_layer.dto
{
    /// <summary>One row of the preview. RowNumber is the row in the Excel sheet, so the admin can find it.</summary>
    public class ImportRowDto
    {
        public int RowNumber { get; set; }
        /// <summary>OK | WARNING | ERROR</summary>
        public string Status { get; set; } = "OK";
        public List<string> Messages { get; set; } = new();
        public string AdmissionNumber { get; set; } = "";
        public string FullName { get; set; } = "";
        public string Gender { get; set; } = "";
        public string DateOfBirth { get; set; } = "";
        /// <summary>The admission date given in the file (dd/mm/yyyy), or empty when the file left it blank (the start of the academic year is used).</summary>
        public string AdmissionDate { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string SectionName { get; set; } = "";
        public string RollNumber { get; set; } = "";
        public string Guardian { get; set; } = "";
        public string GuardianMobile { get; set; } = "";
    }

    public class ImportSummaryDto
    {
        public int Total { get; set; }
        public int Ok { get; set; }
        public int Warnings { get; set; }
        public int Errors { get; set; }
        /// <summary>True only when the file has rows, no row errors and no file-level problem.</summary>
        public bool CanImport { get; set; }
        public int MaxRows { get; set; }
    }

    public class ImportPreviewDto
    {
        public ImportSummaryDto Summary { get; set; } = new();
        /// <summary>Problems with the file as a whole (wrong sheet, missing columns, too many rows, no Existing Student admission type...).</summary>
        public List<string> FileErrors { get; set; } = new();
        public string AcademicYearName { get; set; } = "";
        public List<ImportRowDto> Rows { get; set; } = new();
    }

    public class ImportClassCountDto
    {
        public string ClassName { get; set; } = "";
        public string SectionName { get; set; } = "";
        public int Students { get; set; }
    }

    public class ImportResultDto
    {
        public int Imported { get; set; }
        public string AcademicYearName { get; set; } = "";
        public int WarningRows { get; set; }
        public string FirstStudentId { get; set; } = "";
        public string LastStudentId { get; set; } = "";
        public List<ImportClassCountDto> ByClass { get; set; } = new();
        /// <summary>Things the school still has to do (fees, logins). Plain sentences.</summary>
        public List<string> Notes { get; set; } = new();
    }
}
