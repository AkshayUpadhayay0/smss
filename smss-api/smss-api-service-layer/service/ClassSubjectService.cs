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
        private readonly ILogger<ClassSubjectService> _logger;

        public ClassSubjectService(IClassSubjectRepository repo, ILogger<ClassSubjectService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<object>> GetByClassAsync(string? schoolId, long? classId)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            if (classId == null || classId <= 0) return Fail(400, "classId is required.");
            try
            {
                if (await _repo.GetClassAsync(schoolId, classId.Value) == null) return Fail(400, InvalidClassMessage);
                var rows = await _repo.GetByClassAsync(schoolId, classId.Value);
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
                if (await _repo.ExistsAsync(classId, subjectId)) return Fail(409, DuplicateMessage);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeId == null) return ConfigError("Active status missing from lut_status");

                var entity = NewMapping(schoolId, classId, subjectId, activeId.Value, DateTime.UtcNow);
                await _repo.AddAsync(entity);
                await _repo.SaveChangesAsync();

                var row = await _repo.GetWithNamesByIdAsync(schoolId, entity.ClassSubjectId);
                return row == null ? Fail(404, "Class-subject mapping not found") : Ok(201, "Subject mapped to class successfully", ToResponse(row));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "mapping subject to class"); }
        }

        public async Task<ApiResponse<object>> DeleteAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id);
                if (entity == null) return Fail(404, "Class-subject mapping not found");

                _repo.Remove(entity);
                await _repo.SaveChangesAsync();
                return Ok(200, "Subject removed from class successfully", new { });
            }
            catch (Exception ex) { return Error(ex, "removing class subject"); }
        }

        // Replaces the class's whole subject set: adds the missing pairs, deletes the ones no longer listed.
        // One SaveChanges = one transaction, so a failure leaves the class exactly as it was.
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

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeId == null) return ConfigError("Active status missing from lut_status");

                var existing = await _repo.GetTrackedByClassAsync(schoolId, classId);
                var now = DateTime.UtcNow;

                _repo.RemoveRange(existing.Where(m => !wanted.Contains(m.SubjectId)));
                var already = existing.Select(m => m.SubjectId).ToHashSet();
                foreach (var subjectId in wanted.Where(id => !already.Contains(id)))   // pairs already mapped are left alone
                    await _repo.AddAsync(NewMapping(schoolId, classId, subjectId, activeId.Value, now));

                await _repo.SaveChangesAsync();

                var rows = await _repo.GetByClassAsync(schoolId, classId);
                return Ok(200, "Class subjects saved successfully", rows.Select(ToResponse).ToList());
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                // Another request mapped the same pair at the same moment; nothing was saved here
                return Fail(409, "The class's subjects were changed by someone else. Please reload and try again.");
            }
            catch (Exception ex) { return Error(ex, "saving class subjects"); }
        }

        // ---------------- helpers ----------------
        private static TbClassSubjects NewMapping(string schoolId, long classId, long subjectId, int activeId, DateTime now) => new()
        {
            SchoolId = schoolId,
            ClassId = classId,
            SubjectId = subjectId,
            StatusId = activeId,
            CreatedAt = now,
            UpdatedAt = now
        };

        private static ClassSubjectResponse ToResponse(ClassSubjectWithNames r) => new()
        {
            ClassSubjectId = r.Mapping.ClassSubjectId,
            SchoolId = r.Mapping.SchoolId,
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
