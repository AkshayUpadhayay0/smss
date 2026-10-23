using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class AdmissionDocumentService : IAdmissionDocumentService
    {
        private const string Rejected = "REJECTED";
        private const string Cancelled = "CANCELLED";
        private const string Admitted = "ADMITTED";

        private readonly IAdmissionDocumentRepository _repo;
        private readonly IFileStorageService _storage;
        private readonly ILogger<AdmissionDocumentService> _logger;

        public AdmissionDocumentService(IAdmissionDocumentRepository repo, IFileStorageService storage, ILogger<AdmissionDocumentService> logger)
        {
            _repo = repo;
            _storage = storage;
            _logger = logger;
        }

        public async Task<ApiResponse<List<AdmissionDocumentDto>>> GetDocumentsAsync(string? schoolId, long admissionId, bool includeRemoved, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<AdmissionDocumentDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                if (await _repo.GetApplicationStatusAsync(schoolId, admissionId, ct) == null) return Result<List<AdmissionDocumentDto>>(404, "Admission application not found.", null);
                var rows = await _repo.GetDocumentsAsync(schoolId, admissionId, ct);
                return Result(200, "Successfully fetched", rows.Where(r => includeRemoved || r.StatusName == StatusNames.Active).Select(ToDto).ToList());
            }
            catch (Exception ex) { return Error<List<AdmissionDocumentDto>>(ex, "listing application documents"); }
        }

        public async Task<ApiResponse<AdmissionDocumentDto>> UploadDocumentAsync(string? schoolId, long admissionId, long documentTypeId, string? remarks, long uploadedByUserId,
            Stream content, string fileName, long length, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<AdmissionDocumentDto>(400, TenantMessages.NoSchool, null);
            string? savedKey = null;
            var committed = false;
            try
            {
                remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
                if (remarks != null && remarks.Length > 500) return Result<AdmissionDocumentDto>(400, "Remarks can be at most 500 characters.", null);

                var application = await _repo.GetApplicationStatusAsync(schoolId, admissionId, ct);
                if (application == null) return Result<AdmissionDocumentDto>(404, "Admission application not found.", null);
                var locked = Locked(application.StatusName);
                if (locked != null) return Result<AdmissionDocumentDto>(409, locked, null);

                var (doc, error) = await DocumentUploadValidator.ValidateAsync(content, fileName, length, ct);
                if (doc == null) return Result<AdmissionDocumentDto>(400, error!, null);
                using var _ = doc;

                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError<AdmissionDocumentDto>("Active status is missing in lut_status");
                if (!await _repo.DocumentTypeUsableAsync(schoolId, documentTypeId, activeId.Value, ct))
                    return Result<AdmissionDocumentDto>(400, "The selected document type was not found for students in your school.", null);

                var schoolCode = await _repo.GetSchoolCodeAsync(schoolId, ct);
                if (!SchoolCodeRules.IsFolderSafe(schoolCode)) return Result<AdmissionDocumentDto>(400, "This school's code can't be used as a folder name. Please contact support.", null);

                savedKey = await _storage.SaveAdmissionDocumentAsync(schoolCode!, admissionId, doc.Content, doc.Extension, ct);
                var now = DateTime.UtcNow;
                var entity = new TbAdmissionApplicationDocument
                {
                    SchoolId = schoolId, AdmissionId = admissionId, DocumentTypeId = documentTypeId, DocumentPath = savedKey, OriginalFileName = SafeFileName(fileName),
                    ContentType = DocumentRules.ContentTypeOf(doc.Extension), FileSize = doc.Length, Remarks = remarks, UploadedBy = uploadedByUserId,
                    StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now,
                };
                await _repo.AddAsync(entity, ct);
                await _repo.SaveChangesAsync(ct);
                committed = true;

                var row = (await _repo.GetDocumentsAsync(schoolId, admissionId, ct)).First(r => r.Document.ApplicationDocumentId == entity.ApplicationDocumentId);
                return Result(201, "Document uploaded successfully.", ToDto(row));
            }
            catch (Exception ex)
            {
                if (savedKey != null && !committed) _storage.DeletePrivate(savedKey);   // no orphan file if the DB save failed
                return Error<AdmissionDocumentDto>(ex, "uploading an application document");
            }
        }

        public async Task<(ApiResponse<AdmissionDocumentDto>? Failure, Stream? Content, string? ContentType, string? FileName)> OpenDocumentAsync(string? schoolId, long admissionId, long documentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return (Result<AdmissionDocumentDto>(400, TenantMessages.NoSchool, null), null, null, null);
            try
            {
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                var doc = await _repo.GetDocumentAsync(schoolId, admissionId, documentId, false, ct);
                // a removed document is hidden: 404, same as one that never existed (or belongs to another school or application)
                if (doc == null || doc.StatusId != activeId) return (Result<AdmissionDocumentDto>(404, "Document not found.", null), null, null, null);

                var stream = _storage.OpenPrivate(doc.DocumentPath);
                if (stream == null)
                {
                    _logger.LogError("Application document {DocumentId} is in the database but its file is missing", documentId);
                    return (Result<AdmissionDocumentDto>(404, "The file for this document could not be found.", null), null, null, null);
                }
                return (null, stream, doc.ContentType, doc.OriginalFileName);
            }
            catch (Exception ex) { return (Error<AdmissionDocumentDto>(ex, "opening an application document"), null, null, null); }
        }

        public async Task<ApiResponse<AdmissionDocumentDto>> RemoveDocumentAsync(string? schoolId, long admissionId, long documentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<AdmissionDocumentDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var application = await _repo.GetApplicationStatusAsync(schoolId, admissionId, ct);
                if (application == null) return Result<AdmissionDocumentDto>(404, "Admission application not found.", null);
                var locked = Locked(application.StatusName);
                if (locked != null) return Result<AdmissionDocumentDto>(409, locked, null);

                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                var inactiveId = await _repo.GetGeneralStatusIdAsync(StatusNames.Inactive, ct);
                if (activeId == null || inactiveId == null) return ConfigError<AdmissionDocumentDto>("Active / Inactive statuses are missing in lut_status");

                var doc = await _repo.GetDocumentAsync(schoolId, admissionId, documentId, true, ct);
                if (doc == null || doc.StatusId != activeId) return Result<AdmissionDocumentDto>(404, "Document not found.", null);
                doc.StatusId = inactiveId.Value;
                doc.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                var row = (await _repo.GetDocumentsAsync(schoolId, admissionId, ct)).First(r => r.Document.ApplicationDocumentId == documentId);
                return Result(200, "Document removed.", ToDto(row));
            }
            catch (Exception ex) { return Error<AdmissionDocumentDto>(ex, "removing an application document"); }
        }

        // ---------------------------------------------------------------- helpers
        /// <summary>Documents are frozen once an application reaches an end state (reads and downloads stay open).</summary>
        private static string? Locked(string statusName) => statusName switch
        {
            Rejected => "This application was rejected, so its documents can no longer be changed.",
            Cancelled => "This application was withdrawn, so its documents can no longer be changed.",
            Admitted => "This applicant has already been admitted as a student, so the application documents can no longer be changed.",
            _ => null,
        };

        private static string SafeFileName(string name)
        {
            var n = Path.GetFileName(name ?? string.Empty).Trim();
            if (n.Length == 0) n = "document";
            return n.Length > 255 ? n[^255..] : n;
        }

        private static AdmissionDocumentDto ToDto(AdmissionDocumentRow r) => new()
        {
            ApplicationDocumentId = r.Document.ApplicationDocumentId, DocumentTypeId = r.Document.DocumentTypeId, DocumentTypeName = r.DocumentTypeName,
            FileName = r.Document.OriginalFileName, ContentType = r.Document.ContentType, FileSize = r.Document.FileSize, Remarks = r.Document.Remarks,
            UploadedByName = r.UploadedByName, UploadedAt = r.Document.CreatedAt, StatusName = r.StatusName,
        };

        private ApiResponse<T> ConfigError<T>(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
