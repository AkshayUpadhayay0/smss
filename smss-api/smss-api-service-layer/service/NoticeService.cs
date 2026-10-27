using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class NoticeService : INoticeService
    {
        private const string SchoolIdLogin = "School Id";   // the School Admin's own login (UserTypes.SchoolId)

        private readonly INoticeRepository _repo;
        private readonly ILogger<NoticeService> _logger;

        public NoticeService(INoticeRepository repo, ILogger<NoticeService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // ---------------------------------------------------------------- audience
        public async Task<IReadOnlyCollection<string>> ResolveAudienceTiersAsync(string? schoolId, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Array.Empty<string>();
            var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
            if (activeId == null) return Array.Empty<string>();
            var facts = await _repo.GetAudienceFactsAsync(schoolId, userId, activeId.Value, ct);
            return facts == null ? Array.Empty<string>() : Tiers(facts);
        }

        private static List<string> Tiers(NoticeAudienceFacts f)
        {
            var tiers = new List<string> { NoticeAudiences.All };
            // staff: the School Admin's school-id login, or an employee with an Active record and at least one Active role (Teacher, Accountant, Admission Staff, Principal, ...)
            if (f.UserType == SchoolIdLogin || (f.HasActiveEmployee && f.HasActiveRole)) tiers.Add(NoticeAudiences.Staff);
            // students and guardians: an Active student's login; and any Guardian login, recognised by its login type or role so it needs no change once guardian logins exist
            var guardian = string.Equals(f.UserType, "Guardian", StringComparison.OrdinalIgnoreCase) || f.ActiveRoleCodes.Contains(RoleCodes.Guardian, StringComparer.OrdinalIgnoreCase);
            if ((string.Equals(f.UserType, UserTypes.Student, StringComparison.OrdinalIgnoreCase) && f.HasActiveStudent) || guardian) tiers.Add(NoticeAudiences.StudentsGuardians);
            return tiers;
        }

        // ---------------------------------------------------------------- create / update / delete
        public async Task<ApiResponse<NoticeDto>> CreateAsync(string? schoolId, NoticeCaller caller, SaveNoticeRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<NoticeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var bad = Validate(req, out var title, out var body, out var audience);
                if (bad != null) return Result<NoticeDto>(400, bad, null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<NoticeDto>();

                var now = DateTime.UtcNow;
                var entity = new TbNotice { SchoolId = schoolId, Title = title!, Body = body!, Audience = audience!, CreatedBy = caller.UserId, PublishedAt = now, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now };
                await _repo.AddAsync(entity, ct);
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, entity.NoticeId, caller.UserId, 201, "Notice published.", ct);
            }
            catch (Exception ex) { return Error<NoticeDto>(ex, "publishing a notice"); }
        }

        public async Task<ApiResponse<NoticeDto>> UpdateAsync(string? schoolId, NoticeCaller caller, long id, SaveNoticeRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<NoticeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var bad = Validate(req, out var title, out var body, out var audience);
                if (bad != null) return Result<NoticeDto>(400, bad, null);
                var (entity, failure) = await LoadForChangeAsync(schoolId, caller, id, ct);
                if (entity == null) return failure!;
                entity.Title = title!; entity.Body = body!; entity.Audience = audience!; entity.UpdatedAt = DateTime.UtcNow;   // published_at stays: it is when it first went out
                await _repo.SaveChangesAsync(ct);
                return await ReplyAsync(schoolId, id, caller.UserId, 200, "Notice updated.", ct);
            }
            catch (Exception ex) { return Error<NoticeDto>(ex, "updating a notice"); }
        }

        public async Task<ApiResponse<NoticeDto>> DeleteAsync(string? schoolId, NoticeCaller caller, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<NoticeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var (entity, failure) = await LoadForChangeAsync(schoolId, caller, id, ct);
                if (entity == null) return failure!;
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (inactiveId == null) return ConfigError<NoticeDto>();
                entity.StatusId = inactiveId.Value; entity.UpdatedAt = DateTime.UtcNow;   // hidden, never removed
                await _repo.SaveChangesAsync(ct);
                var row = await _repo.GetRowAsync(schoolId, id, caller.UserId, ct);
                return row == null ? Result<NoticeDto>(500, "Something went wrong. Please try again later.", null) : Result(200, "Notice deleted.", ToDto(row, "Inactive"));
            }
            catch (Exception ex) { return Error<NoticeDto>(ex, "deleting a notice"); }
        }

        // ---------------------------------------------------------------- reads
        public async Task<ApiResponse<NoticeDto>> GetAsync(string? schoolId, NoticeCaller caller, long id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<NoticeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<NoticeDto>();
                var row = await _repo.GetRowAsync(schoolId, id, caller.UserId, ct);
                if (row == null || row.StatusId != activeId) return Result<NoticeDto>(404, "Notice not found.", null);   // another school's, unknown or deleted: all the same 404

                if (!caller.CanManage && row.Audience != NoticeAudiences.All)
                {
                    var tiers = await ResolveAudienceTiersAsync(schoolId, caller.UserId, ct);
                    if (!tiers.Contains(row.Audience)) return Result<NoticeDto>(403, "This notice is not addressed to you.", null);
                }
                await _repo.MarkReadAsync(id, caller.UserId, ct);   // always, for everyone who opens it
                return Result(200, "Successfully fetched", ToDto(row with { IsRead = true }, "Active"));
            }
            catch (Exception ex) { return Error<NoticeDto>(ex, "fetching a notice"); }
        }

        public async Task<ApiResponse<List<NoticeDto>>> GetMyAsync(string? schoolId, NoticeCaller caller, bool unreadOnly, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<NoticeDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rangeError = RangeError(from, to);
                if (rangeError != null) return Result<List<NoticeDto>>(400, rangeError, null);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<NoticeDto>>();
                var tiers = await ResolveAudienceTiersAsync(schoolId, caller.UserId, ct);
                if (tiers.Count == 0) return Result(200, "Successfully fetched", new List<NoticeDto>());
                var rows = await _repo.ListAsync(schoolId, caller.UserId, Filter(tiers, from, to, unreadOnly), activeId.Value, ct);
                return Result(200, "Successfully fetched", rows.Select(r => ToDto(r, "Active")).ToList());
            }
            catch (Exception ex) { return Error<List<NoticeDto>>(ex, "listing my notices"); }
        }

        public async Task<ApiResponse<UnreadCountDto>> GetUnreadCountAsync(string? schoolId, NoticeCaller caller, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<UnreadCountDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<UnreadCountDto>();
                var tiers = await ResolveAudienceTiersAsync(schoolId, caller.UserId, ct);
                var count = tiers.Count == 0 ? 0 : await _repo.CountUnreadAsync(schoolId, caller.UserId, tiers, activeId.Value, ct);
                return Result(200, "Successfully fetched", new UnreadCountDto { Count = count });
            }
            catch (Exception ex) { return Error<UnreadCountDto>(ex, "counting unread notices"); }
        }

        public async Task<ApiResponse<List<NoticeDto>>> ListAsync(string? schoolId, NoticeCaller caller, string? audience, DateOnly? from, DateOnly? to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<NoticeDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var rangeError = RangeError(from, to);
                if (rangeError != null) return Result<List<NoticeDto>>(400, rangeError, null);
                string[]? audiences = null;
                if (!string.IsNullOrWhiteSpace(audience))
                {
                    var a = NoticeAudiences.Values.FirstOrDefault(v => string.Equals(v, audience.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (a == null) return Result<List<NoticeDto>>(400, $"Audience must be one of: {string.Join(", ", NoticeAudiences.Values)}.", null);
                    audiences = new[] { a };
                }
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null) return ConfigError<List<NoticeDto>>();
                var rows = await _repo.ListAsync(schoolId, caller.UserId, Filter(audiences, from, to, false), activeId.Value, ct);
                return Result(200, "Successfully fetched", rows.Select(r => ToDto(r, "Active")).ToList());
            }
            catch (Exception ex) { return Error<List<NoticeDto>>(ex, "listing notices"); }
        }

        // ---------------------------------------------------------------- helpers
        /// <summary>The notice to change: Active, in this school, and the caller is a School Admin or the Principal who created it (a different Principal is refused).</summary>
        private async Task<(TbNotice? Entity, ApiResponse<NoticeDto>? Failure)> LoadForChangeAsync(string schoolId, NoticeCaller caller, long id, CancellationToken ct)
        {
            var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
            if (activeId == null) return (null, ConfigError<NoticeDto>());
            var entity = await _repo.GetActiveForUpdateAsync(schoolId, id, activeId.Value, ct);
            if (entity == null) return (null, Result<NoticeDto>(404, "Notice not found.", null));
            if (!caller.IsSchoolAdmin && entity.CreatedBy != caller.UserId)
                return (null, Result<NoticeDto>(403, "Only the person who published this notice, or a School Admin, can change it.", null));
            return (entity, null);
        }

        private static string? Validate(SaveNoticeRequest req, out string? title, out string? body, out string? audience)
        {
            title = req.Title?.Trim(); body = req.Body?.Trim();
            audience = NoticeAudiences.Values.FirstOrDefault(v => string.Equals(v, req.Audience?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(title)) return "Title is required.";
            if (title.Length > 200) return "The title can be at most 200 characters.";
            if (string.IsNullOrEmpty(body)) return "The notice text is required.";
            if (body.Length > 5000) return "The notice can be at most 5000 characters.";
            if (audience == null) return $"Audience must be exactly one of: {string.Join(", ", NoticeAudiences.Values)}.";
            return null;
        }

        private static NoticeFilter Filter(IReadOnlyCollection<string>? audiences, DateOnly? from, DateOnly? to, bool unreadOnly) =>
            new(audiences, from == null ? null : SchoolClock.StartOfDayUtc(from.Value), to == null ? null : SchoolClock.StartOfDayUtc(to.Value.AddDays(1)), unreadOnly);

        private static string? RangeError(DateOnly? from, DateOnly? to) => from != null && to != null && to < from ? "The 'to' date can't be before the 'from' date." : null;

        private static NoticeDto ToDto(NoticeRow r, string statusName) => new()
        {
            NoticeId = r.NoticeId, Title = r.Title, Body = r.Body, Audience = r.Audience, CreatedBy = r.CreatedBy, CreatedByName = r.CreatedByName,
            PublishedAt = r.PublishedAt, UpdatedAt = r.UpdatedAt, IsRead = r.IsRead, StatusName = statusName,
        };

        private async Task<ApiResponse<NoticeDto>> ReplyAsync(string schoolId, long id, long readerUserId, int code, string message, CancellationToken ct)
        {
            var row = await _repo.GetRowAsync(schoolId, id, readerUserId, ct);
            return row == null ? Result<NoticeDto>(500, "Something went wrong. Please try again later.", null) : Result(code, message, ToDto(row, "Active"));
        }

        private ApiResponse<T> ConfigError<T>()
        {
            _logger.LogError("Configuration problem: a general status is missing in lut_status");
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
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
