using System.Text.Json;
using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    public class GuardianMergeService : IGuardianMergeService
    {
        /// <summary>The audit documents use camelCase keys (studentId, isPrimary, ...), like the rest of the API.</summary>
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private readonly IGuardianRepository _repo;
        private readonly ILogger<GuardianMergeService> _logger;

        public GuardianMergeService(IGuardianRepository repo, ILogger<GuardianMergeService> logger)
        {
            _repo = repo; _logger = logger;
        }

        // ---------------------------------------------------------------- the report
        public async Task<ApiResponse<DuplicateReportDto>> ListDuplicatesAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<DuplicateReportDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError<DuplicateReportDto>("Active status is missing in lut_status");
                var year = await _repo.GetViewedYearIdAsync(schoolId, SchoolClock.Today(), ct);
                var rows = await _repo.ListDuplicateCandidatesAsync(schoolId, activeId.Value, year, ct);
                var report = new DuplicateReportDto();
                foreach (var group in rows.GroupBy(r => r.PhoneKey).OrderBy(g => g.Key))
                {
                    var list = group.OrderBy(r => r.CreatedAt).ThenBy(r => r.GuardianId).ToList();
                    var dto = new DuplicateGroupDto { PhoneKey = group.Key, NameMatch = GuardianNames.CompareAll(list.Select(r => (string?)r.Name).ToList()) };
                    foreach (var r in list)
                        dto.Guardians.Add(new DuplicateGuardianDto
                        {
                            GuardianId = r.GuardianId, Name = r.Name, Relationship = r.Relationship, MobileNumber = r.Mobile, HasWhatsApp = r.HasWhatsApp, HasEmail = r.HasEmail, HasLogin = r.HasLogin,
                            CreatedAt = r.CreatedAt, IsOldest = r.GuardianId == list[0].GuardianId,
                            Students = r.Students.Select(s => new DuplicateStudentDto { StudentId = s.StudentId, StudentName = s.StudentName, AdmissionNumber = s.AdmissionNumber, ClassName = s.ClassName, SectionName = s.SectionName, IsPrimary = s.IsPrimary }).ToList(),
                        });
                    report.Groups.Add(dto);
                }
                report.GroupCount = report.Groups.Count;
                report.GuardianCount = report.Groups.Sum(g => g.Guardians.Count);
                return Result(200, "Successfully fetched", report);
            }
            catch (Exception ex) { return Error<DuplicateReportDto>(ex, "listing duplicate guardians"); }
        }

        // ---------------------------------------------------------------- what a merge would do
        private sealed class Plan
        {
            public TbGuardian Survivor = null!;
            public TbGuardian Absorbed = null!;
            public List<(TbStudentGuardian Link, TbStudentGuardian? SurvivorLink)> Links = new();
            public List<TbAdmission> PrimaryAdmissions = new();
            public List<(TbAdmissionGuardian Row, bool Drop)> ExtraRows = new();
            public List<TbGuardian> Pointers = new();
            public Dictionary<string, string> Copy = new();
            /// <summary>NONE, MOVE or BLOCKED</summary>
            public string Account = "NONE";
        }

        /// <summary>Why this pair cannot be merged (code, message), or null. Checked on the rows as they are (locked rows for a real merge).</summary>
        private static (int Code, string Message)? Blocker(TbGuardian survivor, TbGuardian absorbed, int activeId)
        {
            if (survivor.MergedIntoGuardianId != null) return (409, "The guardian you chose to keep was itself merged into another one. Choose that one instead.");
            if (absorbed.MergedIntoGuardianId != null) return (409, "This guardian was already merged into another one, so it cannot be merged again.");
            if (survivor.StatusId != activeId) return (409, $"The guardian you chose to keep ({GuardianReuse.Display(survivor)}) is inactive.");
            if (absorbed.StatusId != activeId) return (409, $"The guardian to merge ({GuardianReuse.Display(absorbed)}) is inactive.");
            if (survivor.UserId != null && absorbed.UserId != null) return (409, "Both guardians already have a login, so they cannot be merged. Keep the one that should have the login and remove the other login first.");
            return null;
        }

        private async Task<Plan> BuildPlanAsync(string schoolId, TbGuardian survivor, TbGuardian absorbed, bool tracked, CancellationToken ct)
        {
            var plan = new Plan { Survivor = survivor, Absorbed = absorbed };
            var links = await _repo.GetLinksOfGuardianAsync(schoolId, absorbed.GuardianId, tracked, ct);
            var survivorLinks = links.Count == 0 ? new List<TbStudentGuardian>() : await _repo.GetLinksToStudentsAsync(schoolId, links.Select(l => l.StudentId).ToList(), survivor.GuardianId, tracked, ct);
            foreach (var l in links) plan.Links.Add((l, survivorLinks.FirstOrDefault(s => s.StudentId == l.StudentId)));

            plan.PrimaryAdmissions = await _repo.GetAdmissionsOfGuardianAsync(schoolId, absorbed.GuardianId, tracked, ct);
            var extras = await _repo.GetAdmissionGuardianRowsOfGuardianAsync(schoolId, absorbed.GuardianId, tracked, ct);
            if (extras.Count > 0)
            {
                var onSameAdmissions = await _repo.GetAdmissionGuardianRowsAsync(schoolId, extras.Select(e => e.AdmissionId).ToList(), false, ct);
                var admissions = await _repo.GetAdmissionsByIdsAsync(schoolId, extras.Select(e => e.AdmissionId).ToList(), ct);
                foreach (var e in extras)
                {
                    var survivorAlready = onSameAdmissions.Any(o => o.AdmissionId == e.AdmissionId && o.GuardianId == survivor.GuardianId)
                                          || admissions.Any(a => a.AdmissionId == e.AdmissionId && a.GuardianId == survivor.GuardianId);
                    plan.ExtraRows.Add((e, survivorAlready));
                }
            }
            plan.Pointers = await _repo.GetGuardiansMergedIntoAsync(schoolId, absorbed.GuardianId, tracked, ct);

            if (string.IsNullOrWhiteSpace(survivor.WhatsAppNumber) && !string.IsNullOrWhiteSpace(absorbed.WhatsAppNumber)) plan.Copy["whatsapp_number"] = absorbed.WhatsAppNumber!;
            if (string.IsNullOrWhiteSpace(survivor.Email) && !string.IsNullOrWhiteSpace(absorbed.Email)) plan.Copy["email"] = absorbed.Email!;
            if (string.IsNullOrWhiteSpace(survivor.Occupation) && !string.IsNullOrWhiteSpace(absorbed.Occupation)) plan.Copy["occupation"] = absorbed.Occupation!;
            plan.Account = survivor.UserId != null && absorbed.UserId != null ? "BLOCKED" : absorbed.UserId != null ? "MOVE" : "NONE";
            return plan;
        }

        private static MergeGuardianBriefDto Brief(TbGuardian g) => new()
        {
            GuardianId = g.GuardianId, Name = GuardianReuse.Display(g), Relationship = g.Relationship, MobileNumber = g.MobileNumber, WhatsAppNumber = g.WhatsAppNumber, Email = g.Email, Occupation = g.Occupation, HasLogin = g.UserId != null,
        };

        private (MergeRequestDto? Bad, string? Message) CheckRequest(MergeRequestDto request) =>
            request.SurvivorId == request.AbsorbedId ? (request, "Choose two different guardians.") : (null, null);

        public async Task<ApiResponse<MergePreviewDto>> PreviewAsync(string? schoolId, MergeRequestDto request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MergePreviewDto>(400, TenantMessages.NoSchool, null);
            try
            {
                if (CheckRequest(request).Message is { } same) return Result<MergePreviewDto>(400, same, null);
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                if (activeId == null) return ConfigError<MergePreviewDto>("Active status is missing in lut_status");
                var rows = await _repo.GetGuardiansAsync(schoolId, new[] { request.SurvivorId, request.AbsorbedId }, false, ct);
                var survivor = rows.FirstOrDefault(g => g.GuardianId == request.SurvivorId);
                var absorbed = rows.FirstOrDefault(g => g.GuardianId == request.AbsorbedId);
                if (survivor == null || absorbed == null) return Result<MergePreviewDto>(404, "Guardian not found.", null);   // another school's guardian is a 404

                var plan = await BuildPlanAsync(schoolId, survivor, absorbed, false, ct);
                var year = await _repo.GetViewedYearIdAsync(schoolId, SchoolClock.Today(), ct);
                var refs = await _repo.GetStudentRefsAsync(schoolId, plan.Links.Select(l => l.Link.StudentId).ToList(), year, ct);
                var admissionIds = plan.PrimaryAdmissions.Select(a => a.AdmissionId).Concat(plan.ExtraRows.Select(e => e.Row.AdmissionId)).Distinct().ToList();
                var numbers = (await _repo.GetAdmissionsByIdsAsync(schoolId, admissionIds, ct)).ToDictionary(a => a.AdmissionId, a => a.ApplicationNumber);

                var dto = new MergePreviewDto { Survivor = Brief(survivor), Absorbed = Brief(absorbed), FieldsToCopy = plan.Copy, Account = plan.Account, GuardiansToRepoint = plan.Pointers.Select(p => p.GuardianId).ToList() };
                foreach (var (link, survivorLink) in plan.Links)
                {
                    refs.TryGetValue(link.StudentId, out var s);
                    dto.Links.Add(new MergeLinkDto
                    {
                        StudentId = link.StudentId, StudentName = s?.StudentName ?? link.StudentId, AdmissionNumber = s?.AdmissionNumber ?? "", ClassName = s?.ClassName, SectionName = s?.SectionName,
                        Relationship = link.Relationship, IsPrimary = link.IsPrimary, Action = survivorLink == null ? "MOVE" : "COMBINE",
                    });
                }
                dto.Applications.AddRange(plan.PrimaryAdmissions.Select(a => new MergeApplicationDto { AdmissionId = a.AdmissionId, ApplicationNumber = a.ApplicationNumber, Role = "PRIMARY", Action = "REPOINT" }));
                dto.Applications.AddRange(plan.ExtraRows.Select(e => new MergeApplicationDto { AdmissionId = e.Row.AdmissionId, ApplicationNumber = numbers.GetValueOrDefault(e.Row.AdmissionId, ""), Role = "EXTRA", Action = e.Drop ? "DROP" : "REPOINT" }));
                var blocker = Blocker(survivor, absorbed, activeId.Value);
                if (blocker != null) dto.Blockers.Add(blocker.Value.Message);
                dto.CanMerge = dto.Blockers.Count == 0;
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<MergePreviewDto>(ex, "previewing a guardian merge"); }
        }

        // ---------------------------------------------------------------- the merge
        public async Task<ApiResponse<MergeResultDto>> MergeAsync(string? schoolId, MergeRequestDto request, long userId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MergeResultDto>(400, TenantMessages.NoSchool, null);
            try
            {
                if (!request.Confirm) return Result<MergeResultDto>(400, "Confirm the merge to go on. Look at the preview first: a merge moves the parent's students to the other record.", null);
                if (CheckRequest(request).Message is { } same) return Result<MergeResultDto>(400, same, null);
                var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
                if (reason != null && reason.Length > 500) return Result<MergeResultDto>(400, "The reason can be at most 500 characters.", null);
                var activeId = await _repo.GetGeneralStatusIdAsync(StatusNames.Active, ct);
                var inactiveId = await _repo.GetGeneralStatusIdAsync(StatusNames.Inactive, ct);
                if (activeId == null || inactiveId == null) return ConfigError<MergeResultDto>("Active / Inactive statuses are missing in lut_status");

                await using var tx = await _repo.BeginTransactionAsync(ct);
                // both rows are locked FOR UPDATE in id order: two merges of the same pair (or of overlapping pairs) queue up, and the second reads the first one's result
                var locked = await _repo.LockGuardiansAsync(schoolId, new[] { request.SurvivorId, request.AbsorbedId }, ct);
                var survivor = locked.FirstOrDefault(g => g.GuardianId == request.SurvivorId);
                var absorbed = locked.FirstOrDefault(g => g.GuardianId == request.AbsorbedId);
                if (survivor == null || absorbed == null) return Result<MergeResultDto>(404, "Guardian not found.", null);
                if (Blocker(survivor, absorbed, activeId.Value) is { } blocked) return Result<MergeResultDto>(blocked.Code, blocked.Message, null);

                var plan = await BuildPlanAsync(schoolId, survivor, absorbed, true, ct);
                var now = DateTime.UtcNow;
                var snapshot = JsonSerializer.Serialize(new
                {
                    absorbed.GuardianId, absorbed.SchoolId, absorbed.FirstName, absorbed.LastName, absorbed.Relationship, absorbed.MobileNumber, absorbed.Email, absorbed.WhatsAppNumber, absorbed.Occupation,
                    absorbed.StatusId, absorbed.UserId, absorbed.MergedIntoGuardianId, absorbed.CreatedAt, absorbed.UpdatedAt,
                }, Json);
                var movedLinks = new List<object>();
                var repointed = new Dictionary<string, object?>();

                // ---- phase 1: what is removed (an extra link when both are linked to the same student, an application row the survivor already has) and the login of the absorbed guardian
                var combined = new List<(TbStudentGuardian Survivor, TbStudentGuardian Absorbed)>();
                foreach (var (link, survivorLink) in plan.Links)
                {
                    movedLinks.Add(new
                    {
                        studentGuardianId = link.StudentGuardianId, link.StudentId, fromGuardianId = absorbed.GuardianId, link.Relationship, link.IsPrimary, link.IsEmergencyContact, link.CanPickupStudent, link.PortalAccess,
                        action = survivorLink == null ? "MOVED" : "COMBINED", survivorLinkId = survivorLink?.StudentGuardianId,
                        survivorLinkBefore = survivorLink == null ? null : new { survivorLink.IsPrimary, survivorLink.IsEmergencyContact, survivorLink.CanPickupStudent, survivorLink.PortalAccess },
                    });
                    if (survivorLink != null) { combined.Add((survivorLink, link)); _repo.RemoveLink(link); }
                }
                var extraActions = new List<object>();
                var extrasToAdd = new List<TbAdmissionGuardian>();
                foreach (var (row, drop) in plan.ExtraRows)
                {
                    extraActions.Add(new { row.AdmissionId, action = drop ? "DROPPED" : "REPOINTED" });
                    _repo.RemoveAdmissionGuardian(row);
                    if (!drop) extrasToAdd.Add(new TbAdmissionGuardian { AdmissionId = row.AdmissionId, GuardianId = survivor.GuardianId, SchoolId = schoolId, Relationship = row.Relationship, CreatedAt = row.CreatedAt });
                }
                var movedUserId = plan.Account == "MOVE" ? absorbed.UserId : null;
                if (movedUserId != null) absorbed.UserId = null;   // the unique index (one login per guardian) needs it gone before the survivor takes it
                await _repo.SaveChangesAsync(ct);

                // ---- phase 2: everything that is moved, combined or repointed
                foreach (var (s, a) in combined)   // the primary wins; portal access is on if it was on for either
                {
                    s.IsPrimary = s.IsPrimary || a.IsPrimary;
                    s.IsEmergencyContact = s.IsEmergencyContact || a.IsEmergencyContact;
                    s.CanPickupStudent = s.CanPickupStudent || a.CanPickupStudent;
                    s.PortalAccess = s.PortalAccess || a.PortalAccess;
                    s.UpdatedAt = now;
                }
                foreach (var (link, survivorLink) in plan.Links.Where(l => l.SurvivorLink == null)) { link.GuardianId = survivor.GuardianId; link.UpdatedAt = now; }
                foreach (var a in plan.PrimaryAdmissions) { a.GuardianId = survivor.GuardianId; a.UpdatedAt = now; }
                foreach (var row in extrasToAdd) await _repo.AddAdmissionGuardianAsync(row, ct);
                foreach (var p in plan.Pointers) { p.MergedIntoGuardianId = survivor.GuardianId; p.UpdatedAt = now; }   // no chains: everything that pointed at the absorbed guardian points at the survivor
                if (movedUserId != null) survivor.UserId = movedUserId;

                var changes = new List<TbGuardianChange>();
                foreach (var (field, value) in plan.Copy)
                {
                    if (field == "whatsapp_number") survivor.WhatsAppNumber = value;
                    else if (field == "email") survivor.Email = value;
                    else if (field == "occupation") survivor.Occupation = value;
                    if (field != "occupation") changes.Add(new TbGuardianChange { SchoolId = schoolId, GuardianId = survivor.GuardianId, FieldName = field, OldValue = null, NewValue = value, Source = "MERGE", ChangedBy = userId, ChangedAt = now });
                }
                if (plan.Copy.Count > 0) survivor.UpdatedAt = now;
                if (changes.Count > 0) await _repo.AddChangesAsync(changes, ct);

                absorbed.StatusId = inactiveId.Value;
                absorbed.MergedIntoGuardianId = survivor.GuardianId;
                absorbed.UpdatedAt = now;

                repointed["admissions"] = plan.PrimaryAdmissions.Select(a => a.AdmissionId).ToList();
                repointed["admissionExtras"] = extraActions;
                repointed["mergedPointers"] = plan.Pointers.Select(p => p.GuardianId).ToList();
                repointed["account"] = movedUserId == null ? null : new { movedUserId };
                var merge = new TbGuardianMerge
                {
                    SchoolId = schoolId, SurvivorId = survivor.GuardianId, AbsorbedId = absorbed.GuardianId, MergedBy = userId, MergedAt = now,
                    MovedLinks = JsonSerializer.Serialize(movedLinks, Json), CopiedFields = JsonSerializer.Serialize(plan.Copy), AbsorbedSnapshot = snapshot, Reason = reason, Repointed = JsonSerializer.Serialize(repointed, Json),
                };
                await _repo.AddMergeAsync(merge, ct);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return Result(200, "The guardians were merged.", new MergeResultDto
                {
                    MergeId = merge.MergeId, SurvivorId = survivor.GuardianId, AbsorbedId = absorbed.GuardianId,
                    LinksMoved = plan.Links.Count(l => l.SurvivorLink == null), LinksCombined = combined.Count,
                    ApplicationsRepointed = plan.PrimaryAdmissions.Count + plan.ExtraRows.Count(e => !e.Drop), GuardiansRepointed = plan.Pointers.Count,
                    FieldsCopied = plan.Copy.Keys.ToList(), AccountMoved = movedUserId != null,
                });
            }
            catch (Exception ex) { return Error<MergeResultDto>(ex, "merging guardians"); }
        }

        // ---------------------------------------------------------------- helpers
        private ApiResponse<T> ConfigError<T>(string detail)
        {
            _logger.LogError("Configuration problem: {Detail}", detail);
            return Result<T>(500, "The system is not configured correctly. Please contact support.", default);
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
