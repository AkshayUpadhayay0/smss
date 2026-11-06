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
    /// <summary>Co-scholastic and discipline areas (school level) with the rating scale each is graded on.</summary>
    public class ReportActivityAreaService : IReportActivityAreaService
    {
        private const string NameIndex = "uq_tb_raa_name";
        private readonly IReportSetupRepository _repo;
        private readonly ILogger<ReportActivityAreaService> _logger;

        public ReportActivityAreaService(IReportSetupRepository repo, ILogger<ReportActivityAreaService> logger) { _repo = repo; _logger = logger; }

        public async Task<ApiResponse<List<ActivityAreaDto>>> ListAsync(string? schoolId, bool includeInactive, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<ActivityAreaDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<List<ActivityAreaDto>>();
                return Result(200, "Successfully fetched", (await _repo.ListAreasAsync(schoolId, includeInactive, active.Value, ct)).Select(ToDto).ToList());
            }
            catch (Exception ex) { return Error<List<ActivityAreaDto>>(ex, "listing activity areas"); }
        }

        public async Task<ApiResponse<ActivityAreaDto>> CreateAsync(string? schoolId, SaveActivityAreaRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ActivityAreaDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<ActivityAreaDto>();
                var bad = await ValidateAsync(schoolId, req, null, active.Value, ct);
                if (bad != null) return bad;
                var now = DateTime.UtcNow;
                var a = new TbReportActivityArea { SchoolId = schoolId, StatusId = active.Value, CreatedAt = now };
                Apply(a, req, now);
                _repo.Add(a);
                await _repo.SaveChangesAsync(ct);
                return await RowAsync(schoolId, a.AreaId, 201, "Activity area created.", ct);
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == NameIndex) { return Result<ActivityAreaDto>(409, "That activity area already exists.", null); }
            catch (Exception ex) { return Error<ActivityAreaDto>(ex, "creating an activity area"); }
        }

        public async Task<ApiResponse<ActivityAreaDto>> UpdateAsync(string? schoolId, long areaId, SaveActivityAreaRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ActivityAreaDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<ActivityAreaDto>();
                var a = await _repo.GetAreaAsync(schoolId, areaId, true, ct);
                if (a == null || a.StatusId != active.Value) return Result<ActivityAreaDto>(404, "Activity area not found.", null);
                var bad = await ValidateAsync(schoolId, req, areaId, active.Value, ct);
                if (bad != null) return bad;
                Apply(a, req, DateTime.UtcNow);
                await _repo.SaveChangesAsync(ct);
                return await RowAsync(schoolId, areaId, 200, "Activity area saved.", ct);
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == NameIndex) { return Result<ActivityAreaDto>(409, "That activity area already exists.", null); }
            catch (Exception ex) { return Error<ActivityAreaDto>(ex, "updating an activity area"); }
        }

        public async Task<ApiResponse<ActivityAreaDto>> DeleteAsync(string? schoolId, long areaId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ActivityAreaDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                var inactive = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (active == null || inactive == null) return ConfigError<ActivityAreaDto>();
                var a = await _repo.GetAreaAsync(schoolId, areaId, true, ct);
                if (a == null || a.StatusId != active.Value) return Result<ActivityAreaDto>(404, "Activity area not found.", null);
                a.StatusId = inactive.Value; a.UpdatedAt = DateTime.UtcNow;      // grades already given stay on published cards; the area just stops being offered
                await _repo.SaveChangesAsync(ct);
                return await RowAsync(schoolId, areaId, 200, "Activity area removed.", ct);
            }
            catch (Exception ex) { return Error<ActivityAreaDto>(ex, "removing an activity area"); }
        }

        public async Task<ApiResponse<StarterAreasResultDto>> AddStarterAreasAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StarterAreasResultDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await ActiveAsync(ct);
                if (active == null) return ConfigError<StarterAreasResultDto>();
                var now = DateTime.UtcNow;
                await using var tx = await _repo.BeginTransactionAsync(ct);
                var preset = ReportPresets.FindScale(ReportPresets.StarterScalePresetCode)!;
                var scale = await _repo.FindScaleByPresetAsync(schoolId, preset.Code, active.Value, ct);
                if (scale == null)
                {
                    var name = preset.Name;
                    if (await _repo.ScaleNameTakenAsync(schoolId, name, null, active.Value, ct)) name = $"{preset.Name} (copy)";
                    scale = new TbGradeScale { SchoolId = schoolId, ScaleName = name, ScaleKind = preset.Kind, SourcePresetCode = preset.Code, StatusId = active.Value, CreatedAt = now, UpdatedAt = now };
                    _repo.Add(scale);
                    await _repo.SaveChangesAsync(ct);
                    _repo.AddRange(preset.Bands.Select((b, i) => (object)new TbGradeScaleBand { GradeScaleId = scale.GradeScaleId, SchoolId = schoolId, Sequence = (short)(i + 1), GradeCode = b.Code, Label = b.Label, IsFail = b.IsFail }));
                    await _repo.SaveChangesAsync(ct);
                }
                var created = 0; var there = 0; short seq = 0;
                foreach (var s in ReportPresets.StarterAreas)
                {
                    seq++;
                    if (await _repo.AreaNameTakenAsync(schoolId, s.Category, s.Name, null, active.Value, ct)) { there++; continue; }
                    _repo.Add(new TbReportActivityArea { SchoolId = schoolId, Category = s.Category, AreaName = s.Name, Sequence = seq, RatingScaleId = scale.GradeScaleId, StatusId = active.Value, CreatedAt = now, UpdatedAt = now });
                    created++;
                }
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                var all = (await _repo.ListAreasAsync(schoolId, false, active.Value, ct)).Select(ToDto).ToList();
                return Result(created > 0 ? 201 : 200, created > 0 ? $"{created} starter area(s) added." : "The starter areas are already there.", new StarterAreasResultDto { Created = created, AlreadyThere = there, Areas = all });
            }
            catch (Exception ex) { return Error<StarterAreasResultDto>(ex, "adding the starter activity areas"); }
        }

        // ------------------------------------------------------------------ helpers
        private async Task<ApiResponse<ActivityAreaDto>?> ValidateAsync(string schoolId, SaveActivityAreaRequest req, long? exceptId, int activeId, CancellationToken ct)
        {
            if (!ReportSetup.Categories.Contains(req.Category ?? "")) return Result<ActivityAreaDto>(400, "Category must be CO_SCHOLASTIC, DISCIPLINE or OTHER.", null);
            var name = (req.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > 60) return Result<ActivityAreaDto>(400, "An area needs a name of up to 60 characters.", null);
            if (req.Sequence is < 0 or > 1000) return Result<ActivityAreaDto>(400, "The order number must be between 0 and 1000.", null);
            var scale = await _repo.GetScaleAsync(schoolId, req.RatingScaleId!.Value, false, ct);
            if (scale == null || scale.StatusId != activeId) return Result<ActivityAreaDto>(400, "The selected rating scale was not found for your school.", null);
            if (scale.ScaleKind != ReportSetup.Rating) return Result<ActivityAreaDto>(400, "An activity area is graded on a rating scale (A, B, C ...), not on percentage bands.", null);
            if (await _repo.AreaNameTakenAsync(schoolId, req.Category!, name, exceptId, activeId, ct)) return Result<ActivityAreaDto>(409, $"\"{name}\" already exists in that category.", null);
            return null;
        }

        private static void Apply(TbReportActivityArea a, SaveActivityAreaRequest r, DateTime now)
        {
            a.Category = r.Category!; a.AreaName = r.Name!.Trim(); a.Sequence = (short)r.Sequence; a.RatingScaleId = r.RatingScaleId!.Value; a.UpdatedAt = now;
        }

        private async Task<ApiResponse<ActivityAreaDto>> RowAsync(string schoolId, long areaId, int code, string message, CancellationToken ct)
        {
            var active = await ActiveAsync(ct);
            var row = (await _repo.ListAreasAsync(schoolId, true, active ?? 0, ct)).FirstOrDefault(r => r.Area.AreaId == areaId);
            return row == null ? Result<ActivityAreaDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToDto(row));
        }

        private static ActivityAreaDto ToDto(AreaListRow r) => new()
        {
            AreaId = r.Area.AreaId, Category = r.Area.Category, Name = r.Area.AreaName, Sequence = r.Area.Sequence, RatingScaleId = r.Area.RatingScaleId, RatingScaleName = r.ScaleName, StatusName = r.StatusName
        };

        private Task<int?> ActiveAsync(CancellationToken ct) => _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
        private ApiResponse<T> ConfigError<T>() { _logger.LogError("Configuration problem: Active / Inactive statuses are missing in lut_status"); return Result<T>(500, "The system is not configured correctly. Please contact support.", default); }
        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
        private ApiResponse<T> Error<T>(Exception ex, string action) { _logger.LogError(ex, "Error while {Action}", action); return Result<T>(500, "Something went wrong. Please try again later.", default); }
    }
}
