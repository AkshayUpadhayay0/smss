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
        public short? DueDay { get; set; }
        public DateOnly EffectiveFrom { get; set; }
    }

    /// <summary>A structure header with its class / year names and how many lines it has (for the list).</summary>
    public class FeeStructureListRow
    {
        public TbFeeStructures Structure { get; set; } = null!;
        public string AcademicYearName { get; set; } = null!;
        public string ClassName { get; set; } = null!;
        public int LineCount { get; set; }
    }

    /// <summary>One due of one student item of a fee head (DueDate null: the item has no dues at all). Used to work out what removing a fee line would do.</summary>
    public record LineRemovalFact(long ItemId, DateOnly? DueDate, decimal AmountDue, decimal AmountPaid);

    // Every method takes the school id: another school's structures can never be reached through this repository.
    public interface IFeeStructureRepository
    {
        Task<List<FeeStructureListRow>> GetListAsync(string schoolId, long? academicYearId, long? classId);
        Task<TbFeeStructures?> GetByIdAsync(string schoolId, long id, bool track = false);
        /// <summary>The structure with its lines tracked, for replacing the lines.</summary>
        Task<TbFeeStructures?> GetWithDetailsForUpdateAsync(string schoolId, long id);
        Task<List<FeeStructureLineRow>> GetLinesAsync(long feeStructureId);
        /// <summary>Start, end and "current" flag of this school's academic year, or null.</summary>
        Task<(DateOnly Start, DateOnly End, bool IsCurrent)?> GetYearRangeAsync(string schoolId, long academicYearId);
        /// <summary>Fee types of the structure that at least one assigned student has a due for.</summary>
        Task<HashSet<long>> GetFeeTypeIdsWithDuesAsync(string schoolId, long feeStructureId);
        /// <summary>True when any student is assigned to the structure.</summary>
        Task<bool> HasAssignedStudentsAsync(string schoolId, long feeStructureId);
        /// <summary>Every due (TRACKED) of every student assigned to the structure, for one fee head.</summary>
        Task<List<TbStudentFeeDue>> GetLineDuesAsync(string schoolId, long feeStructureId, long feeTypeId);
        /// <summary>Every student item of one fee head on the structure's students (for the structure's year), each with its dues, read fresh and untracked.</summary>
        Task<List<LineRemovalFact>> GetLineRemovalFactsAsync(string schoolId, long feeStructureId, long feeTypeId);
        void AddRemovalLog(TbFeeLineRemovalLog row);
        /// <summary>Deletes the dues with these ids (they are loaded through the change tracker first, so a due already tracked in this request is not attached twice).</summary>
        Task RemoveDuesAsync(IReadOnlyCollection<long> dueIds);
        /// <summary>Takes a transaction-scoped lock for every fee structure save of this school (released at commit / rollback), so two admins cannot sync the same structure at once.</summary>
        Task LockStructureSavesAsync(string schoolId);
        Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync();
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
