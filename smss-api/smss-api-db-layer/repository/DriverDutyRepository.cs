using Microsoft.EntityFrameworkCore;
using Npgsql;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class DriverDutyRepository : IDriverDutyRepository
    {
        private const string OpenSessionIndex = "uq_tb_driver_duty_one_open_per_driver";
        private readonly dbContext _db;
        public DriverDutyRepository(dbContext db) => _db = db;

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<DutyDriver?> GetDutyDriverAsync(string schoolId, long userId, CancellationToken ct)
        {
            // the login's org_user_id IS the driver id; the school must match on BOTH rows
            var row = await (
                from u in _db.Users.AsNoTracking()
                where u.UserId == userId && u.UserType == "Driver" && u.SchoolId == schoolId
                join d in _db.Drivers.AsNoTracking() on new { S = u.SchoolId!, Id = u.OrgUserId! } equals new { S = d.SchoolId, Id = d.DriverId }
                select new { u.UserId, d.SchoolId, d.DriverId, d.FullName, d.StatusId, d.VehicleId }).FirstOrDefaultAsync(ct);
            if (row == null) return null;

            string? label = null, plate = null;
            int? vehicleStatus = null;
            if (row.VehicleId != null)
            {
                var v = await _db.Vehicles.AsNoTracking().Where(x => x.SchoolId == schoolId && x.VehicleId == row.VehicleId)
                    .Select(x => new { x.VehicleLabel, x.RegistrationNumber, x.StatusId }).FirstOrDefaultAsync(ct);
                if (v != null) { label = v.VehicleLabel; plate = v.RegistrationNumber; vehicleStatus = v.StatusId; }
            }
            return new DutyDriver(row.UserId, row.SchoolId, row.DriverId, row.FullName, row.StatusId, row.VehicleId, label, plate, vehicleStatus);
        }

        public Task<List<AreaRef>> GetAssignedAreasAsync(string schoolId, string driverId, CancellationToken ct) =>
            (from da in _db.DriverAreas.AsNoTracking()
             where da.SchoolId == schoolId && da.DriverId == driverId
             join a in _db.TransportAreas.AsNoTracking() on new { da.SchoolId, da.AreaId } equals new { a.SchoolId, a.AreaId }
             orderby a.AreaName
             select new AreaRef(a.AreaId, a.AreaName, a.StatusId)).ToListAsync(ct);

        public Task<TbDriverDutySession?> GetOpenSessionAsync(string schoolId, string driverId, CancellationToken ct) =>
            _db.DriverDutySessions.AsNoTracking().Where(s => s.SchoolId == schoolId && s.DriverId == driverId && s.EndedAt == null).FirstOrDefaultAsync(ct);

        public Task<TbDriverLiveLocation?> GetLiveAsync(string driverId, CancellationToken ct) =>
            _db.DriverLiveLocations.AsNoTracking().Where(l => l.DriverId == driverId).FirstOrDefaultAsync(ct);

        public async Task<DutySessionContext?> GetSessionByTokenHashAsync(string tokenHash, CancellationToken ct)
        {
            var r = await (
                from s in _db.DriverDutySessions.AsNoTracking()
                where s.TrackingTokenHash == tokenHash
                join d in _db.Drivers.AsNoTracking() on new { s.SchoolId, s.DriverId } equals new { d.SchoolId, d.DriverId }
                select new { s, d.FullName, d.StatusId }).FirstOrDefaultAsync(ct);
            return r == null ? null : new DutySessionContext(r.s, r.FullName, r.StatusId);
        }

        public async Task<TbDriverDutySession?> StartSessionAsync(string schoolId, string driverId, long vehicleId, string tokenHash, long? staleSessionId, DateTime now, CancellationToken ct)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                if (staleSessionId != null)
                    await _db.DriverDutySessions.Where(s => s.SessionId == staleSessionId && s.EndedAt == null)
                        .ExecuteUpdateAsync(u => u.SetProperty(s => s.EndedAt, now).SetProperty(s => s.EndReason, "AUTO_TIMEOUT"), ct);
                await _db.DriverLiveLocations.Where(l => l.DriverId == driverId).ExecuteDeleteAsync(ct);

                var session = new TbDriverDutySession { SchoolId = schoolId, DriverId = driverId, VehicleId = vehicleId, StartedAt = now, TrackingTokenHash = tokenHash, CreatedAt = now };
                _db.DriverDutySessions.Add(session);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return session;
            }
            catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.ConstraintName == OpenSessionIndex)
            {
                await tx.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                return null;
            }
        }

        public async Task<bool> ResumeSessionAsync(long sessionId, string newTokenHash, CancellationToken ct)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var updated = await _db.DriverDutySessions.Where(s => s.SessionId == sessionId && s.EndedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.TrackingTokenHash, newTokenHash), ct);
            if (updated == 0) { await tx.RollbackAsync(ct); return false; }
            await _db.DriverLiveLocations.Where(l => l.SessionId == sessionId).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        public async Task<bool> EndSessionAsync(long sessionId, string reason, DateTime now, CancellationToken ct)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var ended = await _db.DriverDutySessions.Where(s => s.SessionId == sessionId && s.EndedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.EndedAt, now).SetProperty(s => s.EndReason, reason), ct);
            await _db.DriverLiveLocations.Where(l => l.SessionId == sessionId).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
            return ended > 0;
        }

        public Task<int> UpsertLocationAsync(string schoolId, string driverId, long sessionId, long vehicleId, LocationFix f, CancellationToken ct) =>
            _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO public.tb_driver_live_location AS live (driver_id, school_id, session_id, vehicle_id, latitude, longitude, accuracy_m, speed_mps, heading_deg, recorded_at, received_at)
