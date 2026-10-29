using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace smss_api_db_layer.entity
{
    /// <summary>One of SMSS's own staff (not a school employee). Hidden with StatusId -> Inactive; never deleted.</summary>
    [Table("tb_company_employees", Schema = "public")]
    public class TbCompanyEmployee
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("company_employee_id")] public long CompanyEmployeeId { get; set; }
        [Column("first_name")][StringLength(100)] public string FirstName { get; set; } = null!;
        [Column("last_name")][StringLength(100)] public string LastName { get; set; } = null!;
        [Column("designation")][StringLength(100)] public string Designation { get; set; } = null!;
        [Column("email")][StringLength(150)] public string? Email { get; set; }
        [Column("mobile")][StringLength(20)] public string? Mobile { get; set; }
        [Column("date_of_joining", TypeName = "date")] public DateOnly DateOfJoining { get; set; }
        [Column("date_of_leaving", TypeName = "date")] public DateOnly? DateOfLeaving { get; set; }
        /// <summary>The current salary. The default of a new salary payment; changing it never touches a payment already recorded.</summary>
        [Column("monthly_salary", TypeName = "numeric(12,2)")] public decimal MonthlySalary { get; set; }
        [Column("status_id")] public int StatusId { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>Global, small and fixed list of expense categories.</summary>
    [Table("lut_expense_category", Schema = "public")]
    public class LutExpenseCategory
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("category_id")] public long CategoryId { get; set; }
        [Column("category_name")][StringLength(60)] public string CategoryName { get; set; } = null!;
        [Column("category_code")][StringLength(40)] public string CategoryCode { get; set; } = null!;
        [Column("is_active")] public bool IsActive { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>An expense of the company, logged directly by the Super Admin. Voiding keeps the row (Voided, with who / when / why).</summary>
    [Table("tb_company_expenses", Schema = "public")]
    public class TbCompanyExpense
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("expense_id")] public long ExpenseId { get; set; }
        [Column("expense_date", TypeName = "date")] public DateOnly ExpenseDate { get; set; }
        [Column("category_id")] public long CategoryId { get; set; }
        [Column("description")][StringLength(500)] public string Description { get; set; } = null!;
        [Column("amount", TypeName = "numeric(12,2)")] public decimal Amount { get; set; }
        [Column("payment_mode")][StringLength(30)] public string PaymentMode { get; set; } = null!;
        /// <summary>tb_users.user_id of who logged it, from the token.</summary>
        [Column("recorded_by")] public long? RecordedBy { get; set; }
        /// <summary>Active or Voided.</summary>
        [Column("status")][StringLength(20)] public string Status { get; set; } = "Active";
        [Column("voided_by")] public long? VoidedBy { get; set; }
        [Column("voided_at")] public DateTime? VoidedAt { get; set; }
        [Column("void_reason")] public string? VoidReason { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// A salary paid to a company employee for one month. The amount is locked when recorded. The database allows only ONE Active payment per
    /// employee per month (partial unique index); a voided month can be paid again.
    /// </summary>
    [Table("tb_company_salary_payments", Schema = "public")]
    public class TbCompanySalaryPayment
    {
        [Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)][Column("salary_payment_id")] public long SalaryPaymentId { get; set; }
        [Column("company_employee_id")] public long CompanyEmployeeId { get; set; }
        [Column("period_month")] public short PeriodMonth { get; set; }
        [Column("period_year")] public short PeriodYear { get; set; }
        [Column("amount", TypeName = "numeric(12,2)")] public decimal Amount { get; set; }
        [Column("payment_date", TypeName = "date")] public DateOnly PaymentDate { get; set; }
        [Column("payment_mode")][StringLength(30)] public string PaymentMode { get; set; } = null!;
        [Column("remarks")] public string? Remarks { get; set; }
        [Column("recorded_by")] public long? RecordedBy { get; set; }
        [Column("status")][StringLength(20)] public string Status { get; set; } = "Active";
        [Column("voided_by")] public long? VoidedBy { get; set; }
        [Column("voided_at")] public DateTime? VoidedAt { get; set; }
        [Column("void_reason")] public string? VoidReason { get; set; }
        [Column("created_at")] public DateTime CreatedAt { get; set; }
        [Column("updated_at")] public DateTime UpdatedAt { get; set; }
    }
}
