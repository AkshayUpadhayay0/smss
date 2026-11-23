using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record CopyYear(long AcademicYearId, string YearName, DateOnly StartDate, bool IsClosed, bool IsActiveStatus);

    /// <summary>What one part of a copy did (or, in a preview, would do).</summary>
    public class CopyPartOutcome
    {
        public Dictionary<string, int> Created { get; } = new();
        public int SkippedExisting { get; set; }
        public List<CopySkip> Skipped { get; } = new();
        /// <summary>Extra facts worth showing (slots flagged "needs a teacher", ...).</summary>
        public Dictionary<string, int> Notes { get; } = new();
    }

    public record CopySkip(string Reason, string Ref);
    public record CopyLogRow(TbYearCopyLog Log, string SourceYearName, string? RunByName);

    // Every method takes the school id: another school's years and rows can never be reached through this repository.
    public interface IYearCopyRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        /// <summary>The per-school academic-setup lock (transaction scoped), the same one every other multi-row setup save takes.</summary>
        Task LockSchoolAsync(string schoolId, CancellationToken ct);
        Task<CopyYear?> GetYearAsync(string schoolId, long academicYearId, CancellationToken ct);

        /// <summary>
        /// Runs ONE part of the copy inside the caller's transaction. It only INSERTs into the target year (skip-if-exists on natural keys; never updates or deletes anything; the source is only read;
        /// masters are only referenced). The caller rolls back for a preview and commits for a real run, so both use exactly this code.
        /// </summary>
        Task<CopyPartOutcome> RunPartAsync(string part, string schoolId, CopyYear source, CopyYear target, long? userId, CancellationToken ct);

        Task AddLogAsync(TbYearCopyLog log, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        void ClearTracker();
        /// <summary>The latest runs into a target year (newest first).</summary>
        Task<List<CopyLogRow>> GetLogAsync(string schoolId, long targetYearId, int take, CancellationToken ct);
    }
}
