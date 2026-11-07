using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>One report card settings row per school. Every method takes the school id: another school's row can never come back.</summary>
    public interface IReportCardSettingsRepository
    {
        Task<TbSchoolReportCardSettings?> GetAsync(string schoolId, CancellationToken ct);
        /// <summary>The row, tracked, for changing it.</summary>
        Task<TbSchoolReportCardSettings?> GetForUpdateAsync(string schoolId, CancellationToken ct);
        void Add(TbSchoolReportCardSettings row);
        /// <summary>True when any publication of this school was drawn with this signature file (its config snapshot names the key). Such a file must never be deleted.</summary>
        Task<bool> SignatureInUseAsync(string schoolId, string storageKey, CancellationToken ct);
        /// <summary>The active schemes of the school with their design and classes, for the design chooser.</summary>
        Task<List<SchemeDesignRow>> GetSchemesAsync(string schoolId, int activeStatusId, CancellationToken ct);
        /// <summary>Changes only the design (template_key) of one scheme of this school; false when there is no such active scheme.</summary>
        Task<bool> SetSchemeTemplateAsync(string schoolId, long schemeId, string templateKey, long userId, int activeStatusId, CancellationToken ct);
        /// <summary>One transaction-scoped lock per school: a publish and a settings or signature change take turns, so a card is never drawn from half-changed settings.</summary>
        Task LockSettingsKeyAsync(string schoolId, CancellationToken ct);
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
    }

    public record SchemeDesignRow(long SchemeId, string SchemeName, string EvaluationMode, string TemplateKey, long AcademicYearId, List<string> ClassNames);
}
