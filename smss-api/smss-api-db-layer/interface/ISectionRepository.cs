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
        /// <summary>Moving a section to another class: its year offerings are set aside (temp table) so the section row can change class...</summary>
        Task DetachOfferingsAsync(string schoolId, long sectionId);
        /// <summary>...and put back under the new class, for every year in which that class is offered (capacity and status kept).</summary>
        Task ReattachOfferingsAsync(string schoolId, long sectionId, long newClassId);
        Task<int> SaveChangesAsync();
    }
}
