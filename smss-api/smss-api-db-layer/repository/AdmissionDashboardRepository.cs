using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class AdmissionDashboardRepository : IAdmissionDashboardRepository
    {
        private const string AdmissionStatusType = "ADMISSION_STATUS", GeneralStatusType = "general status", Active = "Active";
        private readonly dbContext _db;
        public AdmissionDashboardRepository(dbContext db) => _db = db;

        public async Task<List<AdmissionMissingDocsRow>> GetMissingDocumentsAsync(string schoolId, IReadOnlyCollection<string> openStatuses, CancellationToken ct)
        {
            var activeId = await _db.LutStatus.AsNoTracking().Where(s => s.Sname == Active && s.Stype == GeneralStatusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);
            if (activeId == null) return new();

            var required = await _db.DocumentTypes.AsNoTracking()
                .Where(t => t.SchoolId == schoolId && t.StatusId == activeId && t.IsRequired && (t.AppliesTo == "STUDENT" || t.AppliesTo == "BOTH"))
                .Select(t => new { t.DocumentTypeId, t.DocumentName }).ToListAsync(ct);
            if (required.Count == 0) return new();

            var statuses = await _db.LutStatus.AsNoTracking().Where(s => s.Stype == AdmissionStatusType && openStatuses.Contains(s.Sname)).ToDictionaryAsync(s => s.Sid, s => s.Sname, ct);
            if (statuses.Count == 0) return new();
            var openIds = statuses.Keys.ToList();

            var apps = await _db.Admissions.AsNoTracking().Where(a => a.SchoolId == schoolId && openIds.Contains(a.StatusId))
                .Select(a => new { a.AdmissionId, a.ApplicationNumber, a.ApplicantFirstName, a.ApplicantMiddleName, a.ApplicantLastName, a.StatusId, a.ApplicationDate }).ToListAsync(ct);
            if (apps.Count == 0) return new();

            var appIds = apps.Select(a => a.AdmissionId).ToList();
            var have = (await _db.AdmissionApplicationDocuments.AsNoTracking()
                    .Where(d => d.SchoolId == schoolId && d.StatusId == activeId && appIds.Contains(d.AdmissionId))
                    .Select(d => new { d.AdmissionId, d.DocumentTypeId }).Distinct().ToListAsync(ct))
                .GroupBy(d => d.AdmissionId).ToDictionary(g => g.Key, g => g.Select(x => x.DocumentTypeId).ToHashSet());

            return apps.Select(a =>
                {
                    var got = have.GetValueOrDefault(a.AdmissionId) ?? new HashSet<long>();
                    var missing = required.Where(t => !got.Contains(t.DocumentTypeId)).Select(t => t.DocumentName).ToList();
                    return new AdmissionMissingDocsRow(a.AdmissionId, a.ApplicationNumber,
                        string.Join(" ", new[] { a.ApplicantFirstName, a.ApplicantMiddleName, a.ApplicantLastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
                        statuses[a.StatusId], a.ApplicationDate, missing);
                })
                .Where(r => r.MissingDocuments.Count > 0)
                .OrderByDescending(r => r.ApplicationDate).ThenByDescending(r => r.AdmissionId).ToList();
        }
    }
}
