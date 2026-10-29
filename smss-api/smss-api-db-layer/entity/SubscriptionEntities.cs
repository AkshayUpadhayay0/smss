using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>
    /// What SMSS charges schools, per student (global, versioned). A change adds a new row and deactivates the old one; the database allows
    /// only ONE active row (partial unique index), so history stays queryable.
    /// </summary>
    [Table("tb_subscription_pricing", Schema = "public")]
    public class TbSubscriptionPricing
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("pricing_id")] public long PricingId { get; set; }
        [Column("rate_per_student_monthly", TypeName = "numeric(10,2)")] public decimal RatePerStudentMonthly { get; set; }
        [Column("yearly_discount_percent", TypeName = "numeric(5,2)")] public decimal YearlyDiscountPercent { get; set; }
        [Column("trial_days")] public int TrialDays { get; set; }
        [Column("effective_from", TypeName = "date")] public DateOnly EffectiveFrom { get; set; }
        [Column("is_active")] public bool IsActive { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }

    /// <summary>One subscription per school. Its status is the SUBSCRIPTION state, separate from tb_schools.school_status_id.</summary>
    [Table("tb_school_subscriptions", Schema = "public")]
    public class TbSchoolSubscription
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("subscription_id")] public long SubscriptionId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        // Declares the dependency so EF inserts the school BEFORE its subscription when both are saved together (registration)
        [ForeignKey(nameof(SchoolId))] public TbSchools? School { get; set; }
        /// <summary>TRIAL, ACTIVE, OVERDUE or CANCELLED (see SubscriptionStatuses).</summary>
        [Column("status")][StringLength(20)] public string Status { get; set; } = "TRIAL";
        /// <summary>MONTHLY or YEARLY; null until the school leaves the trial.</summary>
        [Column("billing_frequency")][StringLength(10)] public string? BillingFrequency { get; set; }
        /// <summary>This school's negotiated rate per student per month. Null = use the global pricing row.</summary>
        [Column("custom_rate_per_student_monthly", TypeName = "numeric(10,2)")] public decimal? CustomRatePerStudentMonthly { get; set; }
        /// <summary>This school's negotiated yearly discount. Null = use the global pricing row.</summary>
        [Column("custom_yearly_discount_percent", TypeName = "numeric(5,2)")] public decimal? CustomYearlyDiscountPercent { get; set; }
        /// <summary>True = the school's teachers and staff are billed like its students. Applies to invoices generated from now on.</summary>
        [Column("include_staff_in_billing")] public bool IncludeStaffInBilling { get; set; }
        [Column("trial_start_date", TypeName = "date")] public DateOnly TrialStartDate { get; set; }
        [Column("trial_end_date", TypeName = "date")] public DateOnly TrialEndDate { get; set; }
        [Column("current_cycle_start_date", TypeName = "date")] public DateOnly? CurrentCycleStartDate { get; set; }
        [Column("current_cycle_end_date", TypeName = "date")] public DateOnly? CurrentCycleEndDate { get; set; }
        [Column("next_renewal_date", TypeName = "date")] public DateOnly? NextRenewalDate { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// An invoice the Super Admin generated. All three head counts are stored for transparency; BillableUserCount is what was actually priced (students,
    /// plus teachers and staff when StaffBilled). Counts, rate and discount are locked at generation, whether they came from the global pricing or the
    /// school's own terms. There is no status column: it is derived from the allocations of Active payments + due date.
    /// </summary>
    [Table("tb_subscription_invoices", Schema = "public")]
    public class TbSubscriptionInvoice
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("invoice_id")] public long InvoiceId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("subscription_id")] public long SubscriptionId { get; set; }
        [Column("billing_period_start", TypeName = "date")] public DateOnly BillingPeriodStart { get; set; }
        [Column("billing_period_end", TypeName = "date")] public DateOnly BillingPeriodEnd { get; set; }
        [Column("billing_frequency")][StringLength(10)] public string BillingFrequency { get; set; } = null!;
        /// <summary>Active students of the current academic year when the invoice was generated.</summary>
        [Column("student_count")] public int StudentCount { get; set; }
        /// <summary>Active Teaching employees at generation.</summary>
        [Column("teacher_count")] public int TeacherCount { get; set; }
        /// <summary>Active Non-Teaching employees at generation.</summary>
        [Column("staff_count")] public int StaffCount { get; set; }
        /// <summary>Whether teachers and staff were part of BillableUserCount (the school's include-staff term at generation).</summary>
        [Column("staff_billed")] public bool StaffBilled { get; set; }
        /// <summary>What was priced: StudentCount, plus TeacherCount + StaffCount when StaffBilled.</summary>
        [Column("billable_user_count")] public int BillableUserCount { get; set; }
        [Column("rate_per_student", TypeName = "numeric(10,2)")] public decimal RatePerStudent { get; set; }
        [Column("discount_percent", TypeName = "numeric(5,2)")] public decimal DiscountPercent { get; set; }
        [Column("gross_amount", TypeName = "numeric(12,2)")] public decimal GrossAmount { get; set; }
        [Column("discount_amount", TypeName = "numeric(12,2)")] public decimal DiscountAmount { get; set; }
        [Column("net_amount", TypeName = "numeric(12,2)")] public decimal NetAmount { get; set; }
        [Column("due_date", TypeName = "date")] public DateOnly DueDate { get; set; }
        /// <summary>tb_users.user_id of the Super Admin who generated it, from the token.</summary>
        [Column("generated_by")] public long? GeneratedBy { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Money received from a school. One transaction, possibly covering several invoices (see Allocations, which add up to Amount exactly).
    /// Voiding keeps the row and its allocations (Voided, with who / when / why); it is never deleted.
    /// </summary>
    [Table("tb_subscription_payments", Schema = "public")]
    public class TbSubscriptionPayment
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("payment_id")] public long PaymentId { get; set; }
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("payment_date")] public DateTime PaymentDate { get; set; }
        [Column("amount", TypeName = "numeric(12,2)")] public decimal Amount { get; set; }
        [Column("payment_mode")][StringLength(30)] public string PaymentMode { get; set; } = null!;
        [Column("reference_number")][StringLength(100)] public string? ReferenceNumber { get; set; }
        [Column("recorded_by")] public long? RecordedBy { get; set; }
        [Column("remarks")] public string? Remarks { get; set; }
        /// <summary>Active or Voided.</summary>
        [Column("status")][StringLength(20)] public string Status { get; set; } = "Active";
        [Column("voided_by")] public long? VoidedBy { get; set; }
        [Column("voided_at")] public DateTime? VoidedAt { get; set; }
        [Column("void_reason")] public string? VoidReason { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }

        public ICollection<TbSubscriptionPaymentAllocation> Allocations { get; set; } = new List<TbSubscriptionPaymentAllocation>();
    }

    /// <summary>How much of a payment went to one invoice. Mirrors tb_fee_payment_allocations.</summary>
    [Table("tb_subscription_payment_allocations", Schema = "public")]
    public class TbSubscriptionPaymentAllocation
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("allocation_id")] public long AllocationId { get; set; }
        [Column("payment_id")] public long PaymentId { get; set; }
        [ForeignKey(nameof(PaymentId))] public TbSubscriptionPayment? Payment { get; set; }
        [Column("invoice_id")] public long InvoiceId { get; set; }
        /// <summary>Same school as the payment and the invoice (composite foreign keys in the database).</summary>
        [Column("school_id")][StringLength(100)] public string SchoolId { get; set; } = null!;
        [Column("allocated_amount", TypeName = "numeric(12,2)")] public decimal AllocatedAmount { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
    }
}
