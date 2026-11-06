using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record ScaleListRow(TbGradeScale Scale, string StatusName, int BandCount, int SchemeCount, int AreaCount);
    public record SchemeListRow(TbReportScheme Scheme, string YearName, string StatusName, int TermCount, int ExamCount, int SubjectCount);
    public record AreaListRow(TbReportActivityArea Area, string ScaleName, string StatusName);
    public record MappedSubject(long ClassId, long SubjectId, string SubjectName);
    public record ClassConflict(long ClassId, long SchemeId, string SchemeName);

    /// <summary>Everything a scheme owns, loaded together (tracked or not) so a service can validate and replace parts of it.</summary>
    public class SchemeGraph
    {
        public TbReportScheme Scheme { get; init; } = null!;
        public List<TbReportSchemeClass> Classes { get; init; } = new();
        public List<TbReportTerm> Terms { get; init; } = new();
        public List<TbReportExam> Exams { get; init; } = new();
        public List<TbReportExamClassDate> ClassDates { get; init; } = new();
        public List<TbReportSchemeSubject> Subjects { get; init; } = new();
        public List<TbReportExamSubject> Cells { get; init; } = new();
        public List<TbReportSubjectSkill> Skills { get; init; } = new();
    }

    // Every method takes the school id: another school's scales, schemes and areas can never be reached through this repository.
    public interface IReportSetupRepository
    {
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        void Add(object entity);
        void AddRange(IEnumerable<object> entities);
        void Remove(object entity);
        void RemoveRange(IEnumerable<object> entities);

        // grade scales
        Task<List<ScaleListRow>> ListScalesAsync(string schoolId, bool includeInactive, int activeStatusId, CancellationToken ct);
        Task<TbGradeScale?> GetScaleAsync(string schoolId, long scaleId, bool track, CancellationToken ct);
        Task<List<TbGradeScaleBand>> GetBandsAsync(string schoolId, long scaleId, bool track, CancellationToken ct);
        Task<bool> ScaleNameTakenAsync(string schoolId, string name, long? exceptId, int activeStatusId, CancellationToken ct);
        Task<TbGradeScale?> FindScaleByPresetAsync(string schoolId, string presetCode, int activeStatusId, CancellationToken ct);
        Task<(int Schemes, int Areas)> ScaleUsageAsync(string schoolId, long scaleId, int activeStatusId, CancellationToken ct);

        // lookups that already exist elsewhere in the project (read-only)
        Task<TbAcademicYears?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<Dictionary<long, string>> ClassNamesAsync(string schoolId, IReadOnlyCollection<long> classIds, int activeStatusId, CancellationToken ct);
        Task<List<MappedSubject>> MappedSubjectsAsync(string schoolId, IReadOnlyCollection<long> classIds, int activeStatusId, CancellationToken ct);
        Task<Dictionary<long, string>> SubjectNamesAsync(string schoolId, IReadOnlyCollection<long> subjectIds, CancellationToken ct);
        Task<Dictionary<long, string>> ScaleNamesAsync(string schoolId, IReadOnlyCollection<long> scaleIds, CancellationToken ct);

        // schemes
        Task<List<SchemeListRow>> ListSchemesAsync(string schoolId, long? academicYearId, bool includeInactive, int activeStatusId, CancellationToken ct);
        Task<TbReportScheme?> GetSchemeAsync(string schoolId, long schemeId, bool track, CancellationToken ct);
        Task<SchemeGraph?> LoadGraphAsync(string schoolId, long schemeId, bool track, CancellationToken ct);
        Task<bool> SchemeNameTakenAsync(string schoolId, long academicYearId, string name, long? exceptId, int activeStatusId, CancellationToken ct);
        Task<List<ClassConflict>> ClassConflictsAsync(string schoolId, long academicYearId, IReadOnlyCollection<long> classIds, long? exceptSchemeId, int activeStatusId, CancellationToken ct);

        // activity areas
        Task<List<AreaListRow>> ListAreasAsync(string schoolId, bool includeInactive, int activeStatusId, CancellationToken ct);
        Task<TbReportActivityArea?> GetAreaAsync(string schoolId, long areaId, bool track, CancellationToken ct);
        Task<bool> AreaNameTakenAsync(string schoolId, string category, string name, long? exceptId, int activeStatusId, CancellationToken ct);
    }
}
