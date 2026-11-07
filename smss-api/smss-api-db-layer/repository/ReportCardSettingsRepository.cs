using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class ReportCardSettingsRepository : IReportCardSettingsRepository
    {
        private readonly dbContext _db;
        public ReportCardSettingsRepository(dbContext db) => _db = db;

        public Task<TbSchoolReportCardSettings?> GetAsync(string schoolId, CancellationToken ct) =>
            _db.SchoolReportCardSettings.AsNoTracking().FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);

        public Task<TbSchoolReportCardSettings?> GetForUpdateAsync(string schoolId, CancellationToken ct) =>
            _db.SchoolReportCardSettings.FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);

        public void Add(TbSchoolReportCardSettings row) => _db.SchoolReportCardSettings.Add(row);

        public async Task<bool> SignatureInUseAsync(string schoolId, string storageKey, CancellationToken ct) =>
            (await _db.Database.SqlQuery<long>($"SELECT count(*) AS \"Value\" FROM public.tb_report_publications WHERE school_id = {schoolId} AND config_snapshot ->> 'signatureKey' = {storageKey}").FirstAsync(ct)) > 0;

        public async Task<List<SchemeDesignRow>> GetSchemesAsync(string schoolId, int activeStatusId, CancellationToken ct)
        {
            var schemes = await _db.ReportSchemes.AsNoTracking().Where(s => s.SchoolId == schoolId && s.StatusId == activeStatusId)
                .Select(s => new { s.SchemeId, s.SchemeName, s.EvaluationMode, s.TemplateKey, s.AcademicYearId }).ToListAsync(ct);
            var ids = schemes.Select(s => s.SchemeId).ToList();
            var classes = await (from sc in _db.ReportSchemeClasses.AsNoTracking() join c in _db.Classes.AsNoTracking() on sc.ClassId equals c.ClassId
                                 where ids.Contains(sc.SchemeId) orderby c.SequenceOrder select new { sc.SchemeId, c.ClassName }).ToListAsync(ct);
            return schemes.OrderBy(s => s.SchemeName, StringComparer.OrdinalIgnoreCase)
                .Select(s => new SchemeDesignRow(s.SchemeId, s.SchemeName, s.EvaluationMode, s.TemplateKey, s.AcademicYearId, classes.Where(c => c.SchemeId == s.SchemeId).Select(c => c.ClassName).ToList())).ToList();
        }

        public async Task<bool> SetSchemeTemplateAsync(string schoolId, long schemeId, string templateKey, long userId, int activeStatusId, CancellationToken ct)
        {
            var n = await _db.ReportSchemes.Where(s => s.SchoolId == schoolId && s.SchemeId == schemeId && s.StatusId == activeStatusId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.TemplateKey, templateKey).SetProperty(s => s.UpdatedBy, userId).SetProperty(s => s.UpdatedAt, DateTime.UtcNow), ct);
            return n > 0;
        }

        public async Task LockSettingsKeyAsync(string schoolId, CancellationToken ct)
        {
            var key = $"report-card-settings:{schoolId}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
