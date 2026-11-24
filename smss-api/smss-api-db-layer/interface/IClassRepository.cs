using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: there is deliberately no way to read or change a class without one.
    public interface IClassRepository
    {
        /// <summary>All the school's classes, or only those OFFERED (Active offering) in the given academic year.</summary>
        Task<List<TbClasses>> GetBySchoolAsync(string schoolId, long? academicYearId = null);
        Task<TbClasses?> GetByIdAsync(string schoolId, long id, bool track = false);
        Task<bool> ExistsAsync(string schoolId, string className, long? excludeId = null);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task<List<(int Sid, string Name)>> GetGeneralStatusesAsync(string statusType);
        Task AddAsync(TbClasses entity);
        Task<IDbContextTransaction> BeginTransactionAsync();
        /// <summary>Offers the class in the school's current, open academic year (no current year: nothing happens).</summary>
        Task OfferInCurrentYearAsync(string schoolId, long classId);
        /// <summary>What still uses the class in an OPEN academic year (empty = safe to deactivate): enrolments, fee structures, subject mappings, class teachers, timetable slots.</summary>
        Task<List<string>> GetUseAsync(string schoolId, long classId);
        /// <summary>The name of the class that already has <paramref name="targetClassId"/> as its next class (other than <paramref name="exceptClassId"/>), or null.</summary>
        Task<string?> GetClassPointingToAsync(string schoolId, long targetClassId, long? exceptClassId = null);
        /// <summary>True when following next_class_id from <paramref name="nextClassId"/> reaches <paramref name="classId"/> (setting it would make a loop).</summary>
        Task<bool> NextWouldLoopAsync(string schoolId, long classId, long nextClassId);
        /// <summary>Class names of the school by id (for showing the next class).</summary>
        Task<Dictionary<long, string>> GetClassNamesAsync(string schoolId);
        Task<int> SaveChangesAsync();
    }
}
