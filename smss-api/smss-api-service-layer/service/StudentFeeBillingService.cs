using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class StudentFeeBillingService : IStudentFeeBillingService
    {
        private readonly IStudentFeeBillingRepository _repo;
        private readonly IStudentFeeRepository _fees;
        private readonly ILogger<StudentFeeBillingService> _logger;

        public StudentFeeBillingService(IStudentFeeBillingRepository repo, IStudentFeeRepository fees, ILogger<StudentFeeBillingService> logger)
        {
            _repo = repo;
            _fees = fees;
            _logger = logger;
        }

        // ================= syncing items and dues =================

        /// <summary>
        /// Brings a student's fee items and installments in line with their fee structure. THE RULES (see FeeSchedule for the arithmetic):
        ///  - An item's frequency is always the fee line's (the billing cycle); the configured amount is the amount PER cycle; an installment plan only splits each cycle.
        ///  - A due is never created before the start: the later of the line's effective date and the student's start (charge-from / enrolment date), and never before today,
        ///    unless <see cref="FeeSyncOptions.Backdate"/> (the explicit, confirmed back-dating, which creates BACKDATED dues and logs them).
        ///  - A due with a payment is never touched; an unpaid due that is already past is never deleted; only unpaid FUTURE dues are replaced.
        ///  - No year total is recomputed from another frequency, so nothing can shrink: if the arithmetic ever came out negative the item is left exactly as it is and an error is logged.
        /// </summary>
        public async Task<FeeSyncResult> SyncAsync(string schoolId, string studentId, long academicYearId, IReadOnlyList<FeeItemSelection>? selections, CancellationToken ct, FeeSyncOptions? options = null)
        {
            var result = new FeeSyncResult();
            var year = await _repo.GetAcademicYearAsync(schoolId, academicYearId, ct);
            if (year == null) return result;

            var assignment = await _fees.GetAssignmentAsync(schoolId, studentId, academicYearId, track: false, ct);
            var lines = assignment == null ? new List<SourceLineRow>() : await _repo.GetSourceLinesAsync(assignment.FeeStructureId, ct);
            var items = await _repo.GetItemsAsync(schoolId, studentId, academicYearId, ct);
            var now = DateTime.UtcNow;
            var today = SchoolClock.Today();

            // The student's own start: the charge-from date they were admitted with, else their enrolment date, never before the year starts
            var enrolled = await _repo.GetEnrollmentStartAsync(schoolId, studentId, academicYearId, ct);
            var studentStart = options?.ChargeFrom ?? enrolled ?? year.StartDate;
            if (studentStart < year.StartDate) studentStart = year.StartDate;
            var backdate = options is { Backdate: true };
            var log = backdate
                ? new TbFeeBackdatingLog
                {
                    SchoolId = schoolId, Kind = "STUDENT", StudentId = studentId, AcademicYearId = academicYearId, FromDate = studentStart, ToDate = today,
                    Reason = options!.BackdatingReason ?? string.Empty, CreatedBy = options.UserId ?? 0, CreatedAt = now
                }
                : null;

            // 1. one item per fee head of the structure
            foreach (var line in lines)
            {
                var allowedPlans = FeeSchedule.AllowedPlans(line.FrequencyCode);
                var item = items.FirstOrDefault(i => i.FeeTypeId == line.FeeTypeId);
                var selection = selections?.FirstOrDefault(s => s.FeeTypeId == line.FeeTypeId);

                if (item == null)
                {
                    var plan = FeeSchedule.NormalisePlan(selection?.InstallmentPlan);
                    item = new TbStudentFeeItem
                    {
                        StudentId = studentId, SchoolId = schoolId, AcademicYearId = academicYearId, FeeTypeId = line.FeeTypeId,
                        // the billing cycle is the line's; an admission-time plan only counts when it is allowed for this fee head
                        FrequencyId = line.FrequencyId, InstallmentPlan = allowedPlans.Contains(plan) ? plan : FeeSchedule.PlanNone,
                        BaseAmount = line.Amount, ConcessionAmount = 0, NetAmount = line.Amount,
                        // a non-optional fee is always applied; an optional one only when it was opted into
                        IsApplicable = !line.IsOptional || (selection?.IsApplicable ?? false), IsActive = true, CreatedAt = now, UpdatedAt = now
                    };
                    await _repo.AddItemAsync(item, ct);
                    items.Add(item);
                }
                else
                {
                    item.IsActive = true;
                    item.BaseAmount = line.Amount;                      // the structure may have been edited
                    item.FrequencyId = line.FrequencyId;                // a student never has a frequency of their own
                    if (!allowedPlans.Contains(FeeSchedule.NormalisePlan(item.InstallmentPlan))) item.InstallmentPlan = FeeSchedule.PlanNone;
                    item.UpdatedAt = now;
                }
            }

            // 2. items whose fee head is no longer in the structure: dropped, unless money was already paid against them
            foreach (var item in items.Where(i => lines.All(l => l.FeeTypeId != i.FeeTypeId)).ToList())
            {
                if (item.Dues.Any(d => d.AmountPaid > 0))
                {
                    item.IsActive = false;
                    _repo.RemoveDues(item.Dues.Where(d => d.AmountPaid == 0).ToList());
                    item.UpdatedAt = now;
                }
                else
                {
                    _repo.RemoveItem(item);   // its dues go with it
                    items.Remove(item);
                }
            }

            // 3. concessions -> concession_amount / net_amount (only items that apply to the student take a discount)
            var applied = await _fees.GetAppliedConcessionsAsync(schoolId, studentId, ct);
            var typesInStructure = items.Where(i => i.IsActive).Select(i => i.FeeTypeId).ToHashSet();
            var usable = applied.Where(c => c.ApplicableFeeTypeId == null || typesInStructure.Contains(c.ApplicableFeeTypeId.Value)).ToList();
            var discounting = items.Where(i => i.IsActive && i.IsApplicable).ToList();
            var discounts = StudentFeeService.Discounts(discounting.Select(i => (i.FeeTypeId, i.BaseAmount)).ToList(), usable);
            foreach (var item in items)
            {
                var idx = discounting.IndexOf(item);
                var discount = idx >= 0 ? discounts[idx] : 0m;
                if (item.ConcessionAmount != discount || item.NetAmount != item.BaseAmount - discount)
                {
                    item.ConcessionAmount = discount;
                    item.NetAmount = item.BaseAmount - discount;
                    item.UpdatedAt = now;
                }
            }

            // 4. installments
            foreach (var item in items.Where(i => i.IsActive || i.Dues.Count > 0))
            {
                var line = lines.FirstOrDefault(l => l.FeeTypeId == item.FeeTypeId);
                RegenerateDues(item, line, year.StartDate, studentStart, today, now, log, result, options?.NoNewDuesForFeeTypes?.Contains(item.FeeTypeId) == true);
            }

            if (log != null && result.BackdatedCount > 0)
            {
                // reached from the back-dated dues, so it is saved with them; its figures are final now
                log.DuesCount = result.BackdatedCount;
                log.TotalAmount = result.BackdatedAmount;
                log.StudentsCount = 1;
            }

            await _repo.SaveChangesAsync(ct);
            return result;
        }

        /// <summary>
        /// Brings an item's installments in line with its billing cycle, plan and net amount (per cycle). See <see cref="SyncAsync"/> for the rules.
        ///  - Not applicable / inactive: the unpaid FUTURE installments go; nothing with a payment and nothing already past is deleted.
        ///  - First time (no installments yet): the installments owed from the start (see FeeSchedule.Owed).
        ///  - Otherwise: installments with a payment, or due today or earlier, stay exactly as they are; the unpaid FUTURE ones are updated in place (same month), added or removed to match
        ///    the schedule from the start. Amounts come from the schedule (cycle amount split by the plan), never from "what is left of a year total", so they cannot go negative.
        /// </summary>
        private void RegenerateDues(TbStudentFeeItem item, SourceLineRow? line, DateOnly yearStart, DateOnly studentStart, DateOnly today, DateTime now, TbFeeBackdatingLog? log, FeeSyncResult result, bool noNewDues = false)
        {
            var existing = item.Dues.ToList();

            if (!item.IsActive || !item.IsApplicable || line == null)
            {
                _repo.RemoveDues(existing.Where(d => d.AmountPaid == 0 && d.DueDate > today).ToList());
                return;
            }

            var plan = FeeSchedule.NormalisePlan(item.InstallmentPlan);
            var full = FeeSchedule.FullSchedule(yearStart, line.FrequencyCode, line.DueDay, plan, item.NetAmount);

            // The guard: never act on arithmetic that came out wrong. The item is left exactly as it is.
            if (item.NetAmount < 0 || full.Any(f => f.Amount < 0))
            {
                _logger.LogError("Fee item {ItemId} of student {StudentId}: the schedule came out negative (net {Net}); its dues were left unchanged", item.StudentFeeItemId, item.StudentId, item.NetAmount);
                return;
            }

            var single = FeeSchedule.Installments(line.FrequencyCode) == 1;
            var from = studentStart > line.EffectiveFrom ? studentStart : line.EffectiveFrom;   // the later of the line's effective date and the student's start
            var start = log != null ? from : (from > today ? from : today);                     // never before today unless explicitly back-dated

            if (existing.Count == 0)
            {
                if (item.NetAmount <= 0 || noNewDues) return;
                foreach (var o in FeeSchedule.Owed(full, start, single)) AddDue(item, o.Date, o.Amount, now, o.Date < today ? log : null, result);
                return;
            }

            // An installment belongs to a month of the schedule (its slot), not to an exact date, so moving the due day within a month never creates a second installment for that month.
            static int Slot(DateOnly d) => FeeSchedule.Slot(d);

            var retained = existing.Where(d => d.AmountPaid > 0 || d.DueDate <= today).ToList();   // payments and the past: never touched
            var retainedSlots = retained.Select(d => Slot(d.DueDate)).ToHashSet();
            var replaceable = existing.Except(retained).ToList();                                    // unpaid and in the future

            var desired = (single && retained.Count > 0)
                ? new List<FeeSchedule.Scheduled>()   // a single charge that already has its installment is not charged twice
                : FeeSchedule.Owed(full, start > today ? start : today, single).Where(o => o.Date > today && !retainedSlots.Contains(Slot(o.Date))).ToList();
            if (item.NetAmount <= 0) desired.Clear();

            // A plan splits each billing cycle's amount. When part of a cycle is already billed (a retained installment: paid, or already due), only what is LEFT of that cycle's own
            // amount is spread over its remaining installments, so changing the plan can never bill a cycle twice. Never negative: if the retained part is more than the cycle's amount the item is left as it is.
            if (plan != FeeSchedule.PlanNone && desired.Count > 0)
            {
                var cycleMonths = FeeSchedule.CycleMonths(line.FrequencyCode);
                var cycles = FeeSchedule.Installments(line.FrequencyCode);
                int CycleOf(DateOnly d) => Math.Clamp((d.Year * 12 + d.Month - (yearStart.Year * 12 + yearStart.Month)) / cycleMonths, 0, cycles - 1);
                var billed = retained.GroupBy(d => CycleOf(d.DueDate)).ToDictionary(g => g.Key, g => g.Sum(d => d.AmountDue));
                var resplit = new List<FeeSchedule.Scheduled>();
                foreach (var cycle in desired.GroupBy(o => o.Cycle))
                {
                    var open = cycle.ToList();
                    if (billed.TryGetValue(cycle.Key, out var already))
                    {
                        var left = item.NetAmount - already;
                        if (left < 0)
                        {
                            _logger.LogError("Fee item {ItemId} of student {StudentId}: cycle {Cycle} is already billed {Billed} of {Cycle_Amount}; its future dues were left unchanged", item.StudentFeeItemId, item.StudentId, cycle.Key, already, item.NetAmount);
                            return;
                        }
                        var parts = FeeSchedule.Split(left, open.Count);
                        for (var k = 0; k < open.Count; k++) open[k] = open[k] with { Amount = parts[k] };
                    }
                    resplit.AddRange(open);
                }
                desired = resplit.Where(o => o.Amount > 0).ToList();
            }

            var reusable = replaceable.GroupBy(d => Slot(d.DueDate)).ToDictionary(g => g.Key, g => g.OrderBy(d => d.DueDate).First());
            if (noNewDues) desired = desired.Where(o => reusable.ContainsKey(Slot(o.Date))).ToList();   // only what already exists is updated
            var used = new HashSet<TbStudentFeeDue>();
            foreach (var o in desired)
            {
                if (reusable.TryGetValue(Slot(o.Date), out var same) && used.Add(same))
                {
                    // same month: update in place (the date follows the due day, the amount follows the schedule)
                    if (same.DueDate != o.Date || same.AmountDue != o.Amount) { same.DueDate = o.Date; same.AmountDue = o.Amount; same.UpdatedAt = now; }
                }
                else
                {
                    AddDue(item, o.Date, o.Amount, now, null, result);
                }
            }
            _repo.RemoveDues(replaceable.Where(d => !used.Contains(d)).ToList());   // unpaid FUTURE installments that no longer exist in the schedule
        }

        private static void AddDue(TbStudentFeeItem item, DateOnly date, decimal amount, DateTime now, TbFeeBackdatingLog? log, FeeSyncResult result)
        {
            var due = new TbStudentFeeDue
            {
                StudentId = item.StudentId, SchoolId = item.SchoolId, DueDate = date, AmountDue = amount, AmountPaid = 0, CreatedAt = now, UpdatedAt = now,
                Source = log == null ? "AUTO" : "BACKDATED", BackdatingLog = log
            };
            if (log != null) { result.BackdatedCount++; result.BackdatedAmount += amount; }
            item.Dues.Add(due);
        }

        public async Task ApplyDueDayChangeAsync(string schoolId, long feeStructureId, long feeTypeId, int newDueDay, CancellationToken ct)
        {
            var today = SchoolClock.Today();
            var now = DateTime.UtcNow;
            var items = await _repo.GetItemsForStructureLineAsync(schoolId, feeStructureId, feeTypeId, ct);

            foreach (var item in items)
            {
                var taken = item.Dues.Select(d => d.DueDate).ToHashSet();
                // only installments with nothing paid that are still in the future; paid / part-paid ones keep their date
                foreach (var due in item.Dues.Where(d => d.AmountPaid == 0 && d.DueDate > today).OrderBy(d => d.DueDate).ToList())
                {
                    var moved = FeeSchedule.WithDay(due.DueDate.Year, due.DueDate.Month, newDueDay);
                    if (moved == due.DueDate || taken.Contains(moved)) continue;
                    taken.Remove(due.DueDate);
                    taken.Add(moved);
                    due.DueDate = moved;
                    due.UpdatedAt = now;
                }
            }
            await _repo.SaveChangesAsync(ct);
        }

        public async Task SyncStructureAsync(string schoolId, long feeStructureId, CancellationToken ct, IReadOnlySet<long>? noNewDuesForFeeTypes = null)
        {
            foreach (var (studentId, yearId) in await _repo.GetAssignedStudentsAsync(schoolId, feeStructureId, ct))
            {
                try { await SyncAsync(schoolId, studentId, yearId, null, ct, noNewDuesForFeeTypes == null ? null : new FeeSyncOptions { NoNewDuesForFeeTypes = noNewDuesForFeeTypes }); }
                catch (Exception ex)
                {
                    // one student's problem must not stop the others from being brought up to date
                    _logger.LogError(ex, "Could not bring the fee items of student {StudentId} up to date after structure {FeeStructureId} changed", studentId, feeStructureId);
                }
            }
        }

        // ================= reads =================

        public async Task<ApiResponse<StudentFeeItemsDto>> GetItemsAsync(string? schoolId, string studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentFeeItemsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var student = await _fees.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result<StudentFeeItemsDto>(404, "Student not found.", null);
                return Result(200, "Successfully fetched", await BuildItemsAsync(schoolId, studentId, ct));
            }
            catch (Exception ex) { return Error<StudentFeeItemsDto>(ex, "fetching student fee items"); }
        }

        public async Task<ApiResponse<StudentFeeItemsDto>> GetOwnItemsAsync(string? schoolId, long userId, string requestedStudentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentFeeItemsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                // 404, not 403, for another student: a 403 would confirm the id is real
                var own = await _fees.GetUserLoginIdAsync(schoolId, userId, ct);
                if (string.IsNullOrEmpty(own) || !string.Equals(own, requestedStudentId, StringComparison.Ordinal)) return Result<StudentFeeItemsDto>(404, "Student not found.", null);
                return await GetItemsAsync(schoolId, own, ct);
            }
            catch (Exception ex) { return Error<StudentFeeItemsDto>(ex, "fetching a student's own fee items"); }
        }

        public async Task<ApiResponse<StudentFeeDuesDto>> GetDuesAsync(string? schoolId, string studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentFeeDuesDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var student = await _fees.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result<StudentFeeDuesDto>(404, "Student not found.", null);

                var ctx = await LoadAsync(schoolId, studentId, ct);
                var dto = new StudentFeeDuesDto { StudentId = studentId, AcademicYearId = ctx.Year?.AcademicYearId, AcademicYearName = ctx.Year?.YearName };
                if (ctx.Year == null) return Result(200, "Successfully fetched", dto);

                var today = SchoolClock.Today();
                var names = await _repo.GetFeeTypeNamesAsync(schoolId, ct);
                var freqNames = ctx.Frequencies;

                foreach (var item in ctx.Items.Where(i => i.IsActive || i.Dues.Count > 0))
                {
                    foreach (var d in item.Dues.OrderBy(d => d.DueDate))
                    {
                        var status = FeeSchedule.Status(d.AmountDue, d.AmountPaid, d.DueDate, today);
                        dto.Dues.Add(new StudentFeeDueDto
                        {
                            FeeDueId = d.FeeDueId, StudentFeeItemId = item.StudentFeeItemId, FeeTypeId = item.FeeTypeId,
                            FeeTypeName = names.GetValueOrDefault(item.FeeTypeId, string.Empty),
                            FrequencyName = freqNames.TryGetValue(item.FrequencyId, out var f) ? f.FrequencyName : string.Empty,
                            InstallmentPlan = FeeSchedule.NormalisePlan(item.InstallmentPlan), Source = d.Source,
                            DueDate = d.DueDate, AmountDue = d.AmountDue, AmountPaid = d.AmountPaid,
                            Outstanding = Math.Max(d.AmountDue - d.AmountPaid, 0), Status = status
                        });
                    }
                }
                dto.Dues = dto.Dues.OrderBy(d => d.DueDate).ThenBy(d => d.FeeTypeName).ToList();
                dto.TotalDue = dto.Dues.Sum(d => d.AmountDue);
                dto.TotalPaid = dto.Dues.Sum(d => d.AmountPaid);
                dto.Outstanding = dto.Dues.Sum(d => d.Outstanding);
                dto.OverdueCount = dto.Dues.Count(d => d.Status == FeeSchedule.StatusOverdue);
                dto.OverdueAmount = dto.Dues.Where(d => d.Status == FeeSchedule.StatusOverdue).Sum(d => d.Outstanding);
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<StudentFeeDuesDto>(ex, "fetching student fee dues"); }
        }

        public async Task<string?> CheckChargeFromAsync(string schoolId, long academicYearId, DateOnly chargeFrom, bool confirmedBackdating, string? reason, CancellationToken ct)
        {
            var year = await _repo.GetAcademicYearAsync(schoolId, academicYearId, ct);
            if (year == null) return "The selected academic year was not found for your school.";
            if (chargeFrom < year.StartDate) return $"Fees can't be charged from before the academic year starts ({year.StartDate:dd MMM yyyy}).";
            if (chargeFrom > year.EndDate) return $"Fees can't be charged from after the academic year ends ({year.EndDate:dd MMM yyyy}).";
            if (chargeFrom >= SchoolClock.Today()) return null;

            // an earlier date creates dues that are already past: only in the current year, only explicitly confirmed, and always with a written reason
            if (!year.IsCurrent) return "Fees can be charged from an earlier date only in the current academic year.";
            if (!confirmedBackdating) return "Charging fees from an earlier date creates dues that are already past. Review the preview and confirm to continue.";
            if (string.IsNullOrWhiteSpace(reason)) return "A reason is required to charge fees from an earlier date.";
            if (reason.Trim().Length > 500) return "The reason can be at most 500 characters.";
            return null;
        }

        public async Task<ApiResponse<FeePreviewDto>> PreviewAsync(string? schoolId, long academicYearId, long classId, DateOnly? startDate, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<FeePreviewDto>(400, TenantMessages.NoSchool, null);
            try
            {
                // ids come from the query string: they must belong to THIS school
                var year = await _repo.GetAcademicYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<FeePreviewDto>(400, "The selected academic year was not found for your school.", null);
                var today = SchoolClock.Today();
                var chargeFrom = startDate ?? today;
                if (chargeFrom < year.StartDate) chargeFrom = year.StartDate;

                var dto = new FeePreviewDto { AcademicYearId = academicYearId, ClassId = classId, ChargeFrom = chargeFrom, IsBackdated = chargeFrom < today };
                if (dto.IsBackdated)
                {
                    if (!year.IsCurrent) { dto.BackdatingAllowed = false; dto.BackdatingMessage = "Fees can be charged from an earlier date only in the current academic year."; }
                }
                var structure = await _repo.FindActiveStructureAsync(schoolId, classId, academicYearId, ct);
                if (structure == null) return Result(200, "Successfully fetched", dto);

                dto.HasFeeStructure = true;
                dto.FeeStructureId = structure.FeeStructureId;
                dto.StructureName = structure.StructureName;
                dto.Lines = (await _repo.GetSourceLinesAsync(structure.FeeStructureId, ct)).Select(l =>
                {
                    var from = chargeFrom > l.EffectiveFrom ? chargeFrom : l.EffectiveFrom;
                    var single = FeeSchedule.Installments(l.FrequencyCode) == 1;
                    var options = AllowedPlans(l, l.Amount, year.StartDate, from, today, single);
                    return new FeePreviewLineDto
                    {
                        FeeTypeId = l.FeeTypeId, FeeTypeName = l.FeeTypeName, FeeTypeCode = l.FeeTypeCode, IsOptional = l.IsOptional, Amount = l.Amount,
                        StructureFrequencyId = l.FrequencyId, StructureFrequencyName = l.FrequencyName,
                        PeriodsPerYear = FeeSchedule.Installments(l.FrequencyCode), DueDay = l.DueDay, EffectiveFrom = l.EffectiveFrom,
                        AllowedPlans = options
                    };
                }).ToList();
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<FeePreviewDto>(ex, "building the fee preview"); }
        }

        // ================= editing one item =================

        public async Task<ApiResponse<StudentFeeItemsDto>> UpdateItemAsync(string? schoolId, string studentId, long itemId, UpdateFeeItemRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<StudentFeeItemsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var student = await _fees.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result<StudentFeeItemsDto>(404, "Student not found.", null);
                if (req.InstallmentPlan == null && req.IsApplicable == null) return Result<StudentFeeItemsDto>(400, "Nothing to change: send an installmentPlan and/or isApplicable.", null);

                var item = await _repo.GetItemAsync(schoolId, studentId, itemId, ct);
                if (item == null || !item.IsActive) return Result<StudentFeeItemsDto>(404, "Fee item not found.", null);

                if (req.InstallmentPlan != null)
                {
                    var assignment = await _fees.GetAssignmentAsync(schoolId, studentId, item.AcademicYearId, track: false, ct);
                    var line = assignment == null ? null : (await _repo.GetSourceLinesAsync(assignment.FeeStructureId, ct)).FirstOrDefault(l => l.FeeTypeId == item.FeeTypeId);
                    if (line == null) return Result<StudentFeeItemsDto>(400, "This fee head is no longer part of the student's fee structure.", null);

                    var plan = FeeSchedule.NormalisePlan(req.InstallmentPlan);
                    if (!FeeSchedule.AllowedPlans(line.FrequencyCode).Contains(plan))
                        return Result<StudentFeeItemsDto>(400, $"A {line.FrequencyName} fee can't be paid with that installment plan. Allowed: {string.Join(", ", FeeSchedule.AllowedPlans(line.FrequencyCode))}.", null);
                    item.InstallmentPlan = plan;   // only the unpaid FUTURE installments are re-split by the sync below
                }
                if (req.IsApplicable is bool applicable)
                {
                    if (!applicable)
                    {
                        var assignedFor = await _fees.GetAssignmentAsync(schoolId, studentId, item.AcademicYearId, track: false, ct);
                        var itemLine = assignedFor == null ? null : (await _repo.GetSourceLinesAsync(assignedFor.FeeStructureId, ct)).FirstOrDefault(l => l.FeeTypeId == item.FeeTypeId);
                        if (itemLine is { IsOptional: false })
                            return Result<StudentFeeItemsDto>(400, $"{itemLine.FeeTypeName} is not an optional fee, so it can't be switched off.", null);
                    }
                    item.IsApplicable = applicable;
                }
                item.UpdatedAt = DateTime.UtcNow;

                // recalculates concessions (they spread over the items that apply) and regenerates the unpaid future dues
                await SyncAsync(schoolId, studentId, item.AcademicYearId, null, ct);
                return Result(200, "Fee item updated.", await BuildItemsAsync(schoolId, studentId, ct));
            }
            catch (Exception ex) { return Error<StudentFeeItemsDto>(ex, "updating fee item"); }
        }

        // ================= helpers =================

        private sealed class Loaded
        {
            public TbAcademicYears? Year { get; init; }
            public List<TbStudentFeeItem> Items { get; init; } = new();
            public Dictionary<long, LutFeeFrequency> Frequencies { get; init; } = new();
        }

        /// <summary>The student's current academic year and its items. A student who has a fee structure but no items yet (assigned
        /// before items existed, or a failed generation) gets them created here, so the screen heals itself.</summary>
        private async Task<Loaded> LoadAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var enrollment = (await _fees.GetEnrollmentsAsync(schoolId, studentId, ct))
                .OrderByDescending(e => e.AcademicYearIsCurrent).ThenByDescending(e => e.AcademicYearStart).FirstOrDefault();
            var frequencies = (await _repo.GetFrequenciesAsync(ct)).ToDictionary(f => f.FrequencyId);
            if (enrollment == null) return new Loaded { Frequencies = frequencies };

            var year = await _repo.GetAcademicYearAsync(schoolId, enrollment.AcademicYearId, ct);
            var items = await _repo.GetItemsAsync(schoolId, studentId, enrollment.AcademicYearId, ct);
            if (items.Count == 0 && await _fees.GetAssignmentAsync(schoolId, studentId, enrollment.AcademicYearId, track: false, ct) != null)
            {
                await SyncAsync(schoolId, studentId, enrollment.AcademicYearId, null, ct);
                items = await _repo.GetItemsAsync(schoolId, studentId, enrollment.AcademicYearId, ct);
            }
            return new Loaded { Year = year, Items = items, Frequencies = frequencies };
        }

        private async Task<StudentFeeItemsDto> BuildItemsAsync(string schoolId, string studentId, CancellationToken ct)
        {
            var ctx = await LoadAsync(schoolId, studentId, ct);
            var dto = new StudentFeeItemsDto { StudentId = studentId, AcademicYearId = ctx.Year?.AcademicYearId, AcademicYearName = ctx.Year?.YearName };
            if (ctx.Year == null) return dto;

            var assignment = await _fees.GetAssignmentAsync(schoolId, studentId, ctx.Year.AcademicYearId, track: false, ct);
            var lines = assignment == null ? new List<SourceLineRow>() : await _repo.GetSourceLinesAsync(assignment.FeeStructureId, ct);
            var today = SchoolClock.Today();

            foreach (var item in ctx.Items.Where(i => i.IsActive))
            {
                var line = lines.FirstOrDefault(l => l.FeeTypeId == item.FeeTypeId);
                if (line == null) continue;

                var upcoming = item.Dues.Where(d => d.AmountDue > d.AmountPaid && d.DueDate >= today).OrderBy(d => d.DueDate).FirstOrDefault();
                dto.Items.Add(new StudentFeeItemDto
                {
                    StudentFeeItemId = item.StudentFeeItemId, FeeTypeId = item.FeeTypeId, FeeTypeName = line.FeeTypeName, FeeTypeCode = line.FeeTypeCode,
                    FrequencyId = line.FrequencyId,
                    FrequencyName = line.FrequencyName,
                    StructureFrequencyName = line.FrequencyName,
                    InstallmentPlan = FeeSchedule.NormalisePlan(item.InstallmentPlan),
                    InstallmentPlanName = FeeSchedule.PlanName(FeeSchedule.NormalisePlan(item.InstallmentPlan)),
                    AllowedPlans = AllowedPlans(line, item.NetAmount, ctx.Year?.StartDate ?? today, null, today, false),
                    EffectiveFrom = line.EffectiveFrom,
                    DueDay = line.DueDay,
                    IsOptional = line.IsOptional,
                    BaseAmount = item.BaseAmount, ConcessionAmount = item.ConcessionAmount, NetAmount = item.NetAmount,
                    AnnualNetAmount = item.NetAmount * FeeSchedule.Installments(line.FrequencyCode),
                    IsApplicable = item.IsApplicable,
                    DueCount = item.Dues.Count, NextDueDate = upcoming?.DueDate,
                    TotalDue = item.Dues.Sum(d => d.AmountDue), TotalPaid = item.Dues.Sum(d => d.AmountPaid)
                });
            }
            dto.Items = dto.Items.OrderBy(i => i.FeeTypeName).ToList();
            dto.AnnualNetTotal = dto.Items.Where(i => i.IsApplicable).Sum(i => i.AnnualNetAmount);
            return dto;
        }

        /// <summary>
        /// The installment plans a fee line allows. With <paramref name="from"/> (a preview) each option also says how many installments fall on or after the start and how many
        /// before today (what a back-dated "Charge fees from" would create), with their amounts, for the given per-cycle amount.
        /// </summary>
        private static List<InstallmentPlanOptionDto> AllowedPlans(SourceLineRow line, decimal cycleAmount, DateOnly yearStart, DateOnly? from, DateOnly today, bool single)
        {
            return FeeSchedule.AllowedPlans(line.FrequencyCode).Select(code =>
            {
                var full = FeeSchedule.FullSchedule(yearStart, line.FrequencyCode, line.DueDay, code, cycleAmount);
                var option = new InstallmentPlanOptionDto
                {
                    Code = code, Name = FeeSchedule.PlanName(code), InstallmentsPerCycle = FeeSchedule.PlanInstallmentsPerCycle(line.FrequencyCode, code), Installments = full.Count
                };
                if (from is DateOnly f)
                {
                    var forward = FeeSchedule.Owed(full, f > today ? f : today, single);
                    option.RemainingInstallments = forward.Count;
                    option.RemainingAmount = forward.Sum(x => x.Amount);
                    var past = f < today ? full.Where(x => x.Date >= f && x.Date < today).ToList() : new List<FeeSchedule.Scheduled>();
                    if (single && f < today && line.EffectiveFrom <= f) past = full.Where(x => x.Date < today).Select(x => x with { Date = x.Date < f ? f : x.Date }).ToList();
                    option.BackdatedInstallments = past.Count;
                    option.BackdatedAmount = past.Sum(x => x.Amount);
                }
                return option;
            }).ToList();
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
