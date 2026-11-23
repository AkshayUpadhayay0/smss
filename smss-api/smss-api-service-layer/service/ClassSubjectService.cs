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
    // A mapping is a pure join row: it is either present or absent. Removing one really deletes it
    // (status_id is just set to Active on insert, for consistency with the other tables, and never toggled).
    public class ClassSubjectService : IClassSubjectService
    {
        private const string InvalidClassMessage = "The selected class was not found for your school.";
        private const string InvalidSubjectMessage = "One or more selected subjects were not found for your school.";
        private const string DuplicateMessage = "This subject is already mapped to this class.";

        private readonly IClassSubjectRepository _repo;
        private readonly ISubjectSetupService _setup;
        private readonly ILogger<ClassSubjectService> _logger;

        public ClassSubjectService(IClassSubjectRepository repo, ISubjectSetupService setup, ILogger<ClassSubjectService> logger)
        {
            _repo = repo;
            _setup = setup;
            _logger = logger;
        }

        public async Task<ApiResponse<object>> GetByClassAsync(string? schoolId, long? classId, long? academicYearId)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            if (classId == null || classId <= 0) return Fail(400, "classId is required.");
            try
            {
                if (await _repo.GetClassAsync(schoolId, classId.Value) == null) return Fail(400, InvalidClassMessage);
                var (year, error) = await ResolveYearAsync(schoolId, academicYearId, ct: default);
                if (year == null) return error!;
                var rows = await _repo.GetByClassAsync(schoolId, classId.Value, year.AcademicYearId);
                return Ok(200, "Successfully fetched", rows.Select(ToResponse).ToList());
            }
            catch (Exception ex) { return Error(ex, "fetching class subjects"); }
        }

        // 404 for another school's row too, so ids from other schools can't be probed
        public async Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var row = await _repo.GetWithNamesByIdAsync(schoolId, id);
                return row == null ? Fail(404, "Class-subject mapping not found") : Ok(200, "Successfully fetched", ToResponse(row));
            }
            catch (Exception ex) { return Error(ex, "fetching class subject"); }
        }

        public async Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateClassSubjectRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var classId = req.ClassId!.Value;
                var subjectId = req.SubjectId!.Value;
                // The DB would accept another school's ids, so these checks are what keep tenants apart
                if (await _repo.GetClassAsync(schoolId, classId) == null) return Fail(400, InvalidClassMessage);
                if (await _repo.CountSubjectsAsync(schoolId, new[] { subjectId }) != 1) return Fail(400, InvalidSubjectMessage);
                var (year, error) = await ResolveYearAsync(schoolId, req.AcademicYearId, ct: default);
                if (year == null) return error!;
                if (year.ClosedAt != null) return Fail(409, ClosedYear.Message);
                if (await _repo.ExistsAsync(year.AcademicYearId, classId, subjectId)) return Fail(409, DuplicateMessage);

                // the same save as Academic Setup (offering check, lock, transaction): the class's current subjects plus this one
                var current = (await _repo.GetByClassAsync(schoolId, classId, year.AcademicYearId)).Select(r => r.Mapping.SubjectId).ToList();
                var saved = await _setup.SaveAsync(schoolId, null, year.AcademicYearId, classId, KeepingOverrides(await _setup.GetAsync(schoolId, year.AcademicYearId, classId, default), current.Append(subjectId).ToList()), default);
                if (!saved.Status) return Fail(saved.StatusCode, saved.Message);

                var entity = (await _repo.GetByClassAsync(schoolId, classId, year.AcademicYearId)).FirstOrDefault(r => r.Mapping.SubjectId == subjectId);
                return entity == null ? Fail(404, "Class-subject mapping not found") : Ok(201, "Subject mapped to class successfully", ToResponse(entity));
            }
            catch (Exception ex) { return Error(ex, "mapping subject to class"); }
        }

        public async Task<ApiResponse<object>> DeleteAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id);
                if (entity == null) return Fail(404, "Class-subject mapping not found");

                var year = await _repo.GetYearAsync(schoolId, entity.AcademicYearId, default);
                if (year == null) return Fail(404, "Class-subject mapping not found");
                if (year.ClosedAt != null) return Fail(409, ClosedYear.Message);
                var current = (await _repo.GetByClassAsync(schoolId, entity.ClassId, entity.AcademicYearId)).Select(r => r.Mapping.SubjectId).Where(x => x != entity.SubjectId).ToList();
                var saved = await _setup.SaveAsync(schoolId, null, entity.AcademicYearId, entity.ClassId, KeepingOverrides(await _setup.GetAsync(schoolId, entity.AcademicYearId, entity.ClassId, default), current), default);
                if (!saved.Status) return Fail(saved.StatusCode, saved.Message);
                return Ok(200, "Subject removed from class successfully", new { });
            }
            catch (Exception ex) { return Error(ex, "removing class subject"); }
        }

        // Replaces the class's whole subject set for the year (the section exceptions are kept where they still make sense).
        // Same rules as Academic Setup: closed year 409, the class must be offered, a subject that something uses can't be removed (409 with counts).
        public async Task<ApiResponse<object>> BulkSetAsync(string? schoolId, BulkSetClassSubjectsRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var classId = req.ClassId!.Value;
                if (await _repo.GetClassAsync(schoolId, classId) == null) return Fail(400, InvalidClassMessage);

                var wanted = req.SubjectIds!.Distinct().ToList();   // repeated ids are harmless: a pair is mapped once
                if (wanted.Any(id => id <= 0)) return Fail(400, InvalidSubjectMessage);
                if (wanted.Count > 0 && await _repo.CountSubjectsAsync(schoolId, wanted) != wanted.Count)
                    return Fail(400, InvalidSubjectMessage);

                var (year, error) = await ResolveYearAsync(schoolId, req.AcademicYearId, ct: default);
                if (year == null) return error!;
                var saved = await _setup.SaveAsync(schoolId, null, year.AcademicYearId, classId, KeepingOverrides(await _setup.GetAsync(schoolId, year.AcademicYearId, classId, default), wanted), default);
                if (!saved.Status) return Fail(saved.StatusCode, saved.Message);

                var rows = await _repo.GetByClassAsync(schoolId, classId, year.AcademicYearId);
                return Ok(200, "Class subjects saved successfully", rows.Select(ToResponse).ToList());
            }
            catch (Exception ex) { return Error(ex, "saving class subjects"); }
        }

        /// <summary>The year asked for, else the school's CURRENT one (Q9: callers without a year get the current year, with a log line).</summary>
        private async Task<(TbAcademicYears? Year, ApiResponse<object>? Error)> ResolveYearAsync(string schoolId, long? academicYearId, CancellationToken ct)
        {
            if (academicYearId != null)
            {
                var y = await _repo.GetYearAsync(schoolId, academicYearId.Value, ct);
                return y == null ? (null, Fail(400, "The selected academic year was not found for your school.")) : (y, null);
            }
            var current = await _repo.GetCurrentYearAsync(schoolId, ct);
            if (current == null) return (null, Fail(409, "The school has no current academic year, so choose an academic year."));
            _logger.LogInformation("Class-subject call without an academic year: defaulted to the current year {YearId} of school {SchoolId}", current.AcademicYearId, schoolId);
            return (current, null);
        }

        /// <summary>A save request with the new subject list and the year's current section exceptions, minus the ones the new list makes meaningless.</summary>
        private static SaveYearClassSubjectsRequest KeepingOverrides(ApiResponse<YearClassSubjectsDto> now, List<long> subjectIds)
        {
            var keep = subjectIds.ToHashSet();
            var sections = (now.Data?.Sections ?? new()).Select(s => new SectionSubjectOverrideRequest
            {
                SectionId = s.SectionId,
                ExtraSubjectIds = s.ExtraSubjectIds.Where(x => !keep.Contains(x)).ToList(),
                NotTaughtSubjectIds = s.NotTaughtSubjectIds.Where(x => keep.Contains(x)).ToList()
            }).Where(s => s.ExtraSubjectIds.Count + s.NotTaughtSubjectIds.Count > 0).ToList();
            return new SaveYearClassSubjectsRequest { SubjectIds = subjectIds.Distinct().ToList(), Sections = sections };
        }

        // ---------------- helpers ----------------
        private static ClassSubjectResponse ToResponse(ClassSubjectWithNames r) => new()
        {
            ClassSubjectId = r.Mapping.ClassSubjectId,
            SchoolId = r.Mapping.SchoolId,
            AcademicYearId = r.Mapping.AcademicYearId,
            ClassId = r.Mapping.ClassId,
            ClassName = r.ClassName,
            SubjectId = r.Mapping.SubjectId,
            SubjectName = r.SubjectName,
            SubjectCode = r.SubjectCode,
            StatusId = r.Mapping.StatusId,
            CreatedAt = r.Mapping.CreatedAt,
            UpdatedAt = r.Mapping.UpdatedAt
        };

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

        private static ApiResponse<object> Ok(int code, string message, object data) =>
            new() { Status = true, StatusCode = code, Message = message, Data = data };

        private static ApiResponse<object> Fail(int code, string message) =>
            new() { Status = false, StatusCode = code, Message = message, Data = null };

        private ApiResponse<object> Error(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Fail(500, "Something went wrong. Please try again later.");
        }

        private ApiResponse<object> ConfigError(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Fail(500, "The system is not configured correctly. Please contact support.");
        }
    }
}
