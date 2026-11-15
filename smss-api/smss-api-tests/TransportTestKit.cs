using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.@interface;

namespace smss_api_tests
{
    /// <summary>
    /// An in-memory stand-in for the Transport tables and the few others the services touch. The fake repositories below filter by school exactly like the real ones
    /// (every query is school-scoped), so the service tests prove tenant isolation, not just that a method was called. What they cannot prove is the database itself
    /// (partial unique indexes, composite foreign keys): that is what the SQL script's constraint checks and the live smoke test are for.
    /// </summary>
    public class TransportStore
    {
        public const int Active = 1, Inactive = 2;
        public const string SchoolA = "SCHA", SchoolB = "SCHB";

        public List<TbSchools> Schools = new()
        {
            new() { SchoolId = SchoolA, SchoolCode = "SCH-A", SchoolName = "School A", SchoolShortCode = "DPS001", SchoolStatusId = Active },
            new() { SchoolId = SchoolB, SchoolCode = "SCH-B", SchoolName = "School B", SchoolShortCode = "XYZ002", SchoolStatusId = Active },
        };
        public List<TbTransportArea> Areas = new();
        public List<TbVehicle> Vehicles = new();
        public List<TbDriver> Drivers = new();
        public List<TbDriverArea> Links = new();
        public List<TbUsers> Users = new();
        public List<TbRefreshTokens> Tokens = new();
        public List<LutRole> Roles = new() { new() { RoleId = 11, RoleName = "Driver", RoleCode = "DRIVER", StatusId = Active } };
        public List<LutVehicleType> Types = new() { new() { VehicleTypeId = 1, TypeCode = "BUS", TypeName = "Bus", IsActive = true }, new() { VehicleTypeId = 2, TypeCode = "VAN", TypeName = "Van", IsActive = true } };
        public List<LutVehicleOwnership> Ownerships = new() { new() { OwnershipId = 1, OwnershipCode = "OWN", OwnershipName = "Own", IsActive = true }, new() { OwnershipId = 2, OwnershipCode = "RENTED", OwnershipName = "Rented", IsActive = true } };
        private long _area = 1, _vehicle = 1, _link = 1, _user = 100;
        public long NextArea() => _area++;
        public long NextVehicle() => _vehicle++;
        public long NextLink() => _link++;
        public long NextUser() => _user++;
        public static string StatusName(int id) => id == Active ? "Active" : "Inactive";

        public TbTransportArea AddArea(string school, string name, int status = Active, decimal fee = 500) { var a = new TbTransportArea { AreaId = NextArea(), SchoolId = school, AreaName = name, TransportFee = fee, StatusId = status }; Areas.Add(a); return a; }
        public TbVehicle AddVehicle(string school, string label, string plate, int status = Active) { var v = new TbVehicle { VehicleId = NextVehicle(), SchoolId = school, VehicleLabel = label, RegistrationNumber = plate, VehicleTypeId = 1, OwnershipId = 1, StatusId = status }; Vehicles.Add(v); return v; }
        public TbDriver AddDriver(string school, string id, string name, int status = Active, long? vehicleId = null, string dl = "DL12345", string? email = null)
        {
            var d = new TbDriver { DriverId = id, SchoolId = school, FullName = name, MobileNumber = "9876543210", Email = email, Address = "addr", DlNumber = dl, DlValidUpto = new DateOnly(2035, 1, 1), VehicleId = vehicleId, StatusId = status };
            Drivers.Add(d); return d;
        }
        public void Link(string school, string driverId, long areaId, string? pickup = null, string? drop = null) =>
            Links.Add(new TbDriverArea { DriverAreaId = NextLink(), SchoolId = school, DriverId = driverId, AreaId = areaId, MorningPickupTime = T(pickup), AfternoonDropTime = T(drop) });
        private static TimeOnly? T(string? hhmm) => hhmm == null ? null : TimeOnly.ParseExact(hhmm, "HH:mm");
    }

