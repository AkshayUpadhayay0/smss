using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class HomeworkService : IHomeworkService
    {
        private const int MaxDescription = 2000;

        private readonly IHomeworkRepository _repo;
        private readonly ITimetableService _timetable;
        private readonly IFileStorageService _storage;
        private readonly ILogger<HomeworkService> _logger;

        public HomeworkService(IHomeworkRepository repo, ITimetableService timetable, IFileStorageService storage, ILogger<HomeworkService> logger)
        {
            _repo = repo;
            _timetable = timetable;
            _storage = storage;
            _logger = logger;
        }

        // ---------------------------------------------------------------- create
        public async Task<ApiResponse<HomeworkDto>> CreateAsync(string? schoolId, HomeworkCaller caller, CreateHomeworkRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<HomeworkDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var bad = ValidateText(req.Title, req.Description, out var title, out var description);
                if (bad != null) return Result<HomeworkDto>(400, bad, null);
                var assigned = req.AssignedDate ?? SchoolClock.Today();
                var due = req.DueDate!.Value;
                if (due < assigned) return Result<HomeworkDto>(400, "The due date can't be before the assigned date.", null);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<HomeworkDto>();
                var (loginId, _) = await _repo.GetUserAsync(schoolId, caller.UserId, ct);

                // who owns it: a teacher always themselves; a School Admin names the teacher (or is one)
                string? ownerId;
                if (caller.IsSchoolAdmin) ownerId = string.IsNullOrWhiteSpace(req.EmployeeId) ? loginId : req.EmployeeId.Trim();
                else
                {
                    if (!string.IsNullOrWhiteSpace(req.EmployeeId) && !string.Equals(req.EmployeeId.Trim(), loginId, StringComparison.Ordinal))
                        return Result<HomeworkDto>(403, "A teacher can only set homework as themselves.", null);
                    ownerId = loginId;
                }
                var owner = string.IsNullOrEmpty(ownerId) ? null : await _repo.GetEmployeeAsync(schoolId, ownerId, ct);
                if (owner == null)
                    return caller.IsSchoolAdmin ? Result<HomeworkDto>(400, "Choose the teacher who owns this homework.", null) : Result<HomeworkDto>(403, "Your account is not linked to an employee record.", null);
                if (owner.StatusId != activeId || owner.EmployeeType != EmployeeTypes.Teaching || owner.EmploymentBlocksLogin)
                    return Result<HomeworkDto>(400, "The owning teacher must be an active teaching employee.", null);

                var section = await _repo.GetSectionAsync(schoolId, req.SectionId!.Value, ct);
                if (section == null) return Result<HomeworkDto>(400, "The selected section was not found for your school.", null);
                if (section.SectionStatusId != activeId || section.ClassStatusId != activeId) return Result<HomeworkDto>(400, "The selected section (or its class) is inactive.", null);
                var subject = await _repo.GetSubjectAsync(schoolId, req.SubjectId!.Value, section.ClassId, activeId.Value, ct);
                if (subject == null || subject.StatusId != activeId) return Result<HomeworkDto>(400, "The selected subject was not found for your school.", null);
                // the same rule the Timetable enforces: a class can't be given homework in a subject it doesn't take. Everyone goes through it, a School Admin included.
                if (!subject.MappedToClass)
                    return Result<HomeworkDto>(400, $"{subject.SubjectName} is not taught in {section.ClassName}. Add it to the class under Class-Subject Mapping first.", null);

                var year = req.AcademicYearId != null ? await _repo.GetYearAsync(schoolId, req.AcademicYearId.Value, ct) : await _repo.GetYearContainingAsync(schoolId, assigned, ct);
                if (year == null)
                    return Result<HomeworkDto>(400, req.AcademicYearId != null ? "The selected academic year was not found for your school." : "No academic year covers the assigned date. Choose an academic year.", null);

                // a Teacher may only set homework for a section + subject the Timetable gives them; a School Admin bypasses this
                if (!caller.IsSchoolAdmin && !await _timetable.IsTeachingSectionSubjectAsync(schoolId, owner.EmployeeId, section.SectionId, subject.SubjectId, year.AcademicYearId, ct))
                    return Result<HomeworkDto>(403, $"You don't currently teach {subject.SubjectName} to {section.ClassName} - {section.SectionName} in {year.YearName}, so you can't set homework for it. Ask a School Admin to add it to your timetable.", null);

                var now = DateTime.UtcNow;
                var entity = new TbHomework
                {
                    SchoolId = schoolId, AcademicYearId = year.AcademicYearId, ClassId = section.ClassId, SectionId = section.SectionId, SubjectId = subject.SubjectId, EmployeeId = owner.EmployeeId,
                    Title = title!, Description = description, AssignedDate = assigned, DueDate = due, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now,
                };
                await _repo.AddAsync(entity, ct);
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, entity.HomeworkId, 201, "Homework published.", ct);
            }
            catch (Exception ex) { return Error<HomeworkDto>(ex, "creating homework"); }
        }

        // ---------------------------------------------------------------- update / delete
        public async Task<ApiResponse<HomeworkDto>> UpdateAsync(string? schoolId, HomeworkCaller caller, long id, UpdateHomeworkRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<HomeworkDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var bad = ValidateText(req.Title, req.Description, out var title, out var description);
                if (bad != null) return Result<HomeworkDto>(400, bad, null);
                if (req.DueDate!.Value < req.AssignedDate!.Value) return Result<HomeworkDto>(400, "The due date can't be before the assigned date.", null);

                var (entity, failure) = await LoadForChangeAsync(schoolId, caller, id, ct);
                if (entity == null) return failure!;
                entity.Title = title!; entity.Description = description; entity.AssignedDate = req.AssignedDate.Value; entity.DueDate = req.DueDate.Value; entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, id, 200, "Homework updated.", ct);
            }
            catch (Exception ex) { return Error<HomeworkDto>(ex, "updating homework"); }
        }

        public async Task<ApiResponse<HomeworkDto>> DeleteAsync(string? schoolId, HomeworkCaller caller, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<HomeworkDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (entity, failure) = await LoadForChangeAsync(schoolId, caller, id, ct);
                if (entity == null) return failure!;
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (inactiveId == null) return ConfigError<HomeworkDto>();
                entity.StatusId = inactiveId.Value; entity.UpdatedAt = DateTime.UtcNow;   // hidden, never removed; its attachment file stays too
                await _repo.SaveChangesAsync(ct);
                var row = await _repo.GetRowAsync(schoolId, id, ct);
                return row == null ? Result<HomeworkDto>(500, "Something went wrong. Please try again later.", null) : Result(200, "Homework deleted.", ToDto(row, "Inactive"));
            }
            catch (Exception ex) { return Error<HomeworkDto>(ex, "deleting homework"); }
        }

        // ---------------------------------------------------------------- reads
        public async Task<ApiResponse<HomeworkDto>> GetAsync(string? schoolId, HomeworkCaller caller, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<HomeworkDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (row, failure) = await LoadVisibleAsync(schoolId, caller, id, ct);
                return row == null ? failure! : Result(200, "Successfully fetched", ToDto(row, "Active"));
            }
            catch (Exception ex) { return Error<HomeworkDto>(ex, "fetching homework"); }
        }

        public async Task<ApiResponse<List<HomeworkDto>>> GetMyAsync(string? schoolId, HomeworkCaller caller, long? academicYearId, long? sectionId, long? subjectId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<HomeworkDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rangeError = RangeError(from, to);
                if (rangeError != null) return Result<List<HomeworkDto>>(400, rangeError, null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<HomeworkDto>>();
                // the caller's own employee id is their login id; someone without one (the school-id login) owns nothing
                var (loginId, _) = await _repo.GetUserAsync(schoolId, caller.UserId, ct);
                if (string.IsNullOrEmpty(loginId)) return Result(200, "Successfully fetched", new List<HomeworkDto>());
                var rows = await _repo.ListAsync(schoolId, new HomeworkFilter(academicYearId, sectionId, subjectId, loginId, from, to), activeId.Value, ct);
                return Result(200, "Successfully fetched", Newest(rows));
            }
            catch (Exception ex) { return Error<List<HomeworkDto>>(ex, "listing my homework"); }
        }

        public async Task<ApiResponse<List<HomeworkDto>>> ListAsync(string? schoolId, long? academicYearId, long? sectionId, long? subjectId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<HomeworkDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rangeError = RangeError(from, to);
                if (rangeError != null) return Result<List<HomeworkDto>>(400, rangeError, null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<HomeworkDto>>();
                var rows = await _repo.ListAsync(schoolId, new HomeworkFilter(academicYearId, sectionId, subjectId, null, from, to), activeId.Value, ct);
                return Result(200, "Successfully fetched", Newest(rows));
            }
            catch (Exception ex) { return Error<List<HomeworkDto>>(ex, "listing homework"); }
        }

        public async Task<ApiResponse<List<HomeworkDto>>> GetSectionAsync(string? schoolId, long sectionId, long? academicYearId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<HomeworkDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rangeError = RangeError(from, to);
                if (rangeError != null) return Result<List<HomeworkDto>>(400, rangeError, null);
                if (await _repo.GetSectionAsync(schoolId, sectionId, ct) == null) return Result<List<HomeworkDto>>(404, "Section not found.", null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<HomeworkDto>>();
                var rows = await _repo.ListAsync(schoolId, new HomeworkFilter(academicYearId, sectionId, null, null, from, to), activeId.Value, ct);
                return Result(200, "Successfully fetched", Newest(rows));
            }
            catch (Exception ex) { return Error<List<HomeworkDto>>(ex, "listing a section's homework"); }
        }

        public async Task<ApiResponse<List<HomeworkDto>>> GetStudentMyAsync(string? schoolId, HomeworkCaller caller, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<HomeworkDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rangeError = RangeError(from, to);
                if (rangeError != null) return Result<List<HomeworkDto>>(400, rangeError, null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<HomeworkDto>>();
                var (sectionId, yearId) = await CurrentSectionAsync(schoolId, caller.UserId, activeId.Value, ct);
                if (sectionId == null || yearId == null) return Result(200, "Successfully fetched", new List<HomeworkDto>());   // not enrolled in the current year: nothing to show
                var rows = await _repo.ListAsync(schoolId, new HomeworkFilter(yearId, sectionId, null, null, from, to), activeId.Value, ct);
                return Result(200, "Successfully fetched", rows.OrderBy(r => r.DueDate).ThenBy(r => r.HomeworkId).Select(r => ToDto(r, "Active")).ToList());
            }
            catch (Exception ex) { return Error<List<HomeworkDto>>(ex, "listing a student's homework"); }
        }

        // ---------------------------------------------------------------- attachment
        public async Task<ApiResponse<HomeworkDto>> UploadAttachmentAsync(string? schoolId, HomeworkCaller caller, long id, Stream content, string fileName, long length, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<HomeworkDto>(400, TenantMessages.NoSchool, null);
            string? newKey = null;
            var committed = false;
            try
            {
                var (entity, failure) = await LoadForChangeAsync(schoolId, caller, id, ct);
                if (entity == null) return failure!;
                var (doc, error) = await DocumentUploadValidator.ValidateAsync(content, fileName, length, ct);
                if (doc == null) return Result<HomeworkDto>(400, error!, null);
                using var _ = doc;

                var school = await _repo.GetSchoolAsync(schoolId, ct);
                if (!SchoolCodeRules.IsFolderSafe(school?.SchoolCode)) return Result<HomeworkDto>(400, "This school's code can't be used as a folder name. Please contact support.", null);

                var oldKey = entity.AttachmentPath;
                newKey = await _storage.SaveHomeworkAttachmentAsync(school!.SchoolCode, id, doc.Content, doc.Extension, ct);
                entity.AttachmentPath = newKey; entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                committed = true;
                if (oldKey != null) _storage.DeletePrivate(oldKey);   // the single attachment is replaced
                return await ReplyAsync(schoolId, id, 200, "Attachment saved.", ct);
            }
            catch (Exception ex)
            {
                if (newKey != null && !committed) _storage.DeletePrivate(newKey);   // no orphan file if the DB save failed
                return Error<HomeworkDto>(ex, "uploading a homework attachment");
            }
        }

        public async Task<ApiResponse<HomeworkDto>> RemoveAttachmentAsync(string? schoolId, HomeworkCaller caller, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<HomeworkDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (entity, failure) = await LoadForChangeAsync(schoolId, caller, id, ct);
                if (entity == null) return failure!;
                if (entity.AttachmentPath == null) return Result<HomeworkDto>(404, "This homework has no attachment.", null);
                var key = entity.AttachmentPath;
                entity.AttachmentPath = null; entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                _storage.DeletePrivate(key);
                return await ReplyAsync(schoolId, id, 200, "Attachment removed.", ct);
            }
            catch (Exception ex) { return Error<HomeworkDto>(ex, "removing a homework attachment"); }
        }

        public async Task<(ApiResponse<HomeworkDto>? Failure, Stream? Content, string? ContentType, string? FileName)> OpenAttachmentAsync(string? schoolId, HomeworkCaller caller, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return (Result<HomeworkDto>(400, TenantMessages.NoSchool, null), null, null, null);
            try
            {
                // the same visibility rule as reading the homework row itself
                var (row, failure) = await LoadVisibleAsync(schoolId, caller, id, ct);
                if (row == null) return (failure, null, null, null);
                if (row.AttachmentPath == null) return (Result<HomeworkDto>(404, "This homework has no attachment.", null), null, null, null);

                var stream = _storage.OpenPrivate(row.AttachmentPath);
                if (stream == null)
                {
                    _logger.LogError("Homework {HomeworkId} has an attachment in the database but its file is missing", id);
                    return (Result<HomeworkDto>(404, "The attachment file could not be found.", null), null, null, null);
                }
                var ext = Path.GetExtension(row.AttachmentPath);
                return (null, stream, DocumentRules.ContentTypeOf(ext), AttachmentName(row.HomeworkId, row.AttachmentPath));
            }
            catch (Exception ex) { return (Error<HomeworkDto>(ex, "opening a homework attachment"), null, null, null); }
        }

        // ---------------------------------------------------------------- access
        /// <summary>The row to change: Active, in this school, and the caller is its owning teacher or a School Admin (a different teacher, even an eligible one, is refused).</summary>
        private async Task<(TbHomework? Entity, ApiResponse<HomeworkDto>? Failure)> LoadForChangeAsync(string schoolId, HomeworkCaller caller, long id, CancellationToken ct)
        {
            var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
            if (activeId == null) return (null, ConfigError<HomeworkDto>());
            var entity = await _repo.GetActiveForUpdateAsync(schoolId, id, activeId.Value, ct);
            if (entity == null) return (null, Result<HomeworkDto>(404, "Homework not found.", null));
            if (!caller.IsSchoolAdmin)
            {
                var (loginId, _) = await _repo.GetUserAsync(schoolId, caller.UserId, ct);
                if (!caller.IsTeacher || !string.Equals(loginId, entity.EmployeeId, StringComparison.Ordinal))
                    return (null, Result<HomeworkDto>(403, "Only the teacher who set this homework, or a School Admin, can change it.", null));
            }
            return (entity, null);
        }

        /// <summary>The row to read: Active, and the caller is a School Admin, its owning teacher, or a student currently in its section.</summary>
        private async Task<(HomeworkRow? Row, ApiResponse<HomeworkDto>? Failure)> LoadVisibleAsync(string schoolId, HomeworkCaller caller, long id, CancellationToken ct)
        {
            var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
            if (activeId == null) return (null, ConfigError<HomeworkDto>());
            var row = await _repo.GetRowAsync(schoolId, id, ct);
            if (row == null || row.StatusId != activeId) return (null, Result<HomeworkDto>(404, "Homework not found.", null));   // a deleted one is hidden from everyone
            if (caller.IsSchoolAdmin) return (row, null);

            if (caller.IsTeacher)
            {
                var (loginId, _) = await _repo.GetUserAsync(schoolId, caller.UserId, ct);
                if (string.Equals(loginId, row.EmployeeId, StringComparison.Ordinal)) return (row, null);
            }
            if (caller.IsStudent)
            {
                var (sectionId, yearId) = await CurrentSectionAsync(schoolId, caller.UserId, activeId.Value, ct);
                if (sectionId == row.SectionId && yearId == row.AcademicYearId) return (row, null);
            }
            return (null, Result<HomeworkDto>(403, "You don't have access to this homework.", null));
        }

        /// <summary>A student's section in the school's current academic year (Active student, Active enrollment) and that year; nulls when there is none.</summary>
        private async Task<(long? SectionId, long? YearId)> CurrentSectionAsync(string schoolId, long userId, int activeId, CancellationToken ct)
        {
            var (loginId, _) = await _repo.GetUserAsync(schoolId, userId, ct);
            if (string.IsNullOrEmpty(loginId)) return (null, null);
            var year = await _repo.GetYearContainingAsync(schoolId, SchoolClock.Today(), ct);
            if (year == null) return (null, null);
            return (await _repo.GetStudentSectionAsync(schoolId, loginId, year.AcademicYearId, activeId, ct), year.AcademicYearId);
        }

        // ---------------------------------------------------------------- helpers
        private static string? ValidateText(string? rawTitle, string? rawDescription, out string? title, out string? description)
        {
            title = rawTitle?.Trim();
            description = string.IsNullOrWhiteSpace(rawDescription) ? null : rawDescription.Trim();
            if (string.IsNullOrEmpty(title)) return "Title is required.";
            if (title.Length > 200) return "The title can be at most 200 characters.";
            if (description != null && description.Length > MaxDescription) return $"The description can be at most {MaxDescription} characters.";
            return null;
        }

        private static string? RangeError(DateOnly? from, DateOnly? to) => from != null && to != null && to < from ? "The 'to' date can't be before the 'from' date." : null;

        private static string AttachmentName(long id, string path) => $"homework-{id}{Path.GetExtension(path).ToLowerInvariant()}";

        private static List<HomeworkDto> Newest(List<HomeworkRow> rows) =>
            rows.OrderByDescending(r => r.AssignedDate).ThenByDescending(r => r.HomeworkId).Select(r => ToDto(r, "Active")).ToList();

        private static HomeworkDto ToDto(HomeworkRow r, string statusName) => new()
        {
            HomeworkId = r.HomeworkId, AcademicYearId = r.AcademicYearId, AcademicYearName = r.AcademicYearName, ClassId = r.ClassId, ClassName = r.ClassName, SectionId = r.SectionId, SectionName = r.SectionName,
            SubjectId = r.SubjectId, SubjectName = r.SubjectName, EmployeeId = r.EmployeeId, TeacherName = string.Join(' ', new[] { r.FirstName, r.MiddleName, r.LastName }.Where(p => !string.IsNullOrWhiteSpace(p))),
            Title = r.Title, Description = r.Description, AssignedDate = r.AssignedDate, DueDate = r.DueDate,
            HasAttachment = r.AttachmentPath != null, AttachmentFileName = r.AttachmentPath == null ? null : AttachmentName(r.HomeworkId, r.AttachmentPath), StatusName = statusName,
        };

        private async Task<ApiResponse<HomeworkDto>> ReplyAsync(string schoolId, long id, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetRowAsync(schoolId, id, ct);
            return row == null ? Result<HomeworkDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToDto(row, "Active"));
        }

        private ApiResponse<T> ConfigError<T>()
        {
            _logger.LogError("Configuration problem: a general status is missing in lut_status");
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
