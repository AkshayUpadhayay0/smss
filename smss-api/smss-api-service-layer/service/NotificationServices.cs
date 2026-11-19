using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>Validation shared by the notification endpoints (the server is the authority).</summary>
    public static class NotificationRules
    {
        public const int MaxLogDays = 92, MaxLogRows = 500, DefaultLogDays = 7, MaxGraceDays = 30;

        public static string? NormalizePlatform(string? value)
        {
            var p = value?.Trim().ToUpperInvariant();
            return p is "ANDROID" or "IOS" ? p : null;
        }

        public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public static bool TryParseSendTime(string? value, out TimeOnly time) =>
            TimeOnly.TryParseExact(value?.Trim(), "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out time);
    }

    public class DeviceService : IDeviceService
    {
        private readonly INotificationRepository _repo;
        private readonly ILogger<DeviceService> _logger;
        public DeviceService(INotificationRepository repo, ILogger<DeviceService> logger) { _repo = repo; _logger = logger; }

        public async Task<ApiResponse<DeviceResultDto>> RegisterAsync(string? schoolId, long callerUserId, RegisterDeviceRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Reply<DeviceResultDto>(400, TenantMessages.NoSchool, null);
            var token = request.FcmToken?.Trim();
            if (string.IsNullOrEmpty(token) || token.Length > 1024) return Reply<DeviceResultDto>(400, "A valid device token is required.", null);
            var platform = NotificationRules.NormalizePlatform(request.Platform);
            if (platform == null) return Reply<DeviceResultDto>(400, "Platform must be ANDROID or IOS.", null);
            var label = NotificationRules.Blank(request.DeviceLabel);
            var version = NotificationRules.Blank(request.AppVersion);
            if (label is { Length: > 100 }) return Reply<DeviceResultDto>(400, "The device label is too long (100 characters at most).", null);
            if (version is { Length: > 30 }) return Reply<DeviceResultDto>(400, "The app version is too long (30 characters at most).", null);
            try
            {
                await _repo.UpsertDeviceAsync(schoolId, callerUserId, token, platform, label, version, ct);
                return Reply(200, "Device registered.", new DeviceResultDto { Registered = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while registering a device for user {UserId}", callerUserId);   // never the token
                return Reply<DeviceResultDto>(500, "Something went wrong. Please try again later.", null);
            }
        }

        public async Task<ApiResponse<DeviceResultDto>> UnregisterAsync(string? schoolId, long callerUserId, UnregisterDeviceRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Reply<DeviceResultDto>(400, TenantMessages.NoSchool, null);
            var token = request.FcmToken?.Trim();
            if (string.IsNullOrEmpty(token) || token.Length > 1024) return Reply<DeviceResultDto>(400, "A valid device token is required.", null);
            try
            {
                await _repo.DeactivateOwnDeviceAsync(callerUserId, token, ct);
                return Reply(200, "Device unregistered.", new DeviceResultDto { Registered = false });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while unregistering a device for user {UserId}", callerUserId);
                return Reply<DeviceResultDto>(500, "Something went wrong. Please try again later.", null);
            }
        }

        private static ApiResponse<T> Reply<T>(int code, string message, T? data) =>
            new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };
    }

    public class NotificationSettingsService : INotificationSettingsService
    {
        private readonly INotificationRepository _repo;
        private readonly ILogger<NotificationSettingsService> _logger;
        public NotificationSettingsService(INotificationRepository repo, ILogger<NotificationSettingsService> logger) { _repo = repo; _logger = logger; }

        public async Task<ApiResponse<NotificationSettingsDto>> GetAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Reply<NotificationSettingsDto>(400, TenantMessages.NoSchool, null);
            try { return Reply(200, "Successfully fetched", ToDto(await _repo.GetSettingsAsync(schoolId, ct))); }
            catch (Exception ex) { return Error<NotificationSettingsDto>(ex, "fetching notification settings"); }
        }

        public async Task<ApiResponse<NotificationSettingsDto>> SaveAsync(string? schoolId, long callerUserId, NotificationSettingsRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Reply<NotificationSettingsDto>(400, TenantMessages.NoSchool, null);
            if (!NotificationRules.TryParseSendTime(request.FeeReminderSendTime, out var sendTime))
                return Reply<NotificationSettingsDto>(400, "The send time must look like 09:00 (24-hour, India time).", null);
            if (request.FeeReminderGraceDays is < 0 or > NotificationRules.MaxGraceDays)
                return Reply<NotificationSettingsDto>(400, $"Grace days must be between 0 and {NotificationRules.MaxGraceDays}.", null);
            try
            {
                var row = await _repo.GetSettingsForUpdateAsync(schoolId, ct);
                var now = DateTime.UtcNow;
                if (row == null)
                {
                    row = new TbSchoolNotificationSettings { SchoolId = schoolId, CreatedAt = now };
                    _repo.AddSettings(row);
                }
                row.FeeReminderEnabled = request.FeeReminderEnabled;
                row.FeeReminderSendTime = sendTime;
                row.FeeReminderGraceDays = (short)request.FeeReminderGraceDays;
                row.UpdatedBy = callerUserId;
                row.UpdatedAt = now;
                await _repo.SaveChangesAsync(ct);
                return Reply(200, "Notification settings saved.", ToDto(row));
            }
            catch (Exception ex) { return Error<NotificationSettingsDto>(ex, "saving notification settings"); }
        }

        private static NotificationSettingsDto ToDto(TbSchoolNotificationSettings? r) => r == null
            ? new NotificationSettingsDto()
            : new NotificationSettingsDto
            {
                FeeReminderEnabled = r.FeeReminderEnabled,
                FeeReminderSendTime = r.FeeReminderSendTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                FeeReminderGraceDays = r.FeeReminderGraceDays,
                LastFeeReminderRunDate = r.LastFeeReminderRunDate
            };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Reply<T>(500, "Something went wrong. Please try again later.", default);
        }

        private static ApiResponse<T> Reply<T>(int code, string message, T? data) =>
            new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };
    }

    public class NotificationLogService : INotificationLogService
    {
        private readonly INotificationRepository _repo;
        private readonly ILogger<NotificationLogService> _logger;
        public NotificationLogService(INotificationRepository repo, ILogger<NotificationLogService> logger) { _repo = repo; _logger = logger; }

        public async Task<ApiResponse<List<PushLogItemDto>>> ListAsync(string? schoolId, string? studentId, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Reply<List<PushLogItemDto>>(400, TenantMessages.NoSchool, null);
            var today = SchoolClock.Today();
            var end = to ?? today;
            var start = from ?? end.AddDays(-(NotificationRules.DefaultLogDays - 1));
            if (start > end) return Reply<List<PushLogItemDto>>(400, "'from' must not be after 'to'.", null);
            if (end.DayNumber - start.DayNumber >= NotificationRules.MaxLogDays) return Reply<List<PushLogItemDto>>(400, $"Choose a range of at most {NotificationRules.MaxLogDays} days.", null);
            var student = NotificationRules.Blank(studentId);
            if (student is { Length: > 20 }) return Reply<List<PushLogItemDto>>(404, "Student not found.", null);
            try
            {
                // A student of another school looks exactly like one that does not exist
                if (student != null && !await _repo.StudentBelongsToSchoolAsync(schoolId, student, ct)) return Reply<List<PushLogItemDto>>(404, "Student not found.", null);
                var rows = await _repo.GetLogAsync(schoolId, student, start, end, NotificationRules.MaxLogRows, ct);
                return Reply(200, "Successfully fetched", rows.Select(r => new PushLogItemDto
                {
                    NotificationId = r.NotificationId, StudentId = r.StudentId, NotificationType = r.NotificationType, NotifyDate = r.NotifyDate,
                    Title = r.Title, Status = r.Status, AttemptCount = r.AttemptCount, DevicesTargeted = r.DevicesTargeted, DevicesSucceeded = r.DevicesSucceeded,
                    ErrorSummary = r.ErrorSummary, CreatedAt = r.CreatedAt, SentAt = r.SentAt
                }).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching the notification log");
                return Reply<List<PushLogItemDto>>(500, "Something went wrong. Please try again later.", null);
            }
        }

        private static ApiResponse<T> Reply<T>(int code, string message, T? data) =>
            new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };
    }
}
