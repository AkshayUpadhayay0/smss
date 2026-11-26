using smss_api_db_layer.helper;
using Microsoft.EntityFrameworkCore;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class NotificationRepository : INotificationRepository
    {
        private const string GeneralStatusType = "general status", Active = "Active";

        private readonly dbContext _db;
        private bool _lockHeld;
        public NotificationRepository(dbContext db) => _db = db;

        // ---- devices ----
        public async Task UpsertDeviceAsync(string schoolId, long userId, string token, string platform, string? label, string? appVersion, CancellationToken ct)
        {
            // One statement: concurrent registers of the same token cannot collide on the unique index
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO public.tb_user_devices (user_id, school_id, fcm_token, platform, device_label, app_version, is_active, last_seen_at, created_at, updated_at)
                VALUES ({userId}, {schoolId}, {token}, {platform}, {label}::varchar, {appVersion}::varchar, true, now(), now(), now())
                ON CONFLICT (fcm_token) DO UPDATE SET
                    user_id = EXCLUDED.user_id, school_id = EXCLUDED.school_id, platform = EXCLUDED.platform,
                    device_label = EXCLUDED.device_label, app_version = EXCLUDED.app_version,
                    is_active = true, last_seen_at = now(), updated_at = now()", ct);
        }

        public Task<int> DeactivateOwnDeviceAsync(long userId, string token, CancellationToken ct) =>
            _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE public.tb_user_devices SET is_active = false, updated_at = now()
                                                        WHERE fcm_token = {token} AND user_id = {userId} AND is_active", ct);

        public Task<int> DeactivateTokensAsync(IReadOnlyCollection<string> tokens, CancellationToken ct)
        {
            if (tokens.Count == 0) return Task.FromResult(0);
            var arr = tokens.ToArray();
            return _db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE public.tb_user_devices SET is_active = false, updated_at = now()
                                                               WHERE fcm_token = ANY({arr}) AND is_active", ct);
        }

        public Task<List<ActiveDevice>> GetActiveDevicesAsync(string schoolId, IReadOnlyCollection<long> userIds, CancellationToken ct)
        {
            var ids = userIds.ToArray();
            return _db.UserDevices.AsNoTracking()
                .Where(d => d.SchoolId == schoolId && d.IsActive && ids.Contains(d.UserId))
                .OrderBy(d => d.DeviceId)
                .Select(d => new ActiveDevice(d.UserId, d.DeviceId, d.FcmToken))
                .ToListAsync(ct);
        }

        // ---- settings ----
        public Task<TbSchoolNotificationSettings?> GetSettingsAsync(string schoolId, CancellationToken ct) =>
            _db.SchoolNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);

        public Task<TbSchoolNotificationSettings?> GetSettingsForUpdateAsync(string schoolId, CancellationToken ct) =>
            _db.SchoolNotificationSettings.FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);

        public void AddSettings(TbSchoolNotificationSettings row) => _db.SchoolNotificationSettings.Add(row);

        public async Task<List<TbSchoolNotificationSettings>> GetEnabledSettingsAsync(CancellationToken ct)
        {
            var activeId = await ActiveStatusIdAsync(ct);
            if (activeId == null) return new();
            return await (from s in _db.SchoolNotificationSettings.AsNoTracking()
                          join sc in _db.Schools.AsNoTracking() on s.SchoolId equals sc.SchoolId
                          where s.FeeReminderEnabled && sc.SchoolStatusId == activeId
                          orderby s.SchoolId
                          select s).ToListAsync(ct);
        }

        // ---- the daily job ----
        // The lock is SESSION-level, so it lives on one physical connection. Open/Close below keep this DbContext's single connection open for the whole run:
        // lock, every query of the run, and unlock all use it, and nothing else can take it from the pool in between.
        public async Task<bool> TryLockSchoolRunAsync(string schoolId, CancellationToken ct)
        {
            var key = LockKey(schoolId);
            await _db.Database.OpenConnectionAsync(ct);
            try
            {
                var got = (await _db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock(hashtextextended({key}, 0)) AS \"Value\"").ToListAsync(ct)).FirstOrDefault();
                if (got) { _lockHeld = true; return true; }
            }
            catch
            {
                // Cancelled or failed while asking: the server may or may not have granted it, so never hand this connection back without releasing
                await ReleaseAndCloseAsync(key);
                throw;
            }
            await _db.Database.CloseConnectionAsync();
            return false;
        }

        public async Task<bool> UnlockSchoolRunAsync(string schoolId)
        {
            if (!_lockHeld) return false;
            _lockHeld = false;
            return await ReleaseAndCloseAsync(LockKey(schoolId));
        }

        private static string LockKey(string schoolId) => "fee-reminder:" + schoolId;

        /// <summary>
        /// Unlock on the same connection (no cancellation token: a cancelled run must still release), then close it. If the unlock fails or the connection is
        /// broken, fall back to pg_advisory_unlock_all, and if even that fails, discard the whole pool entry so a still-locked connection can never be reused.
        /// </summary>
        private async Task<bool> ReleaseAndCloseAsync(string key)
        {
            var released = false;
            try
            {
                released = (await _db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock(hashtextextended({key}, 0)) AS \"Value\"").ToListAsync()).FirstOrDefault();
            }
            catch
            {
                try { await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock_all()"); released = true; }
                catch
                {
                    if (_db.Database.GetDbConnection() is Npgsql.NpgsqlConnection n) Npgsql.NpgsqlConnection.ClearPool(n);
                }
            }
            finally
            {
                try { await _db.Database.CloseConnectionAsync(); } catch { /* the connection is already gone */ }
            }
            return released;
        }

        public Task<long?> GetCurrentAcademicYearIdAsync(string schoolId, CancellationToken ct) =>
            _db.AcademicYears.AsNoTracking().Where(y => y.SchoolId == schoolId && y.IsCurrent).OrderByDescending(y => y.StartDate)
                .Select(y => (long?)y.AcademicYearId).FirstOrDefaultAsync(ct);

        public async Task<List<EligibleStudent>> GetEligibleStudentsAsync(string schoolId, long academicYearId, string studentUserType, CancellationToken ct)
        {
            var activeId = await ActiveStatusIdAsync(ct);
            if (activeId == null) return new();
            var rows = await (
                from s in _db.Students.AsNoTracking()
                join e in _db.StudentEnrollments.AsNoTracking().LiveOutcome(_db.AcademicYears) on s.StudentId equals e.StudentId
                join u in _db.Users.AsNoTracking() on s.StudentId equals u.OrgUserId
                where s.SchoolId == schoolId && e.SchoolId == schoolId && u.SchoolId == schoolId
                      && s.StatusId == activeId && e.StatusId == activeId && e.AcademicYearId == academicYearId
                      && u.UserType == studentUserType && u.StatusId == activeId
                select new { s.StudentId, u.UserId }).Distinct().ToListAsync(ct);
            return rows.Select(r => new EligibleStudent(r.StudentId, r.UserId)).OrderBy(r => r.StudentId, StringComparer.Ordinal).ToList();
        }

        public async Task<int> CreateLogRowsAsync(string schoolId, DateOnly date, string type, string title, string body, IReadOnlyCollection<EligibleStudent> students, CancellationToken ct)
        {
            if (students.Count == 0) return 0;
            var studentIds = students.Select(s => s.StudentId).ToArray();
            var userIds = students.Select(s => s.UserId).ToArray();
            return await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO public.tb_push_notification_log (school_id, user_id, student_id, notification_type, notify_date, title, body, status)
                SELECT {schoolId}, x.user_id, x.student_id, {type}, {date}, {title}, {body}, 'PENDING'
                FROM unnest({studentIds}::varchar[], {userIds}::bigint[]) AS x(student_id, user_id)
                ON CONFLICT (student_id, notification_type, notify_date) DO NOTHING", ct);
        }

        public Task<List<TbPushNotificationLog>> GetOpenLogRowsAsync(string schoolId, DateOnly date, string type, short maxAttempts, bool includeDryRun, CancellationToken ct) =>
            _db.PushNotificationLog
                .Where(l => l.SchoolId == schoolId && l.NotifyDate == date && l.NotificationType == type
                            && (l.Status == "PENDING" || (l.Status == "FAILED" && l.AttemptCount < maxAttempts) || (includeDryRun && l.Status == "DRY_RUN")))
                .OrderBy(l => l.NotificationId).ToListAsync(ct);

        // ---- admin reads ----
        public Task<bool> StudentBelongsToSchoolAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Students.AsNoTracking().AnyAsync(s => s.SchoolId == schoolId && s.StudentId == studentId, ct);

        public Task<List<TbPushNotificationLog>> GetLogAsync(string schoolId, string? studentId, DateOnly from, DateOnly to, int take, CancellationToken ct)
        {
            var q = _db.PushNotificationLog.AsNoTracking().Where(l => l.SchoolId == schoolId && l.NotifyDate >= from && l.NotifyDate <= to);
            if (studentId != null) q = q.Where(l => l.StudentId == studentId);
            return q.OrderByDescending(l => l.NotifyDate).ThenBy(l => l.StudentId).Take(take).ToListAsync(ct);
        }

        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
        public void ClearTracking() => _db.ChangeTracker.Clear();

        private Task<int?> ActiveStatusIdAsync(CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == Active && s.Stype == GeneralStatusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);
    }
}
