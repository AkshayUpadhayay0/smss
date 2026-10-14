using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>One fee structure line joined to its fee type and frequency names.</summary>
    public class FeeStructureLineRow
    {
        public long FeeStructureDetailId { get; set; }
        public long FeeTypeId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FeeTypeCode { get; set; } = null!;
        public long FrequencyId { get; set; }
        public string FrequencyName { get; set; } = null!;
        public decimal Amount { get; set; }
    }

    /// <summary>A structure header with its class / year names and how many lines it has (for the list).</summary>
    public class FeeStructureListRow
    {
        public TbFeeStructures Structure { get; set; } = null!;
        public string AcademicYearName { get; set; } = null!;
        public string ClassName { get; set; } = null!;
        public int LineCount { get; set; }
    }

    // Every method takes the school id: another school's structures can never be reached through this repository.
    public interface IFeeStructureRepository
    {
        Task<List<FeeStructureListRow>> GetListAsync(string schoolId, long? academicYearId, long? classId);
        Task<TbFeeStructures?> GetByIdAsync(string schoolId, long id, bool track = false);
        /// <summary>The structure with its lines tracked, for replacing the lines.</summary>
        Task<TbFeeStructures?> GetWithDetailsForUpdateAsync(string schoolId, long id);
        Task<List<FeeStructureLineRow>> GetLinesAsync(long feeStructureId);
        Task<string?> GetAcademicYearNameAsync(string schoolId, long academicYearId);
        Task<string?> GetClassNameAsync(string schoolId, long classId);
        Task<bool> StructureExistsAsync(string schoolId, long academicYearId, long classId);
        /// <summary>This school's fee types among the ids (id -> IsActive); ids that are not this school's are absent.</summary>
        Task<Dictionary<long, bool>> GetFeeTypeActiveFlagsAsync(string schoolId, IReadOnlyCollection<long> ids);
        /// <summary>Active fee frequencies among the ids.</summary>
        Task<HashSet<long>> GetActiveFrequencyIdsAsync(IReadOnlyCollection<long> ids);
        Task<List<LutFeeFrequency>> GetFrequenciesAsync();
        Task AddAsync(TbFeeStructures entity);
        void RemoveDetails(IEnumerable<TbFeeStructureDetails> details);
        Task<int> SaveChangesAsync();
    }
}
