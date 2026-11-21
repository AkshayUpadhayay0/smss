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
    /// <summary>
    /// Which classes and sections a school offers in an academic year (S2). The masters (Classes, Sections) are never changed here.
    /// A save replaces the year's whole offered set under the per-school academic-setup lock; a closed year refuses it; something in use cannot be withdrawn.
    /// </summary>
    public class AcademicSetupService : IAcademicSetupService
    {
        private readonly IAcademicSetupRepository _repo;
        private readonly ILogger<AcademicSetupService> _logger;

        public AcademicSetupService(IAcademicSetupRepository repo, ILogger<AcademicSetupService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<YearOfferingDto>> GetOfferingAsync(string? schoolId, long academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<YearOfferingDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var dto = await BuildAsync(schoolId, academicYearId, ct);
                return dto == null ? Result<YearOfferingDto>(404, "Academic year not found", null) : Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<YearOfferingDto>(ex, "fetching the year's classes and sections"); }
        }

        public async Task<ApiResponse<YearOfferingDto>> SaveOfferingAsync(string? schoolId, long userId, long academicYearId, SaveYearOfferingRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<YearOfferingDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<YearOfferingDto>(404, "Academic year not found", null);
                if (year.IsClosed) return Result<YearOfferingDto>(409, ClosedYear.Message, null);

                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType, ct);
                if (activeId == null || inactiveId == null)
                {
                    _logger.LogError("Configuration problem: Active / Inactive statuses are missing in lut_status");
                    return Result<YearOfferingDto>(500, "The system is not configured correctly. Please contact support.", null);
                }

                var classMasters = (await _repo.GetClassMastersAsync(schoolId, ct)).ToDictionary(c => c.ClassId);
                var sectionMasters = (await _repo.GetSectionMastersAsync(schoolId, ct)).ToDictionary(s => s.SectionId);

                // ---- the request: every id must be this school's, a section must sit under its own class, nothing twice
                var wantedClasses = new Dictionary<long, List<OfferedSectionRequest>>();
                var wantedSections = new Dictionary<long, int?>();
                foreach (var c in req.Classes!)
                {
                    var classId = c.ClassId!.Value;
                    if (!classMasters.ContainsKey(classId)) return Result<YearOfferingDto>(400, "A class in the list was not found for your school.", null);
                    if (wantedClasses.ContainsKey(classId)) return Result<YearOfferingDto>(400, $"{classMasters[classId].ClassName} is listed more than once.", null);
                    var list = c.Sections ?? new();
                    wantedClasses[classId] = list;
                    foreach (var s in list)
                    {
                        var sectionId = s.SectionId!.Value;
                        if (!sectionMasters.TryGetValue(sectionId, out var master)) return Result<YearOfferingDto>(400, "A section in the list was not found for your school.", null);
                        if (master.ClassId != classId) return Result<YearOfferingDto>(400, $"Section {master.SectionName} does not belong to {classMasters[classId].ClassName}.", null);
                        if (!wantedSections.TryAdd(sectionId, s.MaxStrength)) return Result<YearOfferingDto>(400, $"Section {master.SectionName} of {classMasters[classId].ClassName} is listed more than once.", null);
                    }
                }

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);   // one academic-setup save per school at a time
                var classRows = await _repo.GetClassOfferingsAsync(schoolId, academicYearId, ct);
                var sectionRows = await _repo.GetSectionOfferingsAsync(schoolId, academicYearId, ct);
                var classRow = classRows.ToDictionary(r => r.ClassId);
                var sectionRow = sectionRows.ToDictionary(r => r.SectionId);

                // ---- something that is not offered yet needs an ACTIVE master
                foreach (var classId in wantedClasses.Keys)
                {
                    var offered = classRow.TryGetValue(classId, out var r) && r.StatusId == activeId;
                    if (!offered && classMasters[classId].StatusId != activeId)
                        return Result<YearOfferingDto>(400, $"{classMasters[classId].ClassName} is inactive. Activate it under Classes before offering it.", null);
                }
                foreach (var sectionId in wantedSections.Keys)
                {
                    var offered = sectionRow.TryGetValue(sectionId, out var r) && r.StatusId == activeId;
                    var m = sectionMasters[sectionId];
                    if (!offered && m.StatusId != activeId)
                        return Result<YearOfferingDto>(400, $"Section {m.SectionName} of {classMasters[m.ClassId].ClassName} is inactive. Activate it under Sections before offering it.", null);
                }

                // ---- withdrawing: refused while the year uses it
                var withdrawnClasses = classRows.Where(r => r.StatusId == activeId && !wantedClasses.ContainsKey(r.ClassId)).Select(r => r.ClassId).ToList();
                var withdrawnSections = sectionRows.Where(r => r.StatusId == activeId && !wantedSections.ContainsKey(r.SectionId)).Select(r => r.SectionId).ToList();
                if (withdrawnClasses.Count + withdrawnSections.Count > 0)
                {
                    var enrolments = await _repo.EnrolmentsBySectionAsync(schoolId, academicYearId, activeId.Value, ct);
                    var withFees = await _repo.FeeStructureClassIdsAsync(schoolId, academicYearId, ct);
                    var withTeacher = await _repo.ClassTeacherSectionIdsAsync(schoolId, academicYearId, activeId.Value, ct);
                    var timetable = await _repo.TimetableBySectionAsync(schoolId, academicYearId, activeId.Value, ct);
                    var blockers = new List<string>();
                    foreach (var classId in withdrawnClasses.Where(withFees.Contains))
                        blockers.Add($"{classMasters[classId].ClassName} has a fee structure for this year");
                    foreach (var sectionId in withdrawnSections)
                    {
                        var m = sectionMasters[sectionId];
                        var label = $"{classMasters[m.ClassId].ClassName} - {m.SectionName}";
                        if (enrolments.TryGetValue(sectionId, out var n) && n > 0) blockers.Add($"{label} has {n} enrolled student{(n == 1 ? "" : "s")}");
                        if (withTeacher.Contains(sectionId)) blockers.Add($"{label} has a class teacher");
                        if (timetable.TryGetValue(sectionId, out var t) && t > 0) blockers.Add($"{label} has {t} timetable slot{(t == 1 ? "" : "s")}");
                    }
                    if (blockers.Count > 0)
                    {
                        var shown = string.Join("; ", blockers.Take(8)) + (blockers.Count > 8 ? $"; and {blockers.Count - 8} more" : "");
                        return Result<YearOfferingDto>(409, $"Nothing was saved. These can't be removed from {year.YearName} while they are in use: {shown}.", null);
                    }
                }

                // ---- apply: classes first (sections refer to them)
                var now = DateTime.UtcNow;
                foreach (var classId in wantedClasses.Keys)
                {
                    if (classRow.TryGetValue(classId, out var r))
                    {
                        if (r.StatusId != activeId) { r.StatusId = activeId.Value; r.UpdatedAt = now; r.UpdatedBy = userId; }
                    }
                    else await _repo.AddAsync(new TbAcademicYearClass { SchoolId = schoolId, AcademicYearId = academicYearId, ClassId = classId, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now, CreatedBy = userId, UpdatedBy = userId }, ct);
                }
                foreach (var r in classRows.Where(r => withdrawnClasses.Contains(r.ClassId))) { r.StatusId = inactiveId.Value; r.UpdatedAt = now; r.UpdatedBy = userId; }
                await _repo.SaveChangesAsync(ct);

                foreach (var (sectionId, max) in wantedSections)
                {
                    var cap = (short?)max;
                    if (sectionRow.TryGetValue(sectionId, out var r))
                    {
                        if (r.StatusId != activeId || r.MaxStrength != cap) { r.StatusId = activeId.Value; r.MaxStrength = cap; r.UpdatedAt = now; r.UpdatedBy = userId; }
                    }
                    else await _repo.AddAsync(new TbAcademicYearSection { SchoolId = schoolId, AcademicYearId = academicYearId, ClassId = sectionMasters[sectionId].ClassId, SectionId = sectionId, MaxStrength = cap, StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now, CreatedBy = userId, UpdatedBy = userId }, ct);
                }
                foreach (var r in sectionRows.Where(r => withdrawnSections.Contains(r.SectionId))) { r.StatusId = inactiveId.Value; r.UpdatedAt = now; r.UpdatedBy = userId; }
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                var dto = await BuildAsync(schoolId, academicYearId, ct);
                return Result(200, "The year's classes and sections were saved.", dto!);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation })
            {
                // only reachable when the school's own data changed under the save (the lock makes it very unlikely)
                _logger.LogWarning(ex, "Academic setup save conflicted for school {SchoolId}", schoolId);
                return Result<YearOfferingDto>(409, "The classes or sections changed while you were saving. Please reload and try again.", null);
            }
            catch (Exception ex) { return Error<YearOfferingDto>(ex, "saving the year's classes and sections"); }
        }

        // ---------------- helpers ----------------
        private async Task<YearOfferingDto?> BuildAsync(string schoolId, long academicYearId, CancellationToken ct)
        {
            var year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
            if (year == null) return null;
            var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct) ?? throw new InvalidOperationException("The Active status is missing from lut_status.");

            var classes = await _repo.GetClassMastersAsync(schoolId, ct);
            var sections = (await _repo.GetSectionMastersAsync(schoolId, ct)).ToLookup(s => s.ClassId);
            var classOffers = (await _repo.GetClassOfferingsAsync(schoolId, academicYearId, ct)).ToDictionary(o => o.ClassId);
            var sectionOffers = (await _repo.GetSectionOfferingsAsync(schoolId, academicYearId, ct)).ToDictionary(o => o.SectionId);
            var enrolments = await _repo.EnrolmentsBySectionAsync(schoolId, academicYearId, activeId, ct);
            var withFees = await _repo.FeeStructureClassIdsAsync(schoolId, academicYearId, ct);
            var withTeacher = await _repo.ClassTeacherSectionIdsAsync(schoolId, academicYearId, activeId, ct);
            var timetable = await _repo.TimetableBySectionAsync(schoolId, academicYearId, activeId, ct);

            var dto = new YearOfferingDto { AcademicYearId = year.AcademicYearId, YearName = year.YearName, IsCurrent = year.IsCurrent, IsClosed = year.IsClosed };
            foreach (var c in classes)
            {
                var offered = classOffers.TryGetValue(c.ClassId, out var co) && co.StatusId == activeId;
                var cd = new OfferingClassDto
                {
                    ClassId = c.ClassId, ClassName = c.ClassName, SequenceOrder = c.SequenceOrder, IsActive = c.StatusId == activeId, Offered = offered,
                    HasFeeStructure = withFees.Contains(c.ClassId),
                };
                foreach (var s in sections[c.ClassId])
                {
                    var sOffered = sectionOffers.TryGetValue(s.SectionId, out var so) && so.StatusId == activeId;
                    var year2 = so?.MaxStrength;
                    var n = enrolments.GetValueOrDefault(s.SectionId);
                    cd.Enrolments += n;
                    cd.Sections.Add(new OfferingSectionDto
                    {
                        SectionId = s.SectionId, SectionName = s.SectionName, IsActive = s.StatusId == activeId, Offered = sOffered,
                        MasterMaxStrength = s.MaxStrength, MaxStrength = sOffered ? year2 : null, EffectiveMaxStrength = year2 ?? s.MaxStrength,
                        Enrolments = n, HasClassTeacher = withTeacher.Contains(s.SectionId), TimetableEntries = timetable.GetValueOrDefault(s.SectionId),
                    });
                }
                if (offered) { dto.OfferedClasses++; dto.OfferedSections += cd.Sections.Count(x => x.Offered); }
                dto.Classes.Add(cd);
            }
            return dto;
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) where T : class =>
            new() { Status = code is >= 200 and < 300, StatusCode = code, Message = message, Data = data };

        private ApiResponse<T> Error<T>(Exception ex, string action) where T : class
        {
            _logger.LogError(ex, "Error while {Action}", action);
            return Result<T>(500, "Something went wrong. Please try again later.", null);
        }
    }
}
