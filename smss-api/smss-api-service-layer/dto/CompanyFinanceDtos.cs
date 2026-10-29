using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    // ---------------------------------------------------------------- company employees
    public class CompanyEmployeeDto
    {
        public long CompanyEmployeeId { get; set; }
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string Designation { get; set; } = null!;
        public string? Email { get; set; }
        public string? Mobile { get; set; }
        public DateOnly DateOfJoining { get; set; }
        public DateOnly? DateOfLeaving { get; set; }
        /// <summary>The current salary (what a new salary payment defaults to).</summary>
        public decimal MonthlySalary { get; set; }
        public int StatusId { get; set; }
        /// <summary>Active or Inactive.</summary>
        public string StatusName { get; set; } = null!;
    }

    /// <summary>Create and update share one shape. Blank optional strings become null.</summary>
    public class CompanyEmployeeRequest : IValidatableObject
    {
        [Required][StringLength(100)] public string? FirstName { get; set; }
        [Required][StringLength(100)] public string? LastName { get; set; }
        [Required][StringLength(100)] public string? Designation { get; set; }
        // Blank optional strings become null BEFORE validation (an empty "" would fail [EmailAddress])
        private string? _email;
        private string? _mobile;
        [EmailAddress][StringLength(150)] public string? Email { get => _email; set => _email = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        [StringLength(20)] public string? Mobile { get => _mobile; set => _mobile = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        [Required] public DateOnly? DateOfJoining { get; set; }
        public DateOnly? DateOfLeaving { get; set; }
        [Required][Range(0, 9999999999.99)] public decimal? MonthlySalary { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (DateOfLeaving != null && DateOfJoining != null && DateOfLeaving < DateOfJoining)
                yield return new ValidationResult("The leaving date cannot be before the joining date.", new[] { nameof(DateOfLeaving) });
        }
    }

    // ---------------------------------------------------------------- expenses
    public class ExpenseCategoryDto
    {
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = null!;
        public string CategoryCode { get; set; } = null!;
    }

    public class CreateCompanyExpenseRequest
    {
        [Required] public DateOnly? ExpenseDate { get; set; }
        [Required][Range(1, long.MaxValue)] public long? CategoryId { get; set; }
        [Required][StringLength(500)] public string? Description { get; set; }
        [Required][Range(0.01, 9999999999.99)] public decimal? Amount { get; set; }
        /// <summary>Cash, Cheque, UPI, Card, Bank Transfer or Online.</summary>
        [Required] public string? PaymentMode { get; set; }
    }

    public class CompanyExpenseDto
    {
        public long ExpenseId { get; set; }
        public DateOnly ExpenseDate { get; set; }
        public long CategoryId { get; set; }
        public string CategoryName { get; set; } = null!;
        public string Description { get; set; } = null!;
        public decimal Amount { get; set; }
        public string PaymentMode { get; set; } = null!;
        public string? RecordedByName { get; set; }
        /// <summary>Active or Voided.</summary>
        public string Status { get; set; } = null!;
        public DateTime? VoidedAt { get; set; }
        public string? VoidReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CompanyExpensePagedDto
    {
        public List<CompanyExpenseDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        /// <summary>Sum of the Active expenses that match the filters (all pages, voided ones excluded).</summary>
        public decimal ActiveTotal { get; set; }
    }

    public class VoidCompanyRecordRequest
    {
        [Required] public string? VoidReason { get; set; }
    }

    // ---------------------------------------------------------------- salary payments
    public class RecordCompanySalaryRequest
    {
        [Required][Range(1, long.MaxValue)] public long? CompanyEmployeeId { get; set; }
        [Required][Range(1, 12)] public int? PeriodMonth { get; set; }
        [Required][Range(2000, 2100)] public int? PeriodYear { get; set; }
        /// <summary>Omit to pay the employee's current monthly salary. Give it for a bonus month or a partial month.</summary>
        [Range(0.01, 9999999999.99)] public decimal? Amount { get; set; }
        /// <summary>Omit for today.</summary>
        public DateOnly? PaymentDate { get; set; }
        [Required] public string? PaymentMode { get; set; }
        public string? Remarks { get; set; }
    }

    public class CompanySalaryPaymentDto
    {
        public long SalaryPaymentId { get; set; }
        public long CompanyEmployeeId { get; set; }
        public string EmployeeName { get; set; } = null!;
        public int PeriodMonth { get; set; }
        public int PeriodYear { get; set; }
        public decimal Amount { get; set; }
        public DateOnly PaymentDate { get; set; }
        public string PaymentMode { get; set; } = null!;
        public string? Remarks { get; set; }
        public string? RecordedByName { get; set; }
        /// <summary>Active or Voided.</summary>
        public string Status { get; set; } = null!;
        public DateTime? VoidedAt { get; set; }
        public string? VoidReason { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CompanySalaryPagedDto
    {
        public List<CompanySalaryPaymentDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        /// <summary>Sum of the Active payments that match the filters (all pages).</summary>
        public decimal ActiveTotal { get; set; }
    }

    // ---------------------------------------------------------------- financial overview
    public class FinanceMonthDto
    {
        /// <summary>First day of the month.</summary>
        public DateOnly Month { get; set; }
        public string Label { get; set; } = null!;
        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }
        public decimal SalaryPayouts { get; set; }
        public decimal NetProfit { get; set; }
    }

    public class CompanyFinanceOverviewDto
    {
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
        /// <summary>Money actually collected from schools: Active subscription payments in the range (India date).</summary>
        public decimal Revenue { get; set; }
        /// <summary>Active company expenses dated in the range.</summary>
        public decimal Expenses { get; set; }
        /// <summary>Active salary payments with a payment date in the range.</summary>
        public decimal SalaryPayouts { get; set; }
        public decimal NetProfit { get; set; }
        /// <summary>The same three figures per calendar month across the range, zero-filled.</summary>
        public List<FinanceMonthDto> Months { get; set; } = new();
    }
}
