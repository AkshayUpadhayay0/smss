using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>
    /// The Admission Staff dashboard, read-only. The application list comes from the Admission service (the same read the Admissions screen uses), so the counts equal that screen;
    /// the "missing documents" figure comes from a small read-only repository. A part that fails is named in PartsUnavailable, never shown as 0.
    /// </summary>
    public class AdmissionDashboardService : IAdmissionDashboardService
    {
        public const int RecentRows = 10;
        public const int NewDays = 7;
        /// <summary>Every status in pipeline order, so the dashboard always lists all six (zero-filled).</summary>
        public static readonly string[] StatusOrder = { "APPLICATION_SUBMITTED", "UNDER_REVIEW", "APPROVED", "ADMITTED", "REJECTED", "CANCELLED" };
        /// <summary>Applications still open: documents can still be added to them (Rejected, Withdrawn and Admitted ones are frozen).</summary>
        public static readonly string[] OpenStatuses = { "APPLICATION_SUBMITTED", "UNDER_REVIEW", "APPROVED" };

        private readonly IAdmissionService _admissions;
        private readonly IAdmissionDashboardRepository _repo;
        private readonly ILogger<AdmissionDashboardService> _logger;

        public AdmissionDashboardService(IAdmissionService admissions, IAdmissionDashboardRepository repo, ILogger<AdmissionDashboardService> logger)
        {
            _admissions = admissions; _repo = repo; _logger = logger;
        }

        public async Task<ApiResponse<AdmissionDashboardDto>> GetSummaryAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                var today = SchoolClock.Today();
                var dto = new AdmissionDashboardDto { Today = today };

                try
                {
                    var all = await _admissions.GetAllAsync(schoolId, null, null, null, ct);
                    if (all.Status && all.Data != null)
                    {
                        dto.ByStatus = ByStatus(all.Data);
                        dto.TotalApplications = all.Data.Count;
                        dto.NewThisWeek = NewThisWeek(all.Data, today);
                        dto.RecentApplications = all.Data.OrderByDescending(a => a.ApplicationDate).ThenByDescending(a => a.AdmissionId).Take(RecentRows)
                            .Select(a => new DashboardAdmissionDto
                            {
                                AdmissionId = a.AdmissionId, ApplicationNumber = a.ApplicationNumber, ApplicantFullName = a.ApplicantFullName,
                                AppliedClassName = a.AppliedClassName, StatusName = a.StatusName, ApplicationDate = a.ApplicationDate
                            }).ToList();
                    }
                    else Down(AdmissionDashboardParts.Applications, dto, null, $"{all.StatusCode}: {all.Message}");
                }
                catch (Exception ex) { Down(AdmissionDashboardParts.Applications, dto, ex); }

                try
                {
                    var missing = await _repo.GetMissingDocumentsAsync(schoolId, OpenStatuses, ct);
                    dto.MissingDocumentsCount = missing.Count;
                    dto.MissingDocuments = missing.Take(RecentRows).Select(m => new AdmissionMissingDocumentsDto
                    {
                        AdmissionId = m.AdmissionId, ApplicationNumber = m.ApplicationNumber, ApplicantFullName = m.ApplicantName,
                        StatusName = m.StatusName, ApplicationDate = m.ApplicationDate, MissingDocuments = m.MissingDocuments
                    }).ToList();
                }
                catch (Exception ex) { Down(AdmissionDashboardParts.Documents, dto, ex); }

                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building the Admission dashboard");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        // ---------------------------------------------------------------- pure helpers (unit-testable)
        /// <summary>All six statuses in pipeline order with their counts (0 for a status nobody is in); a status the list does not know is appended after them.</summary>
        public static List<AdmissionStatusCountDto> ByStatus(IEnumerable<AdmissionResponseDto> all)
        {
            var counts = all.GroupBy(a => a.StatusName).ToDictionary(g => g.Key, g => g.Count());
            var rows = StatusOrder.Select(s => new AdmissionStatusCountDto { StatusName = s, Count = counts.GetValueOrDefault(s) }).ToList();
            rows.AddRange(counts.Where(kv => !StatusOrder.Contains(kv.Key)).OrderBy(kv => kv.Key).Select(kv => new AdmissionStatusCountDto { StatusName = kv.Key, Count = kv.Value }));
            return rows;
        }

        /// <summary>Applications dated in the last <paramref name="days"/> days, today included.</summary>
        public static int NewThisWeek(IEnumerable<AdmissionResponseDto> all, DateOnly today, int days = NewDays)
        {
            var from = today.AddDays(-(days - 1));
            return all.Count(a => DateOnly.FromDateTime(a.ApplicationDate) >= from && DateOnly.FromDateTime(a.ApplicationDate) <= today);
        }

        private void Down(string part, AdmissionDashboardDto dto, Exception? ex, string? reason = null)
        {
            if (ex != null) _logger.LogWarning(ex, "Admission dashboard: could not load {Part}", part);
            else _logger.LogWarning("Admission dashboard: could not load {Part} ({Reason})", part, reason);
            if (!dto.PartsUnavailable.Contains(part)) dto.PartsUnavailable.Add(part);
        }

        private static ApiResponse<AdmissionDashboardDto> Result(int code, string message, AdmissionDashboardDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
