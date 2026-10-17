using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.context;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;

namespace smss_api_db_layer.repository
{
    public class FeePaymentRepository : IFeePaymentRepository
    {
        private readonly dbContext _db;
        public FeePaymentRepository(dbContext db) => _db = db;

        public Task<TbStudent?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.StudentId == studentId).FirstOrDefaultAsync(ct);

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) => _db.Database.BeginTransactionAsync(ct);

        public Task<List<TbStudentFeeDue>> LockDuesAsync(string schoolId, string studentId, IReadOnlyCollection<long> dueIds, CancellationToken ct)
        {
            var ids = dueIds.ToArray();
            return _db.StudentFeeDues
                .FromSqlInterpolated($@"SELECT * FROM public.tb_student_fee_dues
                                        WHERE school_id = {schoolId} AND student_id = {studentId} AND fee_due_id = ANY({ids})
                                        ORDER BY fee_due_id FOR UPDATE")
                .ToListAsync(ct);
        }

        public Task<List<TbStudentFeeDue>> LockOutstandingDuesAsync(string schoolId, string studentId, CancellationToken ct) =>
            _db.StudentFeeDues
                .FromSqlInterpolated($@"SELECT * FROM public.tb_student_fee_dues
                                        WHERE school_id = {schoolId} AND student_id = {studentId} AND amount_paid < amount_due
                                        ORDER BY due_date, fee_due_id FOR UPDATE")
                .ToListAsync(ct);

        public Task<TbFeePayment?> LockPaymentAsync(string schoolId, long paymentId, CancellationToken ct) =>
            _db.FeePayments
                .FromSqlInterpolated($@"SELECT * FROM public.tb_fee_payments WHERE school_id = {schoolId} AND payment_id = {paymentId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);

        public Task<List<TbFeePaymentAllocation>> GetAllocationsAsync(long paymentId, CancellationToken ct) =>
            _db.FeePaymentAllocations.AsNoTracking().Where(a => a.PaymentId == paymentId).OrderBy(a => a.AllocationId).ToListAsync(ct);

        public async Task LockReceiptNumbersAsync(string schoolId, CancellationToken ct)
        {
            var key = "receipt:" + schoolId;
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }

        public async Task<int> GetMaxReceiptSequenceAsync(string schoolId, string prefix, CancellationToken ct)
        {
            // Fixed-width numbers sort correctly by (length, value), so the biggest is simply the last of that order
            var last = await _db.FeePayments.AsNoTracking()
                .Where(p => p.SchoolId == schoolId && p.ReceiptNumber.StartsWith(prefix))
                .OrderByDescending(p => p.ReceiptNumber.Length).ThenByDescending(p => p.ReceiptNumber)
                .Select(p => p.ReceiptNumber)
                .FirstOrDefaultAsync(ct);
            return last != null && int.TryParse(last[prefix.Length..], out var n) ? n : 0;
        }

        public async Task AddPaymentAsync(TbFeePayment payment, CancellationToken ct) => await _db.FeePayments.AddAsync(payment, ct);
        public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
        public void ClearTracking() => _db.ChangeTracker.Clear();

        public async Task<List<PaymentListRow>> GetPaymentsAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var payments = await _db.FeePayments.AsNoTracking()
                .Where(p => p.SchoolId == schoolId && p.StudentId == studentId)
                .OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.PaymentId)
                .ToListAsync(ct);
            var ids = payments.Select(p => p.PaymentId).ToList();
            var counts = await _db.FeePaymentAllocations.AsNoTracking().Where(a => ids.Contains(a.PaymentId))
                .GroupBy(a => a.PaymentId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var userIds = payments.SelectMany(p => new[] { p.CollectedBy, p.VoidedBy }).Where(u => u != null).Select(u => u!.Value).Distinct().ToList();
            var names = userIds.Count == 0 ? new Dictionary<long, string>()
                : await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId, u => u.DisplayName ?? u.OrgUserId ?? string.Empty, ct);

            return payments.Select(p => new PaymentListRow
            {
                Payment = p,
                CollectedByName = p.CollectedBy is long u && names.TryGetValue(u, out var n) ? n : null,
                VoidedByName = p.VoidedBy is long v && names.TryGetValue(v, out var vn) ? vn : null,
                DueCount = counts.GetValueOrDefault(p.PaymentId)
            }).ToList();
        }

        public Task<TbFeePayment?> GetPaymentAsync(string schoolId, long paymentId, CancellationToken ct) =>
            _db.FeePayments.AsNoTracking().Include(p => p.Allocations)
                .Where(p => p.SchoolId == schoolId && p.PaymentId == paymentId).FirstOrDefaultAsync(ct);

        public async Task<List<DueDetailRow>> GetDueDetailsAsync(IReadOnlyCollection<long> dueIds, CancellationToken ct)
        {
            var rows = await (
                from d in _db.StudentFeeDues.AsNoTracking()
                join i in _db.StudentFeeItems.AsNoTracking() on d.StudentFeeItemId equals i.StudentFeeItemId
                join t in _db.FeeTypes.AsNoTracking() on i.FeeTypeId equals t.FeeTypeId
                join f in _db.FeeFrequencies.AsNoTracking() on i.FrequencyId equals f.FrequencyId
                where dueIds.Contains(d.FeeDueId)
                select new { d.FeeDueId, t.FeeTypeName, f.FrequencyName, d.DueDate, d.AmountDue, d.AmountPaid }).ToListAsync(ct);

            return rows.Select(r => new DueDetailRow
            {
                FeeDueId = r.FeeDueId, FeeTypeName = r.FeeTypeName, FrequencyName = r.FrequencyName,
                DueDate = r.DueDate, AmountDue = r.AmountDue, AmountPaid = r.AmountPaid
            }).ToList();
        }

        public async Task<ReceiptSchoolRow?> GetReceiptSchoolAsync(string schoolId, CancellationToken ct)
        {
            var s = await _db.Schools.AsNoTracking().Where(x => x.SchoolId == schoolId).FirstOrDefaultAsync(ct);
            if (s == null) return null;
            var address = string.Join(", ", new[] { s.AddressLine1, s.AddressLine2, s.Pincode }.Where(p => !string.IsNullOrWhiteSpace(p)));
            return new ReceiptSchoolRow
            {
                SchoolName = s.SchoolName, Address = address.Length == 0 ? null : address,
                Phone = s.MobileNumber, Email = s.Email, LogoUrl = s.LogoUrl
            };
        }

        public Task<string?> GetUserNameAsync(long userId, CancellationToken ct) =>
            _db.Users.AsNoTracking().Where(u => u.UserId == userId).Select(u => u.DisplayName ?? u.OrgUserId).FirstOrDefaultAsync(ct);
    }
}
