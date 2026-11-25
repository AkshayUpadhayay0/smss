using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>Statuses of a student's required document. PENDING is derived (no row), never stored.</summary>
    public static class StudentDocumentStatuses
    {
        public const string Pending = "PENDING";
        public const string Submitted = "SUBMITTED";
        public const string Waived = "WAIVED";

        /// <summary>The requirement applies to students admitted ON OR AFTER the date it was switched on, so existing students are never flagged all at once.</summary>
        public static bool Applies(DateTime admissionDate, DateTime requiredFrom) => admissionDate.Date >= requiredFrom.Date;

        /// <summary>The status of one required type for one student: the active row's status, else PENDING.</summary>
        public static string Of(TbStudentDocument? active) => active == null || active.VoidedAt != null ? Pending : active.Status;
    }

    public class StudentDocumentService : IStudentDocumentService
    {
        private readonly IStudentDocumentRepository _repo;
        private readonly IFileStorageService _storage;
        private readonly ILogger<StudentDocumentService> _logger;

        public StudentDocumentService(IStudentDocumentRepository repo, IFileStorageService storage, ILogger<StudentDocumentService> logger)
        {
            _repo = repo; _storage = storage; _logger = logger;
        }

        // ---------------------------------------------------------------- read
        public async Task<ApiResponse<StudentDocumentsDto>> GetAsync(string? schoolId, string studentId, StudentDocumentCaller caller, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentDocumentsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result<StudentDocumentsDto>(404, "Student not found.", null);
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError<StudentDocumentsDto>("Active status is missing in lut_status");

                if (!caller.CanManage)
                {
                    // a Teacher reads the statuses of the students of the section they are class teacher of, nobody else's
                    if (!caller.IsTeacher) return Result<StudentDocumentsDto>(403, "You do not have access to student documents.", null);
                    var login = await _repo.GetLoginIdAsync(schoolId, caller.UserId, ct);
                    if (string.IsNullOrEmpty(login) || !await _repo.IsClassTeacherOfStudentAsync(schoolId, login, studentId, activeId.Value, ct))
                        return Result<StudentDocumentsDto>(403, "You can only see the documents of students in the section you are the class teacher of.", null);
                }

                var types = await _repo.GetRequiredTypesAsync(schoolId, activeId.Value, ct);
                var rows = await _repo.GetDocumentsAsync(schoolId, studentId, false, ct);
                var dto = new StudentDocumentsDto { StudentId = student.StudentId, StudentName = student.FullName, CanManage = caller.CanManage };
                foreach (var t in types.Where(t => StudentDocumentStatuses.Applies(student.AdmissionDate, t.RequiredFrom)))
                {
                    var row = rows.FirstOrDefault(r => r.Document.DocumentTypeId == t.TypeId);
                    dto.Required.Add(new StudentRequiredDocumentDto
                    {
                        DocumentTypeId = t.TypeId, DocumentTypeName = t.Name, DocumentTypeCode = t.Code,
                        Status = StudentDocumentStatuses.Of(row?.Document), StudentDocumentId = row?.Document.StudentDocumentId,
                    });
                }
                dto.PendingCount = dto.Required.Count(r => r.Status == StudentDocumentStatuses.Pending);
                dto.Documents = rows.Select(r => ToDto(r, caller.CanManage)).ToList();
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<StudentDocumentsDto>(ex, "reading student documents"); }
        }

        // ---------------------------------------------------------------- upload
        public async Task<ApiResponse<StudentDocumentDto>> UploadAsync(string? schoolId, string studentId, long documentTypeId, string? remarks, long userId,
            Stream content, string fileName, long length, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentDocumentDto>(400, TenantMessages.NoSchool, null);
            string? savedKey = null;
            var committed = false;
            try
            {
                remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
                if (remarks != null && remarks.Length > 500) return Result<StudentDocumentDto>(400, "Remarks can be at most 500 characters.", null);
                if (await _repo.GetStudentAsync(schoolId, studentId, ct) == null) return Result<StudentDocumentDto>(404, "Student not found.", null);

                var (doc, error) = await DocumentUploadValidator.ValidateAsync(content, fileName, length, ct);
                if (doc == null) return Result<StudentDocumentDto>(400, error!, null);
                using var _ = doc;

                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError<StudentDocumentDto>("Active status is missing in lut_status");
                if (!await _repo.DocumentTypeUsableAsync(schoolId, documentTypeId, activeId.Value, ct))
                    return Result<StudentDocumentDto>(400, "The selected document type was not found for students in your school.", null);

                var schoolCode = await _repo.GetSchoolCodeAsync(schoolId, ct);
                if (!SchoolCodeRules.IsFolderSafe(schoolCode)) return Result<StudentDocumentDto>(400, "This school's code can't be used as a folder name. Please contact support.", null);

                savedKey = await _storage.SaveStudentDocumentAsync(schoolCode!, studentId, doc.Content, doc.Extension, ct);
                var now = DateTime.UtcNow;
                await using (var tx = await _repo.BeginTransactionAsync(ct))
                {
                    await _repo.LockStudentAsync(schoolId, studentId, ct);   // two uploads for one student queue up
                    var current = await _repo.GetActiveForTypeAsync(schoolId, studentId, documentTypeId, ct);
                    if (current != null)
                    {
                        current.VoidedAt = now; current.VoidedBy = userId; current.VoidReason = "Replaced by a newer upload."; current.UpdatedAt = now;
                        await _repo.SaveChangesAsync(ct);   // the old row is out of the partial unique index before the new one goes in
                    }
                    var entity = new TbStudentDocument
                    {
                        SchoolId = schoolId, StudentId = studentId, DocumentTypeId = documentTypeId, Status = StudentDocumentStatuses.Submitted, DocumentPath = savedKey,
                        OriginalFileName = SafeFileName(fileName), ContentType = DocumentRules.ContentTypeOf(doc.Extension), FileSize = doc.Length, Remarks = remarks,
                        CreatedBy = userId, CreatedAt = now, UpdatedAt = now,
                    };
                    await _repo.AddAsync(entity, ct);
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    committed = true;
                    var row = (await _repo.GetDocumentsAsync(schoolId, studentId, false, ct)).First(r => r.Document.StudentDocumentId == entity.StudentDocumentId);
                    return Result(201, "Document uploaded successfully.", ToDto(row, true));
                }
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                if (savedKey != null && !committed) _storage.DeletePrivate(savedKey);
                return Result<StudentDocumentDto>(409, "This student's document was changed at the same moment. Please try again.", null);
            }
            catch (Exception ex)
            {
                if (savedKey != null && !committed) _storage.DeletePrivate(savedKey);   // no orphan file if the DB save failed
                return Error<StudentDocumentDto>(ex, "uploading a student document");
            }
        }

        // ---------------------------------------------------------------- waive
        public async Task<ApiResponse<StudentDocumentDto>> WaiveAsync(string? schoolId, string studentId, WaiveStudentDocumentRequestDto request, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentDocumentDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var reason = request.Reason?.Trim();
                if (string.IsNullOrEmpty(reason)) return Result<StudentDocumentDto>(400, "A reason is required to waive a document.", null);
                if (reason.Length > 500) return Result<StudentDocumentDto>(400, "The reason can be at most 500 characters.", null);
                if (await _repo.GetStudentAsync(schoolId, studentId, ct) == null) return Result<StudentDocumentDto>(404, "Student not found.", null);
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError<StudentDocumentDto>("Active status is missing in lut_status");
                if (!await _repo.DocumentTypeUsableAsync(schoolId, request.DocumentTypeId, activeId.Value, ct))
                    return Result<StudentDocumentDto>(400, "The selected document type was not found for students in your school.", null);

                var now = DateTime.UtcNow;
                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockStudentAsync(schoolId, studentId, ct);
                var current = await _repo.GetActiveForTypeAsync(schoolId, studentId, request.DocumentTypeId, ct);
                if (current != null)
                    return Result<StudentDocumentDto>(409, current.Status == StudentDocumentStatuses.Waived ? "This document is already waived for the student." : "This student already has this document. Void it first if you want to waive it instead.", null);
                var entity = new TbStudentDocument
                {
                    SchoolId = schoolId, StudentId = studentId, DocumentTypeId = request.DocumentTypeId, Status = StudentDocumentStatuses.Waived,
                    WaiverReason = reason, WaivedBy = userId, WaivedAt = now, CreatedBy = userId, CreatedAt = now, UpdatedAt = now,
                };
                await _repo.AddAsync(entity, ct);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                var row = (await _repo.GetDocumentsAsync(schoolId, studentId, false, ct)).First(r => r.Document.StudentDocumentId == entity.StudentDocumentId);
                return Result(201, "Document waived.", ToDto(row, true));
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Result<StudentDocumentDto>(409, "This student's document was changed at the same moment. Please try again.", null);
            }
            catch (Exception ex) { return Error<StudentDocumentDto>(ex, "waiving a student document"); }
        }

        // ---------------------------------------------------------------- void
        public async Task<ApiResponse<StudentDocumentDto>> VoidAsync(string? schoolId, string studentId, long documentId, string? reason, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentDocumentDto>(400, TenantMessages.NoSchool, null);
            try
            {
                reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
                if (reason != null && reason.Length > 500) return Result<StudentDocumentDto>(400, "The reason can be at most 500 characters.", null);
                var doc = await _repo.GetDocumentAsync(schoolId, studentId, documentId, true, ct);
                if (doc == null) return Result<StudentDocumentDto>(404, "Document not found.", null);
                if (doc.VoidedAt != null) return Result<StudentDocumentDto>(409, "This document was already voided.", null);
                var now = DateTime.UtcNow;
                doc.VoidedAt = now; doc.VoidedBy = userId; doc.VoidReason = reason; doc.UpdatedAt = now;   // the file stays in the private store: nothing is hard-deleted
                await _repo.SaveChangesAsync(ct);
                var row = (await _repo.GetDocumentsAsync(schoolId, studentId, true, ct)).First(r => r.Document.StudentDocumentId == documentId);
                return Result(200, "Document voided.", ToDto(row, true));
            }
            catch (Exception ex) { return Error<StudentDocumentDto>(ex, "voiding a student document"); }
        }

        // ---------------------------------------------------------------- download
        public async Task<(ApiResponse<StudentDocumentDto>? Failure, Stream? Content, string? ContentType, string? FileName)> OpenAsync(string? schoolId, string studentId, long documentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return (Result<StudentDocumentDto>(400, TenantMessages.NoSchool, null), null, null, null);
            try
            {
                var doc = await _repo.GetDocumentAsync(schoolId, studentId, documentId, false, ct);
                // a voided or waived row has no file to give: 404, same as one that never existed or belongs to another school or student
                if (doc == null || doc.VoidedAt != null || doc.Status != StudentDocumentStatuses.Submitted || doc.DocumentPath == null)
                    return (Result<StudentDocumentDto>(404, "Document not found.", null), null, null, null);
                var stream = _storage.OpenPrivate(doc.DocumentPath);
                if (stream == null)
                {
                    _logger.LogError("Student document {DocumentId} is in the database but its file is missing", documentId);
                    return (Result<StudentDocumentDto>(404, "The file for this document could not be found.", null), null, null, null);
                }
                return (null, stream, doc.ContentType, doc.OriginalFileName);
            }
            catch (Exception ex) { return (Error<StudentDocumentDto>(ex, "opening a student document"), null, null, null); }
        }

        // ---------------------------------------------------------------- pending
        public async Task<ApiResponse<PendingStudentDocumentsDto>> ListPendingAsync(string? schoolId, long? classId, long? sectionId, string? search, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PendingStudentDocumentsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<PendingStudentDocumentsDto>("Active status is missing in lut_status");
                var all = await _repo.ListPendingAsync(schoolId, active.Value, active.Value, null, null, null, ct);
                var shown = classId == null && sectionId == null && string.IsNullOrWhiteSpace(search) ? all : await _repo.ListPendingAsync(schoolId, active.Value, active.Value, classId, sectionId, search, ct);
                return Result(200, "Successfully fetched", new PendingStudentDocumentsDto
                {
                    TotalStudents = all.Count,
                    Students = shown.Select(r => new PendingStudentDocumentsRowDto { StudentId = r.StudentId, FullName = r.FullName, AdmissionNumber = r.AdmissionNumber, ClassName = r.ClassName, SectionName = r.SectionName, Missing = r.Missing }).ToList(),
                });
            }
            catch (Exception ex) { return Error<PendingStudentDocumentsDto>(ex, "listing pending student documents"); }
        }

        public async Task<ApiResponse<int>> CountPendingAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<int>(400, TenantMessages.NoSchool, 0);
            try
            {
                var active = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (active == null) return ConfigError<int>("Active status is missing in lut_status");
                return Result(200, "Successfully fetched", await _repo.CountPendingStudentsAsync(schoolId, active.Value, active.Value, ct));
            }
            catch (Exception ex) { return Error<int>(ex, "counting pending student documents"); }
        }

        // ---------------------------------------------------------------- carry-over (after the admission is committed)
        public async Task<int> CarryOverFromApplicationAsync(string schoolId, long admissionId, string studentId, long? userId, CancellationToken ct)
        {
            var carried = 0;
            try
            {
                var active = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                var schoolCode = await _repo.GetSchoolCodeAsync(schoolId, ct);
                if (active == null || !SchoolCodeRules.IsFolderSafe(schoolCode)) return 0;
                foreach (var source in await _repo.GetApplicationDocumentsToCarryAsync(schoolId, admissionId, active.Value, ct))
                {
                    string? newKey = null;
                    try
                    {
                        using var file = _storage.OpenPrivate(source.DocumentPath);
                        if (file == null) { _logger.LogWarning("Application document {DocumentId} has no file in the private store; not carried over", source.ApplicationDocumentId); continue; }
                        newKey = await _storage.SaveStudentDocumentAsync(schoolCode!, studentId, file, Path.GetExtension(source.DocumentPath), ct);
                        var now = DateTime.UtcNow;
                        await _repo.AddAsync(new TbStudentDocument
                        {
                            SchoolId = schoolId, StudentId = studentId, DocumentTypeId = source.DocumentTypeId, Status = StudentDocumentStatuses.Submitted, DocumentPath = newKey,
                            OriginalFileName = source.OriginalFileName, ContentType = source.ContentType, FileSize = source.FileSize, Remarks = source.Remarks,
                            SourceApplicationDocumentId = source.ApplicationDocumentId, CreatedBy = userId ?? source.UploadedBy, CreatedAt = now, UpdatedAt = now,
                        }, ct);
                        await _repo.SaveChangesAsync(ct);
                        carried++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not carry application document {DocumentId} over to student {StudentId}", source.ApplicationDocumentId, studentId);
                        _repo.ClearTracking();
                        if (newKey != null) _storage.DeletePrivate(newKey);   // the copy of a row that did not save
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not carry the application documents of {AdmissionId} over to student {StudentId}", admissionId, studentId); }
            return carried;
        }

        // ---------------------------------------------------------------- helpers
        private static string SafeFileName(string name)
        {
            var n = Path.GetFileName(name ?? string.Empty).Trim();
            if (n.Length == 0) n = "document";
            return n.Length > 255 ? n[^255..] : n;
        }

        private static StudentDocumentDto ToDto(StudentDocumentRow r, bool full)
        {
            var d = r.Document;
            return new StudentDocumentDto
            {
                StudentDocumentId = d.StudentDocumentId, DocumentTypeId = d.DocumentTypeId, DocumentTypeName = r.TypeName, Status = d.Status,
                FileName = full ? d.OriginalFileName : null, ContentType = full ? d.ContentType : null, FileSize = full ? d.FileSize : null, Remarks = full ? d.Remarks : null,
                WaiverReason = full ? d.WaiverReason : null, ByName = full ? r.ByName : null,
                At = d.Status == StudentDocumentStatuses.Waived ? d.WaivedAt ?? d.CreatedAt : d.CreatedAt,
                FromApplication = d.SourceApplicationDocumentId != null,
                Voided = d.VoidedAt != null, VoidedAt = d.VoidedAt, VoidReason = full ? d.VoidReason : null,
            };
        }

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
