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
    public class SubscriptionPricingService : ISubscriptionPricingService
    {
        private readonly ISubscriptionRepository _repo;
        private readonly ILogger<SubscriptionPricingService> _logger;

        public SubscriptionPricingService(ISubscriptionRepository repo, ILogger<SubscriptionPricingService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<SubscriptionPricingViewDto>> GetAsync(CancellationToken ct)
        {
            try
            {
                var history = await _repo.GetPricingHistoryAsync(ct);
                var dtos = history.Select(ToDto).ToList();
                return Result(200, "Successfully fetched", new SubscriptionPricingViewDto { Current = dtos.FirstOrDefault(d => d.IsActive), History = dtos });
            }
            catch (Exception ex) { return Error<SubscriptionPricingViewDto>(ex, "fetching subscription pricing"); }
        }

        public async Task<ApiResponse<SubscriptionPricingDto>> UpdateAsync(UpdateSubscriptionPricingRequest req, CancellationToken ct)
        {
            try
            {
                var next = new TbSubscriptionPricing
                {
                    RatePerStudentMonthly = req.RatePerStudentMonthly!.Value,
                    YearlyDiscountPercent = req.YearlyDiscountPercent!.Value,
                    TrialDays = req.TrialDays!.Value,
                    EffectiveFrom = SchoolClock.Today(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await _repo.ReplaceActivePricingAsync(next, ct);
                return Result(200, "Pricing updated. It applies to schools registered and invoices generated from now on.", ToDto(next));
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Someone else saved a new version at the same moment; nothing of ours was kept
                return Result<SubscriptionPricingDto>(409, "The pricing was changed by someone else at the same time. Reload and try again.", null);
            }
            catch (Exception ex) { return Error<SubscriptionPricingDto>(ex, "updating subscription pricing"); }
        }

        private static SubscriptionPricingDto ToDto(TbSubscriptionPricing p) => new()
        {
            PricingId = p.PricingId, RatePerStudentMonthly = p.RatePerStudentMonthly, YearlyDiscountPercent = p.YearlyDiscountPercent,
            TrialDays = p.TrialDays, EffectiveFrom = p.EffectiveFrom, IsActive = p.IsActive, CreatedAt = p.CreatedAt
        };

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
