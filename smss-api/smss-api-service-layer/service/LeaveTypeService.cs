using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class LeaveTypeService : ILeaveTypeService
    {
        private readonly ILeaveRepository _repo;
        private readonly ILogger<LeaveTypeService> _logger;

        public LeaveTypeService(ILeaveRepository repo, ILogger<LeaveTypeService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<List<LeaveTypeDto>>> GetAsync(string? appliesTo, CancellationToken ct)
        {
            try
            {
                var who = (appliesTo ?? string.Empty).Trim().ToLowerInvariant();
                if (who != "" && who != "employee" && who != "student")
                    return new() { Status = false, StatusCode = 400, Message = "appliesTo must be employee or student.", Data = null };

                var types = await _repo.GetLeaveTypesAsync(ct);
                var list = types.Where(t => who == "" || (who == "employee" ? t.AppliesToEmployee : t.AppliesToStudent))
                    .Select(t => new LeaveTypeDto { LeaveTypeId = t.LeaveTypeId, TypeName = t.TypeName, TypeCode = t.TypeCode, AppliesToEmployee = t.AppliesToEmployee, AppliesToStudent = t.AppliesToStudent }).ToList();
                return new() { Status = true, StatusCode = 200, Message = "Successfully fetched", Data = list };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching leave types");
                return new() { Status = false, StatusCode = 500, Message = "Something went wrong. Please try again later.", Data = null };
            }
        }
    }
}
