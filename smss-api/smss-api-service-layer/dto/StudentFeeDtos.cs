using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // None of these carry a school id: it always comes from the token.

    public class AssignFeeStructureRequest
    {
        [Required(ErrorMessage = "Fee structure is required."), Range(1, long.MaxValue, ErrorMessage = "Fee structure is required.")]
        public long? FeeStructureId { get; set; }
    }

    public class ApplyConcessionRequest
    {
        [Required(ErrorMessage = "Concession is required."), Range(1, long.MaxValue, ErrorMessage = "Concession is required.")]
        public long? ConcessionId { get; set; }

        [StringLength(500, ErrorMessage = "Remarks can be at most 500 characters.")]
        public string? Remarks { get; set; }
    }

    /// <summary>One fee head of the student's structure with what the applied concessions take off it.</summary>
    public class StudentFeeLineDto
    {
        public long FeeTypeId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FeeTypeCode { get; set; } = null!;
        public long FrequencyId { get; set; }
        public string FrequencyName { get; set; } = null!;
        public decimal Amount { get; set; }
        public decimal Discount { get; set; }
        public decimal NetAmount { get; set; }
    }

    public class StudentFeeConcessionDto
    {
        public long StudentFeeConcessionId { get; set; }
        public long ConcessionId { get; set; }
        public string ConcessionName { get; set; } = null!;
        public string ConcessionCode { get; set; } = null!;
        public string ConcessionType { get; set; } = null!;
        public decimal Value { get; set; }
        public long? ApplicableFeeTypeId { get; set; }
        public string? ApplicableFeeTypeName { get; set; }
        public string? Remarks { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime AppliedAt { get; set; }
        /// <summary>False when it targets a fee head the student's current structure does not have (so it takes nothing off).</summary>
        public bool IsApplicable { get; set; }
    }

    /// <summary>Gross, concession and net for one frequency. Different frequencies are never added together.</summary>
    public class StudentFeeTotalDto
    {
        public long FrequencyId { get; set; }
        public string FrequencyName { get; set; } = null!;
        public decimal Gross { get; set; }
        public decimal Concession { get; set; }
        public decimal NetPayable { get; set; }
    }

    public class StudentFeeStructureOptionDto
    {
        public long FeeStructureId { get; set; }
        public string StructureName { get; set; } = null!;
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
    }

    public class StudentFeeConcessionOptionDto
    {
        public long ConcessionId { get; set; }
        public string ConcessionName { get; set; } = null!;
        public string ConcessionCode { get; set; } = null!;
        public string ConcessionType { get; set; } = null!;
        public decimal Value { get; set; }
        public long? ApplicableFeeTypeId { get; set; }
        public string? ApplicableFeeTypeName { get; set; }
    }

    /// <summary>Everything the student's Fees tab shows, in one response.</summary>
    public class StudentFeesDto
    {
        public string StudentId { get; set; } = null!;
        public string StudentName { get; set; } = null!;

        /// <summary>The enrollment the fees are for (current academic year first). Null when the student has no enrollment.</summary>
        public long? AcademicYearId { get; set; }
        public string? AcademicYearName { get; set; }
        public long? ClassId { get; set; }
        public string? ClassName { get; set; }

        public long? FeeStructureId { get; set; }
        public string? StructureName { get; set; }
        public bool StructureIsActive { get; set; }

        public List<StudentFeeLineDto> Lines { get; set; } = new();
        public List<StudentFeeConcessionDto> Concessions { get; set; } = new();
        public List<StudentFeeTotalDto> Totals { get; set; } = new();

        /// <summary>Active fee structures of the academic year (all classes), for assigning or reassigning.</summary>
        public List<StudentFeeStructureOptionDto> AvailableStructures { get; set; } = new();
        /// <summary>Active concessions not yet applied that apply to all fee heads or to a fee head in this structure.</summary>
        public List<StudentFeeConcessionOptionDto> AvailableConcessions { get; set; } = new();
    }
}
