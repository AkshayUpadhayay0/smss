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
    public class FeePaymentService : IFeePaymentService
    {
        private const string ReceiptConstraint = "uq_tb_fee_payments_school_receipt";

        private readonly IFeePaymentRepository _repo;
        private readonly IStudentFeeRepository _fees;
        private readonly ILogger<FeePaymentService> _logger;

        public FeePaymentService(IFeePaymentRepository repo, IStudentFeeRepository fees, ILogger<FeePaymentService> logger)
        {
            _repo = repo;
            _fees = fees;
            _logger = logger;
        }

        public async Task<ApiResponse<FeeReceiptDto>> RecordPaymentAsync(string? schoolId, string studentId, long collectedByUserId, RecordPaymentRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<FeeReceiptDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var mode = PaymentModes.Canonical(req.PaymentMode);
                if (mode == null) return Result<FeeReceiptDto>(400, $"Payment mode must be one of: {string.Join(", ", PaymentModes.All)}.", null);

                var amount = req.Amount!.Value;
                if (decimal.Round(amount, 2) != amount) return Result<FeeReceiptDto>(400, "Amount can have at most 2 decimal places.", null);

                // 404 for another school's student too, so ids from other schools can't be probed
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result<FeeReceiptDto>(404, "Student not found.", null);

                var explicitAllocations = req.Allocations is { Count: > 0 };
                if (explicitAllocations)
                {
                    var bad = req.Allocations!.FirstOrDefault(a => decimal.Round(a.Amount!.Value, 2) != a.Amount!.Value);
                    if (bad != null) return Result<FeeReceiptDto>(400, "Allocated amounts can have at most 2 decimal places.", null);
                    if (req.Allocations!.Select(a => a.FeeDueId).Distinct().Count() != req.Allocations!.Count)
                        return Result<FeeReceiptDto>(400, "An installment can appear only once in a payment.", null);
                    if (req.Allocations!.Sum(a => a.Amount!.Value) != amount)
                        return Result<FeeReceiptDto>(400, $"The allocations add up to {req.Allocations!.Sum(a => a.Amount!.Value):0.00}, but the payment is {amount:0.00}. They must be equal.", null);
                }

                // Two receipts can be numbered at the same moment: the unique index lets only one through and the other simply retries
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    await using var tx = await _repo.BeginTransactionAsync(ct);
                    try
                    {
                        var now = DateTime.UtcNow;

                        // ---- the installments, locked so nobody else can pay them while this transaction decides ----
                        List<TbStudentFeeDue> dues;
                        var plan = new List<(TbStudentFeeDue Due, decimal Amount)>();

                        if (explicitAllocations)
                        {
                            var ids = req.Allocations!.Select(a => a.FeeDueId!.Value).ToList();
                            dues = await _repo.LockDuesAsync(schoolId, studentId, ids, ct);
                            if (dues.Count != ids.Count) return Result<FeeReceiptDto>(400, "One or more installments do not belong to this student.", null);

                            foreach (var a in req.Allocations!)
                            {
                                var due = dues.First(d => d.FeeDueId == a.FeeDueId!.Value);
                                var outstanding = due.AmountDue - due.AmountPaid;
                                if (a.Amount!.Value > outstanding)
                                    return Result<FeeReceiptDto>(400,
                                        $"The installment due {due.DueDate:dd MMM yyyy} has only {outstanding:0.00} outstanding, so {a.Amount.Value:0.00} can't be applied to it.", null);
                                plan.Add((due, a.Amount.Value));
                            }
                        }
                        else
                        {
                            // Convenience mode: oldest installment first, until the amount is used up
                            dues = await _repo.LockOutstandingDuesAsync(schoolId, studentId, ct);
                            var totalOutstanding = dues.Sum(d => d.AmountDue - d.AmountPaid);
                            if (amount > totalOutstanding)
                                return Result<FeeReceiptDto>(400, totalOutstanding == 0
                                    ? "This student has nothing outstanding to pay."
                                    : $"The payment of {amount:0.00} is more than the {totalOutstanding:0.00} outstanding. Nothing was recorded.", null);

                            var left = amount;
                            foreach (var due in dues)   // already ordered oldest first
                            {
                                if (left <= 0) break;
                                var take = Math.Min(left, due.AmountDue - due.AmountPaid);
                                plan.Add((due, take));
                                left -= take;
                            }
                        }

                        // ---- the payment, its allocations and each installment's running total: one SaveChanges, one transaction ----
                        var year = SchoolClock.Today().Year;
                        var prefix = $"RCP-{year}-";
                        await _repo.LockReceiptNumbersAsync(schoolId, ct);   // one payment at a time per school picks its number
                        var next = await _repo.GetMaxReceiptSequenceAsync(schoolId, prefix, ct) + 1;

                        var payment = new TbFeePayment
                        {
                            StudentId = studentId,
                            SchoolId = schoolId,
                            PaymentDate = now,
                            Amount = amount,
                            PaymentMode = mode,
                            Status = PaymentStatuses.Active,
                            ReceiptNumber = $"{prefix}{next:D4}",
                            CollectedBy = collectedByUserId,
                            Remarks = string.IsNullOrWhiteSpace(req.Remarks) ? null : req.Remarks.Trim(),
                            CreatedAt = now,
                            UpdatedAt = now
                        };
                        foreach (var (due, part) in plan)
                        {
                            payment.Allocations.Add(new TbFeePaymentAllocation { FeeDueId = due.FeeDueId, AllocatedAmount = part, CreatedAt = now });
                            due.AmountPaid += part;
                            due.UpdatedAt = now;
                        }

                        await _repo.AddPaymentAsync(payment, ct);
                        await _repo.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);

                        return Result(201, "Payment recorded.", await BuildReceiptAsync(schoolId, payment.PaymentId, ct));
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                                       && pg.ConstraintName == ReceiptConstraint && attempt < 2)
                    {
                        _logger.LogWarning(ex, "Receipt number collision for school {SchoolId}; retrying.", schoolId);   // the transaction rolls back on dispose
                        _repo.ClearTracking();
                    }
                }
                return Result<FeeReceiptDto>(409, "Could not allocate a receipt number. Nothing was recorded — please try again.", null);
            }
            catch (Exception ex) { return Error<FeeReceiptDto>(ex, "recording a fee payment"); }
        }

        /// <summary>
        /// Soft void: the payment stays (Voided, with who / when / why), its allocations stay as the audit trail, and what it had put against
        /// each installment is taken back off that installment's amount_paid, all in one transaction. Installment status is computed from
        /// amount_paid on every read, so the installment simply reads Overdue / Pending again: nothing else needs to be touched.
        /// This reverses the fee bookkeeping only; it does not move or track any cash.
        /// </summary>
        public async Task<ApiResponse<PaymentSummaryDto>> VoidPaymentAsync(string? schoolId, long paymentId, long voidedByUserId, VoidPaymentRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<PaymentSummaryDto>(400, TenantMessages.NoSchool, null);
            var reason = req.VoidReason?.Trim();
            if (string.IsNullOrEmpty(reason)) return Result<PaymentSummaryDto>(400, "A reason is required to void a payment.", null);

            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);

                // the payment first (so two voids of it can't run together), then the installments it touched
                var payment = await _repo.LockPaymentAsync(schoolId, paymentId, ct);
                if (payment == null) return Result<PaymentSummaryDto>(404, "Payment not found.", null);
                if (payment.Status == PaymentStatuses.Voided)
                    return Result<PaymentSummaryDto>(404, $"Payment {payment.ReceiptNumber} has already been voided, so it can't be voided again.", null);

                var allocations = await _repo.GetAllocationsAsync(paymentId, ct);
                var dues = await _repo.LockDuesAsync(schoolId, payment.StudentId, allocations.Select(a => a.FeeDueId).ToList(), ct);
                if (dues.Count != allocations.Select(a => a.FeeDueId).Distinct().Count())
                {
                    _logger.LogError("Payment {PaymentId} refers to installments that are missing; refusing to void", paymentId);
                    return Result<PaymentSummaryDto>(409, "This payment's installments could not all be found, so it can't be voided. Please contact support.", null);
                }

                var now = DateTime.UtcNow;
                foreach (var a in allocations)
                {
                    var due = dues.First(d => d.FeeDueId == a.FeeDueId);
                    if (due.AmountPaid < a.AllocatedAmount)
                    {
                        // can't happen unless the books were edited by hand: never push a total below zero
                        _logger.LogError("Voiding payment {PaymentId} would take installment {FeeDueId} below zero ({Paid} paid, {Allocated} to reverse)", paymentId, due.FeeDueId, due.AmountPaid, a.AllocatedAmount);
                        return Result<PaymentSummaryDto>(409, "The installment totals no longer match this payment, so it can't be voided automatically. Please contact support.", null);
                    }
                    due.AmountPaid -= a.AllocatedAmount;
                    due.UpdatedAt = now;
                }

                payment.Status = PaymentStatuses.Voided;
                payment.VoidedBy = voidedByUserId;
                payment.VoidedAt = now;
                payment.VoidReason = reason;
                payment.UpdatedAt = now;

                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                var voidedByName = await _repo.GetUserNameAsync(voidedByUserId, ct);
                var collectedBy = payment.CollectedBy is long u ? await _repo.GetUserNameAsync(u, ct) : null;
                return Result(200, $"Payment {payment.ReceiptNumber} voided.", new PaymentSummaryDto
                {
                    PaymentId = payment.PaymentId, ReceiptNumber = payment.ReceiptNumber, PaymentDate = payment.PaymentDate, Amount = payment.Amount,
                    PaymentMode = payment.PaymentMode, CollectedByName = collectedBy, Remarks = payment.Remarks, DueCount = allocations.Count,
                    Status = payment.Status, VoidedAt = payment.VoidedAt, VoidedByName = voidedByName, VoidReason = payment.VoidReason
                });
            }
            catch (Exception ex) { return Error<PaymentSummaryDto>(ex, "voiding a fee payment"); }
        }

        public async Task<ApiResponse<List<PaymentSummaryDto>>> GetPaymentsAsync(string? schoolId, string studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<PaymentSummaryDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                if (await _repo.GetStudentAsync(schoolId, studentId, ct) == null) return Result<List<PaymentSummaryDto>>(404, "Student not found.", null);
                var rows = await _repo.GetPaymentsAsync(schoolId, studentId, ct);
                return Result(200, "Successfully fetched", rows.Select(r => new PaymentSummaryDto
                {
                    PaymentId = r.Payment.PaymentId, ReceiptNumber = r.Payment.ReceiptNumber, PaymentDate = r.Payment.PaymentDate,
                    Amount = r.Payment.Amount, PaymentMode = r.Payment.PaymentMode, CollectedByName = r.CollectedByName,
                    Remarks = r.Payment.Remarks, DueCount = r.DueCount,
                    Status = r.Payment.Status, VoidedAt = r.Payment.VoidedAt, VoidedByName = r.VoidedByName, VoidReason = r.Payment.VoidReason
                }).ToList());
            }
            catch (Exception ex) { return Error<List<PaymentSummaryDto>>(ex, "fetching payment history"); }
        }

        public async Task<ApiResponse<FeeReceiptDto>> GetReceiptAsync(string? schoolId, long paymentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<FeeReceiptDto>(400, TenantMessages.NoSchool, null);
            try
            {
                // 404 for another school's payment too
                var receipt = await BuildReceiptAsync(schoolId, paymentId, ct);
                return receipt == null ? Result<FeeReceiptDto>(404, "Payment not found.", null) : Result(200, "Successfully fetched", receipt);
            }
            catch (Exception ex) { return Error<FeeReceiptDto>(ex, "fetching a receipt"); }
        }

        private async Task<FeeReceiptDto?> BuildReceiptAsync(string schoolId, long paymentId, CancellationToken ct)
        {
            var payment = await _repo.GetPaymentAsync(schoolId, paymentId, ct);
            if (payment == null) return null;

            var school = await _repo.GetReceiptSchoolAsync(schoolId, ct);
            var student = await _repo.GetStudentAsync(schoolId, payment.StudentId, ct);
            var enrollment = (await _fees.GetEnrollmentsAsync(schoolId, payment.StudentId, ct))
                .OrderByDescending(e => e.AcademicYearIsCurrent).ThenByDescending(e => e.AcademicYearStart).FirstOrDefault();
            var details = (await _repo.GetDueDetailsAsync(payment.Allocations.Select(a => a.FeeDueId).ToList(), ct)).ToDictionary(d => d.FeeDueId);
            var collectedBy = payment.CollectedBy is long u ? await _repo.GetUserNameAsync(u, ct) : null;

            return new FeeReceiptDto
            {
                PaymentId = payment.PaymentId, ReceiptNumber = payment.ReceiptNumber, PaymentDate = payment.PaymentDate, Amount = payment.Amount,
                PaymentMode = payment.PaymentMode, Remarks = payment.Remarks, CollectedByName = collectedBy,
                Status = payment.Status, VoidedAt = payment.VoidedAt, VoidReason = payment.VoidReason,
                VoidedByName = payment.VoidedBy is long vb ? await _repo.GetUserNameAsync(vb, ct) : null,
                School = new ReceiptSchoolDto
                {
                    Name = school?.SchoolName ?? string.Empty, Address = school?.Address, Phone = school?.Phone, Email = school?.Email, LogoUrl = school?.LogoUrl
                },
                Student = new ReceiptStudentDto
                {
                    StudentId = payment.StudentId,
                    Name = student == null ? string.Empty : string.Join(' ', new[] { student.FirstName, student.MiddleName, student.LastName }.Where(p => !string.IsNullOrWhiteSpace(p))),
                    ClassName = enrollment?.ClassName, AcademicYearName = enrollment?.AcademicYearName
                },
                Lines = payment.Allocations.OrderBy(a => details.TryGetValue(a.FeeDueId, out var d) ? d.DueDate : DateOnly.MaxValue).ThenBy(a => a.AllocationId)
                    .Where(a => details.ContainsKey(a.FeeDueId))
                    .Select(a =>
                    {
                        var d = details[a.FeeDueId];
                        return new ReceiptLineDto
                        {
                            FeeDueId = a.FeeDueId, FeeTypeName = d.FeeTypeName, FrequencyName = d.FrequencyName, DueDate = d.DueDate,
                            AmountDue = d.AmountDue, AllocatedAmount = a.AllocatedAmount, PaidToDate = d.AmountPaid
                        };
                    }).ToList()
            };
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
