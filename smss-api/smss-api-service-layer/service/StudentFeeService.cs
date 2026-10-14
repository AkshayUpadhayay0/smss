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
    public class StudentFeeService : IStudentFeeService
    {
        private const string Percentage = "PERCENTAGE";

        private readonly IStudentFeeRepository _repo;
        private readonly ILogger<StudentFeeService> _logger;

        public StudentFeeService(IStudentFeeRepository repo, ILogger<StudentFeeService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<StudentFeesDto>> GetFeesAsync(string? schoolId, string studentId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                // 404 for another school's student too, so ids from other schools can't be probed
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result(404, "Student not found.", null);
                return Result(200, "Successfully fetched", await BuildAsync(schoolId, student, ct));
            }
            catch (Exception ex) { return Error(ex, "fetching student fees"); }
        }

        public async Task<ApiResponse<StudentFeesDto>> AssignStructureAsync(string? schoolId, string studentId, AssignFeeStructureRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result(404, "Student not found.", null);

                var enrollment = await CurrentEnrollmentAsync(schoolId, studentId, ct);
                if (enrollment == null) return Result(400, "This student is not enrolled in any class yet, so there is no academic year to assign fees for.", null);

                // The id comes from the client: it must be THIS school's, active, and for the student's academic year
                var structure = await _repo.GetStructureAsync(schoolId, req.FeeStructureId!.Value, ct);
                if (structure == null) return Result(400, "The selected fee structure was not found for your school.", null);
                if (!structure.IsActive) return Result(400, "That fee structure is inactive. Activate it first.", null);
                if (structure.AcademicYearId != enrollment.AcademicYearId)
                    return Result(400, $"That fee structure is for a different academic year than the student's ({enrollment.AcademicYearName}).", null);

                var now = DateTime.UtcNow;
                var assignment = await _repo.GetAssignmentAsync(schoolId, studentId, enrollment.AcademicYearId, track: true, ct);
                if (assignment == null)
                {
                    await _repo.AddAssignmentAsync(new TbStudentFeeStructure
                    {
                        StudentId = studentId, FeeStructureId = structure.FeeStructureId, SchoolId = schoolId,
                        AcademicYearId = enrollment.AcademicYearId, CreatedAt = now, UpdatedAt = now
                    }, ct);
                }
                else
                {
                    assignment.FeeStructureId = structure.FeeStructureId;
                    assignment.UpdatedAt = now;
                }
                await _repo.SaveChangesAsync(ct);

                return Result(200, "Fee structure assigned.", await BuildAsync(schoolId, student, ct));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Result(409, "The student's fee structure was just changed by someone else. Please try again.", null); }
            catch (Exception ex) { return Error(ex, "assigning fee structure"); }
        }

        public async Task<ApiResponse<StudentFeesDto>> ApplyConcessionAsync(string? schoolId, string studentId, long approvedByUserId, ApplyConcessionRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result(404, "Student not found.", null);

                var enrollment = await CurrentEnrollmentAsync(schoolId, studentId, ct);
                var assignment = enrollment == null ? null : await _repo.GetAssignmentAsync(schoolId, studentId, enrollment.AcademicYearId, track: false, ct);
                if (assignment == null) return Result(400, "Assign a fee structure to the student before adding concessions.", null);

                var concession = await _repo.GetConcessionAsync(schoolId, req.ConcessionId!.Value, ct);
                if (concession == null) return Result(400, "The selected concession was not found for your school.", null);
                if (!concession.IsActive) return Result(400, "That concession is inactive. Activate it first.", null);

                // A concession on one fee head only makes sense when the student's structure has that fee head
                if (concession.ApplicableFeeTypeId is long ft)
                {
                    var lines = await _repo.GetStructureLinesAsync(assignment.FeeStructureId, ct);
                    if (lines.All(l => l.FeeTypeId != ft))
                        return Result(400, "That concession is for a fee head that is not in this student's fee structure.", null);
                }

                if (await _repo.GetAppliedConcessionAsync(studentId, concession.ConcessionId, track: false, ct) != null)
                    return Result(409, "This concession is already applied to the student.", null);

                var now = DateTime.UtcNow;
                await _repo.AddAppliedConcessionAsync(new TbStudentFeeConcession
                {
                    StudentId = studentId,
                    ConcessionId = concession.ConcessionId,
                    ApprovedBy = approvedByUserId,
                    Remarks = string.IsNullOrWhiteSpace(req.Remarks) ? null : req.Remarks.Trim(),
                    CreatedAt = now,
                    UpdatedAt = now
                }, ct);
                await _repo.SaveChangesAsync(ct);

                return Result(201, "Concession applied.", await BuildAsync(schoolId, student, ct));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex)) { return Result(409, "This concession is already applied to the student.", null); }
            catch (Exception ex) { return Error(ex, "applying fee concession"); }
        }

        public async Task<ApiResponse<StudentFeesDto>> RemoveConcessionAsync(string? schoolId, string studentId, long concessionId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result(400, TenantMessages.NoSchool, null);
            try
            {
                var student = await _repo.GetStudentAsync(schoolId, studentId, ct);
                if (student == null) return Result(404, "Student not found.", null);

                // The student is this school's (checked above), so their applied row is too
                var applied = await _repo.GetAppliedConcessionAsync(studentId, concessionId, track: true, ct);
                if (applied == null) return Result(404, "That concession is not applied to this student.", null);

                _repo.RemoveAppliedConcession(applied);
                await _repo.SaveChangesAsync(ct);

                return Result(200, "Concession removed.", await BuildAsync(schoolId, student, ct));
            }
            catch (Exception ex) { return Error(ex, "removing fee concession"); }
        }

        // ---------------- building the response ----------------

        /// <summary>The enrollment fees are for: the school's current academic year first, else the most recent year.</summary>
        private async Task<StudentFeeEnrollmentRow?> CurrentEnrollmentAsync(string schoolId, string studentId, CancellationToken ct) =>
            (await _repo.GetEnrollmentsAsync(schoolId, studentId, ct))
                .OrderByDescending(e => e.AcademicYearIsCurrent)
                .ThenByDescending(e => e.AcademicYearStart)
                .FirstOrDefault();

        private async Task<StudentFeesDto> BuildAsync(string schoolId, TbStudent student, CancellationToken ct)
        {
            var dto = new StudentFeesDto
            {
                StudentId = student.StudentId,
                StudentName = string.Join(' ', new[] { student.FirstName, student.MiddleName, student.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)))
            };

            var enrollment = await CurrentEnrollmentAsync(schoolId, student.StudentId, ct);
            if (enrollment == null) return dto;

            dto.AcademicYearId = enrollment.AcademicYearId;
            dto.AcademicYearName = enrollment.AcademicYearName;
            dto.ClassId = enrollment.ClassId;
            dto.ClassName = enrollment.ClassName;
            dto.AvailableStructures = (await _repo.GetActiveStructuresAsync(schoolId, enrollment.AcademicYearId, ct))
                .Select(s => new StudentFeeStructureOptionDto { FeeStructureId = s.FeeStructureId, StructureName = s.StructureName, ClassId = s.ClassId, ClassName = s.ClassName })
                .ToList();

            var assignment = await _repo.GetAssignmentAsync(schoolId, student.StudentId, enrollment.AcademicYearId, track: false, ct);
            var structure = assignment == null ? null : await _repo.GetStructureAsync(schoolId, assignment.FeeStructureId, ct);
            if (structure == null) return dto;

            dto.FeeStructureId = structure.FeeStructureId;
            dto.StructureName = structure.StructureName;
            dto.StructureIsActive = structure.IsActive;

            var lines = await _repo.GetStructureLinesAsync(structure.FeeStructureId, ct);
            var applied = await _repo.GetAppliedConcessionsAsync(schoolId, student.StudentId, ct);
            var feeTypesInStructure = lines.Select(l => l.FeeTypeId).ToHashSet();
            bool Applicable(AppliedConcessionRow c) => c.ApplicableFeeTypeId == null || feeTypesInStructure.Contains(c.ApplicableFeeTypeId.Value);

            var discounts = Discounts(lines, applied.Where(Applicable).ToList());
            for (var i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                dto.Lines.Add(new StudentFeeLineDto
                {
                    FeeTypeId = l.FeeTypeId, FeeTypeName = l.FeeTypeName, FeeTypeCode = l.FeeTypeCode,
                    FrequencyId = l.FrequencyId, FrequencyName = l.FrequencyName,
                    Amount = l.Amount, Discount = discounts[i], NetAmount = l.Amount - discounts[i]
                });
            }

            // Per frequency: a monthly and an annual fee are never added into one number
            dto.Totals = dto.Lines
                .GroupBy(l => (l.FrequencyId, l.FrequencyName))
                .OrderBy(g => g.Key.FrequencyId)
                .Select(g => new StudentFeeTotalDto
                {
                    FrequencyId = g.Key.FrequencyId,
                    FrequencyName = g.Key.FrequencyName,
                    Gross = g.Sum(l => l.Amount),
                    Concession = g.Sum(l => l.Discount),
                    NetPayable = g.Sum(l => l.NetAmount)
                }).ToList();

            dto.Concessions = applied.Select(c => new StudentFeeConcessionDto
            {
                StudentFeeConcessionId = c.StudentFeeConcessionId, ConcessionId = c.ConcessionId, ConcessionName = c.ConcessionName,
                ConcessionCode = c.ConcessionCode, ConcessionType = c.ConcessionType, Value = c.Value,
                ApplicableFeeTypeId = c.ApplicableFeeTypeId, ApplicableFeeTypeName = c.ApplicableFeeTypeName,
                Remarks = c.Remarks, ApprovedByName = c.ApprovedByName, AppliedAt = c.AppliedAt, IsApplicable = Applicable(c)
            }).ToList();

            var appliedIds = applied.Select(c => c.ConcessionId).ToHashSet();
            dto.AvailableConcessions = (await _repo.GetActiveConcessionsAsync(schoolId, ct))
                .Where(c => !appliedIds.Contains(c.ConcessionId) && (c.ApplicableFeeTypeId == null || feeTypesInStructure.Contains(c.ApplicableFeeTypeId.Value)))
                .Select(c => new StudentFeeConcessionOptionDto
                {
                    ConcessionId = c.ConcessionId, ConcessionName = c.ConcessionName, ConcessionCode = c.ConcessionCode,
                    ConcessionType = c.ConcessionType, Value = c.Value, ApplicableFeeTypeId = c.ApplicableFeeTypeId, ApplicableFeeTypeName = c.ApplicableFeeTypeName
                }).ToList();

            return dto;
        }

        /// <summary>
        /// What the applied concessions take off each line (same order as <paramref name="lines"/>), never more than the line:
        ///  - PERCENTAGE: that percentage of each line it applies to (one fee head, or every line).
        ///  - FIXED on one fee head: the amount off that line.
        ///  - FIXED on the whole structure: spread over the lines in proportion to their amounts, so the structure as a whole
        ///    gets the fixed amount off (a whole-structure FIXED value is not applied once per line).
        /// Several concessions add up, capped at the line's amount.
        /// </summary>
        public static decimal[] Discounts(IReadOnlyList<StudentFeeLineRow> lines, IReadOnlyList<AppliedConcessionRow> concessions)
        {
            var discount = new decimal[lines.Count];
            var grossAll = lines.Sum(l => l.Amount);

            foreach (var c in concessions)
            {
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (c.ApplicableFeeTypeId is long ft && line.FeeTypeId != ft) continue;

                    if (c.ConcessionType == Percentage) discount[i] += line.Amount * c.Value / 100m;
                    else if (c.ApplicableFeeTypeId != null) discount[i] += c.Value;
                    else if (grossAll > 0) discount[i] += c.Value * line.Amount / grossAll;
                }
            }

            for (var i = 0; i < discount.Length; i++)
                discount[i] = Math.Min(Math.Round(discount[i], 2, MidpointRounding.AwayFromZero), lines[i].Amount);
            return discount;
        }

        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

        private static ApiResponse<StudentFeesDto> Result(int code, string message, StudentFeesDto? data) =>
            new() { Status = code < 400, StatusCode = code, Message = message, Data = data };

        private ApiResponse<StudentFeesDto> Error(Exception ex, string action)
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result(500, "Something went wrong. Please try again later.", null);
        }
    }
}
