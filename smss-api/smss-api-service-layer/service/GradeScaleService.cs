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
    public interface IGradeScaleService
    {
        Task<ApiResponse<List<GradeScaleListDto>>> ListAsync(string? schoolId, bool includeInactive, CancellationToken ct);
        Task<ApiResponse<GradeScaleDto>> GetAsync(string? schoolId, long id, CancellationToken ct);
        Task<ApiResponse<GradeScaleDto>> CreateAsync(string? schoolId, SaveGradeScaleRequest req, CancellationToken ct);
        Task<ApiResponse<GradeScaleDto>> UpdateAsync(string? schoolId, long id, SaveGradeScaleRequest req, CancellationToken ct);
        /// <summary>Copies a preset into the school's own scale (an editable copy).</summary>
        Task<ApiResponse<GradeScaleDto>> FromPresetAsync(string? schoolId, GradeScaleFromPresetRequest req, CancellationToken ct);
        /// <summary>Soft delete (Inactive). 409 while an active scheme or activity area uses the scale.</summary>
        Task<ApiResponse<GradeScaleDto>> DeleteAsync(string? schoolId, long id, CancellationToken ct);
        ApiResponse<List<ScalePresetDto>> Presets();
    }

    public class GradeScaleService : IGradeScaleService
    {
        private const string NameIndex = "uq_tb_grade_scales_school_name";
        private readonly IReportSetupRepository _repo;
        private readonly ILogger<GradeScaleService> _logger;

        public GradeScaleService(IReportSetupRepository repo, ILogger<GradeScaleService> logger) { _repo = repo; _logger = logger; }

        public async Task<ApiResponse<List<GradeScaleListDto>>> ListAsync(string? schoolId, bool includeInactive, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<GradeScaleListDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (active == null) return ConfigError<List<GradeScaleListDto>>();
                var rows = await _repo.ListScalesAsync(schoolId, includeInactive, active.Value, ct);
                return Result(200, "Successfully fetched", rows.Select(r => ToList(r)).ToList());
            }
            catch (Exception ex) { return Error<List<GradeScaleListDto>>(ex, "listing grade scales"); }
        }

        public async Task<ApiResponse<GradeScaleDto>> GetAsync(string? schoolId, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<GradeScaleDto>(400, TenantMessages.NoSchool, null);
            try { return await DetailAsync(schoolId, id, 200, "Successfully fetched", ct); }
            catch (Exception ex) { return Error<GradeScaleDto>(ex, "fetching a grade scale"); }
        }

        public async Task<ApiResponse<GradeScaleDto>> CreateAsync(string? schoolId, SaveGradeScaleRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<GradeScaleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (active == null) return ConfigError<GradeScaleDto>();
                var error = GradeScaleRules.Validate(req.Kind, req.Bands, out var bands);
                if (error != null) return Result<GradeScaleDto>(400, error, null);
                var name = req.Name!.Trim();
                if (await _repo.ScaleNameTakenAsync(schoolId, name, null, active.Value, ct)) return Result<GradeScaleDto>(409, $"A grade scale named \"{name}\" already exists.", null);
                return await InsertAsync(schoolId, name, req.Kind!, null, bands, active.Value, ct);
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == NameIndex) { return Result<GradeScaleDto>(409, "A grade scale with that name already exists.", null); }
            catch (Exception ex) { return Error<GradeScaleDto>(ex, "creating a grade scale"); }
        }

        public async Task<ApiResponse<GradeScaleDto>> FromPresetAsync(string? schoolId, GradeScaleFromPresetRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<GradeScaleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var preset = ReportPresets.FindScale(req.PresetCode);
                if (preset == null) return Result<GradeScaleDto>(400, "That preset does not exist.", null);
                var active = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (active == null) return ConfigError<GradeScaleDto>();
                var name = string.IsNullOrWhiteSpace(req.Name) ? preset.Name : req.Name.Trim();
                if (await _repo.ScaleNameTakenAsync(schoolId, name, null, active.Value, ct)) return Result<GradeScaleDto>(409, $"A grade scale named \"{name}\" already exists. Give the copy another name.", null);
                return await InsertAsync(schoolId, name, preset.Kind, preset.Code, ToClean(preset), active.Value, ct);
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == NameIndex) { return Result<GradeScaleDto>(409, "A grade scale with that name already exists.", null); }
            catch (Exception ex) { return Error<GradeScaleDto>(ex, "copying a grade scale preset"); }
        }

        public async Task<ApiResponse<GradeScaleDto>> UpdateAsync(string? schoolId, long id, SaveGradeScaleRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<GradeScaleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (active == null) return ConfigError<GradeScaleDto>();
                var scale = await _repo.GetScaleAsync(schoolId, id, true, ct);
                if (scale == null) return Result<GradeScaleDto>(404, "Grade scale not found.", null);
                var error = GradeScaleRules.Validate(req.Kind, req.Bands, out var bands);
                if (error != null) return Result<GradeScaleDto>(400, error, null);
                var name = req.Name!.Trim();
                if (await _repo.ScaleNameTakenAsync(schoolId, name, id, active.Value, ct)) return Result<GradeScaleDto>(409, $"A grade scale named \"{name}\" already exists.", null);
                var (schemes, areas) = await _repo.ScaleUsageAsync(schoolId, id, active.Value, ct);
                if (req.Kind != scale.ScaleKind && (schemes > 0 || areas > 0))
                    return Result<GradeScaleDto>(409, "This scale is in use, so it can't change between percentage bands and ratings. Create a new scale instead.", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                _repo.RemoveRange((await _repo.GetBandsAsync(schoolId, id, true, ct)).Cast<object>());
                await _repo.SaveChangesAsync(ct);                       // the old bands go first: the new ones reuse their sequence numbers
                scale.ScaleName = name; scale.ScaleKind = req.Kind!; scale.UpdatedAt = DateTime.UtcNow;
                _repo.AddRange(BandRows(schoolId, id, bands));
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return await DetailAsync(schoolId, id, 200, "Grade scale saved.", ct);
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == NameIndex) { return Result<GradeScaleDto>(409, "A grade scale with that name already exists.", null); }
            catch (Exception ex) { return Error<GradeScaleDto>(ex, "updating a grade scale"); }
        }

        public async Task<ApiResponse<GradeScaleDto>> DeleteAsync(string? schoolId, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<GradeScaleDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                var inactive = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (active == null || inactive == null) return ConfigError<GradeScaleDto>();
                var scale = await _repo.GetScaleAsync(schoolId, id, true, ct);
                if (scale == null) return Result<GradeScaleDto>(404, "Grade scale not found.", null);
                var (schemes, areas) = await _repo.ScaleUsageAsync(schoolId, id, active.Value, ct);
                if (schemes > 0 || areas > 0) return Result<GradeScaleDto>(409, $"This scale is used by {schemes} report scheme(s) and {areas} activity area(s). Change those first.", null);
                scale.StatusId = inactive.Value; scale.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                return await DetailAsync(schoolId, id, 200, "Grade scale removed.", ct);
            }
            catch (Exception ex) { return Error<GradeScaleDto>(ex, "removing a grade scale"); }
        }

        public ApiResponse<List<ScalePresetDto>> Presets() => Result(200, "Successfully fetched", ReportPresets.Scales.Select(p => new ScalePresetDto
        {
            Code = p.Code, Name = p.Name, Kind = p.Kind, Note = p.Note, Bands = ToBandDtos(p.Kind, ToClean(p))
        }).ToList())!;

        // ------------------------------------------------------------------ helpers
        private async Task<ApiResponse<GradeScaleDto>> InsertAsync(string schoolId, string name, string kind, string? presetCode, List<GradeScaleRules.Clean> bands, int activeId, CancellationToken ct)
        {
            await using var tx = await _repo.BeginTransactionAsync(ct);
            var now = DateTime.UtcNow;
            var scale = new TbGradeScale { SchoolId = schoolId, ScaleName = name, ScaleKind = kind, SourcePresetCode = presetCode, StatusId = activeId, CreatedAt = now, UpdatedAt = now };
            _repo.Add(scale);
            await _repo.SaveChangesAsync(ct);
            _repo.AddRange(BandRows(schoolId, scale.GradeScaleId, bands));
            await _repo.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await DetailAsync(schoolId, scale.GradeScaleId, 201, "Grade scale created.", ct);
        }

        private static IEnumerable<object> BandRows(string schoolId, long scaleId, List<GradeScaleRules.Clean> bands) =>
            bands.Select((b, i) => (object)new TbGradeScaleBand
            {
                GradeScaleId = scaleId, SchoolId = schoolId, Sequence = (short)(i + 1), GradeCode = b.Code, Label = b.Label, MinPercent = b.Min, GradePoint = b.Point, IsFail = b.IsFail, Description = b.Description
            });

        private static List<GradeScaleRules.Clean> ToClean(ReportPresets.ScalePreset p) =>
            p.Bands.Select(b => new GradeScaleRules.Clean(b.Code, b.Label, b.Min, null, b.IsFail, b.Description)).OrderBy(b => b.Min).ToList();

        /// <summary>Percent scales are shown highest first with their derived upper edge; ratings in their own order.</summary>
        private static List<GradeBandDto> ToBandDtos(string kind, List<GradeScaleRules.Clean> lowestFirst)
        {
            var dtos = new List<GradeBandDto>();
            for (var i = 0; i < lowestFirst.Count; i++)
            {
                var b = lowestFirst[i];
                dtos.Add(new GradeBandDto
                {
                    Sequence = i + 1, GradeCode = b.Code, Label = b.Label, MinPercent = b.Min, GradePoint = b.Point, IsFail = b.IsFail, Description = b.Description,
                    UpToPercent = kind == ReportSetup.PercentBands ? (i + 1 < lowestFirst.Count ? lowestFirst[i + 1].Min : 100m) : null
                });
            }
            if (kind == ReportSetup.PercentBands) dtos.Reverse();
            return dtos;
        }

        private async Task<ApiResponse<GradeScaleDto>> DetailAsync(string schoolId, long id, int code, string message, CancellationToken ct)
        {
            var active = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
            if (active == null) return ConfigError<GradeScaleDto>();
            var row = (await _repo.ListScalesAsync(schoolId, true, active.Value, ct)).FirstOrDefault(r => r.Scale.GradeScaleId == id);
            if (row == null) return Result<GradeScaleDto>(404, "Grade scale not found.", null);
            var bands = await _repo.GetBandsAsync(schoolId, id, false, ct);
            var dto = new GradeScaleDto
            {
                GradeScaleId = row.Scale.GradeScaleId, ScaleName = row.Scale.ScaleName, Kind = row.Scale.ScaleKind, SourcePresetCode = row.Scale.SourcePresetCode,
                BandCount = row.BandCount, SchemeCount = row.SchemeCount, AreaCount = row.AreaCount, StatusName = row.StatusName,
                Bands = ToBandDtos(row.Scale.ScaleKind, bands.Select(b => new GradeScaleRules.Clean(b.GradeCode, b.Label, b.MinPercent, b.GradePoint, b.IsFail, b.Description)).ToList())
            };
            return Result(code, message, dto);
        }

        private static GradeScaleListDto ToList(ScaleListRow r) => new()
        {
            GradeScaleId = r.Scale.GradeScaleId, ScaleName = r.Scale.ScaleName, Kind = r.Scale.ScaleKind, SourcePresetCode = r.Scale.SourcePresetCode,
            BandCount = r.BandCount, SchemeCount = r.SchemeCount, AreaCount = r.AreaCount, StatusName = r.StatusName
        };

        private ApiResponse<T> ConfigError<T>() { _logger.LogError("Configuration problem: Active / Inactive statuses are missing in lut_status"); return Result<T>(500, "The system is not configured correctly. Please contact support.", default); }
        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
        private ApiResponse<T> Error<T>(Exception ex, string action) { _logger.LogError(ex, "Error while {Action}", action); return Result<T>(500, "Something went wrong. Please try again later.", default); }
    }
}
