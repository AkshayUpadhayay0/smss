using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class CompanySalaryService : ICompanySalaryService
    {
        private const string OneActiveIndex = "uq_tb_company_salary_one_active_per_month";
        private const int MaxPageSize = 100;

        private readonly ICompanyFinanceRepository _repo;
        private readonly ILogger<CompanySalaryService> _logger;

        public CompanySalaryService(ICompanyFinanceRepository repo, ILogger<CompanySalaryService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // A flat amount for one employee and one month. It defaults to the employee's CURRENT monthly salary but is its own value from then on:
        // changing monthly_salary later never rewrites this payment.
        public async Task<ApiResponse<CompanySalaryPaymentDto>> RecordAsync(long recordedByUserId, RecordCompanySalaryRequest req, CancellationToken ct)
        {
            var mode = PaymentModes.Canonical(req.PaymentMode);
            if (mode == null) return Result<CompanySalaryPaymentDto>(400, $"Payment mode must be one of: {string.Join(", ", PaymentModes.All)}.", null);
            var paymentDate = req.PaymentDate ?? SchoolClock.Today();
            if (paymentDate > SchoolClock.Today()) return Result<CompanySalaryPaymentDto>(400, "The payment date can't be in the future.", null);
            var month = req.PeriodMonth!.Value;
            var year = req.PeriodYear!.Value;
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (activeId == null)
                {
                    _logger.LogError("Configuration problem: Active status is missing in lut_status");
                    return Result<CompanySalaryPaymentDto>(500, "The system is not configured correctly. Please contact support.", null);
                }
                var employee = await _repo.GetEmployeeAsync(req.CompanyEmployeeId!.Value, false, ct);
                if (employee == null) return Result<CompanySalaryPaymentDto>(404, "Employee not found", null);
                if (employee.StatusId != activeId) return Result<CompanySalaryPaymentDto>(409, "This employee is inactive. Activate them before recording a salary payment.", null);

                // The month must fall inside their employment
                var period = year * 12 + month;
                if (period < employee.DateOfJoining.Year * 12 + employee.DateOfJoining.Month)
                    return Result<CompanySalaryPaymentDto>(400, $"{employee.FirstName} joined on {employee.DateOfJoining:dd MMM yyyy}, so there is no salary for {month:00}/{year}.", null);
                if (employee.DateOfLeaving is { } left && period > left.Year * 12 + left.Month)
                    return Result<CompanySalaryPaymentDto>(400, $"{employee.FirstName} left on {left:dd MMM yyyy}, so there is no salary for {month:00}/{year}.", null);

                var amount = req.Amount is { } given ? Math.Round(given, 2, MidpointRounding.AwayFromZero) : employee.MonthlySalary;
                if (amount <= 0) return Result<CompanySalaryPaymentDto>(400, req.Amount == null ? "This employee's monthly salary is 0, so enter the amount to pay." : "The amount must be greater than zero.", null);

                if (await _repo.ActiveSalaryExistsAsync(employee.CompanyEmployeeId, month, year, ct))
                    return Result<CompanySalaryPaymentDto>(409, $"{employee.FirstName} has already been paid for {month:00}/{year}. Void that payment first to record it again.", null);

                var now = DateTime.UtcNow;
                var payment = new TbCompanySalaryPayment
                {
                    CompanyEmployeeId = employee.CompanyEmployeeId, PeriodMonth = (short)month, PeriodYear = (short)year, Amount = amount, PaymentDate = paymentDate,
                    PaymentMode = mode, Remarks = string.IsNullOrWhiteSpace(req.Remarks) ? null : req.Remarks.Trim(), RecordedBy = recordedByUserId,
                    Status = PaymentStatuses.Active, CreatedAt = now, UpdatedAt = now
                };
                _repo.AddSalaryPayment(payment);
                await _repo.SaveChangesAsync(ct);
                return Result(201, "Salary payment recorded", ToDto((await _repo.GetSalaryRowAsync(payment.SalaryPaymentId, ct))!));
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == OneActiveIndex)
            {
                // Two requests raced past the check above; the index caught the second
                return Result<CompanySalaryPaymentDto>(409, "This employee has already been paid for that month.", null);
            }
            catch (Exception ex) { return Error<CompanySalaryPaymentDto>(ex, "recording a salary payment"); }
        }

        // Soft void: the row stays (Voided, with who / when / why), drops out of every total, and the month can be paid again
        public async Task<ApiResponse<CompanySalaryPaymentDto>> VoidAsync(long id, long voidedByUserId, VoidCompanyRecordRequest req, CancellationToken ct)
        {
            var reason = string.IsNullOrWhiteSpace(req.VoidReason) ? null : req.VoidReason.Trim();
            if (reason == null) return Result<CompanySalaryPaymentDto>(400, "A reason is required to void a salary payment.", null);
            try
            {
                await using (var tx = await _repo.BeginTransactionAsync(ct))
                {
                    var payment = await _repo.LockSalaryPaymentAsync(id, ct);
                    if (payment == null) return Result<CompanySalaryPaymentDto>(404, "Salary payment not found", null);
                    if (payment.Status == PaymentStatuses.Voided) return Result<CompanySalaryPaymentDto>(409, "This salary payment is already voided.", null);

                    var now = DateTime.UtcNow;
                    payment.Status = PaymentStatuses.Voided;
                    payment.VoidedBy = voidedByUserId;
                    payment.VoidedAt = now;
                    payment.VoidReason = reason;
                    payment.UpdatedAt = now;
                    await _repo.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                return Result(200, "Salary payment voided", ToDto((await _repo.GetSalaryRowAsync(id, ct))!));
            }
            catch (Exception ex) { return Error<CompanySalaryPaymentDto>(ex, "voiding a salary payment"); }
        }

        public async Task<ApiResponse<CompanySalaryPagedDto>> ListAsync(long? companyEmployeeId, int? year, int? month, string? status, int page, int pageSize, CancellationToken ct)
        {
            if (month is < 1 or > 12) return Result<CompanySalaryPagedDto>(400, "Month must be from 1 to 12.", null);
            string? filter = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                filter = new[] { PaymentStatuses.Active, PaymentStatuses.Voided }.FirstOrDefault(s => string.Equals(s, status.Trim(), StringComparison.OrdinalIgnoreCase));
                if (filter == null) return Result<CompanySalaryPagedDto>(400, "Status must be Active or Voided.", null);
            }
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
            try
            {
                var (rows, total, activeTotal) = await _repo.ListSalaryPaymentsAsync(companyEmployeeId, year, month, filter, page, pageSize, ct);
                return Result(200, "Successfully fetched", new CompanySalaryPagedDto { Items = rows.Select(ToDto).ToList(), Total = total, Page = page, PageSize = pageSize, ActiveTotal = activeTotal });
            }
            catch (Exception ex) { return Error<CompanySalaryPagedDto>(ex, "listing salary payments"); }
        }

        private static CompanySalaryPaymentDto ToDto(SalaryRow r) => new()
        {
            SalaryPaymentId = r.Payment.SalaryPaymentId, CompanyEmployeeId = r.Payment.CompanyEmployeeId, EmployeeName = r.EmployeeName,
            PeriodMonth = r.Payment.PeriodMonth, PeriodYear = r.Payment.PeriodYear, Amount = r.Payment.Amount, PaymentDate = r.Payment.PaymentDate,
            PaymentMode = r.Payment.PaymentMode, Remarks = r.Payment.Remarks, RecordedByName = r.RecordedByName, Status = r.Payment.Status,
            VoidedAt = r.Payment.VoidedAt, VoidReason = r.Payment.VoidReason, CreatedAt = r.Payment.CreatedAt
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
