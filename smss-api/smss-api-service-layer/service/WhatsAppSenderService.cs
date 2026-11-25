using System.Text.RegularExpressions;
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
    public class WhatsAppSenderService : IWhatsAppSenderService
    {
        private static readonly Regex PhoneNumberId = new(@"^[0-9]{5,50}$", RegexOptions.Compiled);
        private readonly ISchoolWhatsAppSenderRepository _repo;
        private readonly ILogger<WhatsAppSenderService> _logger;

        public WhatsAppSenderService(ISchoolWhatsAppSenderRepository repo, ILogger<WhatsAppSenderService> logger)
        {
            _repo = repo; _logger = logger;
        }

        public async Task<ApiResponse<SenderStatusDto>> GetAsync(string? callerSchoolId, string requestedSchoolId, bool isSuperAdmin, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(requestedSchoolId)) return Result<SenderStatusDto>(400, TenantMessages.NoSchool, null);
                // another school's row is a 404 for everyone but the Super Admin (it is never confirmed to exist)
                if (!isSuperAdmin && !string.Equals(callerSchoolId, requestedSchoolId, StringComparison.OrdinalIgnoreCase)) return Result<SenderStatusDto>(404, "School not found.", null);
                var school = await _repo.GetSchoolAsync(requestedSchoolId, ct);
                if (school == null) return Result<SenderStatusDto>(404, "School not found.", null);
                var row = await GetOrCreateAsync(school, ct);
                return Result(200, "Successfully fetched", await ToDtoAsync(school, row, isSuperAdmin, ct));
            }
            catch (Exception ex) { return Error<SenderStatusDto>(ex, "reading a WhatsApp sender status"); }
        }

        public async Task<ApiResponse<SenderStatusDto>> SaveAsync(string requestedSchoolId, SaveSenderRequestDto request, long userId, CancellationToken ct)
        {
            try
            {
                var status = request.Status?.Trim().ToUpperInvariant() ?? "";
                if (!SenderStatuses.All.Contains(status)) return Result<SenderStatusDto>(400, "The status must be NOT_REGISTERED, PENDING or ACTIVE.", null);
                string? number = null;
                if (!string.IsNullOrWhiteSpace(request.WhatsAppNumber))
                {
                    number = PhoneNumbers.Normalise(request.WhatsAppNumber);
                    if (number == null) return Result<SenderStatusDto>(400, "The WhatsApp number is not valid. Enter 10 digits, or the number with its country code, for example +91 98765 43210.", null);
                }
                else if (status != SenderStatuses.NotRegistered) return Result<SenderStatusDto>(400, $"A WhatsApp number is required for the status {status}.", null);
                var phoneId = string.IsNullOrWhiteSpace(request.PhoneNumberId) ? null : request.PhoneNumberId.Trim();
                if (phoneId != null && !PhoneNumberId.IsMatch(phoneId)) return Result<SenderStatusDto>(400, "The Meta phone number id is not valid: it is a number of 5 to 50 digits.", null);
                if (status == SenderStatuses.Active && phoneId == null) return Result<SenderStatusDto>(400, "ACTIVE needs the Meta phone number id: it is what the school's messages are sent from.", null);

                var school = await _repo.GetSchoolAsync(requestedSchoolId, ct);
                if (school == null) return Result<SenderStatusDto>(404, "School not found.", null);
                var row = await GetOrCreateAsync(school, ct);   // the row exists from here on
                var now = DateTime.UtcNow;
                row.Status = status;
                row.WhatsAppNumber = number;
                row.PhoneNumberId = phoneId;
                row.VerifiedAt = status == SenderStatuses.Active ? row.VerifiedAt ?? now : null;   // verified_at only means something while ACTIVE (the database requires it then)
                row.UpdatedBy = userId;
                row.UpdatedAt = now;
                await _repo.SaveChangesAsync(ct);
                return Result(200, "WhatsApp sender saved.", await ToDtoAsync(school, row, true, ct));
            }
            catch (Exception ex) { return Error<SenderStatusDto>(ex, "saving a WhatsApp sender status"); }
        }

        /// <summary>The school's sender row, made (NOT_REGISTERED, with the school's own number) when it has none. Two requests creating it at once both end with the one row.</summary>
        private async Task<TbSchoolWhatsAppSender> GetOrCreateAsync(SchoolSenderInfo school, CancellationToken ct)
        {
            var row = await _repo.GetSenderAsync(school.SchoolId, true, ct);
            if (row != null) return row;
            var now = DateTime.UtcNow;
            try
            {
                await _repo.AddSenderAsync(new TbSchoolWhatsAppSender { SchoolId = school.SchoolId, WhatsAppNumber = PhoneNumbers.Normalise(school.WhatsAppNumber), Status = SenderStatuses.NotRegistered, CreatedAt = now, UpdatedAt = now }, ct);
                await _repo.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                _repo.ClearTracking();   // someone else made it first: read theirs
            }
            return (await _repo.GetSenderAsync(school.SchoolId, true, ct))!;
        }

        private async Task<SenderStatusDto> ToDtoAsync(SchoolSenderInfo school, TbSchoolWhatsAppSender row, bool superAdmin, CancellationToken ct)
        {
            var active = row.Status == SenderStatuses.Active;
            return new SenderStatusDto
            {
                SchoolId = school.SchoolId, SchoolName = school.SchoolName, WhatsAppNumber = row.WhatsAppNumber, Status = row.Status,
                StatusLabel = row.Status switch { SenderStatuses.Active => "Active", SenderStatuses.Pending => "Pending", _ => "Not registered" },
                SendsFrom = active ? "The school's own WhatsApp number" : "The platform's WhatsApp number, with the school's name in the message",
                VerifiedAt = row.VerifiedAt, UpdatedAt = row.UpdatedAt, CanEdit = superAdmin,
                PhoneNumberId = superAdmin ? row.PhoneNumberId : null,
                UpdatedByName = superAdmin && row.UpdatedBy != null ? await _repo.GetUserNameAsync(row.UpdatedBy.Value, ct) : null,
            };
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
