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
        /// <summary>The primary guardian's WhatsApp number as it will be stored (with the country code), and email.</summary>
        public string GuardianWhatsApp { get; set; } = "";
        public string GuardianEmail { get; set; } = "";
        /// <summary>"A guardian with this number already exists in the school (sibling?)": an information hint, not a warning or an error. The file's guardian is still created as a NEW record (never linked automatically).</summary>
        public string GuardianHint { get; set; } = "";
    }

    public class ImportSummaryDto
    {
        public int Total { get; set; }
        public int Ok { get; set; }
        public int Warnings { get; set; }
        public int Errors { get; set; }
        /// <summary>Rows that can be imported now: no error (a warning does not stop a row).</summary>
        public int Importable { get; set; }
        /// <summary>True when the file has no file-level problem and at least one row can be imported. Rows with errors are skipped by the import and listed, not blocking the rest.</summary>
        public bool CanImport { get; set; }
        public int MaxRows { get; set; }
        /// <summary>Rows without an error whose guardian's number already belongs to a guardian of the school (maybe a sibling). Information only.</summary>
        public int GuardianHints { get; set; }
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
        /// <summary>Rows that had errors and were NOT imported (their sheet row numbers are in <see cref="SkippedRowNumbers"/>); download the error report, fix them and upload them again.</summary>
        public int SkippedRows { get; set; }
        public List<int> SkippedRowNumbers { get; set; } = new();
        public string FirstStudentId { get; set; } = "";
        public string LastStudentId { get; set; } = "";
        public List<ImportClassCountDto> ByClass { get; set; } = new();
        /// <summary>Things the school still has to do (fees, logins). Plain sentences.</summary>
        public List<string> Notes { get; set; } = new();
    }
}
