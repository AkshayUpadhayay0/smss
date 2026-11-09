using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class EnquiryService : IEnquiryService
    {
        private const int MaxPageSize = 100;
        private const string ThanksMessage = "Thank you! Your request has been received. We will get back to you shortly.";

        private readonly IEnquiryRepository _repo;
        private readonly ILogger<EnquiryService> _logger;

        public EnquiryService(IEnquiryRepository repo, ILogger<EnquiryService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // ---------------------------------------------------------------- public submit
        public async Task<ApiResponse<object>> SubmitAsync(CreateEnquiryRequest request, string? ipAddress, string? userAgent, CancellationToken ct)
        {
            try
            {
                // Honeypot: only a bot fills it. Answer exactly as for a real submission so it learns nothing, and store nothing.
                if (!string.IsNullOrWhiteSpace(request.Website)) return Result<object>(201, ThanksMessage, null);

                var errors = EnquiryRules.CleanAndValidate(request, SchoolClock.Today());
                if (errors.Count > 0) return new ApiResponse<object> { Status = false, StatusCode = 400, Message = "Please correct the highlighted fields.", Data = errors };

                var entity = new TbEnquiry
                {
                    EnquiryType = request.EnquiryType!,
                    FullName = request.FullName!,
                    Designation = request.Designation,
                    SchoolName = request.SchoolName,
                    Email = request.Email!,
                    Phone = request.Phone,
                    City = request.City,
                    StudentCountRange = request.StudentCountRange,
                    Subject = request.Subject,
                    Message = request.Message,
                    PreferredDemoDate = request.PreferredDemoDate,
                    PreferredTimeSlot = request.PreferredTimeSlot,
                    ConsentGiven = true,
                    Status = EnquiryStatuses.New,
                    IpAddress = Truncate(ipAddress, 45),
                    UserAgent = Truncate(userAgent, 500),
                    CreatedAt = DateTime.UtcNow,
                };
                await _repo.AddAsync(entity, ct);
                await _repo.SaveChangesAsync(ct);
                return Result<object>(201, ThanksMessage, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Error<object>(ex, "saving a public enquiry"); }
        }

        // ---------------------------------------------------------------- Super Admin
        public async Task<ApiResponse<EnquiryListDto>> ListAsync(string? type, string? status, DateOnly? from, DateOnly? to, string? search, string? sort, int page, int pageSize, CancellationToken ct)
        {
            try
            {
                type = type?.Trim().ToUpperInvariant(); status = status?.Trim().ToUpperInvariant();
                if (!string.IsNullOrEmpty(type) && !EnquiryTypes.All.Contains(type)) return Result<EnquiryListDto>(400, "Unknown enquiry type.", null);
                if (!string.IsNullOrEmpty(status) && !EnquiryStatuses.All.Contains(status)) return Result<EnquiryListDto>(400, "Unknown enquiry status.", null);
                if (from != null && to != null && from > to) return Result<EnquiryListDto>(400, "The start date must not be after the end date.", null);

                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
                var filter = new EnquiryFilter(
                    string.IsNullOrEmpty(type) ? null : type,
                    string.IsNullOrEmpty(status) ? null : status,
                    from == null ? null : SchoolClock.StartOfDayUtc(from.Value),
                    to == null ? null : SchoolClock.StartOfDayUtc(to.Value.AddDays(1)),
                    EnquiryRules.Clean(search),
                    string.Equals(sort, "created_asc", StringComparison.OrdinalIgnoreCase));

                var (rows, total) = await _repo.ListAsync(filter, page, pageSize, ct);
                var dto = new EnquiryListDto
                {
                    Total = total, Page = page, PageSize = pageSize,
                    Items = rows.Select(r => new EnquiryListItemDto
                    {
                        EnquiryId = r.EnquiryId, EnquiryType = r.EnquiryType, FullName = r.FullName, SchoolName = r.SchoolName, Email = r.Email,
                        Phone = r.Phone, City = r.City, Subject = r.Subject, Status = r.Status, CreatedAt = r.CreatedAt,
                    }).ToList(),
                };
                return Result(200, "Enquiries loaded.", dto);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Error<EnquiryListDto>(ex, "listing enquiries"); }
        }

        public async Task<ApiResponse<EnquiryDetailDto>> GetAsync(long id, CancellationToken ct)
        {
            try
            {
                var row = await _repo.GetDetailAsync(id, ct);
                return row == null ? Result<EnquiryDetailDto>(404, "Enquiry not found.", null) : Result(200, "Enquiry loaded.", ToDetail(row.Enquiry, row.HandledByName));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Error<EnquiryDetailDto>(ex, "loading an enquiry"); }
        }

        public async Task<ApiResponse<EnquiryDetailDto>> UpdateStatusAsync(long id, long handledByUserId, UpdateEnquiryStatusRequest request, CancellationToken ct)
        {
            try
            {
                var status = request.Status?.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(status) || !EnquiryStatuses.All.Contains(status)) return Result<EnquiryDetailDto>(400, "Choose a valid status.", null);
                var notes = request.AdminNotes == null ? null : EnquiryRules.Clean(request.AdminNotes, keepLineBreaks: true) ?? string.Empty;
                if (notes != null && notes.Length > EnquiryRules.MaxNotes) return Result<EnquiryDetailDto>(400, $"Notes can be at most {EnquiryRules.MaxNotes} characters.", null);

                var entity = await _repo.GetForUpdateAsync(id, ct);
                if (entity == null) return Result<EnquiryDetailDto>(404, "Enquiry not found.", null);

                entity.Status = status;
                if (notes != null) entity.AdminNotes = notes.Length == 0 ? null : notes;
                entity.HandledByUserId = handledByUserId;   // always the signed-in user, never the request
                entity.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);

                var row = await _repo.GetDetailAsync(id, ct);
                return row == null ? Result<EnquiryDetailDto>(404, "Enquiry not found.", null) : Result(200, "Enquiry updated.", ToDetail(row.Enquiry, row.HandledByName));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Error<EnquiryDetailDto>(ex, "updating an enquiry"); }
        }

        public async Task<ApiResponse<EnquirySummaryDto>> GetSummaryAsync(CancellationToken ct)
        {
            try
            {
                var today = SchoolClock.Today();
                var c = await _repo.GetCountsAsync(SchoolClock.StartOfDayUtc(new DateOnly(today.Year, today.Month, 1)), ct);
                return Result(200, "Summary loaded.", new EnquirySummaryDto
                {
                    New = c.New, Contacted = c.Contacted, DemoScheduled = c.DemoScheduled, NewDemo = c.NewDemo, NewContact = c.NewContact, TotalThisMonth = c.TotalThisMonth,
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { return Error<EnquirySummaryDto>(ex, "counting enquiries"); }
        }

        // ---------------------------------------------------------------- helpers
        private static EnquiryDetailDto ToDetail(TbEnquiry e, string? handledByName) => new()
        {
            EnquiryId = e.EnquiryId, EnquiryType = e.EnquiryType, FullName = e.FullName, SchoolName = e.SchoolName, Email = e.Email, Phone = e.Phone, City = e.City,
            Subject = e.Subject, Status = e.Status, CreatedAt = e.CreatedAt, Designation = e.Designation, StudentCountRange = e.StudentCountRange, Message = e.Message,
            PreferredDemoDate = e.PreferredDemoDate, PreferredTimeSlot = e.PreferredTimeSlot, ConsentGiven = e.ConsentGiven, AdminNotes = e.AdminNotes,
            HandledByName = handledByName, UpdatedAt = e.UpdatedAt,
        };

        private static string? Truncate(string? value, int max) => string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        // Technical detail goes to the log only; the caller gets a generic message. No request body or personal data is logged.
        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
