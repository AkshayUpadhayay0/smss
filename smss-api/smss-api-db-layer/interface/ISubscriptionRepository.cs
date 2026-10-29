using Microsoft.EntityFrameworkCore.Storage;
using smss_api_db_layer.entity;

namespace smss_api_db_layer.@interface
{
    public record BillableCounts(int Teachers, int Staff, int Students, bool HasCurrentYear);

    public record InvoiceRow(TbSubscriptionInvoice Invoice, string SchoolName, decimal PaidAmount);

    /// <summary>One school on the Super Admin dashboard. Subscription is null for a school with no subscription row.</summary>
    public record SchoolSubscriptionRow(string SchoolId, string SchoolCode, string SchoolName, int? SchoolStatusId, TbSchoolSubscription? Subscription, bool HasOverdueInvoice);

    public record SubscriptionTotals(int Trial, int Active, int Overdue, int Cancelled, int NotSet, int TrialExpired, decimal Collected, decimal Outstanding, decimal OverdueAmount);

    /// <summary>Students that are billed, plus the teacher / staff head count that is shown for information only.</summary>
    public record SchoolHeadcount(int Students, int Teachers, int Staff);

    public record RecentSchoolRow(string SchoolId, string SchoolCode, string SchoolName, string? BoardName, string? SchoolTypeName, DateTime CreatedAt, string? StatusName);

    public record RecentPaymentRow(long PaymentId, string SchoolId, string SchoolName, DateTime PaymentDate, decimal Amount, string PaymentMode, string? ReferenceNumber, string Status);

    /// <summary>One invoice's size, due date and what Active payments have covered, for classifying it on the dashboard.</summary>
    public record InvoiceAmounts(decimal NetAmount, DateOnly DueDate, decimal Paid);

    public record RenewalRow(string SchoolId, string SchoolName, string? BillingFrequency, DateOnly NextRenewalDate);

    public interface ISubscriptionRepository
    {
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);

        // pricing
        Task<TbSubscriptionPricing?> GetActivePricingAsync(CancellationToken ct);
        Task<List<TbSubscriptionPricing>> GetPricingHistoryAsync(CancellationToken ct);
        /// <summary>Deactivates the current Active row and inserts <paramref name="next"/> as the new one, in one transaction.</summary>
        Task ReplaceActivePricingAsync(TbSubscriptionPricing next, CancellationToken ct);

        // subscriptions
        Task<bool> SchoolExistsAsync(string schoolId, CancellationToken ct);
        Task<TbSchoolSubscription?> GetSubscriptionAsync(string schoolId, CancellationToken ct);
        /// <summary>Tracked and row-locked (FOR UPDATE), so two Super Admin actions on the same school (generate, record a payment) run one after the other. Needs an open transaction.</summary>
        Task<TbSchoolSubscription?> LockSubscriptionAsync(string schoolId, CancellationToken ct);
        Task<int?> GetStatusIdByNameAsync(string statusName, string statusType, CancellationToken ct);
        Task<BillableCounts> CountBillableUsersAsync(string schoolId, int activeStatusId, CancellationToken ct);

        // invoices
        Task<TbSubscriptionInvoice?> GetLatestInvoiceAsync(long subscriptionId, CancellationToken ct);
        /// <summary>Sum of this invoice's allocations from Active payments.</summary>
        Task<decimal> GetPaidAmountAsync(long invoiceId, CancellationToken ct);
        /// <summary>A school's invoices with what is paid on each, oldest billing period first; only <paramref name="invoiceIds"/> when given.</summary>
        Task<List<InvoiceRow>> GetSchoolInvoicesAsync(string schoolId, IReadOnlyCollection<long>? invoiceIds, CancellationToken ct);
        void AddInvoice(TbSubscriptionInvoice invoice);
        Task<InvoiceRow?> GetInvoiceAsync(long invoiceId, CancellationToken ct);
        Task<(List<InvoiceRow> Rows, int Total)> ListInvoicesAsync(string? schoolId, string? status, DateOnly today, int page, int pageSize, CancellationToken ct);

        // payments
        Task<TbSubscriptionPayment?> LockPaymentAsync(long paymentId, CancellationToken ct);
        /// <summary>Every payment that has an allocation to this invoice (voided ones too), newest first, each with ALL its allocations.</summary>
        Task<List<TbSubscriptionPayment>> GetPaymentsForInvoiceAsync(long invoiceId, CancellationToken ct);
        Task<List<TbSubscriptionPaymentAllocation>> GetAllocationsAsync(long paymentId, CancellationToken ct);
        /// <summary>Adds the payment together with its Allocations.</summary>
        void AddPayment(TbSubscriptionPayment payment);

        // dashboard
        Task<int> CountSchoolsAsync(int? statusId, CancellationToken ct);
        Task<SubscriptionTotals> GetTotalsAsync(DateOnly today, CancellationToken ct);
        /// <summary>Customer schools registered before this instant.</summary>
        Task<int> CountSchoolsCreatedBeforeAsync(DateTime beforeUtc, CancellationToken ct);
        /// <summary>When each customer school registered, in [fromUtc, toExclusiveUtc). The service buckets them by India month.</summary>
        Task<List<DateTime>> GetSchoolCreatedAtAsync(DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct);
        Task<List<RecentSchoolRow>> GetRecentSchoolsAsync(int take, CancellationToken ct);
        /// <summary>Latest payments (voided ones too, with their status) dated in [fromUtc, toExclusiveUtc).</summary>
        Task<List<RecentPaymentRow>> GetRecentPaymentsAsync(DateTime fromUtc, DateTime toExclusiveUtc, int take, CancellationToken ct);
        /// <summary>Invoices generated in [fromUtc, toExclusiveUtc).</summary>
        Task<List<InvoiceAmounts>> GetInvoiceAmountsAsync(DateTime fromUtc, DateTime toExclusiveUtc, CancellationToken ct);
        Task<(List<RenewalRow> Rows, int Total)> GetUpcomingRenewalsAsync(DateOnly today, DateOnly until, int take, CancellationToken ct);
        Task<(List<SchoolSubscriptionRow> Rows, int Total)> ListSchoolSubscriptionsAsync(string? search, string? status, DateOnly today, int page, int pageSize, CancellationToken ct);
        /// <summary>Live head counts per school: billable students (active, current academic year) and, for information, active teachers and staff.</summary>
        Task<Dictionary<string, SchoolHeadcount>> GetHeadcountsAsync(IReadOnlyCollection<string> schoolIds, int activeStatusId, CancellationToken ct);

        Task<int> SaveChangesAsync(CancellationToken ct);
    }
}
