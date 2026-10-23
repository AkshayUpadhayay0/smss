using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class AdmissionDocumentRepository : IAdmissionDocumentRepository
    {
        private readonly dbContext _db;
        public AdmissionDocumentRepository(dbContext db) => _db = db;

        public Task<int?> GetGeneralStatusIdAsync(string name, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == name && s.Stype == "general status").Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<AdmissionStatusRow?> GetApplicationStatusAsync(string schoolId, long admissionId, CancellationToken ct) =>
            (from a in _db.Admissions.AsNoTracking()
             join s in _db.LutStatus.AsNoTracking() on a.StatusId equals s.Sid
             where a.SchoolId == schoolId && a.AdmissionId == admissionId
             select new AdmissionStatusRow(a.AdmissionId, s.Sname)).FirstOrDefaultAsync(ct);

        public Task<bool> DocumentTypeUsableAsync(string schoolId, long documentTypeId, int activeStatusId, CancellationToken ct) =>
            _db.DocumentTypes.AnyAsync(t => t.SchoolId == schoolId && t.DocumentTypeId == documentTypeId && t.StatusId == activeStatusId && (t.AppliesTo == "STUDENT" || t.AppliesTo == "BOTH"), ct);

        public Task<string?> GetSchoolCodeAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => (string?)s.SchoolCode).FirstOrDefaultAsync(ct);

        public async Task AddAsync(TbAdmissionApplicationDocument document, CancellationToken ct) => await _db.AdmissionApplicationDocuments.AddAsync(document, ct);

        public async Task<List<AdmissionDocumentRow>> GetDocumentsAsync(string schoolId, long admissionId, CancellationToken ct)
        {
            var rows = await (
                from d in _db.AdmissionApplicationDocuments.AsNoTracking()
                where d.SchoolId == schoolId && d.AdmissionId == admissionId
                join t in _db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals t.DocumentTypeId
                join st in _db.LutStatus.AsNoTracking() on d.StatusId equals st.Sid
                select new
                {
                    d, t.DocumentName, StatusLabel = st.Sname,
                    UploadedByName = _db.Users.Where(u => u.UserId == d.UploadedBy).Select(u => u.DisplayName ?? u.OrgUserId).FirstOrDefault()
                }).ToListAsync(ct);
            return rows.OrderByDescending(r => r.d.CreatedAt).ThenByDescending(r => r.d.ApplicationDocumentId)
                .Select(r => new AdmissionDocumentRow(r.d, r.DocumentName, r.UploadedByName, r.StatusLabel)).ToList();
        }

        public Task<TbAdmissionApplicationDocument?> GetDocumentAsync(string schoolId, long admissionId, long documentId, bool tracked, CancellationToken ct)
        {
            var q = tracked ? _db.AdmissionApplicationDocuments.AsTracking() : _db.AdmissionApplicationDocuments.AsNoTracking();
            return q.Where(d => d.SchoolId == schoolId && d.AdmissionId == admissionId && d.ApplicationDocumentId == documentId).FirstOrDefaultAsync(ct);
        }

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    }
}
