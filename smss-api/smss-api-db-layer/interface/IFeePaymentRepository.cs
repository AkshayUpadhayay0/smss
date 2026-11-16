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
        public string? ConcessionAppliedByName { get; set; }
        public int DueCount { get; set; }
    }

    public class ReceiptSchoolRow
    {
        public string SchoolName { get; set; } = null!;
        public string? Gstin { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? LogoUrl { get; set; }
    }

    // Every method takes the school id: another school's payments and installments can never be reached through this repository.
    public interface IFeePaymentRepository
    {
        Task<TbStudent?> GetStudentAsync(string schoolId, string studentId, CancellationToken ct);
        /// <summary>One tax type of THIS school (active or not), or null: another school's id answers null too.</summary>
        Task<TbTaxType?> GetTaxTypeAsync(string schoolId, long taxTypeId, CancellationToken ct);
        /// <summary>One concession of THIS school (active or not), or null: another school's id answers null too.</summary>
        Task<TbFeeConcessions?> GetConcessionAsync(string schoolId, long concessionId, CancellationToken ct);
        /// <summary>The school's active concessions, by name (for the payment form).</summary>
        Task<List<ConcessionOptionRow>> GetActiveConcessionsAsync(string schoolId, CancellationToken ct);
        /// <summary>True when this student already has this concession assigned in tb_student_fee_concessions (it is already reducing their installments).</summary>
        Task<bool> HasAssignedConcessionAsync(string schoolId, string studentId, long concessionId, CancellationToken ct);
        /// <summary>Installment id -> the fee type it belongs to, for the installments asked about.</summary>
        Task<Dictionary<long, long>> GetDueFeeTypeIdsAsync(IReadOnlyCollection<long> dueIds, CancellationToken ct);
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

        /// <summary>Takes a transaction-scoped lock for this school's receipt numbers (released at commit/rollback), so concurrent payments queue up instead of colliding on the same number. Inside a transaction.</summary>
        Task LockReceiptNumbersAsync(string schoolId, CancellationToken ct);

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
        /// <summary>The login id (org_user_id) of a user of this school; for a Student login it IS the student id. Null when the user is not in this school.</summary>
        Task<string?> GetUserLoginIdAsync(string schoolId, long userId, CancellationToken ct);
    }
}