VALUES ({driverId}, {schoolId}, {sessionId}, {vehicleId}, {f.Latitude}, {f.Longitude}, {f.AccuracyM}, {f.SpeedMps}, {f.HeadingDeg}, {f.RecordedAt}, {f.ReceivedAt})
ON CONFLICT (driver_id) DO UPDATE SET
    vehicle_id = EXCLUDED.vehicle_id, latitude = EXCLUDED.latitude, longitude = EXCLUDED.longitude,
    accuracy_m = EXCLUDED.accuracy_m, speed_mps = EXCLUDED.speed_mps, heading_deg = EXCLUDED.heading_deg,
    recorded_at = EXCLUDED.recorded_at, received_at = EXCLUDED.received_at
WHERE live.session_id = EXCLUDED.session_id AND live.recorded_at < EXCLUDED.recorded_at", ct);

        public async Task<List<LiveRow>> ListOpenAsync(string schoolId, long? areaId, long? vehicleId, int activeStatusId, CancellationToken ct)
        {
            var q = from s in _db.DriverDutySessions.AsNoTracking()
                    where s.SchoolId == schoolId && s.EndedAt == null
                    join d in _db.Drivers.AsNoTracking() on new { s.SchoolId, s.DriverId } equals new { d.SchoolId, d.DriverId }
                    where d.StatusId == activeStatusId
                    select new { s, d };
            if (vehicleId != null) q = q.Where(x => x.s.VehicleId == vehicleId);
            if (areaId != null) q = q.Where(x => _db.DriverAreas.Any(da => da.SchoolId == schoolId && da.DriverId == x.d.DriverId && da.AreaId == areaId));

            var rows = await (from x in q
                              join v in _db.Vehicles.AsNoTracking() on new { x.s.SchoolId, x.s.VehicleId } equals new { v.SchoolId, v.VehicleId }
                              orderby x.d.FullName, x.d.DriverId
                              select new { x.d.DriverId, x.d.FullName, v.VehicleId, v.VehicleLabel, v.RegistrationNumber, x.s.SessionId, x.s.StartedAt }).ToListAsync(ct);
            if (rows.Count == 0) return new();

            var sessionIds = rows.Select(r => r.SessionId).ToList();
            var fixes = await _db.DriverLiveLocations.AsNoTracking().Where(l => l.SchoolId == schoolId && sessionIds.Contains(l.SessionId)).ToDictionaryAsync(l => l.SessionId, ct);
            return rows.Select(r =>
            {
                fixes.TryGetValue(r.SessionId, out var l);
                return new LiveRow(r.DriverId, r.FullName, r.VehicleId, r.VehicleLabel, r.RegistrationNumber, r.SessionId, r.StartedAt,
                    l?.Latitude, l?.Longitude, l?.AccuracyM, l?.SpeedMps, l?.HeadingDeg, l?.ReceivedAt);
            }).ToList();
        }

        public async Task<Dictionary<string, List<string>>> GetAreaNamesAsync(string schoolId, IReadOnlyCollection<string> driverIds, CancellationToken ct)
        {
            if (driverIds.Count == 0) return new();
            var rows = await (from da in _db.DriverAreas.AsNoTracking()
                              where da.SchoolId == schoolId && driverIds.Contains(da.DriverId)
                              join a in _db.TransportAreas.AsNoTracking() on new { da.SchoolId, da.AreaId } equals new { a.SchoolId, a.AreaId }
                              orderby a.AreaName
                              select new { da.DriverId, a.AreaName }).ToListAsync(ct);
            return rows.GroupBy(r => r.DriverId).ToDictionary(g => g.Key, g => g.Select(r => r.AreaName).ToList());
        }
    }
}
