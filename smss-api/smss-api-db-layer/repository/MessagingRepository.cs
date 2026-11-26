using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class ActionTokenRow
    {
        public long TokenId { get; set; }
        public long UserId { get; set; }
        public string? SchoolId { get; set; }
        public string Purpose { get; set; } = null!;
        public DateTime ExpiresAt { get; set; }
        public DateTime? UsedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public DateTime DbNow { get; set; }
        public ActionTokenInfo ToInfo() => new(TokenId, UserId, SchoolId, Purpose, ExpiresAt, UsedAt, RevokedAt, DbNow);
    }

    public class MessagingRepository : IMessagingRepository
    {
        private readonly dbContext _db;
        public MessagingRepository(dbContext db) => _db = db;

        private static NpgsqlParameter P(string n, NpgsqlDbType t, object? v) => new(n, t) { Value = v ?? DBNull.Value };

        private const string TokenColumns = @"token_id AS ""TokenId"", user_id AS ""UserId"", school_id AS ""SchoolId"", purpose AS ""Purpose"", expires_at AS ""ExpiresAt"", used_at AS ""UsedAt"", revoked_at AS ""RevokedAt"", now() AS ""DbNow""";

        // ------------------------------------------------------------------------------------------------ one-time links
        public async Task<long> IssueTokenAsync(long userId, string? schoolId, string purpose, string tokenHash, TimeSpan lifetime, long? createdBy, CancellationToken ct)
        {
            // one statement: the user's older unused links of this purpose are revoked and the new one is stored, so there is never a moment with two usable links
            var ids = await _db.Database.SqlQueryRaw<long>(@"
WITH revoked AS (
    UPDATE public.tb_user_action_tokens SET revoked_at = now()
     WHERE user_id = @user AND purpose = @purpose AND used_at IS NULL AND revoked_at IS NULL
    RETURNING token_id)
INSERT INTO public.tb_user_action_tokens (user_id, school_id, purpose, token_hash, created_at, expires_at, created_by)
SELECT @user, @school, @purpose, @hash, now(), now() + make_interval(secs => @secs), @by
RETURNING token_id AS ""Value""",
                P("user", NpgsqlDbType.Bigint, userId), P("school", NpgsqlDbType.Varchar, schoolId), P("purpose", NpgsqlDbType.Varchar, purpose), P("hash", NpgsqlDbType.Char, tokenHash),
                P("secs", NpgsqlDbType.Double, lifetime.TotalSeconds), P("by", NpgsqlDbType.Bigint, createdBy)).ToListAsync(ct);
            return ids.Single();
        }

        public async Task<ActionTokenInfo?> ConsumeTokenAsync(string tokenHash, string purpose, CancellationToken ct)
        {
            var rows = await _db.Database.SqlQueryRaw<ActionTokenRow>($@"
UPDATE public.tb_user_action_tokens SET used_at = now()
 WHERE token_hash = @hash AND purpose = @purpose AND used_at IS NULL AND revoked_at IS NULL AND expires_at > now()
RETURNING {TokenColumns}",
                P("hash", NpgsqlDbType.Char, tokenHash), P("purpose", NpgsqlDbType.Varchar, purpose)).ToListAsync(ct);
            return rows.SingleOrDefault()?.ToInfo();
        }

        public async Task<ActionTokenInfo?> PeekTokenAsync(string tokenHash, CancellationToken ct)
        {
            var rows = await _db.Database.SqlQueryRaw<ActionTokenRow>($"SELECT {TokenColumns} FROM public.tb_user_action_tokens WHERE token_hash = @hash", P("hash", NpgsqlDbType.Char, tokenHash)).ToListAsync(ct);
            return rows.SingleOrDefault()?.ToInfo();
        }

        public Task<int> RevokeTokensAsync(long userId, string? purpose, CancellationToken ct) =>
            _db.Database.ExecuteSqlRawAsync(@"
UPDATE public.tb_user_action_tokens SET revoked_at = now()
 WHERE user_id = @user AND used_at IS NULL AND revoked_at IS NULL AND (@purpose::varchar IS NULL OR purpose = @purpose)",
                new object[] { P("user", NpgsqlDbType.Bigint, userId), P("purpose", NpgsqlDbType.Varchar, purpose) }, ct);

        public async Task<int> CountTokensSinceAsync(long userId, string purpose, DateTime sinceUtc, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<int>(@"SELECT count(*)::int AS ""Value"" FROM public.tb_user_action_tokens WHERE user_id = @user AND purpose = @purpose AND created_at >= @since",
                P("user", NpgsqlDbType.Bigint, userId), P("purpose", NpgsqlDbType.Varchar, purpose), P("since", NpgsqlDbType.TimestampTz, sinceUtc)).ToListAsync(ct)).Single();

        // ------------------------------------------------------------------------------------------------ the delivery log
        public async Task<long> AddDeliveryAsync(TbMessageDelivery row, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            row.CreatedAt = now; row.UpdatedAt = now;
            _db.MessageDeliveries.Add(row);
            try { await _db.SaveChangesAsync(ct); }
            finally { _db.Entry(row).State = EntityState.Detached; }
            return row.DeliveryId;
        }

        public async Task<bool> ApplyProviderStatusAsync(string providerMessageId, string status, string? errorCode, string? errorMessage, CancellationToken ct)
        {
            var known = (await _db.Database.SqlQueryRaw<bool>(@"SELECT EXISTS (SELECT 1 FROM public.tb_message_deliveries WHERE provider_message_id = @id) AS ""Value""", P("id", NpgsqlDbType.Varchar, providerMessageId)).ToListAsync(ct)).Single();
            if (!known) return false;
            // forward only: SENT < DELIVERED < READ; FAILED only replaces SENT (a delivered message cannot fail afterwards)
            await _db.Database.ExecuteSqlRawAsync(@"
UPDATE public.tb_message_deliveries SET
       status = @status, updated_at = now(),
       delivered_at = CASE WHEN @status IN ('DELIVERED', 'READ') AND delivered_at IS NULL THEN now() ELSE delivered_at END,
       error_code = CASE WHEN @status = 'FAILED' THEN COALESCE(@code, 'PROVIDER_FAILED') ELSE error_code END,
       error_message = CASE WHEN @status = 'FAILED' THEN @message ELSE error_message END
 WHERE provider_message_id = @id
   AND ((@status = 'DELIVERED' AND status = 'SENT')
     OR (@status = 'READ' AND status IN ('SENT', 'DELIVERED'))
     OR (@status = 'FAILED' AND status = 'SENT'))",
                new object[] { P("id", NpgsqlDbType.Varchar, providerMessageId), P("status", NpgsqlDbType.Varchar, status), P("code", NpgsqlDbType.Varchar, errorCode), P("message", NpgsqlDbType.Varchar, errorMessage) }, ct);
            return true;
        }

        public async Task<int> CountDeliveriesSinceAsync(string schoolId, long guardianId, string purpose, DateTime sinceUtc, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<int>(@"
SELECT count(*)::int AS ""Value"" FROM public.tb_message_deliveries
 WHERE school_id = @school AND guardian_id = @guardian AND purpose = @purpose AND created_at >= @since",
                P("school", NpgsqlDbType.Varchar, schoolId), P("guardian", NpgsqlDbType.Bigint, guardianId), P("purpose", NpgsqlDbType.Varchar, purpose), P("since", NpgsqlDbType.TimestampTz, sinceUtc)).ToListAsync(ct)).Single();

        public Task<TbMessageDelivery?> GetDeliveryAsync(string schoolId, long deliveryId, CancellationToken ct) =>
            _db.MessageDeliveries.AsNoTracking().Where(d => d.SchoolId == schoolId && d.DeliveryId == deliveryId).FirstOrDefaultAsync(ct);

        // ------------------------------------------------------------------------------------------------ guardian login numbers
        public Task<string?> GetSchoolShortCodeAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => s.SchoolShortCode).FirstOrDefaultAsync(ct);

        public async Task<int> NextGuardianNumberAsync(string schoolId, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<int>(@"
INSERT INTO public.tb_guardian_login_counters (school_id, last_number, updated_at) VALUES (@school, 1, now())
ON CONFLICT (school_id) DO UPDATE SET last_number = public.tb_guardian_login_counters.last_number + 1, updated_at = now()
RETURNING last_number AS ""Value""", P("school", NpgsqlDbType.Varchar, schoolId)).ToListAsync(ct)).Single();

        public async Task<bool> LoginIdTakenAsync(string candidate, CancellationToken ct) =>
            (await _db.Database.SqlQueryRaw<bool>(@"
SELECT (EXISTS (SELECT 1 FROM public.tb_users WHERE lower(org_user_id) = lower(@c)) OR EXISTS (SELECT 1 FROM public.tb_students WHERE lower(student_id) = lower(@c))) AS ""Value""",
                P("c", NpgsqlDbType.Varchar, candidate)).ToListAsync(ct)).Single();
    }
}
