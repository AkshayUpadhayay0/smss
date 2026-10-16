using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    /// <summary>An installment with the names a receipt needs.</summary>
    public class DueDetailRow
    {
        public long FeeDueId { get; set; }
        public string FeeTypeName { get; set; } = null!;
        public string FrequencyName { get; set; } = null!;
        public DateOnly DueDate { get; set; }
        public decimal AmountDue { get; set; }
        public decimal AmountPaid { get; set; }
    }

    public class PaymentListRow
    {
        public TbFeePayment Payment { get; set; } = null!;
        public string? CollectedByName { get; set; }
        public string? VoidedByName { get; set; }
        public int DueCount { get; set; }
    }

    public class ReceiptSchoolRow
    {
        public string SchoolName { get; set; } = null!;
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? LogoUrl { get; set; }
    }

    // Every method takes the school id: another school's payments and installments can never be reached through this repository.
    public interface IFeePaymentRepository
    {
        Task<TbStudent?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct);
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);

        /// <summary>
        /// The student's installments with the given ids (TRACKED), locked FOR UPDATE until the transaction ends, so two payments
        /// recorded at the same moment cannot both pay the same installment. Must be called inside a transaction.
        /// </summary>
        Task<List<TbStudentFeeDue>> LockDuesAsync(string schoolId, string studentId, IReadOnlyCollection<long> dueIds, CancellationToken ct);

        /// <summary>All of the student's installments that still have something outstanding, oldest first (TRACKED, locked FOR UPDATE).</summary>
        Task<List<TbStudentFeeDue>> LockOutstandingDuesAsync(string schoolId, string studentId, CancellationToken ct);

        /// <summary>One payment of this school (TRACKED), locked FOR UPDATE until the transaction ends, so two voids of the same payment cannot both run. Inside a transaction.</summary>
        Task<TbFeePayment?> LockPaymentAsync(string schoolId, long paymentId, CancellationToken ct);
        Task<List<TbFeePaymentAllocation>> GetAllocationsAsync(long paymentId, CancellationToken ct);

        /// <summary>The highest receipt sequence already used with this prefix (e.g. "RCP-2026-"), or 0.</summary>
        Task<int> GetMaxReceiptSequenceAsync(string schoolId, string prefix, CancellationToken ct);
        Task AddPaymentAsync(TbFeePayment payment, CancellationToken ct);
        Task<int> SaveChangesAsync(CancellationToken ct);
        void ClearTracking();

        Task<List<PaymentListRow>> GetPaymentsAsync(string schoolId, string studentId, CancellationToken ct);
        Task<TbFeePayment?> GetPaymentAsync(string schoolId, long paymentId, CancellationToken ct);
        Task<List<DueDetailRow>> GetDueDetailsAsync(IReadOnlyCollection<long> dueIds, CancellationToken ct);
        Task<ReceiptSchoolRow?> GetReceiptSchoolAsync(string schoolId, CancellationToken ct);
        Task<string?> GetUserNameAsync(long userId, CancellationToken ct);
    }
}
