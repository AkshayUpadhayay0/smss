using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    internal static class TransportSearch
    {
        /// <summary>An ILIKE pattern that matches the text anywhere, with the LIKE wildcards in the text escaped.</summary>
        public static string Like(string search) => "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
    }

    public class TransportAreaRepository : ITransportAreaRepository
    {
        private readonly dbContext _db;
        public TransportAreaRepository(dbContext db) => _db = db;

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<(List<AreaRow> Rows, int Total)> ListAsync(string schoolId, string? search, int? statusId, int page, int pageSize, CancellationToken ct)
        {
            var q = _db.TransportAreas.AsNoTracking().Where(a => a.SchoolId == schoolId);
            if (statusId != null) q = q.Where(a => a.StatusId == statusId);
            if (!string.IsNullOrWhiteSpace(search)) { var like = TransportSearch.Like(search); q = q.Where(a => EF.Functions.ILike(a.AreaName, like)); }

            var total = await q.CountAsync(ct);
            var rows = await (
                from a in q
                join st in _db.LutStatus.AsNoTracking() on a.StatusId equals st.Sid
                orderby a.AreaName, a.AreaId
                select new { a, StatusName = st.Sname }).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

            var counts = await ActiveDriverCountsAsync(schoolId, rows.Select(r => r.a.AreaId).ToList(), ct);
            return (rows.Select(r => new AreaRow(r.a, r.StatusName, counts.GetValueOrDefault(r.a.AreaId))).ToList(), total);
        }

        public async Task<AreaRow?> GetDetailAsync(string schoolId, long areaId, CancellationToken ct)
        {
            var r = await (
                from a in _db.TransportAreas.AsNoTracking()
                where a.SchoolId == schoolId && a.AreaId == areaId
                join st in _db.LutStatus.AsNoTracking() on a.StatusId equals st.Sid
                select new { a, StatusName = st.Sname }).FirstOrDefaultAsync(ct);
            if (r == null) return null;
            var counts = await ActiveDriverCountsAsync(schoolId, new List<long> { areaId }, ct);
            return new AreaRow(r.a, r.StatusName, counts.GetValueOrDefault(areaId));
        }

        // how many ACTIVE drivers have each area (the Active status is the one called "Active" in lut_status)
        private async Task<Dictionary<long, int>> ActiveDriverCountsAsync(string schoolId, List<long> areaIds, CancellationToken ct)
        {
            if (areaIds.Count == 0) return new();
            var activeId = await GetStatusIdByNameAsync("Active", "general status", ct);
            return await (from da in _db.DriverAreas.AsNoTracking()
                          join d in _db.Drivers.AsNoTracking() on new { da.SchoolId, da.DriverId } equals new { d.SchoolId, d.DriverId }
                          where da.SchoolId == schoolId && areaIds.Contains(da.AreaId) && d.StatusId == activeId
                          group da by da.AreaId into g
                          select new { AreaId = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.AreaId, x => x.N, ct);
        }

        public Task<TbTransportArea?> GetForUpdateAsync(string schoolId, long areaId, CancellationToken ct) =>
            _db.TransportAreas.Where(a => a.SchoolId == schoolId && a.AreaId == areaId).FirstOrDefaultAsync(ct);

        public async Task AddAsync(TbTransportArea area, CancellationToken ct) => await _db.TransportAreas.AddAsync(area, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public Task<bool> ActiveNameTakenAsync(string schoolId, string name, int activeStatusId, long? exceptAreaId, CancellationToken ct)
        {
            var lower = name.ToLower();
            return _db.TransportAreas.AnyAsync(a => a.SchoolId == schoolId && a.StatusId == activeStatusId && a.AreaName.ToLower() == lower && a.AreaId != exceptAreaId, ct);
        }

        public Task<List<DriverRef>> GetActiveDriversOfAreaAsync(string schoolId, long areaId, int activeStatusId, CancellationToken ct) =>
            (from da in _db.DriverAreas.AsNoTracking()
             join d in _db.Drivers.AsNoTracking() on new { da.SchoolId, da.DriverId } equals new { d.SchoolId, d.DriverId }
             where da.SchoolId == schoolId && da.AreaId == areaId && d.StatusId == activeStatusId
             orderby d.FullName, d.DriverId
             select new DriverRef(d.DriverId, d.FullName)).ToListAsync(ct);
    }

    public class VehicleRepository : IVehicleRepository
    {
        private readonly dbContext _db;
        public VehicleRepository(dbContext db) => _db = db;

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<(List<VehicleRow> Rows, int Total)> ListAsync(string schoolId, string? search, int? statusId, long? vehicleTypeId, int page, int pageSize, CancellationToken ct)
        {
            var q = _db.Vehicles.AsNoTracking().Where(v => v.SchoolId == schoolId);
            if (statusId != null) q = q.Where(v => v.StatusId == statusId);
            if (vehicleTypeId != null) q = q.Where(v => v.VehicleTypeId == vehicleTypeId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var like = TransportSearch.Like(search);
                // the plate is stored without spaces, so a search is compared with its spaces removed too
                var plateLike = TransportSearch.Like(string.Concat(search.Where(c => !char.IsWhiteSpace(c))));
                q = q.Where(v => EF.Functions.ILike(v.VehicleLabel, like) || EF.Functions.ILike(v.RegistrationNumber, plateLike));
            }

            var total = await q.CountAsync(ct);
            var rows = await Project(q).OrderBy(r => r.Vehicle.VehicleLabel).ThenBy(r => r.Vehicle.VehicleId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return (await WithCountsAsync(schoolId, rows, ct), total);
        }

        private IQueryable<Row> Project(IQueryable<TbVehicle> q) =>
            from v in q
            join t in _db.LutVehicleTypes.AsNoTracking() on v.VehicleTypeId equals t.VehicleTypeId
            join o in _db.LutVehicleOwnerships.AsNoTracking() on v.OwnershipId equals o.OwnershipId
            join st in _db.LutStatus.AsNoTracking() on v.StatusId equals st.Sid
            select new Row { Vehicle = v, TypeName = t.TypeName, OwnershipName = o.OwnershipName, OwnershipCode = o.OwnershipCode, StatusName = st.Sname };

        private sealed class Row { public TbVehicle Vehicle { get; set; } = null!; public string TypeName { get; set; } = null!; public string OwnershipName { get; set; } = null!; public string OwnershipCode { get; set; } = null!; public string StatusName { get; set; } = null!; }

        private async Task<List<VehicleRow>> WithCountsAsync(string schoolId, List<Row> rows, CancellationToken ct)
        {
            var ids = rows.Select(r => r.Vehicle.VehicleId).ToList();
            var activeId = await GetStatusIdByNameAsync("Active", "general status", ct);
            var counts = ids.Count == 0 ? new Dictionary<long, int>()
                : await _db.Drivers.AsNoTracking().Where(d => d.SchoolId == schoolId && d.StatusId == activeId && d.VehicleId != null && ids.Contains(d.VehicleId.Value))
                    .GroupBy(d => d.VehicleId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, ct);
            return rows.Select(r => new VehicleRow(r.Vehicle, r.TypeName, r.OwnershipName, r.OwnershipCode, r.StatusName, counts.GetValueOrDefault(r.Vehicle.VehicleId))).ToList();
        }

        public async Task<VehicleRow?> GetDetailAsync(string schoolId, long vehicleId, CancellationToken ct)
        {
            var row = await Project(_db.Vehicles.AsNoTracking().Where(v => v.SchoolId == schoolId && v.VehicleId == vehicleId)).FirstOrDefaultAsync(ct);
            return row == null ? null : (await WithCountsAsync(schoolId, new List<Row> { row }, ct))[0];
        }

        public Task<TbVehicle?> GetForUpdateAsync(string schoolId, long vehicleId, CancellationToken ct) =>
            _db.Vehicles.Where(v => v.SchoolId == schoolId && v.VehicleId == vehicleId).FirstOrDefaultAsync(ct);

        public async Task AddAsync(TbVehicle vehicle, CancellationToken ct) => await _db.Vehicles.AddAsync(vehicle, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public Task<bool> ActiveLabelTakenAsync(string schoolId, string label, int activeStatusId, long? exceptVehicleId, CancellationToken ct)
        {
            var lower = label.ToLower();
            return _db.Vehicles.AnyAsync(v => v.SchoolId == schoolId && v.StatusId == activeStatusId && v.VehicleLabel.ToLower() == lower && v.VehicleId != exceptVehicleId, ct);
        }

        public Task<bool> ActivePlateTakenAsync(string schoolId, string plate, int activeStatusId, long? exceptVehicleId, CancellationToken ct)
        {
            var upper = plate.ToUpper();
            return _db.Vehicles.AnyAsync(v => v.SchoolId == schoolId && v.StatusId == activeStatusId && v.RegistrationNumber.ToUpper() == upper && v.VehicleId != exceptVehicleId, ct);
        }

        public Task<List<TransportLookupRow>> GetTypesAsync(CancellationToken ct) =>
            _db.LutVehicleTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.VehicleTypeId).Select(t => new TransportLookupRow(t.VehicleTypeId, t.TypeCode, t.TypeName)).ToListAsync(ct);
        public Task<List<TransportLookupRow>> GetOwnershipsAsync(CancellationToken ct) =>
            _db.LutVehicleOwnerships.AsNoTracking().Where(o => o.IsActive).OrderBy(o => o.OwnershipId).Select(o => new TransportLookupRow(o.OwnershipId, o.OwnershipCode, o.OwnershipName)).ToListAsync(ct);
        public Task<TransportLookupRow?> GetActiveTypeAsync(long id, CancellationToken ct) =>
            _db.LutVehicleTypes.AsNoTracking().Where(t => t.VehicleTypeId == id && t.IsActive).Select(t => new TransportLookupRow(t.VehicleTypeId, t.TypeCode, t.TypeName)).FirstOrDefaultAsync(ct);
        public Task<TransportLookupRow?> GetActiveOwnershipAsync(long id, CancellationToken ct) =>
            _db.LutVehicleOwnerships.AsNoTracking().Where(o => o.OwnershipId == id && o.IsActive).Select(o => new TransportLookupRow(o.OwnershipId, o.OwnershipCode, o.OwnershipName)).FirstOrDefaultAsync(ct);

        public Task<List<DriverRef>> GetActiveDriversOfVehicleAsync(string schoolId, long vehicleId, int activeStatusId, CancellationToken ct) =>
            _db.Drivers.AsNoTracking().Where(d => d.SchoolId == schoolId && d.VehicleId == vehicleId && d.StatusId == activeStatusId)
                .OrderBy(d => d.FullName).ThenBy(d => d.DriverId).Select(d => new DriverRef(d.DriverId, d.FullName)).ToListAsync(ct);
    }

    public class DriverRepository : IDriverRepository
    {
        private readonly dbContext _db;
        public DriverRepository(dbContext db) => _db = db;

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public async Task LockDriverIdsAsync(string schoolId, CancellationToken ct)
        {
            var key = "driver-id:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public async Task<int> GetMaxIdSequenceAsync(string schoolId, string prefix, CancellationToken ct)
        {
            // Ids are prefix + digits; the highest number is the longest then the largest string
            var last = await _db.Drivers.AsNoTracking()
                .Where(d => d.SchoolId == schoolId && d.DriverId.StartsWith(prefix))
                .OrderByDescending(d => d.DriverId.Length).ThenByDescending(d => d.DriverId)
                .Select(d => d.DriverId).FirstOrDefaultAsync(ct);
            return last != null && int.TryParse(last[prefix.Length..], out var n) ? n : 0;
        }

        public Task<TbSchools?> GetSchoolAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).FirstOrDefaultAsync(ct);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public async Task<(List<DriverListRow> Rows, int Total)> ListAsync(string schoolId, string? search, int? statusId, long? vehicleId, int page, int pageSize, CancellationToken ct)
        {
            var q = _db.Drivers.AsNoTracking().Where(d => d.SchoolId == schoolId);
            if (statusId != null) q = q.Where(d => d.StatusId == statusId);
            if (vehicleId != null) q = q.Where(d => d.VehicleId == vehicleId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var like = TransportSearch.Like(search);
                q = q.Where(d => EF.Functions.ILike(d.DriverId, like) || EF.Functions.ILike(d.FullName, like) || EF.Functions.ILike(d.MobileNumber, like) || EF.Functions.ILike(d.DlNumber, like));
            }

            var total = await q.CountAsync(ct);
            var rows = await (
                from d in q
                join st in _db.LutStatus.AsNoTracking() on d.StatusId equals st.Sid
                orderby d.FullName, d.DriverId
                select new
                {
                    d, StatusName = st.Sname,
                    VehicleLabel = _db.Vehicles.Where(v => v.SchoolId == d.SchoolId && v.VehicleId == d.VehicleId).Select(v => v.VehicleLabel).FirstOrDefault(),
                    VehiclePlate = _db.Vehicles.Where(v => v.SchoolId == d.SchoolId && v.VehicleId == d.VehicleId).Select(v => v.RegistrationNumber).FirstOrDefault()
                }).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

            var ids = rows.Select(r => r.d.DriverId).ToList();
            var withLogin = ids.Count == 0 ? new HashSet<string>()
                : (await _db.Users.AsNoTracking().Where(u => u.SchoolId == schoolId && u.OrgUserId != null && ids.Contains(u.OrgUserId)).Select(u => u.OrgUserId!).ToListAsync(ct)).ToHashSet();
            var areaNames = ids.Count == 0 ? new Dictionary<string, List<string>>()
                : (await (from da in _db.DriverAreas.AsNoTracking()
                          join a in _db.TransportAreas.AsNoTracking() on new { da.SchoolId, da.AreaId } equals new { a.SchoolId, a.AreaId }
                          where da.SchoolId == schoolId && ids.Contains(da.DriverId)
                          orderby a.AreaName
                          select new { da.DriverId, a.AreaName }).ToListAsync(ct)).GroupBy(x => x.DriverId).ToDictionary(g => g.Key, g => g.Select(x => x.AreaName).ToList());

            return (rows.Select(r => new DriverListRow(r.d, r.StatusName, r.VehicleLabel, r.VehiclePlate, withLogin.Contains(r.d.DriverId),
                areaNames.GetValueOrDefault(r.d.DriverId) ?? new List<string>())).ToList(), total);
        }

        public async Task<DriverDetailRow?> GetDetailAsync(string schoolId, string driverId, CancellationToken ct)
        {
            var r = await (
                from d in _db.Drivers.AsNoTracking()
                where d.SchoolId == schoolId && d.DriverId == driverId
                join st in _db.LutStatus.AsNoTracking() on d.StatusId equals st.Sid
                select new
                {
                    d, StatusName = st.Sname,
                    VehicleLabel = _db.Vehicles.Where(v => v.SchoolId == d.SchoolId && v.VehicleId == d.VehicleId).Select(v => v.VehicleLabel).FirstOrDefault(),
                    VehiclePlate = _db.Vehicles.Where(v => v.SchoolId == d.SchoolId && v.VehicleId == d.VehicleId).Select(v => v.RegistrationNumber).FirstOrDefault()
                }).FirstOrDefaultAsync(ct);
            if (r == null) return null;
            var loginId = await _db.Users.AsNoTracking().Where(u => u.SchoolId == schoolId && u.OrgUserId == driverId).Select(u => u.OrgUserId).FirstOrDefaultAsync(ct);
            return new DriverDetailRow(r.d, r.StatusName, r.VehicleLabel, r.VehiclePlate, loginId, await GetAssignedAreasAsync(schoolId, driverId, ct));
        }

        public Task<TbDriver?> GetForUpdateAsync(string schoolId, string driverId, CancellationToken ct) =>
            _db.Drivers.Where(d => d.SchoolId == schoolId && d.DriverId == driverId).FirstOrDefaultAsync(ct);

        public async Task AddAsync(TbDriver driver, CancellationToken ct) => await _db.Drivers.AddAsync(driver, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        public Task<bool> EmailTakenAsync(string schoolId, string email, string? exceptDriverId, CancellationToken ct)
        {
            var lower = email.ToLower();
            return _db.Drivers.AnyAsync(d => d.SchoolId == schoolId && d.Email != null && d.Email.ToLower() == lower && d.DriverId != exceptDriverId, ct);
        }

        public Task<bool> ActiveDlTakenAsync(string schoolId, string dlNumber, int activeStatusId, string? exceptDriverId, CancellationToken ct)
        {
            var upper = dlNumber.ToUpper();
            return _db.Drivers.AnyAsync(d => d.SchoolId == schoolId && d.StatusId == activeStatusId && d.DlNumber.ToUpper() == upper && d.DriverId != exceptDriverId, ct);
        }

        public Task<VehicleRef?> GetVehicleAsync(string schoolId, long vehicleId, CancellationToken ct) =>
            _db.Vehicles.AsNoTracking().Where(v => v.SchoolId == schoolId && v.VehicleId == vehicleId)
                .Select(v => new VehicleRef(v.VehicleId, v.VehicleLabel, v.RegistrationNumber, v.StatusId)).FirstOrDefaultAsync(ct);

        public Task<List<AreaRef>> GetAreasAsync(string schoolId, IReadOnlyCollection<long> areaIds, CancellationToken ct)
        {
            var ids = areaIds.ToArray();
            return _db.TransportAreas.AsNoTracking().Where(a => a.SchoolId == schoolId && ids.Contains(a.AreaId)).Select(a => new AreaRef(a.AreaId, a.AreaName, a.StatusId)).ToListAsync(ct);
        }

        public Task<List<AreaRef>> GetAssignedAreasAsync(string schoolId, string driverId, CancellationToken ct) =>
            (from da in _db.DriverAreas.AsNoTracking()
             join a in _db.TransportAreas.AsNoTracking() on new { da.SchoolId, da.AreaId } equals new { a.SchoolId, a.AreaId }
             where da.SchoolId == schoolId && da.DriverId == driverId
             orderby a.AreaName
             select new AreaRef(a.AreaId, a.AreaName, a.StatusId)).ToListAsync(ct);

        public async Task ReplaceAreasAsync(string schoolId, string driverId, IReadOnlyCollection<long> areaIds, CancellationToken ct)
        {
            var wanted = areaIds.ToHashSet();
            var existing = await _db.DriverAreas.Where(x => x.SchoolId == schoolId && x.DriverId == driverId).ToListAsync(ct);
            _db.DriverAreas.RemoveRange(existing.Where(x => !wanted.Contains(x.AreaId)));   // hard delete: a mapping set, not a business record
            var have = existing.Select(x => x.AreaId).ToHashSet();
            var now = DateTime.UtcNow;
            foreach (var id in wanted.Where(id => !have.Contains(id)))
                await _db.DriverAreas.AddAsync(new TbDriverArea { SchoolId = schoolId, DriverId = driverId, AreaId = id, CreatedAt = now }, ct);
        }

        public Task<List<VehicleRef>> GetActiveVehiclesAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.Vehicles.AsNoTracking().Where(v => v.SchoolId == schoolId && v.StatusId == activeStatusId).OrderBy(v => v.VehicleLabel)
                .Select(v => new VehicleRef(v.VehicleId, v.VehicleLabel, v.RegistrationNumber, v.StatusId)).ToListAsync(ct);

        public Task<List<AreaRef>> GetActiveAreasAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.TransportAreas.AsNoTracking().Where(a => a.SchoolId == schoolId && a.StatusId == activeStatusId).OrderBy(a => a.AreaName)
                .Select(a => new AreaRef(a.AreaId, a.AreaName, a.StatusId)).ToListAsync(ct);

        public Task<bool> HasLoginAsync(string schoolId, string driverId, CancellationToken ct) =>
            _db.Users.AnyAsync(u => u.SchoolId == schoolId && u.OrgUserId == driverId, ct);
        public Task<TbUsers?> GetLoginUserAsync(string schoolId, string driverId, CancellationToken ct) =>
            _db.Users.Where(u => u.SchoolId == schoolId && u.OrgUserId == driverId).FirstOrDefaultAsync(ct);
        public Task<LutRole?> GetRoleByCodeAsync(string roleCode, CancellationToken ct) =>
            _db.LutRoles.AsNoTracking().Where(r => r.RoleCode == roleCode).FirstOrDefaultAsync(ct);
        public async Task AddUserAsync(TbUsers user, CancellationToken ct)
        {
            await _db.Users.AddAsync(user, ct);
            await _db.SaveChangesAsync(ct);
        }

        public Task RevokeRefreshTokensAsync(long userId, DateTime revokedAt, CancellationToken ct) =>
            _db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), ct);

        public async Task<(bool Exists, string? Stored)> GetPhotoKeyAsync(string schoolId, string driverId, CancellationToken ct)
        {
            var row = await _db.Drivers.AsNoTracking().Where(d => d.SchoolId == schoolId && d.DriverId == driverId).Select(d => new { d.PhotoPath }).FirstOrDefaultAsync(ct);
            return (row != null, row?.PhotoPath);
        }
    }
}
