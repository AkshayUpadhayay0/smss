using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class SubscriptionRepository : ISubscriptionRepository
    {
        // These spellings are also in the service layer's SubscriptionStatuses / PaymentStatuses; the db layer cannot reference that project
        private const string Trial = "TRIAL", Active = "ACTIVE", Overdue = "OVERDUE", Cancelled = "CANCELLED";
        private const string PaymentActive = "Active";
        private const string SuperAdminUserType = "Super Admin";   // tb_users.user_type, same as UserTypes.SuperAdmin

        private readonly dbContext _db;
        public SubscriptionRepository(dbContext db) => _db = db;

        // What counts as paid: allocations of ACTIVE payments. Voiding a payment drops its allocations out of every total at once.
        private IQueryable<TbSubscriptionPaymentAllocation> ActiveAllocations => _db.SubscriptionPaymentAllocations.Where(a => a.Payment!.Status == PaymentActive);

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

        // ---------------------------------------------------------------- pricing
        public Task<TbSubscriptionPricing?> GetActivePricingAsync(CancellationToken ct) =>
            _db.SubscriptionPricing.AsNoTracking().Where(p => p.IsActive).FirstOrDefaultAsync(ct);

        public Task<List<TbSubscriptionPricing>> GetPricingHistoryAsync(CancellationToken ct) =>
            _db.SubscriptionPricing.AsNoTracking().OrderByDescending(p => p.PricingId).ToListAsync(ct);

        public async Task ReplaceActivePricingAsync(TbSubscriptionPricing next, CancellationToken ct)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            // Old row first: the partial unique index allows one Active row at a time. A concurrent update that slips in between fails on the insert (unique violation) and rolls back.
            await _db.SubscriptionPricing.Where(p => p.IsActive).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false), ct);
            _db.SubscriptionPricing.Add(next);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // ---------------------------------------------------------------- subscriptions
        public Task<bool> SchoolExistsAsync(string schoolId, CancellationToken ct) => _db.Schools.AsNoTracking().AnyAsync(s => s.SchoolId == schoolId, ct);

        public Task<TbSchoolSubscription?> GetSubscriptionAsync(string schoolId, CancellationToken ct) =>
            _db.SchoolSubscriptions.AsNoTracking().Where(s => s.SchoolId == schoolId).FirstOrDefaultAsync(ct);

        public Task<TbSchoolSubscription?> LockSubscriptionAsync(string schoolId, CancellationToken ct) =>
            _db.SchoolSubscriptions
                .FromSqlInterpolated($"SELECT * FROM public.tb_school_subscriptions WHERE school_id = {schoolId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);

        public Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct) =>
            _db.LutStatus.AsNoTracking().Where(s => s.Sname == statusName && s.Stype == statusType).Select(s => (int?)s.Sid).FirstOrDefaultAsync(ct);

        // An employee counts when their record is Active and their employment status does not block login (Active and On Leave count; Resigned,
        // Terminated and Retired do not). The school's own admin login is not a tb_employees row, so it is never counted.
        // Anything that is not "Teaching" is staff, so a new employee type is billed rather than silently skipped.
        public async Task<BillableCounts> CountBillableUsersAsync(string schoolId, int activeStatusId, CancellationToken ct)
        {
            var employeeTypes = from e in _db.Employees.AsNoTracking()
                                join es in _db.LutEmploymentStatuses.AsNoTracking() on e.EmploymentStatusId equals es.EmploymentStatusId
                                where e.SchoolId == schoolId && e.StatusId == activeStatusId && !es.BlocksLogin
                                select e.EmployeeType;
            var teachers = await employeeTypes.CountAsync(t => t == "Teaching", ct);
            var staff = await employeeTypes.CountAsync(t => t != "Teaching", ct);

            var hasYear = await _db.AcademicYears.AsNoTracking().AnyAsync(y => y.SchoolId == schoolId && y.IsCurrent, ct);
            var students = await (from en in _db.StudentEnrollments.AsNoTracking()
                                  join y in _db.AcademicYears.AsNoTracking() on en.AcademicYearId equals y.AcademicYearId
                                  join st in _db.Students.AsNoTracking() on en.StudentId equals st.StudentId
                                  where en.SchoolId == schoolId && y.SchoolId == schoolId && y.IsCurrent && en.StatusId == activeStatusId && st.StatusId == activeStatusId
                                  select en.StudentId).Distinct().CountAsync(ct);
            return new BillableCounts(teachers, staff, students, hasYear);
        }

        // ---------------------------------------------------------------- invoices
        public Task<TbSubscriptionInvoice?> GetLatestInvoiceAsync(long subscriptionId, CancellationToken ct) =>
            _db.SubscriptionInvoices.AsNoTracking().Where(i => i.SubscriptionId == subscriptionId).OrderByDescending(i => i.BillingPeriodStart).FirstOrDefaultAsync(ct);

        public async Task<decimal> GetPaidAmountAsync(long invoiceId, CancellationToken ct) =>
            await ActiveAllocations.AsNoTracking().Where(a => a.InvoiceId == invoiceId).SumAsync(a => (decimal?)a.AllocatedAmount, ct) ?? 0m;

        public async Task<List<InvoiceRow>> GetSchoolInvoicesAsync(string schoolId, IReadOnlyCollection<long>? invoiceIds, CancellationToken ct)
        {
            var q = InvoiceQuery().Where(x => x.Invoice.SchoolId == schoolId);
            if (invoiceIds != null) { var ids = invoiceIds.ToArray(); q = q.Where(x => ids.Contains(x.Invoice.InvoiceId)); }
            var rows = await q.OrderBy(x => x.Invoice.BillingPeriodStart).ThenBy(x => x.Invoice.InvoiceId).ToListAsync(ct);
            return rows.Select(r => new InvoiceRow(r.Invoice, r.SchoolName, r.Paid)).ToList();
        }

        public void AddInvoice(TbSubscriptionInvoice invoice) => _db.SubscriptionInvoices.Add(invoice);

        public async Task<InvoiceRow?> GetInvoiceAsync(long invoiceId, CancellationToken ct)
        {
            var r = await InvoiceQuery().Where(x => x.Invoice.InvoiceId == invoiceId).FirstOrDefaultAsync(ct);
            return r == null ? null : new InvoiceRow(r.Invoice, r.SchoolName, r.Paid);
        }

        public async Task<(List<InvoiceRow> Rows, int Total)> ListInvoicesAsync(string? schoolId, string? status, DateOnly today, int page, int pageSize, CancellationToken ct)
        {
            var q = InvoiceQuery();
            if (!string.IsNullOrEmpty(schoolId)) q = q.Where(x => x.Invoice.SchoolId == schoolId);
            // Status is derived, so it is filtered with the same rule as SubscriptionRules.InvoiceStatus
            q = status switch
            {
                "Paid" => q.Where(x => x.Paid >= x.Invoice.NetAmount),
                "Overdue" => q.Where(x => x.Paid < x.Invoice.NetAmount && x.Invoice.DueDate < today),
                "Pending" => q.Where(x => x.Paid < x.Invoice.NetAmount && x.Invoice.DueDate >= today),
                _ => q
            };
            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(x => x.Invoice.InvoiceId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return (rows.Select(r => new InvoiceRow(r.Invoice, r.SchoolName, r.Paid)).ToList(), total);
        }

        private IQueryable<InvoiceWithPaid> InvoiceQuery() =>
            from i in _db.SubscriptionInvoices.AsNoTracking()
            join s in _db.Schools.AsNoTracking() on i.SchoolId equals s.SchoolId
            select new InvoiceWithPaid
            {
                Invoice = i,
                SchoolName = s.SchoolName,
                Paid = ActiveAllocations.Where(a => a.InvoiceId == i.InvoiceId).Sum(a => (decimal?)a.AllocatedAmount) ?? 0m
            };

        private sealed class InvoiceWithPaid
        {
            public TbSubscriptionInvoice Invoice { get; set; } = null!;
            public string SchoolName { get; set; } = null!;
            public decimal Paid { get; set; }
        }

        // ---------------------------------------------------------------- payments
        public Task<TbSubscriptionPayment?> LockPaymentAsync(long paymentId, CancellationToken ct) =>
            _db.SubscriptionPayments
                .FromSqlInterpolated($"SELECT * FROM public.tb_subscription_payments WHERE payment_id = {paymentId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);

        public Task<List<TbSubscriptionPayment>> GetPaymentsForInvoiceAsync(long invoiceId, CancellationToken ct) =>
            _db.SubscriptionPayments.AsNoTracking().Include(p => p.Allocations)
                .Where(p => p.Allocations.Any(a => a.InvoiceId == invoiceId))
                .OrderByDescending(p => p.PaymentId).ToListAsync(ct);

        public Task<List<TbSubscriptionPaymentAllocation>> GetAllocationsAsync(long paymentId, CancellationToken ct) =>
            _db.SubscriptionPaymentAllocations.AsNoTracking().Where(a => a.PaymentId == paymentId).OrderBy(a => a.AllocationId).ToListAsync(ct);

        public void AddPayment(TbSubscriptionPayment payment) => _db.SubscriptionPayments.Add(payment);

        // ---------------------------------------------------------------- dashboard
        // Customer schools only. The platform's own organisation row (the one the Super Admin login belongs to) is not a school that gets billed.
        private IQueryable<TbSchools> TenantSchools() =>
            _db.Schools.AsNoTracking().Where(s => !_db.Users.Any(u => u.SchoolId == s.SchoolId && u.UserType == SuperAdminUserType));

        public Task<int> CountSchoolsAsync(int? statusId, CancellationToken ct) =>
            TenantSchools().CountAsync(s => statusId == null || s.SchoolStatusId == statusId, ct);

        public async Task<SubscriptionTotals> GetTotalsAsync(DateOnly today, CancellationToken ct)
        {
            // One small row per subscription; the effective status (OVERDUE is derived) is worked out here so it matches the school list
            var subs = await _db.SchoolSubscriptions.AsNoTracking()
                .Select(s => new
                {
                    s.Status,
                    s.TrialEndDate,
                    HasOverdue = _db.SubscriptionInvoices.Any(i => i.SchoolId == s.SchoolId && i.DueDate < today
                        && (ActiveAllocations.Where(a => a.InvoiceId == i.InvoiceId).Sum(a => (decimal?)a.AllocatedAmount) ?? 0m) < i.NetAmount)
                }).ToListAsync(ct);

            var trial = subs.Count(s => s.Status == Trial);
            var cancelled = subs.Count(s => s.Status == Cancelled);
            var running = subs.Where(s => s.Status is Active or Overdue).ToList();
            var overdue = running.Count(s => s.HasOverdue);
            var trialExpired = subs.Count(s => s.Status == Trial && s.TrialEndDate < today);
            var withoutRow = await TenantSchools().CountAsync(s => !_db.SchoolSubscriptions.Any(x => x.SchoolId == s.SchoolId), ct);

            var collected = await _db.SubscriptionPayments.AsNoTracking().Where(p => p.Status == PaymentActive).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

            var invoices = InvoiceQuery().Where(x => x.Paid < x.Invoice.NetAmount);
            var outstanding = await invoices.SumAsync(x => (decimal?)(x.Invoice.NetAmount - x.Paid), ct) ?? 0m;
            var overdueAmount = await invoices.Where(x => x.Invoice.DueDate < today).SumAsync(x => (decimal?)(x.Invoice.NetAmount - x.Paid), ct) ?? 0m;

            return new SubscriptionTotals(trial, running.Count - overdue, overdue, cancelled, withoutRow, trialExpired, collected, outstanding, overdueAmount);
        }

        public Task<int> CountSchoolsCreatedBeforeAsync(DateTime beforeUtc, CancellationToken ct) =>
            TenantSchools().CountAsync(s => s.CreatedAt < beforeUtc, ct);

        public Task<List<DateTime>> GetSchoolCreatedAtAsync(DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct) =>
            TenantSchools().Where(s => s.CreatedAt >= fromUtc && s.CreatedAt < toExclusiveUtc).Select(s => s.CreatedAt).ToListAsync(ct);

        public async Task<List<RecentSchoolRow>> GetRecentSchoolsAsync(int take, CancellationToken ct)
        {
            var rows = await (from s in TenantSchools()
                              join b in _db.LutBoardTypes.AsNoTracking() on s.BoardTypeId equals (long?)b.BoardTypeId into bg
                              from b in bg.DefaultIfEmpty()
                              join t in _db.LutSchoolTypes.AsNoTracking() on s.SchoolTypeId equals (long?)t.SchoolTypeId into tg
                              from t in tg.DefaultIfEmpty()
                              join st in _db.LutStatus.AsNoTracking() on s.SchoolStatusId equals (int?)st.Sid into sg
                              from st in sg.DefaultIfEmpty()
                              orderby s.CreatedAt descending, s.SchoolId descending
                              select new { s.SchoolId, s.SchoolCode, s.SchoolName, Board = b.BoardName, Type = t.SchoolTypeName, s.CreatedAt, Status = st.Sname })
                             .Take(take).ToListAsync(ct);
            return rows.Select(r => new RecentSchoolRow(r.SchoolId, r.SchoolCode, r.SchoolName, r.Board, r.Type, r.CreatedAt, r.Status)).ToList();
        }

        public async Task<List<RecentPaymentRow>> GetRecentPaymentsAsync(DateTime fromUtc, DateTime toExclusiveUtc, int take, CancellationToken ct)
        {
            var rows = await (from p in _db.SubscriptionPayments.AsNoTracking()
                              join s in _db.Schools.AsNoTracking() on p.SchoolId equals s.SchoolId
                              where p.PaymentDate >= fromUtc && p.PaymentDate < toExclusiveUtc
                              orderby p.PaymentDate descending, p.PaymentId descending
                              select new { p.PaymentId, p.SchoolId, s.SchoolName, p.PaymentDate, p.Amount, p.PaymentMode, p.ReferenceNumber, p.Status })
                             .Take(take).ToListAsync(ct);
            return rows.Select(r => new RecentPaymentRow(r.PaymentId, r.SchoolId, r.SchoolName, r.PaymentDate, r.Amount, r.PaymentMode, r.ReferenceNumber, r.Status)).ToList();
        }

        public async Task<List<InvoiceAmounts>> GetInvoiceAmountsAsync(DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct)
        {
            var rows = await InvoiceQuery().Where(x => x.Invoice.CreatedAt >= fromUtc && x.Invoice.CreatedAt < toExclusiveUtc)
                .Select(x => new { x.Invoice.NetAmount, x.Invoice.DueDate, x.Paid }).ToListAsync(ct);
            return rows.Select(r => new InvoiceAmounts(r.NetAmount, r.DueDate, r.Paid)).ToList();
        }

        public async Task<(List<RenewalRow> Rows, int Total)> GetUpcomingRenewalsAsync(DateOnly today, DateOnly until, int take, CancellationToken ct)
        {
            var q = from sub in _db.SchoolSubscriptions.AsNoTracking()
                    join s in TenantSchools() on sub.SchoolId equals s.SchoolId
                    where (sub.Status == Active || sub.Status == Overdue) && sub.NextRenewalDate != null && sub.NextRenewalDate >= today && sub.NextRenewalDate <= until
                    select new { s.SchoolId, s.SchoolName, sub.BillingFrequency, Next = sub.NextRenewalDate!.Value };
            var total = await q.CountAsync(ct);
            var rows = await q.OrderBy(x => x.Next).ThenBy(x => x.SchoolName).Take(take).ToListAsync(ct);
            return (rows.Select(x => new RenewalRow(x.SchoolId, x.SchoolName, x.BillingFrequency, x.Next)).ToList(), total);
        }

        public async Task<(List<SchoolSubscriptionRow> Rows, int Total)> ListSchoolSubscriptionsAsync(string? search, string? status, DateOnly today, int page, int pageSize, CancellationToken ct)
        {
            var q = from s in TenantSchools()
                    join sub in _db.SchoolSubscriptions.AsNoTracking() on s.SchoolId equals sub.SchoolId into g
                    from sub in g.DefaultIfEmpty()
                    select new
                    {
                        s.SchoolId, s.SchoolCode, s.SchoolName, s.SchoolStatusId, Sub = sub,
                        HasOverdue = _db.SubscriptionInvoices.Any(i => i.SchoolId == s.SchoolId && i.DueDate < today
                            && (ActiveAllocations.Where(a => a.InvoiceId == i.InvoiceId).Sum(a => (decimal?)a.AllocatedAmount) ?? 0m) < i.NetAmount)
                    };

            if (!string.IsNullOrWhiteSpace(search))
            {
                var like = "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                q = q.Where(x => EF.Functions.ILike(x.SchoolName, like) || EF.Functions.ILike(x.SchoolCode, like) || EF.Functions.ILike(x.SchoolId, like));
            }
            // Same meaning as SubscriptionRules.EffectiveStatus
            q = status switch
            {
                "NOT_SET" => q.Where(x => x.Sub == null),
                Trial => q.Where(x => x.Sub != null && x.Sub.Status == Trial),
                Cancelled => q.Where(x => x.Sub != null && x.Sub.Status == Cancelled),
                Active => q.Where(x => x.Sub != null && (x.Sub.Status == Active || x.Sub.Status == Overdue) && !x.HasOverdue),
                Overdue => q.Where(x => x.Sub != null && (x.Sub.Status == Active || x.Sub.Status == Overdue) && x.HasOverdue),
                _ => q
            };

            var total = await q.CountAsync(ct);
            var rows = await q.OrderBy(x => x.SchoolName).ThenBy(x => x.SchoolId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return (rows.Select(x => new SchoolSubscriptionRow(x.SchoolId, x.SchoolCode, x.SchoolName, x.SchoolStatusId, x.Sub, x.HasOverdue)).ToList(), total);
        }

        public async Task<Dictionary<string, SchoolHeadcount>> GetHeadcountsAsync(IReadOnlyCollection<string> schoolIds, int activeStatusId, CancellationToken ct)
        {
            var ids = schoolIds.ToArray();
            var result = ids.ToDictionary(id => id, _ => new SchoolHeadcount(0, 0, 0));
            if (ids.Length == 0) return result;

            var employees = await (from e in _db.Employees.AsNoTracking()
                                   join es in _db.LutEmploymentStatuses.AsNoTracking() on e.EmploymentStatusId equals es.EmploymentStatusId
                                   where ids.Contains(e.SchoolId) && e.StatusId == activeStatusId && !es.BlocksLogin
                                   group e by e.SchoolId into g
                                   select new { SchoolId = g.Key, Teachers = g.Count(x => x.EmployeeType == "Teaching"), Staff = g.Count(x => x.EmployeeType != "Teaching") }).ToListAsync(ct);
            foreach (var x in employees) result[x.SchoolId] = result[x.SchoolId] with { Teachers = x.Teachers, Staff = x.Staff };

            var students = await (from en in _db.StudentEnrollments.AsNoTracking()
                                  join y in _db.AcademicYears.AsNoTracking() on en.AcademicYearId equals y.AcademicYearId
                                  join st in _db.Students.AsNoTracking() on en.StudentId equals st.StudentId
                                  where ids.Contains(en.SchoolId) && y.SchoolId == en.SchoolId && y.IsCurrent && en.StatusId == activeStatusId && st.StatusId == activeStatusId
                                  group en.StudentId by en.SchoolId into g
                                  select new { SchoolId = g.Key, Count = g.Distinct().Count() }).ToListAsync(ct);
            foreach (var x in students) result[x.SchoolId] = result[x.SchoolId] with { Students = x.Count };
            return result;
        }
    }
}
