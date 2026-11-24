using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: there is deliberately no way to read or change a section without one.
    public interface ISectionRepository
    {
        /// <summary>All the school's sections (optionally of one class), or only those OFFERED (Active offering) in the given academic year.</summary>
        Task<List<SectionWithClass>> GetBySchoolAsync(string schoolId, long? classId = null, long? academicYearId = null);
        Task<SectionWithClass?> GetWithClassByIdAsync(string schoolId, long id);
        Task<TbSections?> GetByIdAsync(string schoolId, long id, bool track = false);
        /// <summary>The class, only if it belongs to this school.</summary>
        Task<TbClasses?> GetClassAsync(string schoolId, long classId);
        Task<bool> ExistsAsync(long classId, string sectionName, long? excludeId = null);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task<List<(int Sid, string Name)>> GetGeneralStatusesAsync(string statusType);
        Task AddAsync(TbSections entity);
        Task<IDbContextTransaction> BeginTransactionAsync();
        /// <summary>Offers the section in the school's current, open year when its class is offered there.</summary>
        Task OfferInCurrentYearAsync(string schoolId, long sectionId);
        /// <summary>What still uses the section in an OPEN academic year (empty = safe to deactivate).</summary>
        Task<List<string>> GetUseAsync(string schoolId, long sectionId);
        /// <summary>Everything that has ever referenced the section in ANY year (empty = never used, so it may move to another class).</summary>
        Task<List<string>> GetHistoryAsync(string schoolId, long sectionId);
        /// <summary>Names of the years in which the section has an offering row but <paramref name="newClassId"/> is not offered (a move there would break the offering).</summary>
        Task<List<string>> GetYearsWhereClassNotOfferedAsync(string schoolId, long sectionId, long newClassId);
        /// <summary>Moving an UNUSED section: points its offering rows at the new class IN PLACE (capacity and status kept). No offering row is ever deleted.
        /// Must run in the same transaction, before the section's own class_id changes (the key between the two is deferred).</summary>
        Task MoveOfferingsAsync(string schoolId, long sectionId, long newClassId);
        Task<int> SaveChangesAsync();
    }
}