    internal sealed class FakeTx : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public class FakeAreaRepo : ITransportAreaRepository
    {
        private readonly TransportStore _s;
        public FakeAreaRepo(TransportStore s) => _s = s;
        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) => Task.FromResult<int?>(statusName == "Active" ? TransportStore.Active : statusName == "Inactive" ? TransportStore.Inactive : null);
        private int Count(string school, long areaId) => _s.Links.Count(l => l.SchoolId == school && l.AreaId == areaId && _s.Drivers.Any(d => d.SchoolId == school && d.DriverId == l.DriverId && d.StatusId == TransportStore.Active));
        private AreaRow Row(TbTransportArea a) => new(a, TransportStore.StatusName(a.StatusId), Count(a.SchoolId, a.AreaId));
        public Task<(List<AreaRow> Rows, int Total)> ListAsync(string schoolId, string? search, int? statusId, int page, int pageSize, CancellationToken ct)
        {
            var q = _s.Areas.Where(a => a.SchoolId == schoolId && (statusId == null || a.StatusId == statusId) && (string.IsNullOrWhiteSpace(search) || a.AreaName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)));
            return Task.FromResult((q.OrderBy(a => a.AreaName).Skip((page - 1) * pageSize).Take(pageSize).Select(Row).ToList(), q.Count()));
        }
        public Task<AreaRow?> GetDetailAsync(string schoolId, long areaId, CancellationToken ct) => Task.FromResult(_s.Areas.Where(a => a.SchoolId == schoolId && a.AreaId == areaId).Select(Row).Cast<AreaRow?>().FirstOrDefault());
        public Task<TbTransportArea?> GetForUpdateAsync(string schoolId, long areaId, CancellationToken ct) => Task.FromResult(_s.Areas.FirstOrDefault(a => a.SchoolId == schoolId && a.AreaId == areaId));
        public Task AddAsync(TbTransportArea area, CancellationToken ct) { area.AreaId = _s.NextArea(); _s.Areas.Add(area); return Task.CompletedTask; }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public Task<bool> ActiveNameTakenAsync(string schoolId, string name, int activeStatusId, long? exceptAreaId, CancellationToken ct) =>
            Task.FromResult(_s.Areas.Any(a => a.SchoolId == schoolId && a.StatusId == activeStatusId && string.Equals(a.AreaName, name, StringComparison.OrdinalIgnoreCase) && a.AreaId != exceptAreaId));
        public Task<List<DriverRef>> GetActiveDriversOfAreaAsync(string schoolId, long areaId, int activeStatusId, CancellationToken ct) =>
            Task.FromResult(_s.Links.Where(l => l.SchoolId == schoolId && l.AreaId == areaId).Select(l => _s.Drivers.First(d => d.SchoolId == schoolId && d.DriverId == l.DriverId))
                .Where(d => d.StatusId == activeStatusId).OrderBy(d => d.FullName).Select(d => new DriverRef(d.DriverId, d.FullName)).ToList());
    }

    public class FakeVehicleRepo : IVehicleRepository
    {
        private readonly TransportStore _s;
        public FakeVehicleRepo(TransportStore s) => _s = s;
        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) => Task.FromResult<int?>(statusName == "Active" ? TransportStore.Active : statusName == "Inactive" ? TransportStore.Inactive : null);
        private VehicleRow Row(TbVehicle v)
        {
            var t = _s.Types.First(x => x.VehicleTypeId == v.VehicleTypeId); var o = _s.Ownerships.First(x => x.OwnershipId == v.OwnershipId);
            return new VehicleRow(v, t.TypeName, o.OwnershipName, o.OwnershipCode, TransportStore.StatusName(v.StatusId), _s.Drivers.Count(d => d.SchoolId == v.SchoolId && d.VehicleId == v.VehicleId && d.StatusId == TransportStore.Active));
        }
        public Task<(List<VehicleRow> Rows, int Total)> ListAsync(string schoolId, string? search, int? statusId, long? vehicleTypeId, int page, int pageSize, CancellationToken ct)
        {
            var plateSearch = (search ?? "").Replace(" ", "");
            var q = _s.Vehicles.Where(v => v.SchoolId == schoolId && (statusId == null || v.StatusId == statusId) && (vehicleTypeId == null || v.VehicleTypeId == vehicleTypeId)
                && (string.IsNullOrWhiteSpace(search) || v.VehicleLabel.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase) || v.RegistrationNumber.Contains(plateSearch, StringComparison.OrdinalIgnoreCase)));
            return Task.FromResult((q.OrderBy(v => v.VehicleLabel).Skip((page - 1) * pageSize).Take(pageSize).Select(Row).ToList(), q.Count()));
        }
        public Task<VehicleRow?> GetDetailAsync(string schoolId, long vehicleId, CancellationToken ct) => Task.FromResult(_s.Vehicles.Where(v => v.SchoolId == schoolId && v.VehicleId == vehicleId).Select(Row).Cast<VehicleRow?>().FirstOrDefault());
        public Task<TbVehicle?> GetForUpdateAsync(string schoolId, long vehicleId, CancellationToken ct) => Task.FromResult(_s.Vehicles.FirstOrDefault(v => v.SchoolId == schoolId && v.VehicleId == vehicleId));
        public Task AddAsync(TbVehicle vehicle, CancellationToken ct) { vehicle.VehicleId = _s.NextVehicle(); _s.Vehicles.Add(vehicle); return Task.CompletedTask; }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public Task<bool> ActiveLabelTakenAsync(string schoolId, string label, int activeStatusId, long? exceptVehicleId, CancellationToken ct) =>
            Task.FromResult(_s.Vehicles.Any(v => v.SchoolId == schoolId && v.StatusId == activeStatusId && string.Equals(v.VehicleLabel, label, StringComparison.OrdinalIgnoreCase) && v.VehicleId != exceptVehicleId));
        public Task<bool> ActivePlateTakenAsync(string schoolId, string plate, int activeStatusId, long? exceptVehicleId, CancellationToken ct) =>
            Task.FromResult(_s.Vehicles.Any(v => v.SchoolId == schoolId && v.StatusId == activeStatusId && string.Equals(v.RegistrationNumber, plate, StringComparison.OrdinalIgnoreCase) && v.VehicleId != exceptVehicleId));
        public Task<List<TransportLookupRow>> GetTypesAsync(CancellationToken ct) => Task.FromResult(_s.Types.Where(t => t.IsActive).Select(t => new TransportLookupRow(t.VehicleTypeId, t.TypeCode, t.TypeName)).ToList());
        public Task<List<TransportLookupRow>> GetOwnershipsAsync(CancellationToken ct) => Task.FromResult(_s.Ownerships.Where(o => o.IsActive).Select(o => new TransportLookupRow(o.OwnershipId, o.OwnershipCode, o.OwnershipName)).ToList());
        public Task<TransportLookupRow?> GetActiveTypeAsync(long id, CancellationToken ct) => Task.FromResult(_s.Types.Where(t => t.VehicleTypeId == id && t.IsActive).Select(t => new TransportLookupRow(t.VehicleTypeId, t.TypeCode, t.TypeName)).Cast<TransportLookupRow?>().FirstOrDefault());
        public Task<TransportLookupRow?> GetActiveOwnershipAsync(long id, CancellationToken ct) => Task.FromResult(_s.Ownerships.Where(o => o.OwnershipId == id && o.IsActive).Select(o => new TransportLookupRow(o.OwnershipId, o.OwnershipCode, o.OwnershipName)).Cast<TransportLookupRow?>().FirstOrDefault());
        public Task<List<DriverRef>> GetActiveDriversOfVehicleAsync(string schoolId, long vehicleId, int activeStatusId, CancellationToken ct) =>
            Task.FromResult(_s.Drivers.Where(d => d.SchoolId == schoolId && d.VehicleId == vehicleId && d.StatusId == activeStatusId).OrderBy(d => d.FullName).Select(d => new DriverRef(d.DriverId, d.FullName)).ToList());
    }

    public class FakeDriverRepo : IDriverRepository
    {
        private readonly TransportStore _s;
        public FakeDriverRepo(TransportStore s) => _s = s;
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => Task.FromResult<IDbContextTransaction>(new FakeTx());
        public Task LockDriverIdsAsync(string schoolId, CancellationToken ct) => Task.CompletedTask;
        public Task<int> GetMaxIdSequenceAsync(string schoolId, string prefix, CancellationToken ct)
        {
            var last = _s.Drivers.Where(d => d.SchoolId == schoolId && d.DriverId.StartsWith(prefix)).OrderByDescending(d => d.DriverId.Length).ThenByDescending(d => d.DriverId).Select(d => d.DriverId).FirstOrDefault();
            return Task.FromResult(last != null && int.TryParse(last[prefix.Length..], out var n) ? n : 0);
        }
        public Task<TbSchools?> GetSchoolAsync(string schoolId, CancellationToken ct) => Task.FromResult(_s.Schools.FirstOrDefault(x => x.SchoolId == schoolId));
        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) => Task.FromResult<int?>(statusName == "Active" ? TransportStore.Active : statusName == "Inactive" ? TransportStore.Inactive : null);

        private VehicleRef? Veh(string school, long? id) => id == null ? null : _s.Vehicles.Where(v => v.SchoolId == school && v.VehicleId == id).Select(v => new VehicleRef(v.VehicleId, v.VehicleLabel, v.RegistrationNumber, v.StatusId)).FirstOrDefault();
        private List<AreaRef> AreasOf(string school, string driverId) =>
            _s.Links.Where(l => l.SchoolId == school && l.DriverId == driverId).Select(l => (l, a: _s.Areas.First(a => a.SchoolId == school && a.AreaId == l.AreaId))).OrderBy(x => x.a.AreaName)
                .Select(x => new AreaRef(x.a.AreaId, x.a.AreaName, x.a.StatusId, x.l.MorningPickupTime, x.l.AfternoonDropTime)).ToList();

        public Task<(List<DriverListRow> Rows, int Total)> ListAsync(string schoolId, string? search, int? statusId, long? vehicleId, int page, int pageSize, CancellationToken ct)
        {
            bool Match(TbDriver d) => string.IsNullOrWhiteSpace(search) || new[] { d.DriverId, d.FullName, d.MobileNumber, d.DlNumber }.Any(x => x.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
            var q = _s.Drivers.Where(d => d.SchoolId == schoolId && (statusId == null || d.StatusId == statusId) && (vehicleId == null || d.VehicleId == vehicleId) && Match(d));
            var rows = q.OrderBy(d => d.FullName).Skip((page - 1) * pageSize).Take(pageSize).Select(d =>
                new DriverListRow(d, TransportStore.StatusName(d.StatusId), Veh(schoolId, d.VehicleId)?.Label, Veh(schoolId, d.VehicleId)?.Plate,
                    _s.Users.Any(u => u.SchoolId == schoolId && u.OrgUserId == d.DriverId), AreasOf(schoolId, d.DriverId).Select(a => a.Name).ToList())).ToList();
            return Task.FromResult((rows, q.Count()));
        }
        public Task<DriverDetailRow?> GetDetailAsync(string schoolId, string driverId, CancellationToken ct)
        {
            var d = _s.Drivers.FirstOrDefault(x => x.SchoolId == schoolId && x.DriverId == driverId);
            if (d == null) return Task.FromResult<DriverDetailRow?>(null);
            var login = _s.Users.FirstOrDefault(u => u.SchoolId == schoolId && u.OrgUserId == driverId)?.OrgUserId;
            return Task.FromResult<DriverDetailRow?>(new DriverDetailRow(d, TransportStore.StatusName(d.StatusId), Veh(schoolId, d.VehicleId)?.Label, Veh(schoolId, d.VehicleId)?.Plate, login, AreasOf(schoolId, driverId)));
        }
        public Task<TbDriver?> GetForUpdateAsync(string schoolId, string driverId, CancellationToken ct) => Task.FromResult(_s.Drivers.FirstOrDefault(d => d.SchoolId == schoolId && d.DriverId == driverId));
        public Task AddAsync(TbDriver driver, CancellationToken ct) { _s.Drivers.Add(driver); return Task.CompletedTask; }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public Task<bool> EmailTakenAsync(string schoolId, string email, string? exceptDriverId, CancellationToken ct) =>
            Task.FromResult(_s.Drivers.Any(d => d.SchoolId == schoolId && d.Email != null && string.Equals(d.Email, email, StringComparison.OrdinalIgnoreCase) && d.DriverId != exceptDriverId));
        public Task<bool> ActiveDlTakenAsync(string schoolId, string dlNumber, int activeStatusId, string? exceptDriverId, CancellationToken ct) =>
            Task.FromResult(_s.Drivers.Any(d => d.SchoolId == schoolId && d.StatusId == activeStatusId && string.Equals(d.DlNumber, dlNumber, StringComparison.OrdinalIgnoreCase) && d.DriverId != exceptDriverId));
        public Task<VehicleRef?> GetVehicleAsync(string schoolId, long vehicleId, CancellationToken ct) => Task.FromResult(Veh(schoolId, vehicleId));
        public Task<List<AreaRef>> GetAreasAsync(string schoolId, IReadOnlyCollection<long> areaIds, CancellationToken ct) =>
            Task.FromResult(_s.Areas.Where(a => a.SchoolId == schoolId && areaIds.Contains(a.AreaId)).Select(a => new AreaRef(a.AreaId, a.AreaName, a.StatusId)).ToList());
        public Task<List<AreaRef>> GetAssignedAreasAsync(string schoolId, string driverId, CancellationToken ct) => Task.FromResult(AreasOf(schoolId, driverId));
        public Task ReplaceAreasAsync(string schoolId, string driverId, IReadOnlyCollection<DriverAreaInput> areas, CancellationToken ct)
        {
            var ids = areas.Select(a => a.AreaId).ToHashSet();
            _s.Links.RemoveAll(l => l.SchoolId == schoolId && l.DriverId == driverId && !ids.Contains(l.AreaId));   // hard delete
            foreach (var a in areas)
            {
                var row = _s.Links.FirstOrDefault(l => l.SchoolId == schoolId && l.DriverId == driverId && l.AreaId == a.AreaId);
                if (row == null) { _s.Link(schoolId, driverId, a.AreaId); row = _s.Links.Last(); }   // a kept row takes the listed times (null clears)
                row.MorningPickupTime = a.MorningPickupTime; row.AfternoonDropTime = a.AfternoonDropTime;
            }
            return Task.CompletedTask;
        }
        public Task<List<VehicleRef>> GetActiveVehiclesAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            Task.FromResult(_s.Vehicles.Where(v => v.SchoolId == schoolId && v.StatusId == activeStatusId).Select(v => new VehicleRef(v.VehicleId, v.VehicleLabel, v.RegistrationNumber, v.StatusId)).ToList());
        public Task<List<AreaRef>> GetActiveAreasAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            Task.FromResult(_s.Areas.Where(a => a.SchoolId == schoolId && a.StatusId == activeStatusId).Select(a => new AreaRef(a.AreaId, a.AreaName, a.StatusId)).ToList());

        public Task<bool> HasLoginAsync(string schoolId, string driverId, CancellationToken ct) => Task.FromResult(_s.Users.Any(u => u.SchoolId == schoolId && u.OrgUserId == driverId));
        public Task<TbUsers?> GetLoginUserAsync(string schoolId, string driverId, CancellationToken ct) => Task.FromResult(_s.Users.FirstOrDefault(u => u.SchoolId == schoolId && u.OrgUserId == driverId));
        public Task<LutRole?> GetRoleByCodeAsync(string roleCode, CancellationToken ct) => Task.FromResult(_s.Roles.FirstOrDefault(r => r.RoleCode == roleCode));
        public Task AddUserAsync(TbUsers user, CancellationToken ct) { user.UserId = _s.NextUser(); foreach (var r in user.UserRoles) r.UserId = user.UserId; _s.Users.Add(user); return Task.CompletedTask; }
        public Task RevokeRefreshTokensAsync(long userId, DateTime revokedAt, CancellationToken ct) { foreach (var t in _s.Tokens.Where(t => t.UserId == userId && t.RevokedAt == null)) t.RevokedAt = revokedAt; return Task.CompletedTask; }
        public Task<(bool Exists, string? Stored)> GetPhotoKeyAsync(string schoolId, string driverId, CancellationToken ct)
        {
            var d = _s.Drivers.FirstOrDefault(x => x.SchoolId == schoolId && x.DriverId == driverId);
            return Task.FromResult((d != null, d?.PhotoPath));
        }
    }

    /// <summary>The auth repository over the same store, so a test can sign a driver in and out through the real AuthService.</summary>
    public class FakeAuthRepo : IAuthRepository
    {
        private readonly TransportStore _s;
        public FakeAuthRepo(TransportStore s) => _s = s;
        public Task<TbUsers?> GetUserByLoginIdAsync(string loginId, bool track = false) => Task.FromResult(_s.Users.FirstOrDefault(u => string.Equals(u.OrgUserId, loginId, StringComparison.OrdinalIgnoreCase)));
        public Task<TbUsers?> GetUserByIdAsync(long userId, bool track = false) => Task.FromResult(_s.Users.FirstOrDefault(u => u.UserId == userId));
        public Task<bool?> GetFirstLoginFlagAsync(long userId) => Task.FromResult<bool?>(_s.Users.FirstOrDefault(u => u.UserId == userId)?.IsFirstLogin);
        public Task<List<LutRole>> GetRolesByUserIdAsync(long userId) =>
            Task.FromResult(_s.Users.Where(u => u.UserId == userId).SelectMany(u => u.UserRoles).Select(r => _s.Roles.First(x => x.RoleId == r.RoleId)).ToList());
        public Task<TbSchools?> GetSchoolByIdAsync(string schoolId) => Task.FromResult(_s.Schools.FirstOrDefault(x => x.SchoolId == schoolId));
        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType) => Task.FromResult<int?>(statusName == "Active" ? TransportStore.Active : statusName == "Inactive" ? TransportStore.Inactive : null);
        public void AddRefreshToken(TbRefreshTokens token) { token.RefreshTokenId = _s.Tokens.Count + 1; _s.Tokens.Add(token); }
        public Task<TbRefreshTokens?> GetRefreshTokenByHashAsync(string tokenHash) => Task.FromResult(_s.Tokens.FirstOrDefault(t => t.TokenHash == tokenHash));
        public Task RevokeAllRefreshTokensAsync(long userId, DateTime revokedAt) { foreach (var t in _s.Tokens.Where(t => t.UserId == userId && t.RevokedAt == null)) t.RevokedAt = revokedAt; return Task.CompletedTask; }
        public Task DeleteExpiredRefreshTokensAsync(long userId, DateTime now) => Task.CompletedTask;
        public Task<int> SaveChangesAsync() => Task.FromResult(1);
    }

    public class FakeNotifier : INotificationService
    {
        public string? LoginUrl => "http://localhost/login";
        public List<(string? To, string Subject, string Html)> Sent = new();
        public Task<string?> SendAsync(string? ownEmail, string? guardianEmail, (string Subject, string Html) message, string context, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(ownEmail)) return Task.FromResult<string?>(null);
            Sent.Add((ownEmail, message.Subject, message.Html)); return Task.FromResult<string?>(ownEmail);
        }
    }
}
