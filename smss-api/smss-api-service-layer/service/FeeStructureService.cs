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
    public class FeeStructureService : IFeeStructureService
    {
        private const string DuplicateMessage = "A fee structure already exists for this class and academic year.";

        private readonly IFeeStructureRepository _repo;
        private readonly ILogger<FeeStructureService> _logger;

        public FeeStructureService(IFeeStructureRepository repo, ILogger<FeeStructureService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<object>> GetAllAsync(string? schoolId, long? academicYearId, long? classId)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var rows = await _repo.GetListAsync(schoolId, academicYearId, classId);
                return Ok(200, "Successfully fetched", rows.Select(r => ToSummary(new FeeStructureSummaryResponse(), r.Structure, r.AcademicYearName, r.ClassName, r.LineCount)).ToList());
            }
            catch (Exception ex) { return Error(ex, "fetching fee structures"); }
        }

        // Header + lines in one response. 404 for another school's structure too.
        public async Task<ApiResponse<object>> GetByIdAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var header = await _repo.GetByIdAsync(schoolId, id);
                if (header == null) return Fail(404, "Fee structure not found");
                return Ok(200, "Successfully fetched", await ToDetailAsync(header));
            }
            catch (Exception ex) { return Error(ex, "fetching fee structure"); }
        }

        public async Task<ApiResponse<object>> GetFrequenciesAsync()
        {
            try
            {
                var items = await _repo.GetFrequenciesAsync();
                return Ok(200, "Successfully fetched", items.Select(f => new FeeFrequencyResponse
                {
                    FrequencyId = f.FrequencyId, FrequencyName = f.FrequencyName, FrequencyCode = f.FrequencyCode
                }).ToList());
            }
            catch (Exception ex) { return Error(ex, "fetching fee frequencies"); }
        }

        public async Task<ApiResponse<object>> CreateAsync(string? schoolId, CreateFeeStructureRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                // Ids come from the client: every school-owned one must belong to THIS school
                if (await _repo.GetAcademicYearNameAsync(schoolId, req.AcademicYearId!.Value) == null)
                    return Fail(400, "The selected academic year was not found for your school.");
                if (await _repo.GetClassNameAsync(schoolId, req.ClassId!.Value) == null)
                    return Fail(400, "The selected class was not found for your school.");
                if (await _repo.StructureExistsAsync(schoolId, req.AcademicYearId.Value, req.ClassId.Value))
                    return Fail(409, DuplicateMessage);

                var error = await ValidateLinesAsync(schoolId, req.Details, existingFeeTypeIds: null);
                if (error != null) return Fail(400, error);

                var now = DateTime.UtcNow;
                var entity = new TbFeeStructures
                {
                    SchoolId = schoolId,
                    AcademicYearId = req.AcademicYearId.Value,
                    ClassId = req.ClassId.Value,
                    StructureName = req.StructureName.Trim(),
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                foreach (var l in req.Details) entity.Details.Add(NewLine(l, now));

                await _repo.AddAsync(entity);
                await _repo.SaveChangesAsync();   // header + lines in one transaction

                return Ok(201, "Fee structure created successfully", await ToDetailAsync(entity));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, DuplicateMessage); }
            catch (Exception ex) { return Error(ex, "creating fee structure"); }
        }

        public async Task<ApiResponse<object>> UpdateAsync(string? schoolId, long id, UpdateFeeStructureRequest req)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetWithDetailsForUpdateAsync(schoolId, id);
                if (entity == null) return Fail(404, "Fee structure not found");

                // A line already on the structure may keep an inactive fee type; only NEW lines need an active one
                var existingIds = entity.Details.Select(d => d.FeeTypeId).ToHashSet();
                var error = await ValidateLinesAsync(schoolId, req.Details, existingIds);
                if (error != null) return Fail(400, error);

                var now = DateTime.UtcNow;
                var wanted = req.Details.ToDictionary(l => l.FeeTypeId!.Value);

                // Lines are matched on fee type (unique per structure): update in place, add new ones, remove dropped ones
                _repo.RemoveDetails(entity.Details.Where(d => !wanted.ContainsKey(d.FeeTypeId)).ToList());
                foreach (var d in entity.Details.Where(d => wanted.ContainsKey(d.FeeTypeId)))
                {
                    var l = wanted[d.FeeTypeId];
                    d.FrequencyId = l.FrequencyId!.Value;
                    d.Amount = l.Amount!.Value;
                    d.UpdatedAt = now;
                }
                foreach (var l in req.Details.Where(l => !existingIds.Contains(l.FeeTypeId!.Value))) entity.Details.Add(NewLine(l, now));

                entity.StructureName = req.StructureName.Trim();
                entity.UpdatedAt = now;
                await _repo.SaveChangesAsync();

                return Ok(200, "Fee structure updated successfully", await ToDetailAsync(entity));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Fail(409, "A fee type appears twice on this structure."); }
            catch (Exception ex) { return Error(ex, "updating fee structure"); }
        }

        public async Task<ApiResponse<object>> ToggleStatusAsync(string? schoolId, long id)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail(400, TenantMessages.NoSchool);
            try
            {
                var entity = await _repo.GetByIdAsync(schoolId, id, track: true);
                if (entity == null) return Fail(404, "Fee structure not found");

                entity.IsActive = !entity.IsActive;
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync();

                return Ok(200, entity.IsActive ? "Fee structure activated successfully" : "Fee structure deactivated successfully", await ToDetailAsync(entity));
            }
            catch (Exception ex) { return Error(ex, "toggling fee structure status"); }
        }

        // ---------------- helpers ----------------
        /// <summary>Null when the lines are acceptable, otherwise the message to return with a 400.</summary>
        private async Task<string?> ValidateLinesAsync(string schoolId, List<FeeStructureLineRequest> lines, HashSet<long>? existingFeeTypeIds)
        {
            var typeIds = lines.Select(l => l.FeeTypeId!.Value).ToList();
            if (typeIds.Distinct().Count() != typeIds.Count)
                return "A fee type can appear only once on a fee structure.";

            var flags = await _repo.GetFeeTypeActiveFlagsAsync(schoolId, typeIds);
            foreach (var id in typeIds)
            {
                if (!flags.TryGetValue(id, out var active)) return "One or more fee types were not found for your school.";
                var isNewLine = existingFeeTypeIds == null || !existingFeeTypeIds.Contains(id);
                if (isNewLine && !active) return "An inactive fee type cannot be added to a fee structure. Activate it first.";
            }

            var frequencyIds = lines.Select(l => l.FrequencyId!.Value).Distinct().ToList();
            var valid = await _repo.GetActiveFrequencyIdsAsync(frequencyIds);
            if (frequencyIds.Any(f => !valid.Contains(f))) return "One or more fee frequencies are not valid.";

            return null;
        }

        private static TbFeeStructureDetails NewLine(FeeStructureLineRequest l, DateTime now) => new()
        {
            FeeTypeId = l.FeeTypeId!.Value,
            FrequencyId = l.FrequencyId!.Value,
            Amount = l.Amount!.Value,
            CreatedAt = now,
            UpdatedAt = now
        };

        private async Task<FeeStructureResponse> ToDetailAsync(TbFeeStructures s)
        {
            var year = await _repo.GetAcademicYearNameAsync(s.SchoolId, s.AcademicYearId) ?? string.Empty;
            var cls = await _repo.GetClassNameAsync(s.SchoolId, s.ClassId) ?? string.Empty;
            var lines = await _repo.GetLinesAsync(s.FeeStructureId);

            var dto = ToSummary(new FeeStructureResponse(), s, year, cls, lines.Count);
            dto.Details = lines.Select(l => new FeeStructureLineResponse
            {
                FeeStructureDetailId = l.FeeStructureDetailId, FeeTypeId = l.FeeTypeId, FeeTypeName = l.FeeTypeName, FeeTypeCode = l.FeeTypeCode,
                FrequencyId = l.FrequencyId, FrequencyName = l.FrequencyName, Amount = l.Amount
            }).ToList();
            return dto;
        }

        private static T ToSummary<T>(T dto, TbFeeStructures s, string yearName, string className, int lineCount) where T : FeeStructureSummaryResponse
        {
            dto.FeeStructureId = s.FeeStructureId;
            dto.SchoolId = s.SchoolId;
            dto.AcademicYearId = s.AcademicYearId;
            dto.AcademicYearName = yearName;
            dto.ClassId = s.ClassId;
            dto.ClassName = className;
            dto.StructureName = s.StructureName;
            dto.IsActive = s.IsActive;
            dto.StatusName = s.IsActive ? StatusNames.Active : StatusNames.Inactive;
            dto.LineCount = lineCount;
            dto.CreatedAt = s.CreatedAt;
            dto.UpdatedAt = s.UpdatedAt;
            return dto;
        }

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
    }
}
