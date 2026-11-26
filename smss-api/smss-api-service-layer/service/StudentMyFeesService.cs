using Microsoft.Extensions.Logging;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>A student's own fee view: the existing items / dues / payments reads, narrowed to what a student or parent should see. No fee arithmetic of its own.</summary>
    public class StudentMyFeesService : IStudentMyFeesService
    {
        private readonly IStudentRepository _students;
        private readonly IStudentFeeBillingService _billing;
        private readonly IFeePaymentService _payments;
        private readonly ILogger<StudentMyFeesService> _logger;

        public StudentMyFeesService(IStudentRepository students, IStudentFeeBillingService billing, IFeePaymentService payments, ILogger<StudentMyFeesService> logger)
        {
            _students = students;
            _billing = billing;
            _payments = payments;
            _logger = logger;
        }

        public async Task<ApiResponse<StudentMyFeesDto>> GetMyFeesAsync(string? schoolId, long callerUserId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                return await GetFeesForStudentAsync(schoolId, await _students.GetUserLoginIdAsync(schoolId, callerUserId, ct) ?? "", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogError(ex, "Error while building a student's own fees"); return Result(500, "Something went wrong. Please try again later.", null); }
        }

        /// <summary>The own-view fees of ONE student (never a concession reason, never a voided payment). Only for a caller the access helper has cleared (the student, or a linked parent).</summary>
        public async Task<ApiResponse<StudentMyFeesDto>> GetFeesForStudentAsync(string? schoolId, string studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                var student = string.IsNullOrEmpty(studentId) ? null : await _students.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result(404, "Your student record was not found.", null);

                var items = await _billing.GetItemsAsync(schoolId, student.StudentId, ct);
                var dues = await _billing.GetDuesAsync(schoolId, student.StudentId, ct);
                var pays = await _payments.GetPaymentsAsync(schoolId, student.StudentId, ct);
                if (!items.Status || items.Data == null) return Result(items.StatusCode, items.Message, null);
                if (!dues.Status || dues.Data == null) return Result(dues.StatusCode, dues.Message, null);
                if (!pays.Status || pays.Data == null) return Result(pays.StatusCode, pays.Message, null);

                var applicable = items.Data.Items.Where(i => i.IsApplicable).ToList();
                var d = dues.Data;
                return Result(200, "Successfully fetched", new StudentMyFeesDto
                {
                    AcademicYearName = d.AcademicYearName ?? items.Data.AcademicYearName,
                    HasFees = applicable.Count > 0 || d.Dues.Count > 0,
                    OverallStatus = FeeSchedule.Overall(applicable.Count > 0 || d.Dues.Count > 0, d.TotalDue, d.TotalPaid, d.OverdueCount),
                    TotalDue = d.TotalDue, TotalPaid = d.TotalPaid, Balance = d.Outstanding, OverdueAmount = d.OverdueAmount, OverdueCount = d.OverdueCount,
                    Heads = applicable.Select(i => new MyFeeHeadDto
                    {
                        FeeTypeName = i.FeeTypeName, FrequencyName = i.FrequencyName, InstallmentPlan = i.InstallmentPlan, Amount = i.BaseAmount, Concession = i.ConcessionAmount, NetAmount = i.NetAmount, AnnualNetAmount = i.AnnualNetAmount
                    }).ToList(),
                    Dues = d.Dues.Select(x => new MyFeeDueDto
                    {
                        FeeTypeName = x.FeeTypeName, DueDate = x.DueDate, AmountDue = x.AmountDue, AmountPaid = x.AmountPaid, Balance = x.Outstanding, Status = x.Status
                    }).ToList(),
                    // a voided payment no longer counts for anything, and who voided it and why is internal: only Active payments are listed
                    Payments = pays.Data.Where(p => p.Status == "Active").OrderByDescending(p => p.PaymentDate)
                        .Select(p => new MyFeePaymentDto { PaymentDate = p.PaymentDate, Amount = p.Amount, FeeAmount = p.FeeAmount, GrossAmount = p.GrossAmount, NetFeeAmount = p.NetFeeAmount,
                            ConcessionName = p.ConcessionName, ConcessionCode = p.ConcessionCode, ConcessionType = p.ConcessionType, ConcessionValue = p.ConcessionValue, ConcessionAmount = p.ConcessionAmount,   // never the reason: that is for staff
                            TaxName = p.TaxName, TaxCode = p.TaxCode, TaxRatePercent = p.TaxRatePercent, TaxAmount = p.TaxAmount, TotalAmount = p.TotalAmount, ReceiptNumber = p.ReceiptNumber, PaymentMode = p.PaymentMode,
                            LateFee = p.LateFee is { Decision: "APPLIED" } lf ? new PaymentLateFeeDto { Decision = lf.Decision, Amount = lf.Amount, TaxAmount = lf.TaxAmount, RuleText = lf.RuleText, Lines = lf.Lines } : null }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while building a student's own fees");
                return Result(500, "Something went wrong. Please try again later.", null);
            }
        }

        private static ApiResponse<StudentMyFeesDto> Result(int code, string message, StudentMyFeesDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
