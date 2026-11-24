using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class InventoryRepository : IInventoryRepository
    {
        private readonly dbContext _db;
        public InventoryRepository(dbContext db) => _db = db;

        // ---------------------------------------------------------------- plumbing
        public async Task<int?> GetStatusIdAsync(string name, CancellationToken ct) =>
            await _db.LutStatus.AsNoTracking().Where(s => s.Sname == name && s.Stype == "general status").Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        public Task<string?> GetSchoolCodeAsync(string schoolId, CancellationToken ct) =>
            _db.Schools.AsNoTracking().Where(s => s.SchoolId == schoolId).Select(s => s.SchoolCode).FirstOrDefaultAsync(ct);

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public bool InTransaction => _db.Database.CurrentTransaction != null;
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
        public void ClearTracking() => _db.ChangeTracker.Clear();

        public async Task LockNumbersAsync(string key, string schoolId, CancellationToken ct)
        {
            var k = key + ":" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({k}, 0))", ct);
        }

        private async Task<Dictionary<long, string>> UserNamesAsync(IEnumerable<long?> ids, CancellationToken ct)
        {
            var list = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
            if (list.Count == 0) return new();
            return await _db.Users.AsNoTracking().Where(u => list.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.DisplayName ?? u.OrgUserId ?? string.Empty, ct);
        }
        private static string? Name(Dictionary<long, string> names, long? id) => id is long v && names.TryGetValue(v, out var n) ? n : null;

        // ---------------------------------------------------------------- lookups
        public Task<List<LutInvUnit>> GetUnitsAsync(CancellationToken ct) => _db.InvUnits.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.UnitId).ToListAsync(ct);
        public Task<LutInvUnit?> GetUnitAsync(long unitId, CancellationToken ct) => _db.InvUnits.AsNoTracking().Where(u => u.UnitId == unitId && u.IsActive).FirstOrDefaultAsync(ct);

        // ---------------------------------------------------------------- categories
        public async Task<List<InvCategoryRow>> GetCategoriesAsync(string schoolId, int? statusId, CancellationToken ct)
        {
            var q = _db.InvCategories.AsNoTracking().Where(c => c.SchoolId == schoolId);
            if (statusId != null) q = q.Where(c => c.StatusId == statusId);
            var rows = await q.OrderBy(c => c.CategoryName).ToListAsync(ct);
            var counts = await _db.InvItems.AsNoTracking().Where(i => i.SchoolId == schoolId).GroupBy(i => i.CategoryId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
            return rows.Select(c => new InvCategoryRow { Category = c, ItemCount = counts.GetValueOrDefault(c.CategoryId) }).ToList();
        }
        public Task<TbInvCategory?> GetCategoryAsync(string schoolId, long categoryId, CancellationToken ct) =>
            _db.InvCategories.Where(c => c.SchoolId == schoolId && c.CategoryId == categoryId).FirstOrDefaultAsync(ct);
        public Task<bool> CategoryCodeExistsAsync(string schoolId, string code, long? exceptId, CancellationToken ct) =>
            _db.InvCategories.AsNoTracking().AnyAsync(c => c.SchoolId == schoolId && c.CategoryCode == code && (exceptId == null || c.CategoryId != exceptId), ct);
        public async Task AddAsync(TbInvCategory c, CancellationToken ct) => await _db.InvCategories.AddAsync(c, ct);

        // ---------------------------------------------------------------- suppliers
        public async Task<List<TbInvSupplier>> GetSuppliersAsync(string schoolId, int? statusId, CancellationToken ct)
        {
            var q = _db.InvSuppliers.AsNoTracking().Where(s => s.SchoolId == schoolId);
            if (statusId != null) q = q.Where(s => s.StatusId == statusId);
            return await q.OrderBy(s => s.SupplierName).ToListAsync(ct);
        }
        public Task<TbInvSupplier?> GetSupplierAsync(string schoolId, long supplierId, CancellationToken ct) =>
            _db.InvSuppliers.Where(s => s.SchoolId == schoolId && s.SupplierId == supplierId).FirstOrDefaultAsync(ct);
        public Task<bool> SupplierNameExistsAsync(string schoolId, string name, long? exceptId, CancellationToken ct)
        {
            var lower = name.ToLower();
            return _db.InvSuppliers.AsNoTracking().AnyAsync(s => s.SchoolId == schoolId && s.SupplierName.ToLower() == lower && (exceptId == null || s.SupplierId != exceptId), ct);
        }
        public async Task AddAsync(TbInvSupplier s, CancellationToken ct) => await _db.InvSuppliers.AddAsync(s, ct);

        // ---------------------------------------------------------------- items
        private IQueryable<InvItemRow> ItemRows(string schoolId) =>
            from i in _db.InvItems.AsNoTracking()
            where i.SchoolId == schoolId
            join c in _db.InvCategories.AsNoTracking() on new { i.CategoryId, i.SchoolId } equals new { c.CategoryId, c.SchoolId }
            join u in _db.InvUnits.AsNoTracking() on i.UnitId equals u.UnitId
            select new InvItemRow { Item = i, CategoryName = c.CategoryName, UnitName = u.UnitName, UnitCode = u.UnitCode };

        public async Task<(List<InvItemRow> Rows, int Total)> GetItemsAsync(string schoolId, InvItemFilter f, int page, int pageSize, CancellationToken ct)
        {
            var q = ItemRows(schoolId);
            if (f.CategoryId != null) q = q.Where(r => r.Item.CategoryId == f.CategoryId);
            if (f.StatusId != null) q = q.Where(r => r.Item.StatusId == f.StatusId);
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var pat = "%" + f.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                q = q.Where(r => EF.Functions.ILike(r.Item.ItemName, pat) || EF.Functions.ILike(r.Item.ItemCode, pat) || (r.Item.Barcode != null && EF.Functions.ILike(r.Item.Barcode, pat)));
            }
            if (f.LowStock) q = q.Where(r => r.Item.MinStockLevel > 0 && r.Item.CurrentQuantity <= r.Item.MinStockLevel);
            if (f.ExpiringBy is DateOnly by) q = q.Where(r => r.Item.ExpiryDate != null && r.Item.ExpiryDate <= by && r.Item.CurrentQuantity > 0);
            var total = await q.CountAsync(ct);
            var rows = await q.OrderBy(r => r.Item.ItemName).ThenBy(r => r.Item.ItemId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return (rows, total);
        }

        public Task<InvItemRow?> GetItemRowAsync(string schoolId, long itemId, CancellationToken ct) => ItemRows(schoolId).Where(r => r.Item.ItemId == itemId).FirstOrDefaultAsync(ct);
        public Task<TbInvItem?> GetItemAsync(string schoolId, long itemId, CancellationToken ct) => _db.InvItems.Where(i => i.SchoolId == schoolId && i.ItemId == itemId).FirstOrDefaultAsync(ct);
        public Task<bool> BarcodeExistsAsync(string schoolId, string barcode, long? exceptId, CancellationToken ct) =>
            _db.InvItems.AsNoTracking().AnyAsync(i => i.SchoolId == schoolId && i.Barcode == barcode && (exceptId == null || i.ItemId != exceptId), ct);
        public async Task<int> GetMaxItemSequenceAsync(string schoolId, CancellationToken ct)
        {
            // fixed-width codes sort by (length, value); the last of that order is the biggest
            var last = await _db.InvItems.AsNoTracking().Where(i => i.SchoolId == schoolId && i.ItemCode.StartsWith("ITM"))
                .OrderByDescending(i => i.ItemCode.Length).ThenByDescending(i => i.ItemCode).Select(i => i.ItemCode).FirstOrDefaultAsync(ct);
            return last != null && int.TryParse(last[3..], out var n) ? n : 0;
        }
        public async Task AddAsync(TbInvItem i, CancellationToken ct) => await _db.InvItems.AddAsync(i, ct);

        // ---------------------------------------------------------------- the ledger
        public async Task<decimal?> ApplyQuantityDeltaAsync(string schoolId, long itemId, decimal delta, CancellationToken ct)
        {
            var tx = _db.Database.CurrentTransaction ?? throw new InvalidOperationException("A stock change must run inside a transaction.");
            var conn = _db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx.GetDbTransaction();
            // the one place a quantity is written: the guard is part of the UPDATE, so two callers can never both take the last units
            cmd.CommandText = "UPDATE public.tb_inv_items SET current_quantity = current_quantity + @delta, updated_at = now() " +
                              "WHERE item_id = @item AND school_id = @school AND current_quantity + @delta >= 0 RETURNING current_quantity";
            Add(cmd, "delta", DbType.Decimal, delta); Add(cmd, "item", DbType.Int64, itemId); Add(cmd, "school", DbType.String, schoolId);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is null or DBNull ? null : Convert.ToDecimal(result);
        }
        private static void Add(System.Data.Common.DbCommand cmd, string name, DbType type, object value)
        {
            var p = cmd.CreateParameter(); p.ParameterName = name; p.DbType = type; p.Value = value; cmd.Parameters.Add(p);
        }

        public Task<decimal?> GetQuantityAsync(string schoolId, long itemId, CancellationToken ct) =>
            _db.InvItems.AsNoTracking().Where(i => i.SchoolId == schoolId && i.ItemId == itemId).Select(i => (decimal?)i.CurrentQuantity).FirstOrDefaultAsync(ct);
        public async Task AddAsync(TbInvStockMovement m, CancellationToken ct) => await _db.InvStockMovements.AddAsync(m, ct);
        public Task<bool> HasOpeningMovementAsync(string schoolId, long itemId, CancellationToken ct) =>
            _db.InvStockMovements.AsNoTracking().AnyAsync(m => m.SchoolId == schoolId && m.ItemId == itemId && m.MovementType == "OPENING", ct);

        private IQueryable<InvMovementRow> MovementRows(string schoolId) =>
            from m in _db.InvStockMovements.AsNoTracking()
            where m.SchoolId == schoolId
            join i in _db.InvItems.AsNoTracking() on new { m.ItemId, m.SchoolId } equals new { i.ItemId, i.SchoolId }
            join u in _db.InvUnits.AsNoTracking() on i.UnitId equals u.UnitId
            select new InvMovementRow { Movement = m, ItemCode = i.ItemCode, ItemName = i.ItemName, UnitCode = u.UnitCode };

        private async Task NameMovementsAsync(List<InvMovementRow> rows, CancellationToken ct)
        {
            var names = await UserNamesAsync(rows.Select(r => (long?)r.Movement.CreatedBy), ct);
            foreach (var r in rows) r.CreatedByName = Name(names, r.Movement.CreatedBy);
        }

        public async Task<(List<InvMovementRow> Rows, int Total)> GetMovementsAsync(string schoolId, long? itemId, bool adjustmentsOnly, string? reasonCode, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct)
        {
            var q = MovementRows(schoolId);
            if (itemId != null) q = q.Where(r => r.Movement.ItemId == itemId);
            if (adjustmentsOnly) q = q.Where(r => r.Movement.MovementType == "ADJUSTMENT" || r.Movement.MovementType == "OPENING");
            if (reasonCode != null) q = q.Where(r => r.Movement.ReasonCode == reasonCode);
            if (from is DateOnly f) { var s = DayStart(f); q = q.Where(r => r.Movement.CreatedAt >= s); }
            if (to is DateOnly t) { var e = DayStart(t.AddDays(1)); q = q.Where(r => r.Movement.CreatedAt < e); }
            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(r => r.Movement.CreatedAt).ThenByDescending(r => r.Movement.MovementId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            await NameMovementsAsync(rows, ct);
            return (rows, total);
        }

        public async Task<InvMovementRow?> GetMovementRowAsync(string schoolId, long movementId, CancellationToken ct)
        {
            var row = await MovementRows(schoolId).Where(r => r.Movement.MovementId == movementId).FirstOrDefaultAsync(ct);
            if (row != null) await NameMovementsAsync(new List<InvMovementRow> { row }, ct);
            return row;
        }

        // India midnight as a UTC instant, to filter timestamps by the school's calendar day (same idea as SchoolClock.StartOfDayUtc, which lives in the service layer)
        private static DateTime DayStart(DateOnly day)
        {
            var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var tz = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, tz), DateTimeKind.Utc);
        }

        // ---------------------------------------------------------------- stock in
        private IQueryable<InvStockInRow> StockInRows(string schoolId) =>
            from s in _db.InvStockIns.AsNoTracking()
            where s.SchoolId == schoolId
            join i in _db.InvItems.AsNoTracking() on new { s.ItemId, s.SchoolId } equals new { i.ItemId, i.SchoolId }
            join u in _db.InvUnits.AsNoTracking() on i.UnitId equals u.UnitId
            join sup in _db.InvSuppliers.AsNoTracking() on new { Id = s.SupplierId, s.SchoolId } equals new { Id = (long?)sup.SupplierId, sup.SchoolId } into sups
            from sup in sups.DefaultIfEmpty()
            select new InvStockInRow { StockIn = s, ItemCode = i.ItemCode, ItemName = i.ItemName, UnitCode = u.UnitCode, SupplierName = sup == null ? null : sup.SupplierName };

        private async Task NameStockInsAsync(List<InvStockInRow> rows, CancellationToken ct)
        {
            var names = await UserNamesAsync(rows.SelectMany(r => new long?[] { r.StockIn.ReceivedBy, r.StockIn.VoidedBy }), ct);
            foreach (var r in rows) { r.ReceivedByName = Name(names, r.StockIn.ReceivedBy); r.VoidedByName = Name(names, r.StockIn.VoidedBy); }
        }

        public async Task<(List<InvStockInRow> Rows, int Total)> GetStockInsAsync(string schoolId, long? itemId, long? supplierId, DateOnly? from, DateOnly? to, bool includeVoided, int page, int pageSize, CancellationToken ct)
        {
            var q = StockInRows(schoolId);
            if (itemId != null) q = q.Where(r => r.StockIn.ItemId == itemId);
            if (supplierId != null) q = q.Where(r => r.StockIn.SupplierId == supplierId);
            if (from != null) q = q.Where(r => r.StockIn.ReceivedDate >= from);
            if (to != null) q = q.Where(r => r.StockIn.ReceivedDate <= to);
            if (!includeVoided) q = q.Where(r => r.StockIn.Status == "Active");
            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(r => r.StockIn.ReceivedDate).ThenByDescending(r => r.StockIn.StockInId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            await NameStockInsAsync(rows, ct);
            return (rows, total);
        }

        public async Task<InvStockInRow?> GetStockInRowAsync(string schoolId, long stockInId, CancellationToken ct)
        {
            var row = await StockInRows(schoolId).Where(r => r.StockIn.StockInId == stockInId).FirstOrDefaultAsync(ct);
            if (row != null) await NameStockInsAsync(new List<InvStockInRow> { row }, ct);
            return row;
        }

        public Task<TbInvStockIn?> LockStockInAsync(string schoolId, long stockInId, CancellationToken ct) =>
            _db.InvStockIns.FromSqlInterpolated($"SELECT * FROM public.tb_inv_stock_ins WHERE school_id = {schoolId} AND stock_in_id = {stockInId} FOR UPDATE").OrderBy(s => s.StockInId).FirstOrDefaultAsync(ct);   // the primary key: the raw SQL already picks one row, the ordering just makes First deterministic (and silences EF's FirstWithoutOrderByAndFilter warning)
        public async Task AddAsync(TbInvStockIn s, CancellationToken ct) => await _db.InvStockIns.AddAsync(s, ct);

        // ---------------------------------------------------------------- issues
        private async Task<Dictionary<(string Type, string Id), string>> RecipientNamesAsync(string schoolId, IEnumerable<TbInvIssue> issues, CancellationToken ct)
        {
            var list = issues.ToList();
            var map = new Dictionary<(string, string), string>();
            var emp = list.Where(i => i.EmployeeId != null).Select(i => i.EmployeeId!).Distinct().ToList();
            if (emp.Count > 0)
                foreach (var e in await _db.Employees.AsNoTracking().Where(e => e.SchoolId == schoolId && emp.Contains(e.EmployeeId)).Select(e => new { e.EmployeeId, e.FirstName, e.MiddleName, e.LastName }).ToListAsync(ct))
                    map[("STAFF", e.EmployeeId)] = Join(e.FirstName, e.MiddleName, e.LastName);
            var stu = list.Where(i => i.StudentId != null).Select(i => i.StudentId!).Distinct().ToList();
            if (stu.Count > 0)
                foreach (var s in await _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && stu.Contains(s.StudentId)).Select(s => new { s.StudentId, s.FirstName, s.MiddleName, s.LastName }).ToListAsync(ct))
                    map[("STUDENT", s.StudentId)] = Join(s.FirstName, s.MiddleName, s.LastName);
            var dep = list.Where(i => i.DepartmentId != null).Select(i => i.DepartmentId!.Value).Distinct().ToList();
            if (dep.Count > 0)
                foreach (var d in await _db.EmployeeDepartments.AsNoTracking().Where(d => d.SchoolId == schoolId && dep.Contains(d.DepartmentId)).Select(d => new { d.DepartmentId, d.DepartmentName }).ToListAsync(ct))
                    map[("DEPARTMENT", d.DepartmentId.ToString())] = d.DepartmentName;
            return map;
        }
        private static string Join(params string?[] parts) => string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)));

        private async Task<List<InvIssueRow>> ToIssueRowsAsync(string schoolId, List<(TbInvIssue Issue, decimal Total, decimal Returned)> raw, CancellationToken ct)
        {
            var recipients = await RecipientNamesAsync(schoolId, raw.Select(r => r.Issue), ct);
            var users = await UserNamesAsync(raw.SelectMany(r => new long?[] { r.Issue.IssuedBy, r.Issue.VoidedBy }), ct);
            return raw.Select(r =>
            {
                var key = (r.Issue.RecipientType, r.Issue.EmployeeId ?? r.Issue.StudentId ?? r.Issue.DepartmentId?.ToString() ?? "");
                return new InvIssueRow
                {
                    Issue = r.Issue, TotalQuantity = r.Total, ReturnedQuantity = r.Returned,
                    RecipientName = recipients.TryGetValue(key, out var n) ? n : key.Item2,
                    IssuedByName = Name(users, r.Issue.IssuedBy), VoidedByName = Name(users, r.Issue.VoidedBy)
                };
            }).ToList();
        }

        public async Task<(List<InvIssueRow> Rows, int Total)> GetIssuesAsync(string schoolId, InvIssueFilter f, int page, int pageSize, CancellationToken ct)
        {
            var q = _db.InvIssues.AsNoTracking().Where(i => i.SchoolId == schoolId)
                .Select(i => new
                {
                    Issue = i,
                    Total = _db.InvIssueLines.Where(l => l.IssueId == i.IssueId).Sum(l => (decimal?)l.Quantity) ?? 0m,
                    Returned = _db.InvIssueLines.Where(l => l.IssueId == i.IssueId).Sum(l => (decimal?)l.ReturnedQuantity) ?? 0m
                });
            if (f.RecipientType != null) q = q.Where(x => x.Issue.RecipientType == f.RecipientType);
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var pat = "%" + f.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                q = q.Where(x => EF.Functions.ILike(x.Issue.IssueNumber, pat));
            }
            if (f.From != null) q = q.Where(x => x.Issue.IssueDate >= f.From);
            if (f.To != null) q = q.Where(x => x.Issue.IssueDate <= f.To);
            switch (f.DerivedStatus)
            {
                case "Voided": q = q.Where(x => x.Issue.Status == "Voided"); break;
                case "Outstanding": q = q.Where(x => x.Issue.Status == "Active" && x.Returned <= 0); break;
                case "Returned": q = q.Where(x => x.Issue.Status == "Active" && x.Returned >= x.Total); break;
                case "Partially Returned": q = q.Where(x => x.Issue.Status == "Active" && x.Returned > 0 && x.Returned < x.Total); break;
            }
            var total = await q.CountAsync(ct);
            var page_ = await q.OrderByDescending(x => x.Issue.IssueDate).ThenByDescending(x => x.Issue.IssueId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return (await ToIssueRowsAsync(schoolId, page_.Select(x => (x.Issue, x.Total, x.Returned)).ToList(), ct), total);
        }

        public async Task<InvIssueRow?> GetIssueRowAsync(string schoolId, long issueId, CancellationToken ct)
        {
            var x = await _db.InvIssues.AsNoTracking().Where(i => i.SchoolId == schoolId && i.IssueId == issueId)
                .Select(i => new
                {
                    Issue = i,
                    Total = _db.InvIssueLines.Where(l => l.IssueId == i.IssueId).Sum(l => (decimal?)l.Quantity) ?? 0m,
                    Returned = _db.InvIssueLines.Where(l => l.IssueId == i.IssueId).Sum(l => (decimal?)l.ReturnedQuantity) ?? 0m
                }).FirstOrDefaultAsync(ct);
            if (x == null) return null;
            return (await ToIssueRowsAsync(schoolId, new() { (x.Issue, x.Total, x.Returned) }, ct)).First();
        }

        public async Task<List<InvIssueLineRow>> GetIssueLinesAsync(string schoolId, IReadOnlyCollection<long> issueIds, CancellationToken ct)
        {
            var ids = issueIds.ToArray();
            return await (from l in _db.InvIssueLines.AsNoTracking()
                          where l.SchoolId == schoolId && ids.Contains(l.IssueId)
                          join i in _db.InvItems.AsNoTracking() on new { l.ItemId, l.SchoolId } equals new { i.ItemId, i.SchoolId }
                          join u in _db.InvUnits.AsNoTracking() on i.UnitId equals u.UnitId
                          orderby l.IssueLineId
                          select new InvIssueLineRow { Line = l, ItemCode = i.ItemCode, ItemName = i.ItemName, UnitCode = u.UnitCode }).ToListAsync(ct);
        }

        public async Task<List<(long IssueLineId, InvReturnRow Row)>> GetReturnsAsync(string schoolId, IReadOnlyCollection<long> issueLineIds, CancellationToken ct)
        {
            var ids = issueLineIds.ToArray();
            var rows = await _db.InvIssueReturns.AsNoTracking().Where(r => r.SchoolId == schoolId && ids.Contains(r.IssueLineId)).OrderBy(r => r.ReturnId).ToListAsync(ct);
            var users = await UserNamesAsync(rows.Select(r => (long?)r.ReceivedBy), ct);
            return rows.Select(r => (r.IssueLineId, new InvReturnRow { Return = r, ReceivedByName = Name(users, r.ReceivedBy) })).ToList();
        }

        public Task<TbInvIssue?> LockIssueAsync(string schoolId, long issueId, CancellationToken ct) =>
            _db.InvIssues.FromSqlInterpolated($"SELECT * FROM public.tb_inv_issues WHERE school_id = {schoolId} AND issue_id = {issueId} FOR UPDATE").OrderBy(i => i.IssueId).FirstOrDefaultAsync(ct);   // as above
        public Task<List<TbInvIssueLine>> GetTrackedLinesAsync(string schoolId, long issueId, CancellationToken ct) =>
            _db.InvIssueLines.Where(l => l.SchoolId == schoolId && l.IssueId == issueId).OrderBy(l => l.ItemId).ToListAsync(ct);

        public async Task<int> GetMaxIssueSequenceAsync(string schoolId, string prefix, CancellationToken ct)
        {
            var last = await _db.InvIssues.AsNoTracking().Where(i => i.SchoolId == schoolId && i.IssueNumber.StartsWith(prefix))
                .OrderByDescending(i => i.IssueNumber.Length).ThenByDescending(i => i.IssueNumber).Select(i => i.IssueNumber).FirstOrDefaultAsync(ct);
            return last != null && int.TryParse(last[prefix.Length..], out var n) ? n : 0;
        }
        public async Task AddAsync(TbInvIssue i, CancellationToken ct) => await _db.InvIssues.AddAsync(i, ct);
        public async Task AddAsync(TbInvIssueLine l, CancellationToken ct) => await _db.InvIssueLines.AddAsync(l, ct);
        public async Task AddAsync(TbInvIssueReturn r, CancellationToken ct) => await _db.InvIssueReturns.AddAsync(r, ct);

        // ---------------------------------------------------------------- recipients (read-only)
        public async Task<InvRecipientRow?> GetActiveRecipientAsync(string schoolId, string recipientType, string recipientId, int activeStatusId, CancellationToken ct)
        {
            switch (recipientType)
            {
                case "STAFF":
                    return await _db.Employees.AsNoTracking().Where(e => e.SchoolId == schoolId && e.EmployeeId == recipientId && e.StatusId == activeStatusId)
                        .Select(e => new InvRecipientRow { Id = e.EmployeeId, Name = (e.FirstName + " " + e.LastName).Trim(), Detail = e.EmployeeType }).FirstOrDefaultAsync(ct);
                case "STUDENT":
                    return await _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.StudentId == recipientId && s.StatusId == activeStatusId)
                        .Select(s => new InvRecipientRow { Id = s.StudentId, Name = (s.FirstName + (s.LastName == null ? "" : " " + s.LastName)).Trim(), Detail = s.AdmissionNumber }).FirstOrDefaultAsync(ct);
                case "DEPARTMENT":
                    if (!long.TryParse(recipientId, out var depId)) return null;
                    return await _db.EmployeeDepartments.AsNoTracking().Where(d => d.SchoolId == schoolId && d.DepartmentId == depId && d.StatusId == activeStatusId)
                        .Select(d => new InvRecipientRow { Id = d.DepartmentId.ToString(), Name = d.DepartmentName, Detail = null }).FirstOrDefaultAsync(ct);
                default: return null;
            }
        }

        public async Task<List<InvRecipientRow>> SearchRecipientsAsync(string schoolId, string recipientType, string? q, int activeStatusId, int cap, CancellationToken ct)
        {
            var term = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
            var pat = term == null ? null : "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            switch (recipientType)
            {
                case "STAFF":
                {
                    var s = _db.Employees.AsNoTracking().Where(e => e.SchoolId == schoolId && e.StatusId == activeStatusId);
                    if (pat != null) s = s.Where(e => EF.Functions.ILike(e.FirstName + " " + e.LastName, pat) || EF.Functions.ILike(e.EmployeeId, pat));
                    return await s.OrderBy(e => e.FirstName).ThenBy(e => e.EmployeeId).Take(cap)
                        .Select(e => new InvRecipientRow { Id = e.EmployeeId, Name = (e.FirstName + " " + e.LastName).Trim(), Detail = e.EmployeeType }).ToListAsync(ct);
                }
                case "STUDENT":
                {
                    var s = _db.Students.AsNoTracking().Where(x => x.SchoolId == schoolId && x.StatusId == activeStatusId);
                    if (pat != null) s = s.Where(x => EF.Functions.ILike(x.FirstName + (x.LastName == null ? "" : " " + x.LastName), pat) || EF.Functions.ILike(x.StudentId, pat) || EF.Functions.ILike(x.AdmissionNumber, pat));
                    return await s.OrderBy(x => x.FirstName).ThenBy(x => x.StudentId).Take(cap)
                        .Select(x => new InvRecipientRow { Id = x.StudentId, Name = (x.FirstName + (x.LastName == null ? "" : " " + x.LastName)).Trim(), Detail = x.AdmissionNumber }).ToListAsync(ct);
                }
                case "DEPARTMENT":
                {
                    var s = _db.EmployeeDepartments.AsNoTracking().Where(d => d.SchoolId == schoolId && d.StatusId == activeStatusId);
                    if (pat != null) s = s.Where(d => EF.Functions.ILike(d.DepartmentName, pat));
                    return await s.OrderBy(d => d.DepartmentName).Take(cap).Select(d => new InvRecipientRow { Id = d.DepartmentId.ToString(), Name = d.DepartmentName }).ToListAsync(ct);
                }
                default: return new();
            }
        }

        // ---------------------------------------------------------------- dashboard
        public Task<int> CountActiveItemsAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            _db.InvItems.AsNoTracking().CountAsync(i => i.SchoolId == schoolId && i.StatusId == activeStatusId, ct);

        public async Task<decimal> SumStockValueAsync(string schoolId, int activeStatusId, CancellationToken ct) =>
            await _db.InvItems.AsNoTracking().Where(i => i.SchoolId == schoolId && i.StatusId == activeStatusId).SumAsync(i => (decimal?)(i.CurrentQuantity * i.PurchasePrice), ct) ?? 0m;

        private async Task<(int, List<InvItemRow>)> TopAsync(IQueryable<InvItemRow> q, Func<IQueryable<InvItemRow>, IOrderedQueryable<InvItemRow>> order, int top, CancellationToken ct) =>
            (await q.CountAsync(ct), await order(q).Take(top).ToListAsync(ct));

        public Task<(int Count, List<InvItemRow> Top)> GetLowStockAsync(string schoolId, int activeStatusId, int top, CancellationToken ct) =>
            TopAsync(ItemRows(schoolId).Where(r => r.Item.StatusId == activeStatusId && r.Item.MinStockLevel > 0 && r.Item.CurrentQuantity <= r.Item.MinStockLevel),
                q => q.OrderBy(r => r.Item.CurrentQuantity - r.Item.MinStockLevel).ThenBy(r => r.Item.ItemName), top, ct);

        public Task<(int Count, List<InvItemRow> Top)> GetExpiryListAsync(string schoolId, int activeStatusId, DateOnly from, DateOnly to, int top, CancellationToken ct) =>
            TopAsync(ItemRows(schoolId).Where(r => r.Item.StatusId == activeStatusId && r.Item.CurrentQuantity > 0 && r.Item.ExpiryDate != null && r.Item.ExpiryDate >= from && r.Item.ExpiryDate <= to),
                q => q.OrderBy(r => r.Item.ExpiryDate).ThenBy(r => r.Item.ItemName), top, ct);

        public Task<(int Count, List<InvItemRow> Top)> GetExpiredListAsync(string schoolId, int activeStatusId, DateOnly today, int top, CancellationToken ct) =>
            TopAsync(ItemRows(schoolId).Where(r => r.Item.StatusId == activeStatusId && r.Item.CurrentQuantity > 0 && r.Item.ExpiryDate != null && r.Item.ExpiryDate < today),
                q => q.OrderBy(r => r.Item.ExpiryDate).ThenBy(r => r.Item.ItemName), top, ct);

        public Task<int> CountIssuedNotReturnedAsync(string schoolId, CancellationToken ct) =>
            _db.InvIssues.AsNoTracking().CountAsync(i => i.SchoolId == schoolId && i.Status == "Active"
                && _db.InvIssueLines.Any(l => l.IssueId == i.IssueId && l.ReturnedQuantity < l.Quantity), ct);

        public Task<int> CountStockInsSinceAsync(string schoolId, DateOnly since, CancellationToken ct) =>
            _db.InvStockIns.AsNoTracking().CountAsync(s => s.SchoolId == schoolId && s.Status == "Active" && s.ReceivedDate >= since, ct);

        public Task<int> CountIssuesSinceAsync(string schoolId, DateOnly since, CancellationToken ct) =>
            _db.InvIssues.AsNoTracking().CountAsync(i => i.SchoolId == schoolId && i.Status == "Active" && i.IssueDate >= since, ct);
    }
}
