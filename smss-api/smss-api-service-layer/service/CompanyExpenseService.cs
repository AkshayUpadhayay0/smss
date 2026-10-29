using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class CompanyExpenseService : ICompanyExpenseService
    {
        private const int MaxPageSize = 100;

        private readonly ICompanyFinanceRepository _repo;
        private readonly ILogger<CompanyExpenseService> _logger;

        public CompanyExpenseService(ICompanyFinanceRepository repo, ILogger<CompanyExpenseService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<List<ExpenseCategoryDto>>> GetCategoriesAsync(CancellationToken ct)
        {
            try
            {
                var rows = await _repo.GetActiveCategoriesAsync(ct);
                return Result(200, "Successfully fetched", rows.Select(c => new ExpenseCategoryDto { CategoryId = c.CategoryId, CategoryName = c.CategoryName, CategoryCode = c.CategoryCode }).ToList());
            }
            catch (Exception ex) { return Error<List<ExpenseCategoryDto>>(ex, "fetching expense categories"); }
        }

        public async Task<ApiResponse<CompanyExpenseDto>> CreateAsync(long recordedByUserId, CreateCompanyExpenseRequest req, CancellationToken ct)
        {
            var mode = PaymentModes.Canonical(req.PaymentMode);
            if (mode == null) return Result<CompanyExpenseDto>(400, $"Payment mode must be one of: {string.Join(", ", PaymentModes.All)}.", null);
            var description = req.Description!.Trim();
            if (description.Length == 0) return Result<CompanyExpenseDto>(400, "A description is required.", null);
            var amount = Math.Round(req.Amount!.Value, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0) return Result<CompanyExpenseDto>(400, "The amount must be greater than zero.", null);
            if (req.ExpenseDate!.Value > SchoolClock.Today()) return Result<CompanyExpenseDto>(400, "The expense date can't be in the future.", null);
            try
            {
                if (!await _repo.CategoryUsableAsync(req.CategoryId!.Value, ct)) return Result<CompanyExpenseDto>(400, "Choose a valid expense category.", null);

                var now = DateTime.UtcNow;
                var expense = new TbCompanyExpense
                {
                    ExpenseDate = req.ExpenseDate.Value, CategoryId = req.CategoryId.Value, Description = description, Amount = amount, PaymentMode = mode,
                    RecordedBy = recordedByUserId, Status = PaymentStatuses.Active, CreatedAt = now, UpdatedAt = now
                };
                _repo.AddExpense(expense);
                await _repo.SaveChangesAsync(ct);
                var row = await _repo.GetExpenseRowAsync(expense.ExpenseId, ct);
                return Result(201, "Expense recorded", ToDto(row!));
            }
            catch (Exception ex) { return Error<CompanyExpenseDto>(ex, "recording a company expense"); }
        }

        public async Task<ApiResponse<CompanyExpensePagedDto>> ListAsync(long? categoryId, DateOnly? from, DateOnly? to, string? status, int page, int pageSize, CancellationToken ct)
        {
            if (from != null && to != null && from > to) return Result<CompanyExpensePagedDto>(400, "The 'from' date can't be after the 'to' date.", null);
            string? filter = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                filter = new[] { PaymentStatuses.Active, PaymentStatuses.Voided }.FirstOrDefault(s => string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));
                if (filter == null) return Result<CompanyExpensePagedDto>(400, "Status must be Active or Voided.", null);
            }
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
            try
            {
                var (rows, total, activeTotal) = await _repo.ListExpensesAsync(categoryId, from, to, filter, page, pageSize, ct);
                return Result(200, "Successfully fetched", new CompanyExpensePagedDto { Items = rows.Select(ToDto).ToList(), Total = total, Page = page, PageSize = pageSize, ActiveTotal = activeTotal });
            }
            catch (Exception ex) { return Error<CompanyExpensePagedDto>(ex, "listing company expenses"); }
        }

        public async Task<ApiResponse<CompanyExpenseDto>> GetAsync(long id, CancellationToken ct)
        {
            try
            {
                var row = await _repo.GetExpenseRowAsync(id, ct);
                return row == null ? Result<CompanyExpenseDto>(404, "Expense not found", null) : Result(200, "Successfully fetched", ToDto(row));
            }
            catch (Exception ex) { return Error<CompanyExpenseDto>(ex, "fetching a company expense"); }
        }

        // Soft void: the row stays (Voided, with who / when / why) and drops out of every total
        public async Task<ApiResponse<CompanyExpenseDto>> VoidAsync(long id, long voidedByUserId, VoidCompanyRecordRequest req, CancellationToken ct)
        {
            var reason = string.IsNullOrWhiteSpace(req.VoidReason) ? null : req.VoidReason.Trim();
            if (reason == null) return Result<CompanyExpenseDto>(400, "A reason is required to void an expense.", null);
            try
            {
                await using (var tx = await _repo.BeginTransactionAsync(ct))
                {
                    var expense = await _repo.LockExpenseAsync(id, ct);
                    if (expense == null) return Result<CompanyExpenseDto>(404, "Expense not found", null);
                    if (expense.Status == PaymentStatuses.Voided) return Result<CompanyExpenseDto>(409, "This expense is already voided.", null);

                    var now = DateTime.UtcNow;
                    expense.Status = PaymentStatuses.Voided;
                    expense.VoidedBy = voidedByUserId;
                    expense.VoidedAt = now;
                    expense.VoidReason = reason;
                    expense.UpdatedAt = now;
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                return Result(200, "Expense voided", ToDto((await _repo.GetExpenseRowAsync(id, ct))!));
            }
            catch (Exception ex) { return Error<CompanyExpenseDto>(ex, "voiding a company expense"); }
        }

        private static CompanyExpenseDto ToDto(ExpenseRow r) => new()
        {
            ExpenseId = r.Expense.ExpenseId, ExpenseDate = r.Expense.ExpenseDate, CategoryId = r.Expense.CategoryId, CategoryName = r.CategoryName,
            Description = r.Expense.Description, Amount = r.Expense.Amount, PaymentMode = r.Expense.PaymentMode, RecordedByName = r.RecordedByName,
            Status = r.Expense.Status, VoidedAt = r.Expense.VoidedAt, VoidReason = r.Expense.VoidReason, CreatedAt = r.Expense.CreatedAt
        };

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
