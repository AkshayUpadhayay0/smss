using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // ---------------------------------------------------------------- pricing
    public class SubscriptionPricingDto
    {
        public long PricingId { get; set; }
        public decimal RatePerStudentMonthly { get; set; }
        public decimal YearlyDiscountPercent { get; set; }
        public int TrialDays { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SubscriptionPricingViewDto
    {
        public SubscriptionPricingDto? Current { get; set; }
        /// <summary>Every version, newest first (the current one included).</summary>
        public List<SubscriptionPricingDto> History { get; set; } = new();
    }

    /// <summary>Saving creates a NEW pricing version and deactivates the old one. It only affects schools registered, and invoices generated, afterwards.</summary>
    public class UpdateSubscriptionPricingRequest
    {
        [Required][Range(0, 99999999.99)] public decimal? RatePerStudentMonthly { get; set; }
        [Required][Range(0, 100)] public decimal? YearlyDiscountPercent { get; set; }
        [Required][Range(0, 3650)] public int? TrialDays { get; set; }
    }

    // ---------------------------------------------------------------- school subscription
    /// <summary>A school's negotiated terms. Every field is optional: null rate / discount = use the global pricing; staff default to not billed.</summary>
    public class BillingTermsRequest
    {
        [Range(0, 99999999.99)] public decimal? CustomRatePerStudentMonthly { get; set; }
        [Range(0, 100)] public decimal? CustomYearlyDiscountPercent { get; set; }
        public bool? IncludeStaffInBilling { get; set; }
    }

    public class ConfigureBillingRequest : BillingTermsRequest
    {
        /// <summary>MONTHLY or YEARLY.</summary>
        [Required] public string? BillingFrequency { get; set; }
    }

    public class SchoolSubscriptionDto
    {
        public long SubscriptionId { get; set; }
        public string SchoolId { get; set; } = null!;
        /// <summary>The stored state: TRIAL, ACTIVE, OVERDUE or CANCELLED.</summary>
        public string Status { get; set; } = null!;
        public string? BillingFrequency { get; set; }
        /// <summary>The school's own rate; null = the global rate applies.</summary>
        public decimal? CustomRatePerStudentMonthly { get; set; }
        /// <summary>The school's own yearly discount; null = the global discount applies.</summary>
        public decimal? CustomYearlyDiscountPercent { get; set; }
        public bool IncludeStaffInBilling { get; set; }
        /// <summary>What the next invoice would use: the custom value, else the global one (null only if no pricing row exists).</summary>
        public decimal? EffectiveRatePerStudentMonthly { get; set; }
        public decimal? EffectiveYearlyDiscountPercent { get; set; }
        public DateOnly TrialStartDate { get; set; }
        public DateOnly TrialEndDate { get; set; }
        /// <summary>Computed, not stored: still TRIAL and today is past the trial end date.</summary>
        public bool TrialExpired { get; set; }
        public DateOnly? CurrentCycleStartDate { get; set; }
        public DateOnly? CurrentCycleEndDate { get; set; }
        public DateOnly? NextRenewalDate { get; set; }
    }

    // ---------------------------------------------------------------- invoices
    public class GenerateInvoiceRequest
    {
        /// <summary>Optional. Defaults to the later of today and the billing period start. Cannot be in the past.</summary>
        public DateOnly? DueDate { get; set; }
    }

    public class SubscriptionPaymentAllocationDto
    {
        public long InvoiceId { get; set; }
        public decimal AllocatedAmount { get; set; }
    }

    public class SubscriptionPaymentDto
    {
        public long PaymentId { get; set; }
        public string SchoolId { get; set; } = null!;
        public DateTime PaymentDate { get; set; }
        /// <summary>The whole transaction; the allocations add up to exactly this.</summary>
        public decimal Amount { get; set; }
        public string PaymentMode { get; set; } = null!;
        public string? ReferenceNumber { get; set; }
        public string? Remarks { get; set; }
        /// <summary>Active or Voided.</summary>
        public string Status { get; set; } = null!;
        public DateTime? VoidedAt { get; set; }
        public string? VoidReason { get; set; }
        /// <summary>How the payment was split over invoices (kept when it is voided).</summary>
        public List<SubscriptionPaymentAllocationDto> Allocations { get; set; } = new();
    }

    public class SubscriptionInvoiceDto
    {
        public long InvoiceId { get; set; }
        public string SchoolId { get; set; } = null!;
        public string SchoolName { get; set; } = null!;
        public long SubscriptionId { get; set; }
        public DateOnly BillingPeriodStart { get; set; }
        public DateOnly BillingPeriodEnd { get; set; }
        public string BillingFrequency { get; set; } = null!;
        /// <summary>Head counts at generation, all stored whatever was billed.</summary>
        public int StudentCount { get; set; }
        public int TeacherCount { get; set; }
        public int StaffCount { get; set; }
        /// <summary>True when teachers and staff were billed as well as students.</summary>
        public bool StaffBilled { get; set; }
        /// <summary>What was priced: students, plus teachers and staff when StaffBilled.</summary>
        public int BillableUserCount { get; set; }
        /// <summary>The rate actually used (the school's own or the global one), locked at generation.</summary>
        public decimal RatePerStudent { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal NetAmount { get; set; }
        public DateOnly DueDate { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>Sum of the Active payments.</summary>
        public decimal PaidAmount { get; set; }
        public decimal BalanceAmount { get; set; }
        /// <summary>Pending, Paid or Overdue: derived from the payments and the due date, never stored.</summary>
        public string Status { get; set; } = null!;
        /// <summary>Only on the single-invoice endpoint: every payment that has an allocation to this invoice (newest first, voided ones included).</summary>
        public List<SubscriptionPaymentDto>? Payments { get; set; }
    }

    public class SubscriptionInvoicePagedDto
    {
        public List<SubscriptionInvoiceDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    // ---------------------------------------------------------------- payments
    public class SubscriptionPaymentAllocationRequest
    {
        [Required][Range(1, long.MaxValue)] public long? InvoiceId { get; set; }
        [Required][Range(0.01, 9999999999.99)] public decimal? Amount { get; set; }
    }

    public class RecordSubscriptionPaymentRequest
    {
        [Required][StringLength(100, MinimumLength = 1)] public string? SchoolId { get; set; }
        /// <summary>The total received. May cover several invoices.</summary>
        [Required][Range(0.01, 9999999999.99)] public decimal? Amount { get; set; }
        /// <summary>Cash, Cheque, UPI, Card, Bank Transfer or Online.</summary>
        [Required] public string? PaymentMode { get; set; }
        [StringLength(100)] public string? ReferenceNumber { get; set; }
        public string? Remarks { get; set; }
        /// <summary>Optional explicit split. Must add up to Amount exactly. Omit it to pay the school's oldest unpaid invoices first.</summary>
        public List<SubscriptionPaymentAllocationRequest>? Allocations { get; set; }
    }

    public class VoidSubscriptionPaymentRequest
    {
        [Required] public string? VoidReason { get; set; }
    }

    // ---------------------------------------------------------------- Super Admin dashboard
    public class SuperAdminSummaryDto
    {
        public DateOnly AsOf { get; set; }

        // A. School management
        public int TotalSchools { get; set; }
        /// <summary>Schools whose own (operational) status is Active: not the subscription status.</summary>
        public int ActiveSchools { get; set; }

        // B. Subscription & payment management. Counts add up to TotalSchools.
        public int TrialCount { get; set; }
        public int ActiveCount { get; set; }
        /// <summary>Derived: a running subscription with at least one overdue invoice.</summary>
        public int OverdueCount { get; set; }
        public int CancelledCount { get; set; }
        /// <summary>Schools registered before billing existed (no subscription row yet).</summary>
        public int NotSetCount { get; set; }
        /// <summary>Of the TRIAL schools, how many are past their trial end date.</summary>
        public int TrialExpiredCount { get; set; }
        public decimal TotalCollected { get; set; }
        /// <summary>What is still owed on unpaid and part-paid invoices (net amount minus Active payments).</summary>
        public decimal TotalOutstanding { get; set; }
        public decimal OverdueAmount { get; set; }
        public int UpcomingRenewalDays { get; set; }
        public int UpcomingRenewalCount { get; set; }
        /// <summary>The next few, soonest first (UpcomingRenewalCount has the full number).</summary>
        public List<UpcomingRenewalDto> UpcomingRenewals { get; set; } = new();
        public SubscriptionPricingDto? Pricing { get; set; }
    }

    public class UpcomingRenewalDto
    {
        public string SchoolId { get; set; } = null!;
        public string SchoolName { get; set; } = null!;
        public string? BillingFrequency { get; set; }
        public DateOnly NextRenewalDate { get; set; }
    }

    public class SchoolSubscriptionRowDto
    {
        public string SchoolId { get; set; } = null!;
        public string SchoolCode { get; set; } = null!;
        public string SchoolName { get; set; } = null!;
        /// <summary>The school's own status id (tb_schools.school_status_id): Active / Inactive, separate from the subscription.</summary>
        public int? SchoolStatusId { get; set; }
        /// <summary>TRIAL, ACTIVE, OVERDUE (derived), CANCELLED, or NOT_SET when the school has no subscription row.</summary>
        public string SubscriptionStatus { get; set; } = null!;
        public string? BillingFrequency { get; set; }
        public DateOnly? TrialEndDate { get; set; }
        public bool TrialExpired { get; set; }
        public DateOnly? NextRenewalDate { get; set; }
        /// <summary>The school's own terms (null / false = the global terms).</summary>
        public decimal? CustomRatePerStudentMonthly { get; set; }
        public decimal? CustomYearlyDiscountPercent { get; set; }
        public bool IncludeStaffInBilling { get; set; }
        /// <summary>Live head counts: active students of the current academic year, active teachers, active non-teaching staff.</summary>
        public int StudentCount { get; set; }
        public int TeacherCount { get; set; }
        public int StaffCount { get; set; }
        /// <summary>Live: what an invoice generated now would bill under the school's terms (students, plus teachers and staff when included).</summary>
        public int BillableUserCount { get; set; }
    }

    public class SchoolSubscriptionPagedDto
    {
        public List<SchoolSubscriptionRowDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
