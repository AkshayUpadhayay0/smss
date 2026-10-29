using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    // SMSS's own business data. Super Admin only; the controllers check the role. No school is involved anywhere.

    public interface ICompanyEmployeeService
    {
        /// <summary>Everyone on the (small) company team. <paramref name="status"/> is Active or Inactive; blank = both.</summary>
        Task<ApiResponse<List<CompanyEmployeeDto>>> ListAsync(string? search, string? status, CancellationToken ct);
        Task<ApiResponse<CompanyEmployeeDto>> GetAsync(long id, CancellationToken ct);
        Task<ApiResponse<CompanyEmployeeDto>> CreateAsync(CompanyEmployeeRequest req, CancellationToken ct);
        /// <summary>Changing the salary here never rewrites a salary payment already recorded.</summary>
        Task<ApiResponse<CompanyEmployeeDto>> UpdateAsync(long id, CompanyEmployeeRequest req, CancellationToken ct);
        /// <summary>Active becomes Inactive and back: the hide-row pattern. Nothing is deleted.</summary>
        Task<ApiResponse<CompanyEmployeeDto>> ToggleStatusAsync(long id, CancellationToken ct);
    }

    public interface ICompanyExpenseService
    {
        Task<ApiResponse<List<ExpenseCategoryDto>>> GetCategoriesAsync(CancellationToken ct);
        Task<ApiResponse<CompanyExpenseDto>> CreateAsync(long recordedByUserId, CreateCompanyExpenseRequest req, CancellationToken ct);
        Task<ApiResponse<CompanyExpensePagedDto>> ListAsync(long? categoryId, DateOnly? from, DateOnly? to, string? status, int page, int pageSize, CancellationToken ct);
        Task<ApiResponse<CompanyExpenseDto>> GetAsync(long id, CancellationToken ct);
        Task<ApiResponse<CompanyExpenseDto>> VoidAsync(long id, long voidedByUserId, VoidCompanyRecordRequest req, CancellationToken ct);
    }

    public interface ICompanySalaryService
    {
        Task<ApiResponse<CompanySalaryPaymentDto>> RecordAsync(long recordedByUserId, RecordCompanySalaryRequest req, CancellationToken ct);
        Task<ApiResponse<CompanySalaryPaymentDto>> VoidAsync(long id, long voidedByUserId, VoidCompanyRecordRequest req, CancellationToken ct);
        Task<ApiResponse<CompanySalaryPagedDto>> ListAsync(long? companyEmployeeId, int? year, int? month, string? status, int page, int pageSize, CancellationToken ct);
    }

    public interface ICompanyFinanceService
    {
        /// <summary>Revenue, expenses, salary payouts and net profit for a date range, plus the same per month. Defaults to 1 January of this year .. today.</summary>
        Task<ApiResponse<CompanyFinanceOverviewDto>> GetOverviewAsync(DateOnly? from, DateOnly? to, CancellationToken ct);
    }
}
