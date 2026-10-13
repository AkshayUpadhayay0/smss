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
    public class MasterImportService : IMasterImportService
    {
        private readonly IMasterImportRepository _repo;
        private readonly ILogger<MasterImportService> _logger;

        public MasterImportService(IMasterImportRepository repo, ILogger<MasterImportService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<ApiResponse<MasterImportCatalogDto>> GetCatalogAsync(string? schoolId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<MasterImportCatalogDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var classes = await _repo.GetClassIdsByNameAsync(schoolId, ct);
                var subjects = Set(await _repo.GetSubjectNamesAsync(schoolId, ct));
                var designations = Set(await _repo.GetDesignationNamesAsync(schoolId, ct));
                var departments = Set(await _repo.GetDepartmentNamesAsync(schoolId, ct));
                var types = Set(await _repo.GetAdmissionTypeNamesAsync(schoolId, ct));
                var docs = Set(await _repo.GetDocumentCodesAsync(schoolId, ct));

                var dto = new MasterImportCatalogDto
                {
                    DefaultSectionName = MasterImportCatalog.DefaultSectionName,
                    Classes = MasterImportCatalog.Classes.Select(c => Item(c.Name, c.Name, c.Code, classes.ContainsKey(c.Name))).ToList(),
                    Subjects = MasterImportCatalog.Subjects.Select(s => Item(s.Name, s.Name, s.Code, subjects.Contains(s.Name))).ToList(),
                    Designations = MasterImportCatalog.Designations.Select(s => Item(s.Name, s.Name, null, designations.Contains(s.Name))).ToList(),
                    Departments = MasterImportCatalog.Departments.Select(s => Item(s.Name, s.Name, null, departments.Contains(s.Name))).ToList(),
                    AdmissionTypes = MasterImportCatalog.AdmissionTypes.Select(s => Item(s.Name, s.Name, null, types.Contains(s.Name))).ToList(),
                    DocumentTypes = MasterImportCatalog.DocumentTypes
                        .Select(d => { var i = Item(d.Code, d.Name, d.Code, docs.Contains(d.Code)); i.AppliesTo = d.AppliesTo; i.IsRequired = d.IsRequired; return i; })
                        .ToList()
                };
                return Result(200, "Successfully fetched", dto);
            }
            catch (Exception ex) { return Error<MasterImportCatalogDto>(ex, "fetching the import catalog"); }
        }

        public async Task<ApiResponse<ImportMastersResultDto>> ImportAsync(string? schoolId, ImportMastersRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ImportMastersResultDto>(400, TenantMessages.NoSchool, null);
            try
            {
                // ---- only catalog entries can be selected (keys come from the client) ----
                var classPick = Pick(req.Classes, MasterImportCatalog.Classes, c => c.Name, out var badClass);
                var subjectPick = Pick(req.Subjects, MasterImportCatalog.Subjects, c => c.Name, out var badSubject);
                var designationPick = Pick(req.Designations, MasterImportCatalog.Designations, c => c.Name, out var badDesignation);
                var departmentPick = Pick(req.Departments, MasterImportCatalog.Departments, c => c.Name, out var badDepartment);
                var typePick = Pick(req.AdmissionTypes, MasterImportCatalog.AdmissionTypes, c => c.Name, out var badType);
                var docPick = Pick(req.DocumentTypes, MasterImportCatalog.DocumentTypes, c => c.Code, out var badDoc);

                var unknown = new[] { badClass, badSubject, badDesignation, badDepartment, badType, badDoc }.FirstOrDefault(b => b != null);
                if (unknown != null) return Result<ImportMastersResultDto>(400, $"'{unknown}' is not an available option.", null);

                if (classPick.Count + subjectPick.Count + designationPick.Count + departmentPick.Count + typePick.Count + docPick.Count == 0)
                    return Result<ImportMastersResultDto>(400, "Select at least one item to import.", null);

                var activeId = await _repo.GetActiveStatusIdAsync(ct);
                if (activeId == null)
                {
                    _logger.LogError("Configuration problem: Active general status missing from lut_status");
                    return Result<ImportMastersResultDto>(500, "The system is not configured correctly. Please contact support.", null);
                }

                var existingClasses = await _repo.GetClassIdsByNameAsync(schoolId, ct);
                var classesWithSections = await _repo.GetClassIdsWithSectionsAsync(schoolId, ct);
                var subjects = Set(await _repo.GetSubjectNamesAsync(schoolId, ct));
                var designations = Set(await _repo.GetDesignationNamesAsync(schoolId, ct));
                var departments = Set(await _repo.GetDepartmentNamesAsync(schoolId, ct));
                var types = Set(await _repo.GetAdmissionTypeNamesAsync(schoolId, ct));
                var docs = Set(await _repo.GetDocumentCodesAsync(schoolId, ct));

                var now = DateTime.UtcNow;
                var result = new ImportMastersResultDto();

                // ---- build what is missing (an existing one is never changed) ----
                var newClasses = new List<TbClasses>();
                foreach (var c in classPick)
                {
                    if (existingClasses.ContainsKey(c.Name)) { result.Classes.Skipped++; continue; }
                    newClasses.Add(new TbClasses
                    {
                        SchoolId = schoolId, ClassName = c.Name, ClassCode = c.Code, SequenceOrder = c.SequenceOrder,
                        StatusId = activeId, CreatedAt = now, UpdatedAt = now
                    });
                }

                var newSubjects = subjectPick.Where(s => !subjects.Contains(s.Name)).Select(s => new TbSubjects
                {
                    SchoolId = schoolId, SubjectName = s.Name, SubjectCode = s.Code, StatusId = activeId, CreatedAt = now, UpdatedAt = now
                }).ToList();
                var newDesignations = designationPick.Where(s => !designations.Contains(s.Name)).Select(s => new TbEmployeeDesignations
                {
                    SchoolId = schoolId, DesignationName = s.Name, StatusId = activeId, CreatedAt = now, UpdatedAt = now
                }).ToList();
                var newDepartments = departmentPick.Where(s => !departments.Contains(s.Name)).Select(s => new TbEmployeeDepartments
                {
                    SchoolId = schoolId, DepartmentName = s.Name, StatusId = activeId, CreatedAt = now, UpdatedAt = now
                }).ToList();
                var newTypes = typePick.Where(s => !types.Contains(s.Name)).Select(s => new TbAdmissionTypes
                {
                    SchoolId = schoolId, TypeName = s.Name, StatusId = activeId, CreatedAt = now, UpdatedAt = now
                }).ToList();
                var newDocs = docPick.Where(d => !docs.Contains(d.Code)).Select(d => new TbDocumentType
                {
                    SchoolId = schoolId, DocumentCode = d.Code, DocumentName = d.Name, AppliesTo = d.AppliesTo, IsRequired = d.IsRequired,
                    StatusId = activeId.Value, CreatedAt = now, UpdatedAt = now
                }).ToList();

                result.Subjects = Count(newSubjects.Count, subjectPick.Count);
                result.Designations = Count(newDesignations.Count, designationPick.Count);
                result.Departments = Count(newDepartments.Count, departmentPick.Count);
                result.AdmissionTypes = Count(newTypes.Count, typePick.Count);
                result.DocumentTypes = Count(newDocs.Count, docPick.Count);

                // ---- all or nothing ----
                await using var tx = await _repo.BeginTransactionAsync(ct);

                await _repo.AddRangeAsync(newClasses, ct);
                await _repo.AddRangeAsync(newSubjects, ct);
                await _repo.AddRangeAsync(newDesignations, ct);
                await _repo.AddRangeAsync(newDepartments, ct);
                await _repo.AddRangeAsync(newTypes, ct);
                await _repo.AddRangeAsync(newDocs, ct);
                await _repo.SaveChangesAsync(ct);   // new classes now have their ids

                // Section A for every selected class that has no section yet: the classes just created, and any
                // existing selected class that is still empty. A class that already has sections is left alone.
                var sections = new List<TbSections>();
                foreach (var c in classPick)
                {
                    var classId = newClasses.FirstOrDefault(n => n.ClassName == c.Name)?.ClassId
                                  ?? (existingClasses.TryGetValue(c.Name, out var id) ? id : 0);
                    if (classId == 0 || classesWithSections.Contains(classId)) { result.Sections.Skipped++; continue; }
                    sections.Add(new TbSections
                    {
                        SchoolId = schoolId, ClassId = classId, SectionName = MasterImportCatalog.DefaultSectionName,
                        StatusId = activeId, CreatedAt = now, UpdatedAt = now
                    });
                }
                await _repo.AddRangeAsync(sections, ct);
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                result.Classes.Created = newClasses.Count;
                result.Sections.Created = sections.Count;

                var message = result.TotalCreated == 0
                    ? "Everything you selected was already set up — nothing new was added."
                    : $"Imported {result.TotalCreated} item{(result.TotalCreated == 1 ? "" : "s")} into your masters.";
                return Result(200, message, result);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Someone added one of these at the same moment. Nothing was saved; importing again simply skips it.
                _logger.LogWarning(ex, "Master import hit a duplicate for school {SchoolId}", schoolId);
                return Result<ImportMastersResultDto>(409, "Some of these were added at the same time by someone else. Nothing was imported — please try again.", null);
            }
            catch (Exception ex) { return Error<ImportMastersResultDto>(ex, "importing masters"); }
        }

        // ---------------- helpers ----------------
        private static HashSet<string> Set(IEnumerable<string> values) => new(values, StringComparer.OrdinalIgnoreCase);

        private static MasterImportItemDto Item(string key, string name, string? code, bool added) =>
            new() { Key = key, Name = name, Code = code, AlreadyAdded = added };

        private static ImportCountDto Count(int created, int selected) => new() { Created = created, Skipped = selected - created };

        /// <summary>The distinct catalog entries for the keys; <paramref name="unknownKey"/> is the first key that is not in the catalog.</summary>
        private static List<T> Pick<T>(IEnumerable<string>? keys, IReadOnlyList<T> catalog, Func<T, string> keyOf, out string? unknownKey)
        {
            unknownKey = null;
            var picked = new List<T>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in keys ?? Enumerable.Empty<string>())
            {
                var key = raw?.Trim() ?? string.Empty;
                if (key.Length == 0 || !seen.Add(key)) continue;
                var entry = catalog.FirstOrDefault(c => string.Equals(keyOf(c), key, StringComparison.OrdinalIgnoreCase));
                if (entry == null) { unknownKey ??= key; continue; }
                picked.Add(entry);
            }
            return picked;
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
