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
    /// S3: which subjects a class is taught in an academic year, and the per-section exceptions ("also taught here" / "not taught here").
    /// A save replaces the class's whole set for the year under the per-school academic-setup lock, in one transaction.
    /// A closed year refuses it (409); a subject that something in the year uses cannot be taken away (409, with counts).
    /// What a section is actually taught is the view vw_section_effective_subjects (class mapping - REMOVE + ADD): this service only writes its inputs.
    /// </summary>
    public class SubjectSetupService : ISubjectSetupService
    {
        private readonly IClassSubjectRepository _repo;
        private readonly ILogger<SubjectSetupService> _logger;

        public SubjectSetupService(IClassSubjectRepository repo, ILogger<SubjectSetupService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<YearClassSubjectsDto>> GetAsync(string? schoolId, long academicYearId, long classId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<YearClassSubjectsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                if (activeId == null) return Result<YearClassSubjectsDto>(500, "The system is not configured correctly. Please contact support.", null);
                var year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<YearClassSubjectsDto>(404, "Academic year not found", null);
                var cls = await _repo.GetClassAsync(schoolId, classId);
                if (cls == null) return Result<YearClassSubjectsDto>(404, "Class not found", null);
                return Result(200, "Successfully fetched", await BuildAsync(schoolId, year, cls, activeId.Value, ct));
            }
            catch (Exception ex) { return Error<YearClassSubjectsDto>(ex, "fetching the subjects of a class for a year"); }
        }

        public async Task<ApiResponse<YearClassSubjectsDto>> SaveAsync(string? schoolId, long? userId, long academicYearId, long classId, SaveYearClassSubjectsRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<YearClassSubjectsDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType);
                var inactiveId = await _repo.GetStatusIdByNameAsync(StatusNames.Inactive, StatusNames.GeneralType);
                if (activeId == null || inactiveId == null) return Result<YearClassSubjectsDto>(500, "The system is not configured correctly. Please contact support.", null);

                var year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
                if (year == null) return Result<YearClassSubjectsDto>(404, "Academic year not found", null);
                if (year.ClosedAt != null) return Result<YearClassSubjectsDto>(409, ClosedYear.Message, null);
                var cls = await _repo.GetClassAsync(schoolId, classId);
                if (cls == null) return Result<YearClassSubjectsDto>(404, "Class not found", null);
                if (!await _repo.IsClassOfferedAsync(schoolId, academicYearId, classId, activeId.Value, ct))
                    return Result<YearClassSubjectsDto>(409, $"{cls.ClassName} is not offered in {year.YearName}. Offer it first (Academic Setup, classes and sections).", null);

                var wanted = (req.SubjectIds ?? new()).Distinct().ToList();
                var subjects = (await _repo.AllSubjectsAsync(schoolId, ct)).ToDictionary(s => s.SubjectId);
                if (wanted.Any(id => !subjects.ContainsKey(id))) return Result<YearClassSubjectsDto>(400, "One or more selected subjects were not found for your school.", null);

                var sections = await _repo.OfferedSectionsAsync(schoolId, academicYearId, classId, activeId.Value, ct);
                var sectionIds = sections.Select(s => s.SectionId).ToHashSet();
                var wantedOver = new Dictionary<long, (HashSet<long> Extra, HashSet<long> Not)>();
                foreach (var o in req.Sections ?? new())
                {
                    if (!sectionIds.Contains(o.SectionId)) return Result<YearClassSubjectsDto>(400, "A section in the request is not an offered section of this class in this year.", null);
                    if (wantedOver.ContainsKey(o.SectionId)) return Result<YearClassSubjectsDto>(400, "A section appears twice in the request.", null);
                    var extra = (o.ExtraSubjectIds ?? new()).ToHashSet();
                    var not = (o.NotTaughtSubjectIds ?? new()).ToHashSet();
                    var name = sections.First(s => s.SectionId == o.SectionId).SectionName;
                    if (extra.Overlaps(not)) return Result<YearClassSubjectsDto>(400, $"Section {name}: a subject can't be both extra and not taught.", null);
                    if (extra.Any(id => wanted.Contains(id))) return Result<YearClassSubjectsDto>(400, $"Section {name}: an extra subject is already taught by the whole class.", null);
                    if (not.Any(id => !wanted.Contains(id))) return Result<YearClassSubjectsDto>(400, $"Section {name}: only a subject the class has can be marked not taught here.", null);
                    if (extra.Concat(not).Any(id => !subjects.ContainsKey(id))) return Result<YearClassSubjectsDto>(400, "One or more selected subjects were not found for your school.", null);
                    wantedOver[o.SectionId] = (extra, not);
                }

                await using var tx = await _repo.BeginTransactionAsync(ct);
                await _repo.LockSchoolAsync(schoolId, ct);
                // the year may have been closed while we waited for the lock
                year = await _repo.GetYearAsync(schoolId, academicYearId, ct);
                if (year == null || year.ClosedAt != null) return Result<YearClassSubjectsDto>(409, ClosedYear.Message, null);

                var existing = await _repo.GetTrackedByClassAsync(schoolId, classId, academicYearId);
                var existingOver = await _repo.GetOverridesTrackedAsync(schoolId, academicYearId, classId, ct);
                var beforeClass = existing.Where(m => m.StatusId == activeId).Select(m => m.SubjectId).ToHashSet();
                var beforeOver = existingOver.Where(o => o.StatusId == activeId).ToList();
                var wantedSet = wanted.ToHashSet();

                // a subject that is NEWLY mapped / extra must be an Active master (an Inactive one stays only where it already is)
                var addedAny = wanted.Where(id => !beforeClass.Contains(id)).Concat(wantedOver.Values.SelectMany(v => v.Extra)).Distinct().ToList();
                var inactiveNew = addedAny.Where(id => subjects[id].StatusId != activeId).Select(id => subjects[id].SubjectName).ToList();
                if (inactiveNew.Count > 0) return Result<YearClassSubjectsDto>(400, $"These subjects are Inactive and can't be added: {string.Join(", ", inactiveNew)}.", null);

                // effective subjects per section, before and after
                HashSet<long> Effective(HashSet<long> cls, IEnumerable<(string Action, long Subject)> ov)
                {
                    var list = ov.ToList();
                    var res = cls.Where(s => !list.Any(o => o.Action == TbSectionSubjectOverride.Remove && o.Subject == s)).ToHashSet();
                    foreach (var o in list.Where(o => o.Action == TbSectionSubjectOverride.Add && !cls.Contains(o.Subject))) res.Add(o.Subject);
                    return res;
                }
                var removed = new List<(long SectionId, long SubjectId)>();
                var afterAnyTaught = new HashSet<long>();
                foreach (var s in sections)
                {
                    var before = Effective(beforeClass, beforeOver.Where(o => o.SectionId == s.SectionId).Select(o => (o.Action, o.SubjectId)));
                    var ov = wantedOver.TryGetValue(s.SectionId, out var w) ? w : (new HashSet<long>(), new HashSet<long>());
                    var after = Effective(wantedSet, ov.Item1.Select(i => (TbSectionSubjectOverride.Add, i)).Concat(ov.Item2.Select(i => (TbSectionSubjectOverride.Remove, i))));
                    removed.AddRange(before.Where(x => !after.Contains(x)).Select(x => (s.SectionId, x)));
                    afterAnyTaught.UnionWith(after);
                }
                var goneFromClass = beforeClass.Where(x => !wantedSet.Contains(x) && !afterAnyTaught.Contains(x)).ToList();

                if (removed.Count > 0 || goneFromClass.Count > 0)
                {
                    var usage = await _repo.GetUsageAsync(schoolId, academicYearId, classId, activeId.Value, ct);
                    var blocks = SubjectRemovalGuard.Blocks(removed,
                        sections.ToDictionary(s => s.SectionId, s => $"{cls.ClassName} - {s.SectionName}"),
                        subjects.ToDictionary(k => k.Key, k => k.Value.SubjectName), usage, goneFromClass);
                    if (blocks.Count > 0)
                    {
                        _logger.LogInformation("Subjects not saved for class {ClassId}, year {YearId}: {Count} in use", classId, academicYearId, blocks.Count);
                        return new ApiResponse<YearClassSubjectsDto>
                        {
                            Status = false, StatusCode = 409, Data = null,
                            Message = $"Nothing was saved. These can't be removed from {year.YearName} while they are in use: {string.Join("; ", blocks)}."
                        };
                    }
                }

                var now = DateTime.UtcNow;
                // class mapping: a pure join row, so removing really deletes it (as before)
                _repo.RemoveRange(existing.Where(m => !wantedSet.Contains(m.SubjectId)));
                foreach (var m in existing.Where(m => wantedSet.Contains(m.SubjectId) && m.StatusId != activeId)) { m.StatusId = activeId; m.UpdatedAt = now; }
                var already = existing.Select(m => m.SubjectId).ToHashSet();
                foreach (var id in wanted.Where(id => !already.Contains(id)))
                    await _repo.AddAsync(new TbClassSubjects { SchoolId = schoolId, AcademicYearId = academicYearId, ClassId = classId, SubjectId = id, StatusId = activeId, CreatedAt = now, UpdatedAt = now });

                // overrides: never deleted (status flag), so the history of an exception stays
                var wantedPairs = wantedOver.SelectMany(kv => kv.Value.Extra.Select(i => (Section: kv.Key, Subject: i, Action: TbSectionSubjectOverride.Add))
                                  .Concat(kv.Value.Not.Select(i => (Section: kv.Key, Subject: i, Action: TbSectionSubjectOverride.Remove)))).ToList();
                foreach (var o in existingOver)
                {
                    var hit = wantedPairs.FirstOrDefault(p => p.Section == o.SectionId && p.Subject == o.SubjectId);
                    if (hit == default) { if (o.StatusId == activeId) { o.StatusId = inactiveId.Value; o.UpdatedAt = now; o.UpdatedBy = userId; } }
                    else { o.Action = hit.Action; o.StatusId = activeId.Value; o.UpdatedAt = now; o.UpdatedBy = userId; }
                }
                foreach (var p in wantedPairs.Where(p => !existingOver.Any(o => o.SectionId == p.Section && o.SubjectId == p.Subject)))
                    await _repo.AddOverrideAsync(new TbSectionSubjectOverride
                    {
                        SchoolId = schoolId, AcademicYearId = academicYearId, ClassId = classId, SectionId = p.Section, SubjectId = p.Subject, Action = p.Action,
                        StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now, CreatedBy = userId, UpdatedBy = userId
                    }, ct);

                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return Result(200, "Subjects saved", await BuildAsync(schoolId, year, cls, activeId.Value, ct));
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Result<YearClassSubjectsDto>(409, "The subjects were changed by someone else at the same moment. Please reload and try again.", null);
            }
            catch (Exception ex) { return Error<YearClassSubjectsDto>(ex, "saving the subjects of a class for a year"); }
        }

        private async Task<YearClassSubjectsDto> BuildAsync(string schoolId, TbAcademicYears year, TbClasses cls, int activeId, CancellationToken ct)
        {
            var offered = await _repo.IsClassOfferedAsync(schoolId, year.AcademicYearId, cls.ClassId, activeId, ct);
            var mapping = (await _repo.GetTrackedByClassAsync(schoolId, cls.ClassId, year.AcademicYearId)).Where(m => m.StatusId == activeId).Select(m => m.SubjectId).ToHashSet();
            var subjects = await _repo.AllSubjectsAsync(schoolId, ct);
            var sections = await _repo.OfferedSectionsAsync(schoolId, year.AcademicYearId, cls.ClassId, activeId, ct);
            var overrides = (await _repo.GetOverridesTrackedAsync(schoolId, year.AcademicYearId, cls.ClassId, ct)).Where(o => o.StatusId == activeId).ToList();
            var usage = await _repo.GetUsageAsync(schoolId, year.AcademicYearId, cls.ClassId, activeId, ct);

            var dto = new YearClassSubjectsDto
            {
                AcademicYearId = year.AcademicYearId, YearName = year.YearName, IsCurrent = year.IsCurrent, IsClosed = year.ClosedAt != null,
                ClassId = cls.ClassId, ClassName = cls.ClassName, ClassOffered = offered
            };
            foreach (var s in subjects.Where(s => s.StatusId == activeId || mapping.Contains(s.SubjectId)))
            {
                var rows = usage.PerSection.Where(u => u.SubjectId == s.SubjectId).ToList();
                var exam = usage.ExamSetups.GetValueOrDefault(s.SubjectId);
                var total = rows.Sum(r => r.Total) + exam;
                dto.Subjects.Add(new SetupSubjectDto
                {
                    SubjectId = s.SubjectId, SubjectName = s.SubjectName, SubjectCode = s.SubjectCode, IsActive = s.StatusId == activeId, Mapped = mapping.Contains(s.SubjectId),
                    InUse = total,
                    InUseText = total == 0 ? null : SubjectRemovalGuard.UseText(rows.Sum(r => r.Timetable), rows.Sum(r => r.Homework), rows.Sum(r => r.Tests), rows.Sum(r => r.Marks), exam)
                });
            }
            foreach (var sec in sections)
            {
                var mine = overrides.Where(o => o.SectionId == sec.SectionId).ToList();
                var extra = mine.Where(o => o.Action == TbSectionSubjectOverride.Add && !mapping.Contains(o.SubjectId)).Select(o => o.SubjectId).ToList();
                var not = mine.Where(o => o.Action == TbSectionSubjectOverride.Remove && mapping.Contains(o.SubjectId)).Select(o => o.SubjectId).ToList();
                dto.Sections.Add(new SetupSectionDto
                {
                    SectionId = sec.SectionId, SectionName = sec.SectionName, ExtraSubjectIds = extra, NotTaughtSubjectIds = not,
                    InUseSubjectIds = usage.PerSection.Where(u => u.SectionId == sec.SectionId && u.Total > 0).Select(u => u.SubjectId).ToList(),
                    EffectiveCount = mapping.Count - not.Count + extra.Count
                });
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
