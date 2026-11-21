using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// The first-login setup checklist, derived from what already exists: nothing is stored, so it can never drift from the data. A school that already
    /// has an active student is complete whatever the steps say (grandfathering), though the steps still report their real state.
    /// </summary>
    public class OnboardingService : IOnboardingService
    {
        private const int MaxNamesInDetail = 8;
        private readonly IOnboardingRepository _repo;
        private readonly ILogger<OnboardingService> _logger;
        public OnboardingService(IOnboardingRepository repo, ILogger<OnboardingService> logger) { _repo = repo; _logger = logger; }

        public async Task<ApiResponse<OnboardingStatusDto>> GetStatusAsync(string? schoolId, long callerUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Fail<OnboardingStatusDto>(400, TenantMessages.NoSchool);
            try
            {
                return new ApiResponse<OnboardingStatusDto> { Status = true, StatusCode = 200, Message = "Setup status loaded.", Data = await BuildAsync(schoolId, callerUserId, ct) };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Setup status failed for school {SchoolId}", schoolId);
                return Fail<OnboardingStatusDto>(500, "The setup status could not be loaded.");
            }
        }

        public async Task<ApiResponse<SetupIncompleteDto>?> CheckSetupAsync(string? schoolId, CancellationToken ct)
        {
            // No school on the token: not this gate's business, the endpoint answers its own 400.
            if (string.IsNullOrWhiteSpace(schoolId)) return null;
            try
            {
                var status = await BuildAsync(schoolId, null, ct);
                return status.IsComplete ? null : Incomplete(status.NextStep);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // fail closed
                _logger.LogError(ex, "Setup gate failed for school {SchoolId}", schoolId);
                return Incomplete(null);
            }
        }

        private static ApiResponse<SetupIncompleteDto> Incomplete(OnboardingStepDto? next) => new()
        {
            Status = false, StatusCode = 409, Message = SetupGate.Message, Data = new SetupIncompleteDto { NextStep = next },
        };

        private static ApiResponse<T> Fail<T>(int code, string message) => new() { Status = false, StatusCode = code, Message = message, Data = default };

        /// <param name="callerUserId">The user whose password step is reported; null (the gate) means "not asked": a login that got this far has already passed the first-login middleware.</param>
        private async Task<OnboardingStatusDto> BuildAsync(string schoolId, long? callerUserId, CancellationToken ct)
        {
            var active = await _repo.ActiveStatusIdAsync(ct) ?? throw new InvalidOperationException("The Active status is missing from lut_status.");

            var grandfathered = await _repo.HasActiveStudentAsync(schoolId, active, ct);

            var passwordDone = true;
            if (callerUserId != null) passwordDone = await _repo.IsFirstLoginAsync(schoolId, callerUserId.Value, ct) == false;

            var yearId = await _repo.CurrentYearIdAsync(schoolId, active, ct);
            var classes = await _repo.ActiveClassesAsync(schoolId, yearId, active, ct);
            var withSection = await _repo.ClassIdsWithActiveSectionAsync(schoolId, yearId, active, ct);
            var hasFeeType = await _repo.HasActiveFeeTypeAsync(schoolId, ct);
            var withFees = yearId == null ? new HashSet<long>() : await _repo.ClassIdsWithFeeStructureAsync(schoolId, yearId.Value, ct);

            var noSection = classes.Where(c => !withSection.Contains(c.ClassId)).Select(c => c.ClassName).ToList();
            var noFees = classes.Where(c => !withFees.Contains(c.ClassId)).Select(c => c.ClassName).ToList();

            var steps = new List<OnboardingStepDto>
            {
                new() { Code = OnboardingSteps.PasswordChanged, Title = "Change your temporary password", Done = passwordDone, Route = "/change-password",
                        Detail = passwordDone ? "Password changed." : "Replace the temporary password you were given." },
                new() { Code = OnboardingSteps.AcademicYear, Title = "Add the academic year", Done = yearId != null, Route = "/school-setup/academic-year",
                        Detail = yearId != null ? "Current academic year is set." : "Add an academic year and make it the current one." },
                new() { Code = OnboardingSteps.ImportMastersOrClasses, Title = "Import masters or add classes", Done = classes.Count > 0, Route = "/school-setup/import-masters",
                        Detail = classes.Count > 0 ? $"{classes.Count} active class(es)." : "Import the ready-made classes, or add your classes by hand." },
                new() { Code = OnboardingSteps.SchoolSetup, Title = "Complete school setup", Done = classes.Count > 0 && noSection.Count == 0, Route = "/school-setup/sections",
                        Detail = classes.Count == 0 ? "Add at least one class first."
                               : noSection.Count == 0 ? "Every class has a section."
                               : "Add a section to: " + Names(noSection) },
                new() { Code = OnboardingSteps.FeeTypes, Title = "Add fee types", Done = hasFeeType, Route = "/fee-setup/fee-types",
                        Detail = hasFeeType ? "Fee types are set." : "Add at least one active fee type (for example Tuition Fee)." },
                new() { Code = OnboardingSteps.FeeStructure, Title = "Add a fee structure for every class", Done = yearId != null && classes.Count > 0 && noFees.Count == 0, Route = "/fee-setup/fee-structures",
                        Detail = yearId == null ? "Set the current academic year first."
                               : classes.Count == 0 ? "Add classes first."
                               : noFees.Count == 0 ? "Every class has a fee structure."
                               : "Fee structure still needed for: " + Names(noFees) },
            };

            var next = steps.FirstOrDefault(s => !s.Done);
            return new OnboardingStatusDto { IsComplete = grandfathered || next == null, NextStep = grandfathered ? null : next, Steps = steps };
        }

        private static string Names(List<string> names) =>
            names.Count <= MaxNamesInDetail ? string.Join(", ", names) : string.Join(", ", names.Take(MaxNamesInDetail)) + $" and {names.Count - MaxNamesInDetail} more";
    }
}
