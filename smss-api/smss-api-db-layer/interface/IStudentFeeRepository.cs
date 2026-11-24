using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>A student's enrollment joined to its academic year and class (fees are per class and per year).</summary>
    public class StudentFeeEnrollmentRow
    {
        public long AcademicYearId { get; set; }
        public string AcademicYearName { get; set; } = null!;
        public DateOnly AcademicYearStart { get; set; }
        public bool AcademicYearIsCurrent { get; set; }
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
    }

    public class StudentFeeLineRow
    {
        public long FeeTypeId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FeeTypeCode { get; set; } = null!;
        public long FrequencyId { get; set; }
        public string FrequencyName { get; set; } = null!;
        public decimal Amount { get; set; }
    }

    /// <summary>A concession applied to a student, with the concession's own details and the names that go with it.</summary>
    public class AppliedConcessionRow
    {
        public long StudentFeeConcessionId { get; set; }
        public long ConcessionId { get; set; }
        public long AcademicYearId { get; set; }
        public string ConcessionName { get; set; } = null!;
        public string ConcessionCode { get; set; } = null!;
        public string ConcessionType { get; set; } = null!;
        public decimal Value { get; set; }
        public long? ApplicableFeeTypeId { get; set; }
        public string? ApplicableFeeTypeName { get; set; }
        public bool ConcessionIsActive { get; set; }
        public string? Remarks { get; set; }
        public string? ApprovedByName { get; set; }
        public DateTime AppliedAt { get; set; }
    }

    public class FeeStructureOptionRow
    {
        public long FeeStructureId { get; set; }
        public string StructureName { get; set; } = null!;
        public long ClassId { get; set; }
        public string ClassName { get; set; } = null!;
    }

    public class ConcessionOptionRow
    {
        public long ConcessionId { get; set; }
        public string ConcessionName { get; set; } = null!;
        public string ConcessionCode { get; set; } = null!;
        public string ConcessionType { get; set; } = null!;
        public decimal Value { get; set; }
        public long? ApplicableFeeTypeId { get; set; }
        public string? ApplicableFeeTypeName { get; set; }
    }

    // Every method takes the school id: another school's students, structures or concessions can never be reached.
    public interface IStudentFeeRepository
    {
        Task<TbStudent?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>The login id (org_user_id) of a user of this school; for a Student login it IS the student id. Null when the user is not in this school.</summary>
        Task<string?> GetUserLoginIdAsync(string schoolId, long userId, CancellationToken ct);
        Task<List<StudentFeeEnrollmentRow>> GetEnrollmentsAsync(string schoolId, string studentId, CancellationToken ct);

        Task<TbStudentFeeStructure?> GetAssignmentAsync(string schoolId, string studentId, long academicYearId, bool track, CancellationToken ct);
        Task<TbFeeStructures?> GetStructureAsync(string schoolId, long feeStructureId, CancellationToken ct);
        Task<List<StudentFeeLineRow>> GetStructureLinesAsync(long feeStructureId, CancellationToken ct);
        Task<List<FeeStructureOptionRow>> GetActiveStructuresAsync(string schoolId, long academicYearId, CancellationToken ct);

        /// <summary>The concessions assigned to the student FOR THE GIVEN YEAR only: a concession never carries over to another year.</summary>
        Task<List<AppliedConcessionRow>> GetAppliedConcessionsAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct);
        Task<TbStudentFeeConcession?> GetAppliedConcessionAsync(string studentId, long concessionId, long academicYearId, bool track, CancellationToken ct);
        /// <summary>The years the student has this concession for (to tell whether a removal needs the year).</summary>
        Task<List<long>> GetAppliedConcessionYearsAsync(string studentId, long concessionId, CancellationToken ct);
        Task<TbFeeConcessions?> GetConcessionAsync(string schoolId, long concessionId, CancellationToken ct);
        Task<List<ConcessionOptionRow>> GetActiveConcessionsAsync(string schoolId, CancellationToken ct);

        Task AddAssignmentAsync(TbStudentFeeStructure entity, CancellationToken ct);
        Task AddAppliedConcessionAsync(TbStudentFeeConcession entity, CancellationToken ct);
        void RemoveAppliedConcession(TbStudentFeeConcession entity);
        Task<int> SaveChangesAsync(CancellationToken ct);
    }
}
