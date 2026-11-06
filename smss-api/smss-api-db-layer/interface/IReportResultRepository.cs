using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record ResultBand(string Code, string Label, decimal? MinPercent, bool IsFail, short Sequence);
    /// <summary>A student with an Active enrollment, with what a card prints about them.</summary>
    public record ResultStudent(string StudentId, string AdmissionNumber, string FirstName, string? MiddleName, string LastName, string? RollNumber, DateTime DateOfBirth,
        long SectionId, string SectionName, long ClassId, string ClassName);
    public record ResultMark(long ExamSubjectId, long? SkillId, string StudentId, string State, decimal? Marks, string? GradeCode);
    public record ResultGuardian(string StudentId, string Relationship, string Name);
    public record PublicationRow(long PublicationId, long SchemeId, string SchemeName, long SectionId, long ClassId, string ClassName, string SectionName, string Scope, long? TermId, string? TermName,
        int Version, string Status, long PublishedBy, DateTime PublishedAt, string? Note, int StudentCount, string? ExcludedStudents);
    public record PublicationCardRow(long PubStudentId, long PublicationId, string StudentId, string StudentName, string AdmissionNumber, string? RollNumber, string ResultStatus,
        decimal? TotalObtained, decimal? TotalMax, decimal? Percentage, string? GradeCode, int? RankPosition);
    public record MyCardRow(long PublicationId, long PubStudentId, long SchemeId, string SchemeName, string ClassName, string SectionName, string Scope, string? TermName, int Version, DateTime PublishedAt,
        string ResultStatus, decimal? Percentage, string? GradeCode, int? RankPosition);

    /// <summary>Set-based reads for the result engine and the publications. A fixed number of queries whatever the class size.</summary>
    public interface IReportResultRepository
    {
        Task<List<ResultBand>> GetBandsAsync(string schoolId, long scaleId, CancellationToken ct);
        Task<List<ResultStudent>> GetStudentsAsync(string schoolId, long academicYearId, IReadOnlyCollection<long> sectionIds, int activeStatusId, CancellationToken ct);
        Task<List<ResultMark>> GetMarksAsync(string schoolId, IReadOnlyCollection<long> examSubjectIds, IReadOnlyCollection<string> studentIds, CancellationToken ct);
        Task<List<ResultGuardian>> GetParentsAsync(string schoolId, IReadOnlyCollection<string> studentIds, CancellationToken ct);
        Task<List<TbReportStudentActivityGrade>> GetActivityGradesAsync(long schemeId, long sectionId, CancellationToken ct);
        Task<List<TbReportTermStudent>> GetTermInputsAsync(long schemeId, long sectionId, CancellationToken ct);
        Task<Dictionary<long, string>> GetSubjectNamesAsync(string schoolId, IReadOnlyCollection<long> subjectIds, CancellationToken ct);
        Task<List<long>> GetClassSectionIdsAsync(string schoolId, long classId, int activeStatusId, CancellationToken ct);
        Task<Dictionary<long, string>> GetSectionNamesAsync(string schoolId, IReadOnlyCollection<long> sectionIds, CancellationToken ct);

        // publications
        Task<List<PublicationRow>> ListPublicationsAsync(string schoolId, long? sectionId, long? schemeId, CancellationToken ct);
        Task<PublicationRow?> GetPublicationAsync(string schoolId, long publicationId, CancellationToken ct);
        Task<TbReportPublication?> GetPublicationEntityAsync(string schoolId, long publicationId, CancellationToken ct);
        Task<TbReportPublication?> GetCurrentAsync(string schoolId, long schemeId, long sectionId, string scope, long? termId, bool track, CancellationToken ct);
        Task<int> MaxVersionAsync(string schoolId, long schemeId, long sectionId, string scope, long? termId, CancellationToken ct);
        Task<List<PublicationCardRow>> GetCardRowsAsync(long publicationId, CancellationToken ct);
        Task<TbReportPublicationStudent?> GetCardAsync(string schoolId, long publicationId, string studentId, CancellationToken ct);
        Task<List<MyCardRow>> GetMyCardsAsync(string schoolId, string studentId, CancellationToken ct);
        Task<MyCardRow?> GetMyCardAsync(string schoolId, string studentId, long publicationId, CancellationToken ct);
        Task LockPublicationKeyAsync(long schemeId, long sectionId, string scope, long? termId, CancellationToken ct);

        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        void Add(object entity);
        void AddRange(IEnumerable<object> entities);
        void ClearTracking();
    }
}
