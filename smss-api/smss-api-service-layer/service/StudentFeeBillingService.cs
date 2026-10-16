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

        public async Task SyncAsync(string schoolId, string studentId, long academicYearId, IReadOnlyList<FeeItemSelection>? selections, CancellationToken ct)
        {
            var year = await _repo.GetAcademicYearAsync(schoolId, academicYearId, ct);
            if (year == null) return;

            var assignment = await _fees.GetAssignmentAsync(schoolId, studentId, academicYearId, track: false, ct);
            var lines = assignment == null ? new List<SourceLineRow>() : await _repo.GetSourceLinesAsync(assignment.FeeStructureId, ct);
            var frequencies = (await _repo.GetFrequenciesAsync(ct)).ToDictionary(f => f.FrequencyId);
            var items = await _repo.GetItemsAsync(schoolId, studentId, academicYearId, ct);
            var now = DateTime.UtcNow;
            var today = SchoolClock.Today();

            // A student owes recurring fees from the month they were admitted, never retroactively from the start of the year
            var enrolled = await _repo.GetEnrollmentStartAsync(schoolId, studentId, academicYearId, ct);
            var floor = enrolled == null || enrolled.Value < year.StartDate ? year.StartDate : enrolled.Value;

            // 1. one item per fee head of the structure
            foreach (var line in lines)
            {
                var allowedIds = AllowedFrequencies(line, frequencies).Select(f => f.FrequencyId).ToHashSet();
                var item = items.FirstOrDefault(i => i.FeeTypeId == line.FeeTypeId);
                var selection = selections?.FirstOrDefault(s => s.FeeTypeId == line.FeeTypeId);

                if (item == null)
                {
                    item = new TbStudentFeeItem
                    {
                        StudentId = studentId, SchoolId = schoolId, AcademicYearId = academicYearId, FeeTypeId = line.FeeTypeId,
                        // the structure's frequency is the default; an admission-time choice only counts when it is allowed for this fee head
                        FrequencyId = selection?.FrequencyId is long f && allowedIds.Contains(f) ? f : line.FrequencyId,
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
                    if (!allowedIds.Contains(item.FrequencyId)) item.FrequencyId = line.FrequencyId;
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
                RegenerateDues(item, line, frequencies, year.StartDate, floor, today, now);
            }

            await _repo.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Brings an item's installments in line with its frequency, applicability and net amount.
        ///  - Not applicable (or inactive): every unpaid installment goes.
        ///  - First time (no installments yet): the full year's schedule.
        ///  - Otherwise: installments with a payment, or already past their due date, stay exactly as they are; the unpaid FUTURE
        ///    ones are recreated to cover what is still left of the year's net amount. Nothing with a payment is ever touched.
        /// </summary>
        private void RegenerateDues(TbStudentFeeItem item, SourceLineRow? line, Dictionary<long, LutFeeFrequency> frequencies,
            DateOnly yearStart, DateOnly floor, DateOnly today, DateTime now)
        {
            var existing = item.Dues.ToList();

            if (!item.IsActive || !item.IsApplicable || line == null || !frequencies.TryGetValue(item.FrequencyId, out var frequency))
            {
                _repo.RemoveDues(existing.Where(d => d.AmountPaid == 0).ToList());
                return;
            }

            var fullYearDates = FeeSchedule.DueDates(yearStart, frequency.FrequencyCode, line.DueDay);
            var yearNet = item.NetAmount * FeeSchedule.Installments(line.FrequencyCode);   // net per structure period x periods in a year
            var fullYearAmounts = FeeSchedule.Split(yearNet, fullYearDates.Count);

            // Only the installments due on or after the student's start date are owed: same amount per installment as everyone else, just
            // fewer of them, and never an installment that was already overdue before the student joined.
            // (a single charge is still billed in full but dated from the start date; and if no configured date is left this year, one installment
            // at the start date: see FeeSchedule.OwedSchedule)
            var owed = FeeSchedule.OwedSchedule(fullYearDates, floor);
            var dates = owed.Select(o => o.Date).ToList();
            var billable = owed.Sum(o => fullYearAmounts[o.Index]);

            if (existing.Count == 0)
            {
                if (yearNet <= 0) return;
                foreach (var o in owed) AddDue(item, o.Date, fullYearAmounts[o.Index], now);
                return;
            }

            // An installment belongs to a month of the schedule (its slot), not to an exact date, so moving the due day within a month
            // never creates a second installment for that month.
            static int Slot(DateOnly d) => FeeSchedule.Slot(d);

            var retained = existing.Where(d => d.AmountPaid > 0 || d.DueDate <= today).ToList();
            var retainedSlots = retained.Select(d => Slot(d.DueDate)).ToHashSet();
            // installments from before the start date (schedules generated before this rule) are left alone and do not count against what is owed
            var retainedCounted = retained.Where(d => fullYearDates.Count == 1 || d.DueDate >= floor).ToList();
            var replaceable = existing.Except(retained).ToList();
            var reusable = replaceable.GroupBy(d => Slot(d.DueDate)).ToDictionary(g => g.Key, g => g.OrderBy(d => d.DueDate).First());

            // The slots still to be paid: not covered by a retained installment, and either still in the future or already holding an
            // unpaid installment (whose date follows the schedule, e.g. after the due day changed).
            var openDates = dates.Where(d => !retainedSlots.Contains(Slot(d)) && (d > today || reusable.ContainsKey(Slot(d)))).ToList();
            var remaining = billable - retainedCounted.Sum(d => d.AmountDue);

            if (remaining > 0 && openDates.Count == 0)
            {
                // nothing of the schedule is left to pay in: the rest falls due the day after today / the last due date
                var latest = retained.Count == 0 ? today : retained.Max(d => d.DueDate);
                openDates.Add((latest > today ? latest : today).AddDays(1));
            }
            if (remaining <= 0) openDates.Clear();

            var split = FeeSchedule.Split(Math.Max(remaining, 0), openDates.Count);
            var used = new HashSet<TbStudentFeeDue>();
            for (var i = 0; i < openDates.Count; i++)
            {
                if (reusable.TryGetValue(Slot(openDates[i]), out var same) && used.Add(same))
                {
                    // same month: update in place (date follows the due day, amount follows the net amount)
                    if (same.DueDate != openDates[i] || same.AmountDue != split[i]) { same.DueDate = openDates[i]; same.AmountDue = split[i]; same.UpdatedAt = now; }
                }
                else
                {
                    AddDue(item, openDates[i], split[i], now);
                }
            }
            _repo.RemoveDues(replaceable.Where(d => !used.Contains(d)).ToList());   // unpaid future installments that no longer exist
        }

        private static void AddDue(TbStudentFeeItem item, DateOnly date, decimal amount, DateTime now) =>
            item.Dues.Add(new TbStudentFeeDue
            {
                StudentId = item.StudentId, SchoolId = item.SchoolId, DueDate = date, AmountDue = amount, AmountPaid = 0, CreatedAt = now, UpdatedAt = now
            });

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

        public async Task SyncStructureAsync(string schoolId, long feeStructureId, CancellationToken ct)
        {
            foreach (var (studentId, yearId) in await _repo.GetAssignedStudentsAsync(schoolId, feeStructureId, ct))
            {
                try { await SyncAsync(schoolId, studentId, yearId, null, ct); }
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
                            FeeDueId = d.FeeDueId, StudentFeeItemId = item.StudentFeeItemId,
                            FeeTypeName = names.GetValueOrDefault(item.FeeTypeId, string.Empty),
                            FrequencyName = freqNames.TryGetValue(item.FrequencyId, out var f) ? f.FrequencyName : string.Empty,
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

        public async Task<ApiResponse<FeePreviewDto>> PreviewAsync(string? schoolId, long academicYearId, long classId, DateOnly? startDate, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<FeePreviewDto>(400, TenantMessages.NoSchool, null);
            try
            {
                // ids come from the query string: they must belong to THIS school
                var year = await _repo.GetAcademicYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<FeePreviewDto>(400, "The selected academic year was not found for your school.", null);
                var floor = startDate == null || startDate.Value < year.StartDate ? year.StartDate : startDate.Value;

                var dto = new FeePreviewDto { AcademicYearId = academicYearId, ClassId = classId };
                var structure = await _repo.FindActiveStructureAsync(schoolId, classId, academicYearId, ct);
                if (structure == null) return Result(200, "Successfully fetched", dto);

                var frequencies = (await _repo.GetFrequenciesAsync(ct)).ToDictionary(f => f.FrequencyId);
                dto.HasFeeStructure = true;
                dto.FeeStructureId = structure.FeeStructureId;
                dto.StructureName = structure.StructureName;
                dto.Lines = (await _repo.GetSourceLinesAsync(structure.FeeStructureId, ct)).Select(l =>
                {
                    var options = AllowedFrequencies(l, frequencies);
                    foreach (var o in options)
                        o.RemainingInstallments = FeeSchedule.OwedSchedule(FeeSchedule.DueDates(year.StartDate, o.FrequencyCode, l.DueDay), floor).Count;
                    return new FeePreviewLineDto
                    {
                        FeeTypeId = l.FeeTypeId, FeeTypeName = l.FeeTypeName, FeeTypeCode = l.FeeTypeCode, IsOptional = l.IsOptional, Amount = l.Amount,
                        StructureFrequencyId = l.FrequencyId, StructureFrequencyName = l.FrequencyName,
                        PeriodsPerYear = FeeSchedule.Installments(l.FrequencyCode), DueDay = l.DueDay,
                        AllowedFrequencies = options
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
                if (req.FrequencyId == null && req.IsApplicable == null) return Result<StudentFeeItemsDto>(400, "Nothing to change: send a frequency and/or isApplicable.", null);

                var item = await _repo.GetItemAsync(schoolId, studentId, itemId, ct);
                if (item == null || !item.IsActive) return Result<StudentFeeItemsDto>(404, "Fee item not found.", null);

                if (req.FrequencyId is long newFreq && newFreq != item.FrequencyId)
                {
                    var assignment = await _fees.GetAssignmentAsync(schoolId, studentId, item.AcademicYearId, track: false, ct);
                    var line = assignment == null ? null : (await _repo.GetSourceLinesAsync(assignment.FeeStructureId, ct)).FirstOrDefault(l => l.FeeTypeId == item.FeeTypeId);
                    if (line == null) return Result<StudentFeeItemsDto>(400, "This fee head is no longer part of the student's fee structure.", null);

                    var frequencies = (await _repo.GetFrequenciesAsync(ct)).ToDictionary(f => f.FrequencyId);
                    if (AllowedFrequencies(line, frequencies).All(f => f.FrequencyId != newFreq))
                        return Result<StudentFeeItemsDto>(400, $"A {line.FrequencyName} fee can't be paid at that frequency.", null);
                    item.FrequencyId = newFreq;
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
                    FrequencyId = item.FrequencyId,
                    FrequencyName = ctx.Frequencies.TryGetValue(item.FrequencyId, out var f) ? f.FrequencyName : string.Empty,
                    StructureFrequencyName = line.FrequencyName,
                    AllowedFrequencies = AllowedFrequencies(line, ctx.Frequencies),
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

        private static List<FeeFrequencyOptionDto> AllowedFrequencies(SourceLineRow line, Dictionary<long, LutFeeFrequency> frequencies)
        {
            var allowed = FeeSchedule.AllowedStudentFrequencies(line.FrequencyCode);
            return frequencies.Values
                .Where(f => f.IsActive && allowed.Contains(f.FrequencyCode))
                .OrderBy(f => f.FrequencyId)
                .Select(f => new FeeFrequencyOptionDto
                {
                    FrequencyId = f.FrequencyId, FrequencyName = f.FrequencyName, FrequencyCode = f.FrequencyCode, Installments = FeeSchedule.Installments(f.FrequencyCode)
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
