using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    // Every method takes the school id: there is deliberately no way to read or change a mapping without one.
    public interface IClassSubjectRepository
    {
        Task<List<ClassSubjectWithNames>> GetByClassAsync(string schoolId, long classId, long academicYearId);
        Task<ClassSubjectWithNames?> GetWithNamesByIdAsync(string schoolId, long id);
        Task<TbClassSubjects?> GetByIdAsync(string schoolId, long id);   // tracked
        /// <summary>Tracked mappings of one class, for the bulk replace.</summary>
        Task<List<TbClassSubjects>> GetTrackedByClassAsync(string schoolId, long classId, long academicYearId);
        /// <summary>The class, only if it belongs to this school.</summary>
        Task<TbClasses?> GetClassAsync(string schoolId, long classId);
        /// <summary>How many of these subject ids belong to this school.</summary>
        Task<int> CountSubjectsAsync(string schoolId, IReadOnlyCollection<long> subjectIds);
        Task<bool> ExistsAsync(long academicYearId, long classId, long subjectId);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType);
        Task AddAsync(TbClassSubjects entity);
        void Remove(TbClassSubjects entity);
        void RemoveRange(IEnumerable<TbClassSubjects> entities);
        Task<int> SaveChangesAsync();

        // ---- S3: the year, the offering, usage and the section overrides
        Task<TbAcademicYears?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        /// <summary>The school's current year (null when it has none).</summary>
        Task<TbAcademicYears?> GetCurrentYearAsync(string schoolId, CancellationToken ct);
        /// <summary>The class is offered (Active) in that year.</summary>
        Task<bool> IsClassOfferedAsync(string schoolId, long academicYearId, long classId, int activeStatusId, CancellationToken ct);
        /// <summary>The Active sections offered that year for the class.</summary>
        Task<List<SubjectSectionBrief>> OfferedSectionsAsync(string schoolId, long academicYearId, long classId, int activeStatusId, CancellationToken ct);
        /// <summary>Every subject master of the school (the screen shows Inactive ones only when they are mapped).</summary>
        Task<List<TbSubjects>> AllSubjectsAsync(string schoolId, CancellationToken ct);
        /// <summary>What each subject is used by in that year and class, per section (timetable, homework, tests, entered marks) and per class (exam setups).</summary>
        Task<SubjectUsageSet> GetUsageAsync(string schoolId, long academicYearId, long classId, int activeStatusId, CancellationToken ct);
        Task<List<TbSectionSubjectOverride>> GetOverridesTrackedAsync(string schoolId, long academicYearId, long classId, CancellationToken ct);
        Task AddOverrideAsync(TbSectionSubjectOverride entity, CancellationToken ct);
        /// <summary>Takes the per-school academic-setup lock for the rest of the transaction (the same lock the offering save uses).</summary>
        Task LockSchoolAsync(string schoolId, CancellationToken ct);
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
    }

    public record SubjectSectionBrief(long SectionId, string SectionName);

    /// <summary>Counts of what uses one subject in one section (timetable slots, homework, tests, entered marks).</summary>
    public record SubjectUseRow(long SectionId, long SubjectId, int Timetable, int Homework, int Tests, int Marks)
    {
        public int Total => Timetable + Homework + Tests + Marks;
    }

    public class SubjectUsageSet
    {
        public List<SubjectUseRow> PerSection { get; init; } = new();
        /// <summary>Report-card exam setups (scheme subjects of the class in that year), by subject.</summary>
        public Dictionary<long, int> ExamSetups { get; init; } = new();
    }
}
