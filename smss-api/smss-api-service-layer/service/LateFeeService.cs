using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// Late fee, settings and preview (5a). The rule is one row per school (off until the School Admin turns it on) plus a flag on each fee type; the preview works out, with the same arithmetic the payment will use
    /// (<see cref="LateFeeMath"/>), what a payment of this size would be charged today. Nothing here charges anything or changes a due.
    /// </summary>
    public class LateFeeService : ILateFeeService
    {
        private readonly ILateFeeRepository _repo;
        private readonly ILogger<LateFeeService> _logger;
        public LateFeeService(ILateFeeRepository repo, ILogger<LateFeeService> logger) { _repo = repo; _logger = logger; }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) where T : class => new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };

        private async Task<LateFeeRuleDto> BuildAsync(string schoolId, CancellationToken ct)
        {
            var r = await _repo.GetRuleAsync(schoolId, ct);
            return new LateFeeRuleDto
            {
                Enabled = r?.IsEnabled ?? false, RuleType = r?.RuleType, Amount = r?.Amount, Percent = r?.Percent, GraceDays = r?.GraceDays ?? 0, CapPerDue = r?.CapPerDue, TaxApplies = r?.TaxApplies ?? false,
                UpdatedAt = r?.UpdatedAt, UpdatedBy = r?.UpdatedByName,
                FeeTypes = (await _repo.ListFeeTypesAsync(schoolId, ct)).Select(f => new LateFeeTypeDto { FeeTypeId = f.FeeTypeId, Name = f.Name, IsActive = f.IsActive, AppliesLateFee = f.AppliesLateFee }).ToList(),
            };
        }

        public async Task<ApiResponse<LateFeeRuleDto>> GetRuleAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<LateFeeRuleDto>(400, TenantMessages.NoSchool, null);
            try { return Result(200, "Successfully fetched", await BuildAsync(schoolId, ct)); }
            catch (Exception ex) { _logger.LogError(ex, "Late fee rule could not be read"); return Result<LateFeeRuleDto>(500, "The late fee settings could not be read. Please try again.", null); }
        }

        public async Task<ApiResponse<LateFeeRuleDto>> SaveRuleAsync(string? schoolId, long userId, SaveLateFeeRuleRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<LateFeeRuleDto>(400, TenantMessages.NoSchool, null);
            if (request == null) return Result<LateFeeRuleDto>(400, "The request is empty.", null);
            var type = string.IsNullOrWhiteSpace(request.RuleType) ? null : request.RuleType.Trim().ToUpperInvariant();
            if (type != null && !LateFeeRuleTypes.All.Contains(type)) return Result<LateFeeRuleDto>(400, "The rule type must be FLAT, PER_DAY or PERCENT.", null);
            if (request.GraceDays is < 0 or > 365) return Result<LateFeeRuleDto>(400, "The grace days must be between 0 and 365.", null);
            if (request.CapPerDue is <= 0) return Result<LateFeeRuleDto>(400, "The cap per due must be above 0 (leave it empty for no cap).", null);
            if (request.Amount is <= 0) return Result<LateFeeRuleDto>(400, "The late fee amount must be above 0.", null);
            if (request.Percent is <= 0 or > 100) return Result<LateFeeRuleDto>(400, "The late fee percentage must be above 0 and at most 100.", null);
            if (request.Enabled)
            {
                if (type == null) return Result<LateFeeRuleDto>(400, "Choose how the late fee is worked out (a flat amount, an amount per day, or a percentage).", null);
                if (type is LateFeeRuleTypes.Flat or LateFeeRuleTypes.PerDay && (request.Amount ?? 0) <= 0) return Result<LateFeeRuleDto>(400, type == LateFeeRuleTypes.Flat ? "Enter the flat late fee amount." : "Enter the late fee amount per day.", null);
                if (type == LateFeeRuleTypes.Percent && (request.Percent ?? 0) <= 0) return Result<LateFeeRuleDto>(400, "Enter the late fee percentage (above 0 and up to 100).", null);
            }
            var ids = (request.FeeTypeIds ?? new()).Distinct().ToList();
            try
            {
                var own = (await _repo.ListFeeTypesAsync(schoolId, ct)).Select(f => f.FeeTypeId).ToHashSet();
                if (ids.Any(i => !own.Contains(i))) return Result<LateFeeRuleDto>(404, "A fee type was not found.", null);   // another school's fee type is a 404, never confirmed
                // only the figure that belongs to the chosen type is kept
                await _repo.SaveRuleAsync(schoolId, userId, request.Enabled, type, type is LateFeeRuleTypes.Flat or LateFeeRuleTypes.PerDay ? request.Amount : null, type == LateFeeRuleTypes.Percent ? request.Percent : null,
                    request.GraceDays, request.CapPerDue, request.TaxApplies, ct);
                await _repo.SetFlagsAsync(schoolId, ids, ct);
                return Result(200, "The late fee settings were saved. They apply from the next payment.", await BuildAsync(schoolId, ct));
            }
            catch (Exception ex) { _logger.LogError(ex, "Late fee rule could not be saved"); return Result<LateFeeRuleDto>(500, "The late fee settings could not be saved. Please try again.", null); }
        }

        public async Task<ApiResponse<LateFeePreviewDto>> PreviewAsync(string? schoolId, string studentId, LateFeePreviewRequest request, CancellationToken ct, DateOnly? today = null)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<LateFeePreviewDto>(400, TenantMessages.NoSchool, null);
            if (request == null) return Result<LateFeePreviewDto>(400, "The request is empty.", null);
            try
            {
                if (!await _repo.StudentExistsAsync(schoolId, studentId, ct)) return Result<LateFeePreviewDto>(404, "Student not found.", null);
                var asOf = today ?? SchoolClock.Today();
                var dto = new LateFeePreviewDto { Today = asOf };
                var rule = await _repo.GetRuleAsync(schoolId, ct);
                if (rule == null || !rule.IsEnabled || rule.RuleType == null) { dto.Note = "The school does not charge a late fee."; return Result(200, "Successfully fetched", dto); }
                dto.RuleEnabled = true; dto.RuleType = rule.RuleType; dto.GraceDays = rule.GraceDays;
                var values = new LateFeeRuleValues(rule.RuleType, rule.Amount, rule.Percent, rule.GraceDays, rule.CapPerDue);

                // the plan: what this payment applies to which due (explicit allocations, or the oldest dues first)
                var plan = new List<(LateFeeDueRow Due, decimal Applied)>();
                if (request.Allocations is { Count: > 0 })
                {
                    var dues = (await _repo.GetDuesAsync(schoolId, studentId, request.Allocations.Select(a => a.FeeDueId).Distinct().ToList(), ct)).ToDictionary(d => d.FeeDueId);
                    foreach (var a in request.Allocations)
                    {
                        if (!dues.TryGetValue(a.FeeDueId, out var d)) return Result<LateFeePreviewDto>(400, "One or more installments do not belong to this student.", null);
                        if (a.Amount > d.AmountDue - d.AmountPaid) return Result<LateFeePreviewDto>(400, $"The installment due {d.DueDate:dd MMM yyyy} has only {d.AmountDue - d.AmountPaid:0.00} outstanding, so {a.Amount:0.00} can't be applied to it.", null);
                        plan.Add((d, a.Amount));
                    }
                }
                else if (request.Amount is > 0)
                {
                    var left = request.Amount.Value;
                    foreach (var d in await _repo.ListOutstandingDuesAsync(schoolId, studentId, ct))
                    {
                        if (left <= 0) break;
                        var take = Math.Min(left, d.AmountDue - d.AmountPaid);
                        plan.Add((d, take)); left -= take;
                    }
                    if (left > 0) return Result<LateFeePreviewDto>(400, "The amount is more than the student has outstanding.", null);
                }
                else return Result<LateFeePreviewDto>(400, "Give an amount or the installments being paid.", null);

                var flagged = plan.Where(p => p.Due.AppliesLateFee).ToList();
                var collected = (await _repo.GetCollectedAsync(schoolId, flagged.Select(p => p.Due.FeeDueId).ToList(), ct)).ToDictionary(c => c.FeeDueId, c => c.Amount);
                foreach (var (due, applied) in flagged)
                {
                    var c = LateFeeMath.Charge(values, due.DueDate, asOf, applied, collected.GetValueOrDefault(due.FeeDueId));
                    if (c == null) continue;
                    dto.Lines.Add(new LateFeePreviewLineDto { FeeDueId = due.FeeDueId, FeeType = due.FeeTypeName, DueDate = due.DueDate, DaysLate = c.DaysLate, DaysCharged = c.DaysCharged, BaseAmount = c.BaseAmount, ComputedTotal = c.ComputedTotal, AlreadyCollected = c.AlreadyCollected, Amount = c.Amount });
                }
                dto.Total = dto.Lines.Sum(l => l.Amount);
                if (dto.Total > 0 && rule.TaxApplies && request.TaxTypeId is long taxId)
                {
                    var tax = await _repo.GetTaxTypeAsync(schoolId, taxId, ct);
                    if (tax == null) return Result<LateFeePreviewDto>(404, "Tax type not found.", null);
                    if (tax.IsActive) { dto.TaxName = tax.Name; dto.TaxAmount = TaxMath.TaxOn(dto.Total, tax.RatePercent); }
                }
                dto.Note = dto.Lines.Count == 0 ? "No late fee applies to this payment." : null;
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { _logger.LogError(ex, "Late fee preview failed for {StudentId}", studentId); return Result<LateFeePreviewDto>(500, "The late fee could not be worked out. Please try again.", null); }
        }
    }
}
