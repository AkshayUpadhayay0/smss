using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>One exam subject with everything the entry screens need to know about its place in the scheme.</summary>
    public record ExamSubjectInfo(
        long ExamSubjectId, long ExamId, string ExamName, long TermId, string TermName, DateOnly TermStart, DateOnly TermEnd, short TermSequence,
        long SchemeId, string SchemeName, long AcademicYearId, int SchemeStatusId, string EvaluationMode, long? GradeScaleId, string AttendanceSource,
        long SchemeSubjectId, long SubjectId, string SubjectName, string? LabelOverride, short DisplayOrder, long ClassId, string ClassName, string EntryMode, decimal? MaxMarks);

    public record SectionInfo(long SectionId, long ClassId, string ClassName, string SectionName);
    public record RosterStudent(string StudentId, string AdmissionNumber, string FirstName, string? MiddleName, string LastName, string? RollNumber);
    public record SkillInfo(long SkillId, long SchemeSubjectId, string Name, short Sequence);
    public record BandInfo(long GradeScaleId, string Code, string Label, short Sequence);
    public record EntryCountRow(long ExamSubjectId, string State, int Count);

    /// <summary>One (exam subject, section) pair for the "what may I enter" list.</summary>
    public record ContextRow(
        long SchemeId, string SchemeName, string EvaluationMode, long TermId, string TermName, short TermSequence, long ExamId, string ExamName, short ExamSequence,
        long ExamSubjectId, long SchemeSubjectId, long SubjectId, string SubjectName, short DisplayOrder, long ClassId, string ClassName, short ClassOrder,
        long SectionId, string SectionName, string EntryMode, decimal? MaxMarks);

    public record StatusSummary(long ExamSubjectId, long SectionId, string Status, int Version);
    public record TermSectionSummary(long TermId, long SectionId, string Status, DateTime? LockedAt, string? UnlockReason);
    public record MappedPair(long ClassId, long SubjectId);

    /// <summary>A scheme's structure for one class: terms, exams and the exam subjects, flattened.</summary>
    public record StructureRow(long TermId, string TermName, short TermSequence, DateOnly TermStart, DateOnly TermEnd, long ExamId, string ExamName, short ExamSequence,
        long ExamSubjectId, long SchemeSubjectId, string SubjectName, string? LabelOverride, short DisplayOrder, string EntryMode, decimal? MaxMarks);

    public record SchemeHeader(long SchemeId, string SchemeName, long AcademicYearId, int StatusId, string EvaluationMode, long? GradeScaleId, string AttendanceSource);
    public record TermHeader(long TermId, string TermName, short Sequence, DateOnly StartDate, DateOnly EndDate, long SchemeId, string SchemeName, long AcademicYearId, int SchemeStatusId,
        string EvaluationMode, string AttendanceSource, long? GradeScaleId);
    public record AreaInfo(long AreaId, string Category, string Name, short Sequence, long RatingScaleId);

    /// <summary>Which parts of a scheme already have entries. Drives the real "no changes under existing entries" guard.</summary>
    public sealed class EntryFootprint
    {
        public HashSet<long> ExamSubjectIds { get; } = new();
        public HashSet<long> SchemeSubjectIds { get; } = new();
        public HashSet<long> ExamIds { get; } = new();
        public HashSet<long> TermIds { get; } = new();
        public HashSet<long> ClassIds { get; } = new();
        /// <summary>Terms that have a section LOCKED right now.</summary>
        public HashSet<long> LockedTermIds { get; } = new();
        public bool Any => ExamSubjectIds.Count > 0 || TermIds.Count > 0;
    }

    /// <summary>Data access for marks entry and the class teacher's inputs. Set-based reads: a fixed number of queries whatever the class size.</summary>
    public interface IReportMarksRepository
    {
        Task<int?> ActiveStatusIdAsync(CancellationToken ct);
        Task<TbAcademicYears?> CurrentYearAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<TbAcademicYears?> YearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<string?> UserLoginIdAsync(string schoolId, long userId, CancellationToken ct);

        Task<ExamSubjectInfo?> GetExamSubjectAsync(string schoolId, long examSubjectId, CancellationToken ct);
        Task<SectionInfo?> GetSectionAsync(string schoolId, long sectionId, int activeStatusId, CancellationToken ct);
        Task<List<SkillInfo>> GetSkillsAsync(long schemeSubjectId, CancellationToken ct);
        Task<List<BandInfo>> GetBandsAsync(string schoolId, IReadOnlyCollection<long> scaleIds, CancellationToken ct);

        // eligibility inputs
        Task<bool> IsMappedAsync(string schoolId, long classId, long subjectId, int activeStatusId, CancellationToken ct);
        Task<HashSet<long>> ClassTeacherSectionIdsAsync(string schoolId, string employeeId, long academicYearId, int activeStatusId, CancellationToken ct);
        Task<HashSet<(long SectionId, long SubjectId)>> TeachingPairsAsync(string schoolId, string employeeId, long academicYearId, int activeStatusId, CancellationToken ct);

        // roster and entries
        Task<List<RosterStudent>> GetRosterAsync(string schoolId, long academicYearId, long sectionId, int activeStatusId, CancellationToken ct);
        Task<List<RosterStudent>> GetStudentsAsync(string schoolId, IReadOnlyCollection<string> studentIds, CancellationToken ct);
        Task<List<TbReportMark>> GetMarksAsync(string schoolId, long examSubjectId, long sectionId, IReadOnlyCollection<string> rosterIds, bool track, CancellationToken ct);
        Task<TbReportEntryStatus?> GetStatusAsync(long examSubjectId, long sectionId, bool forUpdate, CancellationToken ct);
        Task<TbReportTermSection?> GetTermSectionAsync(long termId, long sectionId, bool track, CancellationToken ct);
        /// <summary>One transaction-scoped lock per (term, section): serialises a lock / unlock against class-teacher input saves.</summary>
        Task LockTermSectionKeyAsync(long termId, long sectionId, CancellationToken ct);
        Task<List<StatusSummary>> GetStatusesForSectionAsync(string schoolId, long sectionId, IReadOnlyCollection<long> examSubjectIds, CancellationToken ct);
        Task<List<TbReportEntryStatus>> GetStatusesForUpdateAsync(string schoolId, long sectionId, IReadOnlyCollection<long> examSubjectIds, CancellationToken ct);
        Task<List<EntryCountRow>> GetEntryCountsAsync(string schoolId, long sectionId, IReadOnlyCollection<string> studentIds, IReadOnlyCollection<long> examSubjectIds, CancellationToken ct);
        Task<List<(long ExamSubjectId, long SchemeSubjectId)>> TermExamSubjectsAsync(long termId, long classId, CancellationToken ct);
        /// <summary>How many entry rows each (exam subject, student) has: one set-based query, used to find who is missing from an accepted grid.</summary>
        Task<List<(long ExamSubjectId, string StudentId, int Count)>> GetStudentEntryCountsAsync(string schoolId, IReadOnlyCollection<string> studentIds, IReadOnlyCollection<long> examSubjectIds, CancellationToken ct);

        // the "what may I enter" list (whole year, a handful of queries)
        Task<List<ContextRow>> GetContextRowsAsync(string schoolId, long academicYearId, int activeStatusId, CancellationToken ct);
        Task<List<StatusSummary>> GetStatusesForYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<List<TermSectionSummary>> GetTermSectionsForYearAsync(string schoolId, long academicYearId, CancellationToken ct);
        Task<HashSet<MappedPair>> GetMappedPairsAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<Dictionary<long, int>> GetSkillCountsAsync(string schoolId, long academicYearId, CancellationToken ct);

        // class review
        Task<SchemeHeader?> GetSchemeAsync(string schoolId, long schemeId, CancellationToken ct);
        Task<TermHeader?> GetTermAsync(string schoolId, long termId, CancellationToken ct);
        Task<List<StructureRow>> GetStructureAsync(string schoolId, long schemeId, long classId, CancellationToken ct);
        Task<bool> ClassInSchemeAsync(long schemeId, long classId, CancellationToken ct);
        Task<long?> LastTermIdAsync(long schemeId, CancellationToken ct);
        Task<List<TermSectionSummary>> GetTermSectionsAsync(long schemeId, long sectionId, CancellationToken ct);
        Task<List<AreaInfo>> GetActivityAreasAsync(string schoolId, int activeStatusId, CancellationToken ct);
        Task<List<TbReportStudentActivityGrade>> GetActivityGradesAsync(long termId, long sectionId, bool track, CancellationToken ct);
        /// <summary>The remarks of one (term, scheme subject) for the given students.</summary>
        Task<List<TbReportSubjectRemark>> GetSubjectRemarksAsync(long termId, long schemeSubjectId, IReadOnlyCollection<string> studentIds, bool track, CancellationToken ct);
        Task<List<TbReportTermStudent>> GetTermStudentsAsync(long termId, long sectionId, bool track, CancellationToken ct);
        Task<List<TbReportYearStudent>> GetYearStudentsAsync(long schemeId, long sectionId, bool track, CancellationToken ct);
        /// <summary>Everything under a scheme that already has entries or a lock: exam subjects, scheme subjects, exams, terms, classes.</summary>
        Task<EntryFootprint> GetFootprintAsync(string schoolId, long schemeId, CancellationToken ct);

        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        void Add(object entity);
        void AddRange(IEnumerable<object> entities);
        void Remove(object entity);
        void RemoveRange(IEnumerable<object> entities);
        void ClearTracking();
    }
}
