using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // schoolId always comes from the caller's token; a student, tax type or fee type of another school answers 404.
    public interface ILateFeeService
    {
        Task<ApiResponse<LateFeeRuleDto>> GetRuleAsync(string? schoolId, CancellationToken ct);
        /// <summary>School Admin: saves the rule and which fee types it applies to (new rule applies from the next payment only).</summary>
        Task<ApiResponse<LateFeeRuleDto>> SaveRuleAsync(string? schoolId, long userId, SaveLateFeeRuleRequest request, CancellationToken ct);
    }
}
