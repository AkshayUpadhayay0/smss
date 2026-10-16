using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>One line of a fee structure with its fee type and frequency names / code.</summary>
    public class SourceLineRow
    {
        public long FeeTypeId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FeeTypeCode { get; set; } = null!;
        /// <summary>Optional fee heads are opted into per student; non-optional ones always apply.</summary>
        public bool IsOptional { get; set; }
        /// <summary>Per period of <see cref="FrequencyCode"/>: 2,500 for a Monthly line means 2,500 each month.</summary>
        public decimal Amount { get; set; }
        public long FrequencyId { get; set; }
        public string FrequencyName { get; set; } = null!;
        public string FrequencyCode { get; set; } = null!;
        public short? DueDay { get; set; }
    }

    // Every method takes the school id: another school's items and dues can never be reached through this repository.
    public interface IStudentFeeBillingRepository
    {
        Task<List<LutFeeFrequency>> GetFrequenciesAsync(CancellationToken ct);
        Task<TbAcademicYears?> GetAcademicYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<List<SourceLineRow>> GetSourceLinesAsync(long feeStructureId, CancellationToken ct);
        /// <summary>This school's ACTIVE structure for the class + year (the one automatic assignment would pick), or null.</summary>
        Task<TbFeeStructures?> FindActiveStructureAsync(string schoolId, long classId, long academicYearId, CancellationToken ct);

        /// <summary>The student's items for the year, TRACKED, each with its dues.</summary>
        Task<List<TbStudentFeeItem>> GetItemsAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct);
        Task<TbStudentFeeItem?> GetItemAsync(string schoolId, string studentId, long itemId, CancellationToken ct);
        /// <summary>Items (TRACKED, with dues) of every student assigned to the structure, for one fee head.</summary>
        Task<List<TbStudentFeeItem>> GetItemsForStructureLineAsync(string schoolId, long feeStructureId, long feeTypeId, CancellationToken ct);
        /// <summary>(student, academic year) of everyone assigned to the structure.</summary>
        Task<List<(string StudentId, long AcademicYearId)>> GetAssignedStudentsAsync(string schoolId, long feeStructureId, CancellationToken ct);
        Task<Dictionary<long, string>> GetFeeTypeNamesAsync(string schoolId, CancellationToken ct);
        /// <summary>The date the student's enrollment for the academic year starts (their admission date for that year), or null when none.</summary>
        Task<DateOnly?> GetEnrollmentStartAsync(string schoolId, string studentId, long academicYearId, CancellationToken ct);

        Task AddItemAsync(TbStudentFeeItem item, CancellationToken ct);
        void RemoveItem(TbStudentFeeItem item);
        void RemoveDues(IEnumerable<TbStudentFeeDue> dues);
        Task<int> SaveChangesAsync(CancellationToken ct);
    }
}
