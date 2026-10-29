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
    public class SubscriptionBillingService : ISubscriptionBillingService
    {
        private const int MaxPageSize = 100;

        private readonly ISubscriptionRepository _repo;
        private readonly ILogger<SubscriptionBillingService> _logger;

        public SubscriptionBillingService(ISubscriptionRepository repo, ILogger<SubscriptionBillingService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // ---------------------------------------------------------------- subscription
        public async Task<ApiResponse<SchoolSubscriptionDto>> GetSubscriptionAsync(string schoolId, CancellationToken ct)
        {
            try
            {
                var sub = await _repo.GetSubscriptionAsync(schoolId, ct);
                return sub == null ? NotFoundSubscription<SchoolSubscriptionDto>() : Result(200, "Successfully fetched", ToDto(sub, await _repo.GetActivePricingAsync(ct)));
            }
            catch (Exception ex) { return Error<SchoolSubscriptionDto>(ex, "fetching a school subscription"); }
        }

        // Leaving the trial: frequency + first cycle. Pricing is global, so there is nothing else to configure.
        public async Task<ApiResponse<SchoolSubscriptionDto>> ConfigureAsync(string schoolId, ConfigureBillingRequest req, CancellationToken ct)
        {
            var frequency = BillingFrequencies.Canonical(req.BillingFrequency);
            if (frequency == null) return Result<SchoolSubscriptionDto>(400, "Billing frequency must be MONTHLY or YEARLY.", null);
            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);
                var sub = await _repo.LockSubscriptionAsync(schoolId, ct);
                if (sub == null) return NotFoundSubscription<SchoolSubscriptionDto>();
                if (sub.Status != SubscriptionStatuses.Trial)
                    return Result<SchoolSubscriptionDto>(409, sub.Status == SubscriptionStatuses.Cancelled ? "This subscription is cancelled." : "Billing is already configured for this school.", null);

                // Billing starts the day after the trial ends; a school configured after its trial ran out starts today
                var today = SchoolClock.Today();
                var start = today > sub.TrialEndDate ? today : sub.TrialEndDate.AddDays(1);
                var end = SubscriptionRules.CycleEnd(start, frequency);

                sub.Status = SubscriptionStatuses.Active;
                sub.BillingFrequency = frequency;
                ApplyTerms(sub, req);
                sub.CurrentCycleStartDate = start;
                sub.CurrentCycleEndDate = end;
                sub.NextRenewalDate = end.AddDays(1);
                sub.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result(200, "Billing configured", ToDto(sub, await _repo.GetActivePricingAsync(ct)));
            }
            catch (Exception ex) { return Error<SchoolSubscriptionDto>(ex, "configuring billing"); }
        }

        // The school's negotiated terms can change at any time (a deal gets renegotiated). Invoices already generated keep the numbers they were
        // locked with; only the next invoice uses the new terms.
        public async Task<ApiResponse<SchoolSubscriptionDto>> UpdateTermsAsync(string schoolId, BillingTermsRequest req, CancellationToken ct)
        {
            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);
                var sub = await _repo.LockSubscriptionAsync(schoolId, ct);
                if (sub == null) return NotFoundSubscription<SchoolSubscriptionDto>();
                if (sub.Status == SubscriptionStatuses.Cancelled) return Result<SchoolSubscriptionDto>(409, "This subscription is cancelled.", null);
                ApplyTerms(sub, req);
                sub.UpdatedAt = DateTime.UtcNow;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result(200, "Billing terms updated. They apply to invoices generated from now on.", ToDto(sub, await _repo.GetActivePricingAsync(ct)));
            }
            catch (Exception ex) { return Error<SchoolSubscriptionDto>(ex, "updating billing terms"); }
        }

        /// <summary>Null rate / discount mean "use the global one"; a missing include-staff flag means staff are not billed.</summary>
        private static void ApplyTerms(TbSchoolSubscription sub, BillingTermsRequest req)
        {
            sub.CustomRatePerStudentMonthly = req.CustomRatePerStudentMonthly is { } rate ? Money(rate) : null;
            sub.CustomYearlyDiscountPercent = req.CustomYearlyDiscountPercent is { } pct ? Money(pct) : null;
            sub.IncludeStaffInBilling = req.IncludeStaffInBilling ?? false;
        }

        // ---------------------------------------------------------------- invoices
        public async Task<ApiResponse<SubscriptionInvoiceDto>> GenerateInvoiceAsync(string schoolId, long generatedByUserId, GenerateInvoiceRequest req, CancellationToken ct)
        {
            var today = SchoolClock.Today();
            if (req.DueDate != null && req.DueDate < today) return Result<SubscriptionInvoiceDto>(400, "The due date can't be in the past.", null);
            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);
                // The lock makes two clicks on "Generate" run one after the other, so the unpaid-invoice check below can't be raced
                var sub = await _repo.LockSubscriptionAsync(schoolId, ct);
                if (sub == null) return NotFoundSubscription<SubscriptionInvoiceDto>();
                if (sub.Status == SubscriptionStatuses.Cancelled) return Result<SubscriptionInvoiceDto>(409, "This subscription is cancelled.", null);
                if (sub.BillingFrequency == null || sub.CurrentCycleStartDate == null)
                    return Result<SubscriptionInvoiceDto>(409, "Configure billing (monthly or yearly) for this school before generating an invoice.", null);

                var pricing = await _repo.GetActivePricingAsync(ct);
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (pricing == null || activeId == null)
                {
                    _logger.LogError("Configuration problem: no Active subscription pricing row, or 'Active' is missing in lut_status");
                    return Result<SubscriptionInvoiceDto>(500, "The system is not configured correctly. Please contact support.", null);
                }

                // The first invoice bills the cycle set when billing was configured; every later one starts the day after the previous period ended
                var latest = await _repo.GetLatestInvoiceAsync(sub.SubscriptionId, ct);
                DateOnly periodStart;
                if (latest == null) periodStart = sub.CurrentCycleStartDate.Value;
                else
                {
                    // An unpaid invoice for a cycle that is still running blocks the next one. Once its period is over the school simply owes it
                    // and the next cycle can be billed too (that is how a school ends up with several unpaid invoices).
                    var paid = await _repo.GetPaidAmountAsync(latest.InvoiceId, ct);
                    if (paid < latest.NetAmount && latest.BillingPeriodEnd >= today)
                        return Result<SubscriptionInvoiceDto>(409, $"Invoice #{latest.InvoiceId} for {latest.BillingPeriodStart:dd MMM yyyy} - {latest.BillingPeriodEnd:dd MMM yyyy} is for the current cycle and not fully paid yet. Collect it before generating the next one.", null);
                    periodStart = latest.BillingPeriodEnd.AddDays(1);
                }
                var periodEnd = SubscriptionRules.CycleEnd(periodStart, sub.BillingFrequency);

                // Live counts, captured on the invoice and never recomputed. All three are stored; the school's terms decide which are priced.
                var counts = await _repo.CountBillableUsersAsync(schoolId, activeId.Value, ct);
                if (!counts.HasCurrentYear)
                    return Result<SubscriptionInvoiceDto>(409, "This school has no current academic year, so its student count can't be determined. Ask the school to set one first.", null);
                var billable = SubscriptionRules.BillableUsers(counts.Students, counts.Teachers, counts.Staff, sub.IncludeStaffInBilling);
                if (billable == 0)
                    return Result<SubscriptionInvoiceDto>(409, sub.IncludeStaffInBilling
                        ? "This school has no active students, teachers or staff yet, so there is nothing to bill."
                        : "This school has no active students yet, so there is nothing to bill. (Teachers and staff are not billed under this school's terms.)", null);

                // The school's own rate / discount win over the global ones; whichever is used is locked on the invoice
                var rate = sub.CustomRatePerStudentMonthly ?? pricing.RatePerStudentMonthly;
                var yearlyDiscount = sub.CustomYearlyDiscountPercent ?? pricing.YearlyDiscountPercent;
                var (gross, discount, net, percent) = SubscriptionRules.Amounts(rate, billable, sub.BillingFrequency, yearlyDiscount);
                var now = DateTime.UtcNow;
                var invoice = new TbSubscriptionInvoice
                {
                    SchoolId = schoolId, SubscriptionId = sub.SubscriptionId,
                    BillingPeriodStart = periodStart, BillingPeriodEnd = periodEnd, BillingFrequency = sub.BillingFrequency,
                    StudentCount = counts.Students, TeacherCount = counts.Teachers, StaffCount = counts.Staff,
                    StaffBilled = sub.IncludeStaffInBilling, BillableUserCount = billable,
                    RatePerStudent = rate, DiscountPercent = percent,
                    GrossAmount = gross, DiscountAmount = discount, NetAmount = net,
                    DueDate = req.DueDate ?? (today > periodStart ? today : periodStart),
                    GeneratedBy = generatedByUserId, CreatedAt = now, UpdatedAt = now
                };
                _repo.AddInvoice(invoice);

                // The subscription's current cycle always follows the latest invoice
                sub.CurrentCycleStartDate = periodStart;
                sub.CurrentCycleEndDate = periodEnd;
                sub.NextRenewalDate = periodEnd.AddDays(1);
                sub.UpdatedAt = now;

                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                var name = (await _repo.GetInvoiceAsync(invoice.InvoiceId, ct))?.SchoolName ?? schoolId;
                return Result(201, "Invoice generated", ToDto(invoice, name, 0m, today));
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Result<SubscriptionInvoiceDto>(409, "An invoice for this billing period already exists.", null);
            }
            catch (Exception ex) { return Error<SubscriptionInvoiceDto>(ex, "generating a subscription invoice"); }
        }

        public async Task<ApiResponse<SubscriptionInvoicePagedDto>> ListInvoicesAsync(string? schoolId, string? status, int page, int pageSize, CancellationToken ct)
        {
            string? canonicalStatus = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                canonicalStatus = InvoiceStatuses.Canonical(status);
                if (canonicalStatus == null) return Result<SubscriptionInvoicePagedDto>(400, "Status must be Pending, Paid or Overdue.", null);
            }
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
            try
            {
                var today = SchoolClock.Today();
                var (rows, total) = await _repo.ListInvoicesAsync(string.IsNullOrWhiteSpace(schoolId) ? null : schoolId.Trim(), canonicalStatus, today, page, pageSize, ct);
                return Result(200, "Successfully fetched", new SubscriptionInvoicePagedDto
                {
                    Items = rows.Select(r => ToDto(r.Invoice, r.SchoolName, r.PaidAmount, today)).ToList(),
                    Total = total, Page = page, PageSize = pageSize
                });
            }
            catch (Exception ex) { return Error<SubscriptionInvoicePagedDto>(ex, "listing subscription invoices"); }
        }

        public async Task<ApiResponse<SubscriptionInvoiceDto>> GetInvoiceAsync(long invoiceId, CancellationToken ct)
        {
            try
            {
                var row = await _repo.GetInvoiceAsync(invoiceId, ct);
                if (row == null) return Result<SubscriptionInvoiceDto>(404, "Invoice not found", null);
                var dto = ToDto(row.Invoice, row.SchoolName, row.PaidAmount, SchoolClock.Today());
                dto.Payments = (await _repo.GetPaymentsForInvoiceAsync(invoiceId, ct)).Select(p => ToDto(p, p.Allocations)).ToList();
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<SubscriptionInvoiceDto>(ex, "fetching a subscription invoice"); }
        }

        // ---------------------------------------------------------------- payments
        // A payment belongs to a school and is split over that school's invoices. The caller sends an explicit split, or none, in which
        // case the oldest unpaid invoices (by billing period) are paid first.
        public async Task<ApiResponse<SubscriptionPaymentDto>> RecordPaymentAsync(long recordedByUserId, RecordSubscriptionPaymentRequest req, CancellationToken ct)
        {
            var mode = PaymentModes.Canonical(req.PaymentMode);
            if (mode == null) return Result<SubscriptionPaymentDto>(400, $"Payment mode must be one of: {string.Join(", ", PaymentModes.All)}.", null);
            var schoolId = req.SchoolId!.Trim();
            var amount = Money(req.Amount!.Value);
            if (amount <= 0) return Result<SubscriptionPaymentDto>(400, "The amount must be greater than zero.", null);

            // Explicit split: shape checks that need no database
            List<(long InvoiceId, decimal Amount)>? split = null;
            if (req.Allocations is { Count: > 0 })
            {
                split = req.Allocations.Select(a => (a.InvoiceId!.Value, Money(a.Amount!.Value))).ToList();
                if (split.Any(a => a.Amount <= 0)) return Result<SubscriptionPaymentDto>(400, "Every allocation must be greater than zero.", null);
                if (split.GroupBy(a => a.InvoiceId).Any(g => g.Count() > 1)) return Result<SubscriptionPaymentDto>(400, "An invoice can appear only once in the allocations.", null);
                var allocated = split.Sum(a => a.Amount);
                if (allocated != amount)
                    return Result<SubscriptionPaymentDto>(400, $"The allocations add up to {allocated:0.00} but the payment is {amount:0.00}. They must match exactly.", null);
            }
            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);
                // The school's subscription row is the lock: payments and invoice generation for one school run one after the other,
                // so every check below is made against what is really owed
                var sub = await _repo.LockSubscriptionAsync(schoolId, ct);
                if (sub == null)
                    return await _repo.SchoolExistsAsync(schoolId, ct) ? NotFoundSubscription<SubscriptionPaymentDto>() : Result<SubscriptionPaymentDto>(404, "School not found", null);

                var now = DateTime.UtcNow;
                var payment = new TbSubscriptionPayment
                {
                    SchoolId = schoolId, PaymentDate = now, Amount = amount, PaymentMode = mode,
                    ReferenceNumber = Clean(req.ReferenceNumber), RecordedBy = recordedByUserId, Remarks = Clean(req.Remarks),
                    Status = PaymentStatuses.Active, CreatedAt = now, UpdatedAt = now
                };

                if (split != null)
                {
                    var invoices = await _repo.GetSchoolInvoicesAsync(schoolId, split.Select(a => a.InvoiceId).ToList(), ct);
                    foreach (var (invoiceId, part) in split)
                    {
                        var row = invoices.FirstOrDefault(r => r.Invoice.InvoiceId == invoiceId);
                        // Not the school's invoice and not an invoice at all look the same on purpose
                        if (row == null) return Result<SubscriptionPaymentDto>(400, $"Invoice #{invoiceId} doesn't belong to this school.", null);
                        var balance = row.Invoice.NetAmount - row.PaidAmount;
                        if (part > balance)
                            return Result<SubscriptionPaymentDto>(409, $"Invoice #{invoiceId} has only {balance:0.00} left to pay; the allocation of {part:0.00} would overpay it.", null);
                        payment.Allocations.Add(NewAllocation(schoolId, invoiceId, part, now));
                    }
                }
                else
                {
                    var open = (await _repo.GetSchoolInvoicesAsync(schoolId, null, ct)).Where(r => r.Invoice.NetAmount > r.PaidAmount).ToList();   // already oldest period first
                    var left = amount;
                    foreach (var row in open)
                    {
                        if (left <= 0) break;
                        var part = Math.Min(left, row.Invoice.NetAmount - row.PaidAmount);
                        payment.Allocations.Add(NewAllocation(schoolId, row.Invoice.InvoiceId, part, now));
                        left -= part;
                    }
                    if (left > 0)
                    {
                        var owed = open.Sum(r => r.Invoice.NetAmount - r.PaidAmount);
                        return Result<SubscriptionPaymentDto>(409, owed == 0m ? "This school has no unpaid invoices." : $"The amount is more than the {owed:0.00} this school owes in total.", null);
                    }
                }

                _repo.AddPayment(payment);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result(201, "Payment recorded", ToDto(payment, payment.Allocations));
            }
            catch (Exception ex) { return Error<SubscriptionPaymentDto>(ex, "recording a subscription payment"); }
        }

        // Soft void: the payment and its allocations stay (Voided, with who / when / why). Every invoice it was allocated to is back to
        // Pending / Overdue by itself, because an invoice's paid amount only counts allocations of Active payments.
        public async Task<ApiResponse<SubscriptionPaymentDto>> VoidPaymentAsync(long paymentId, long voidedByUserId, VoidSubscriptionPaymentRequest req, CancellationToken ct)
        {
            var reason = Clean(req.VoidReason);
            if (reason == null) return Result<SubscriptionPaymentDto>(400, "A reason is required to void a payment.", null);
            try
            {
                await using var tx = await _repo.BeginTransactionAsync(ct);
                var payment = await _repo.LockPaymentAsync(paymentId, ct);
                if (payment == null) return Result<SubscriptionPaymentDto>(404, "Payment not found", null);
                if (payment.Status == PaymentStatuses.Voided) return Result<SubscriptionPaymentDto>(409, "This payment is already voided.", null);

                var now = DateTime.UtcNow;
                payment.Status = PaymentStatuses.Voided;
                payment.VoidedBy = voidedByUserId;
                payment.VoidedAt = now;
                payment.VoidReason = reason;
                payment.UpdatedAt = now;
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result(200, "Payment voided", ToDto(payment, await _repo.GetAllocationsAsync(paymentId, ct)));
            }
            catch (Exception ex) { return Error<SubscriptionPaymentDto>(ex, "voiding a subscription payment"); }
        }

        private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static TbSubscriptionPaymentAllocation NewAllocation(string schoolId, long invoiceId, decimal amount, DateTime now) =>
            new() { SchoolId = schoolId, InvoiceId = invoiceId, AllocatedAmount = amount, CreatedAt = now };

        // ---------------------------------------------------------------- mapping / helpers
        private static SchoolSubscriptionDto ToDto(TbSchoolSubscription s, TbSubscriptionPricing? pricing) => new()
        {
            SubscriptionId = s.SubscriptionId, SchoolId = s.SchoolId, Status = s.Status, BillingFrequency = s.BillingFrequency,
            CustomRatePerStudentMonthly = s.CustomRatePerStudentMonthly, CustomYearlyDiscountPercent = s.CustomYearlyDiscountPercent,
            IncludeStaffInBilling = s.IncludeStaffInBilling,
            EffectiveRatePerStudentMonthly = s.CustomRatePerStudentMonthly ?? pricing?.RatePerStudentMonthly,
            EffectiveYearlyDiscountPercent = s.CustomYearlyDiscountPercent ?? pricing?.YearlyDiscountPercent,
            TrialStartDate = s.TrialStartDate, TrialEndDate = s.TrialEndDate,
            TrialExpired = s.Status == SubscriptionStatuses.Trial && SchoolClock.Today() > s.TrialEndDate,
            CurrentCycleStartDate = s.CurrentCycleStartDate, CurrentCycleEndDate = s.CurrentCycleEndDate, NextRenewalDate = s.NextRenewalDate
        };

        private static SubscriptionInvoiceDto ToDto(TbSubscriptionInvoice i, string schoolName, decimal paid, DateOnly today) => new()
        {
            InvoiceId = i.InvoiceId, SchoolId = i.SchoolId, SchoolName = schoolName, SubscriptionId = i.SubscriptionId,
            BillingPeriodStart = i.BillingPeriodStart, BillingPeriodEnd = i.BillingPeriodEnd, BillingFrequency = i.BillingFrequency,
            StudentCount = i.StudentCount, TeacherCount = i.TeacherCount, StaffCount = i.StaffCount, StaffBilled = i.StaffBilled, BillableUserCount = i.BillableUserCount,
            RatePerStudent = i.RatePerStudent, DiscountPercent = i.DiscountPercent,
            GrossAmount = i.GrossAmount, DiscountAmount = i.DiscountAmount, NetAmount = i.NetAmount,
            DueDate = i.DueDate, CreatedAt = i.CreatedAt,
            PaidAmount = paid, BalanceAmount = Math.Max(0m, i.NetAmount - paid),
            Status = SubscriptionRules.InvoiceStatus(i.NetAmount, paid, i.DueDate, today)
        };

        private static SubscriptionPaymentDto ToDto(TbSubscriptionPayment p, IEnumerable<TbSubscriptionPaymentAllocation> allocations) => new()
        {
            PaymentId = p.PaymentId, SchoolId = p.SchoolId, PaymentDate = p.PaymentDate, Amount = p.Amount,
            PaymentMode = p.PaymentMode, ReferenceNumber = p.ReferenceNumber, Remarks = p.Remarks, Status = p.Status,
            VoidedAt = p.VoidedAt, VoidReason = p.VoidReason,
            Allocations = allocations.OrderBy(a => a.InvoiceId).Select(a => new SubscriptionPaymentAllocationDto { InvoiceId = a.InvoiceId, AllocatedAmount = a.AllocatedAmount }).ToList()
        };

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static ApiResponse<T> NotFoundSubscription<T>() =>
            Result<T>(404, "This school has no subscription record. It was registered before billing was added.", default);

        private static ApiResponse<T> Result<T>(int code, string message, T? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", default);
        }
    }
}
