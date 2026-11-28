using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public static class NotificationTypes
    {
        public const string FeeOverdue = "FEE_OVERDUE";
    }

    public static class PushStatuses
    {
        public const string Pending = "PENDING", Sent = "SENT", Failed = "FAILED", NoDevice = "NO_DEVICE", DryRun = "DRY_RUN";
    }

    /// <summary>
    /// The daily fee-overdue push. "Overdue" is NOT defined here: it is the Student module's own grouped query (the one behind the student-list fee status and the
    /// Fee Dashboard, <see cref="IStudentRepository.GetFeeAggregatesAsync"/>: still owed and due date before the given day). The grace period is applied by moving
    /// that day back (due_date + grace &lt; today is due_date &lt; today - grace), so there is exactly one definition of overdue.
    /// </summary>
    public class FeeReminderService : IFeeReminderService
    {
        /// <summary>Neutral on purpose: no amount, no name on a lock screen a classmate can read.</summary>
        public const string Title = "Fee payment pending";
        public const string Body = "Please clear your dues at the school office.";
        public const short MaxAttempts = 3;
        private const int AggregateChunk = 500;
        private const int SendBatchRows = 250;
        private const string DryRunNote = "Dry run: validated by FCM, not delivered.";

        private readonly INotificationRepository _repo;
        private readonly IStudentRepository _students;
        private readonly IFcmSender _fcm;
        private readonly TimeProvider _clock;
        private readonly Microsoft.Extensions.Options.IOptions<FirebaseOptions> _firebase;
        private readonly ILogger<FeeReminderService> _logger;

        public FeeReminderService(INotificationRepository repo, IStudentRepository students, IFcmSender fcm, TimeProvider clock,
            Microsoft.Extensions.Options.IOptions<FirebaseOptions> firebase, ILogger<FeeReminderService> logger, IEarlierYearDuesRepository? earlier = null)
        {
            _earlier = earlier;
            _repo = repo;
            _students = students;
            _fcm = fcm;
            _clock = clock;
            _firebase = firebase;
            _logger = logger;
        }

        // ------------------------------------------------------------------ the tick

        private enum Action { None, FullRun, Retry }

        public async Task<FeeReminderTickResult> RunTickAsync(CancellationToken ct)
        {
            var result = new FeeReminderTickResult();
            var nowUtc = _clock.GetUtcNow().UtcDateTime;
            var today = SchoolClock.DateOf(nowUtc);
            var timeNow = IndiaTimeOfDay(nowUtc, today);

            var enabled = await _repo.GetEnabledSettingsAsync(ct);
            result.SchoolsChecked = enabled.Count;
            var due = enabled.Select(s => (s.SchoolId, Action: Decide(s, today, timeNow))).Where(x => x.Action != Action.None).ToList();
            if (due.Count == 0) return result;

            // One clear error per run, nothing sent. The API itself has already started fine.
            if (!_fcm.TryEnsureReady(out var reason))
            {
                _logger.LogError("Fee reminders are due for {Count} school(s) but cannot be sent: {Reason}", due.Count, reason);
                result.NotReady = true;
                return result;
            }

            foreach (var (schoolId, _) in due)
            {
                ct.ThrowIfCancellationRequested();
                try { await RunSchoolAsync(schoolId, today, nowUtc, timeNow, result, ct); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    result.SchoolsFailed++;
                    _logger.LogError(ex, "Fee reminder run failed for school {SchoolId}; the other schools continue", schoolId);
                }
                finally { _repo.ClearTracking(); }
            }
            return result;
        }

        /// <summary>After the send time and not yet run today = the full run (also the catch-up tick after downtime); already run today = retry only.</summary>
        private static Action Decide(TbSchoolNotificationSettings s, DateOnly today, TimeOnly timeNow)
        {
            if (!s.FeeReminderEnabled) return Action.None;
            if (timeNow < s.FeeReminderSendTime) return Action.None;
            return s.LastFeeReminderRunDate is { } last && last >= today ? Action.Retry : Action.FullRun;
        }

        private async Task RunSchoolAsync(string schoolId, DateOnly today, DateTime nowUtc, TimeOnly timeNow, FeeReminderTickResult result, CancellationToken ct)
        {
            if (!await _repo.TryLockSchoolRunAsync(schoolId, ct)) { result.SchoolsSkippedLocked++; return; }
            try
            {
                // Re-read under the lock: another instance may just have finished this school's day
                _repo.ClearTracking();
                var settings = await _repo.GetSettingsForUpdateAsync(schoolId, ct);
                if (settings == null) return;
                var action = Decide(settings, today, timeNow);
                if (action == Action.None) return;

                // This run's own counters, so the one log line below describes this school only
                var school = new FeeReminderTickResult();
                int eligible;
                string mode;
                if (action == Action.FullRun)
                {
                    mode = "full";
                    eligible = 0;
                    var yearId = await _repo.GetCurrentAcademicYearIdAsync(schoolId, ct);
                    if (yearId != null)
                    {
                        var overdue = await FindOverdueStudentsAsync(schoolId, yearId.Value, settings.FeeReminderGraceDays, today, ct);
                        eligible = overdue.Count;
                        await _repo.CreateLogRowsAsync(schoolId, today, NotificationTypes.FeeOverdue, Title, Body, overdue, ct);
                    }
                    await SendOpenRowsAsync(schoolId, today, nowUtc, school, ct);
                    settings.LastFeeReminderRunDate = today;
                    settings.UpdatedAt = DateTime.UtcNow;
                    await _repo.SaveChangesAsync(ct);
                    result.SchoolsRun++;
                }
                else
                {
                    mode = "retry";
                    eligible = await SendOpenRowsAsync(schoolId, today, nowUtc, school, ct); // a retry's "eligible" = the rows still open
                    result.SchoolsRetried++;
                }
                result.Sent += school.Sent; result.NoDevice += school.NoDevice; result.Failed += school.Failed; result.DryRun += school.DryRun;

                // ONE line per school run: counts only. Never a student name, a token or the message text.
                // A retry tick that found nothing open (every tick after the send time visits the school) says nothing, so the log is not flooded every 5 minutes.
                if (mode == "full" || eligible > 0)
                    _logger.LogInformation("Fee reminder run for school {SchoolId} ({Mode}): eligible={Eligible} sent={Sent} failed={Failed} noDevice={NoDevice} dryRun={DryRun}",
                        schoolId, mode, eligible, school.Sent, school.Failed, school.NoDevice, school.DryRun);
            }
            finally { await _repo.UnlockSchoolRunAsync(schoolId); }
        }

        // ------------------------------------------------------------------ who is overdue (reuses the Student module's definition)

        private readonly IEarlierYearDuesRepository? _earlier;

        private async Task<List<EligibleStudent>> FindOverdueStudentsAsync(string schoolId, long yearId, int graceDays, DateOnly today, CancellationToken ct)
        {
            var eligible = await _repo.GetEligibleStudentsAsync(schoolId, yearId, UserTypes.Student, ct);
            if (eligible.Count == 0) return eligible;
            var cutoff = today.AddDays(-Math.Max(0, graceDays));
            var overdue = new List<EligibleStudent>();
            var yearStart = _earlier == null ? null : await _earlier.GetYearStartAsync(schoolId, yearId, ct);
            foreach (var chunk in eligible.Chunk(AggregateChunk))
            {
                var aggregates = await _students.GetFeeAggregatesAsync(schoolId, yearId, chunk.Select(e => e.StudentId).ToArray(), cutoff, ct);
                // an installment still unpaid from an EARLIER year counts toward the overdue count too (the reminder text stays the same, neutral, no amounts)
                var earlierOverdue = new HashSet<string>(StringComparer.Ordinal);
                if (_earlier != null && yearStart != null)
                {
                    var ids = chunk.Select(e => e.StudentId).ToList();
                    foreach (var r in await _earlier.GetOutstandingAsync(schoolId, ids, ids.Select(_ => yearStart.Value).ToList(), cutoff, ct)) if (r.OverdueCount > 0) earlierOverdue.Add(r.StudentId);
                }
                overdue.AddRange(chunk.Where(e => (aggregates.TryGetValue(e.StudentId, out var a) && a.OverdueCount > 0) || earlierOverdue.Contains(e.StudentId)));
            }
            return overdue;
        }

        public async Task<ApiResponse<FeeReminderPreviewDto>> PreviewAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Reply<FeeReminderPreviewDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var today = SchoolClock.Today();
                var settings = await _repo.GetSettingsAsync(schoolId, ct);
                var grace = settings?.FeeReminderGraceDays ?? 0;
                var dto = new FeeReminderPreviewDto { Date = today, GraceDays = grace };
                var yearId = await _repo.GetCurrentAcademicYearIdAsync(schoolId, ct);
                if (yearId != null)
                {
                    var overdue = await FindOverdueStudentsAsync(schoolId, yearId.Value, grace, today, ct);
                    var devices = overdue.Count == 0 ? new List<ActiveDevice>() : await _repo.GetActiveDevicesAsync(schoolId, overdue.Select(o => o.UserId).ToArray(), ct);
                    var withDevice = devices.Select(d => d.UserId).ToHashSet();
                    dto.StudentsToRemind = overdue.Count;
                    dto.WithDevice = overdue.Count(o => withDevice.Contains(o.UserId));
                    dto.WithoutDevice = dto.StudentsToRemind - dto.WithDevice;
                }
                return Reply(200, "Successfully fetched", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while previewing fee reminders for school {SchoolId}", schoolId);
                return Reply<FeeReminderPreviewDto>(500, "Something went wrong. Please try again later.", null);
            }
        }

        // ------------------------------------------------------------------ sending

        /// <summary>Sends every open row of the school's day and returns how many rows it handled.</summary>
        private async Task<int> SendOpenRowsAsync(string schoolId, DateOnly today, DateTime nowUtc, FeeReminderTickResult result, CancellationToken ct)
        {
            var rows = await _repo.GetOpenLogRowsAsync(schoolId, today, NotificationTypes.FeeOverdue, MaxAttempts, includeDryRun: !_firebase.Value.DryRun, ct);
            foreach (var batch in rows.Chunk(SendBatchRows))
            {
                ct.ThrowIfCancellationRequested();
                var userIds = batch.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToArray();
                var devices = userIds.Length == 0 ? new List<ActiveDevice>() : await _repo.GetActiveDevicesAsync(schoolId, userIds, ct);
                var byUser = devices.GroupBy(d => d.UserId).ToDictionary(g => g.Key, g => g.ToList());

                // one message per (row, device); remember which row each belongs to
                var messages = new List<FcmMessage>();
                var owners = new List<TbPushNotificationLog>();
                foreach (var row in batch)
                {
                    if (row.UserId is long uid && byUser.TryGetValue(uid, out var own))
                        foreach (var d in own) { messages.Add(new FcmMessage(d.Token, row.Title, row.Body, NotificationTypes.FeeOverdue)); owners.Add(row); }
                    else
                    {
                        row.Status = PushStatuses.NoDevice;
                        row.DevicesTargeted = 0; row.DevicesSucceeded = 0;
                        row.ErrorSummary = null;
                        result.NoDevice++;
                    }
                }

                var deadTokens = new List<string>();
                if (messages.Count > 0)
                {
                    var results = await _fcm.SendAsync(messages, ct);
                    var perRow = owners.Distinct().ToDictionary(r => r, _ => new List<FcmResult>());
                    for (var i = 0; i < messages.Count; i++)
                    {
                        perRow[owners[i]].Add(results[i]);
                        if (results[i].Kind == FcmResultKind.InvalidToken) deadTokens.Add(messages[i].Token);
                    }
                    foreach (var (row, rs) in perRow) Apply(row, rs, nowUtc, result);
                }

                // statuses first, then the dead-token clean-up: if the second fails, the log is still right
                await _repo.SaveChangesAsync(ct);
                if (deadTokens.Count > 0) await _repo.DeactivateTokensAsync(deadTokens.Distinct().ToArray(), ct);
            }
            return rows.Count;
        }

        private void Apply(TbPushNotificationLog row, List<FcmResult> rs, DateTime nowUtc, FeeReminderTickResult result)
        {
            row.AttemptCount++;
            row.DevicesTargeted = (short)rs.Count;
            row.DevicesSucceeded = (short)rs.Count(r => r.Kind == FcmResultKind.Success);
            if (row.DevicesSucceeded > 0)
            {
                if (_firebase.Value.DryRun)
                {
                    // Validated, not delivered: DRY_RUN never counts as sent, so a real run the same day still sends (and updates this row in place)
                    row.Status = PushStatuses.DryRun;
                    row.SentAt = null;
                    row.ErrorSummary = DryRunNote;
                    result.DryRun++;
                    return;
                }
                row.Status = PushStatuses.Sent;
                row.SentAt = nowUtc;
                row.ErrorSummary = null;
                result.Sent++;
                return;
            }
            row.Status = PushStatuses.Failed;
            var transient = rs.Any(r => r.Kind == FcmResultKind.Transient);
            var code = rs.Select(r => r.ErrorCode).FirstOrDefault(c => !string.IsNullOrEmpty(c));
            if (transient)
                row.ErrorSummary = Trim("Temporary delivery problem" + (code != null ? $" ({code})" : "") + (row.AttemptCount >= MaxAttempts ? "; no more retries today." : "; will retry."));
            else
            {
                // Dead tokens or a permanent refusal: retrying the same thing today cannot help
                row.AttemptCount = Math.Max(row.AttemptCount, MaxAttempts);
                row.ErrorSummary = Trim("No device accepted the notification" + (code != null ? $" ({code})" : "") + ".");
            }
            result.Failed++;
        }

        private static string Trim(string s) => s.Length <= 500 ? s : s[..500];

        // ------------------------------------------------------------------ helpers

        /// <summary>The India wall-clock time of an instant, derived from the shared SchoolClock (no second time-zone definition).</summary>
        private static TimeOnly IndiaTimeOfDay(DateTime utc, DateOnly indiaDate) =>
            TimeOnly.FromTimeSpan(utc - SchoolClock.StartOfDayUtc(indiaDate));

        private static ApiResponse<T> Reply<T>(int code, string message, T? data) =>
            new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };
    }
}
