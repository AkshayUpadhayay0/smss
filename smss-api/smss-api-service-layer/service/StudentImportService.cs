using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
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
    /// Student Excel import. A dry run validates everything and writes nothing; the commit validates again from scratch (the preview
    /// is never trusted) and writes every student, enrollment and guardian in ONE transaction, built by the same
    /// StudentOnboardingEntities the single-student direct entry uses. Reads cell VALUES only: a formula cell is refused, never evaluated.
    /// </summary>
    public class StudentImportService : IStudentImportService
    {
        /// <summary>Most students in one file. Set from a measurement (see the stage report), not guessed.</summary>
        public const int MaxRows = 500;
        private const int MaxBlankGap = 200;                 // blank rows tolerated between data rows before the sheet is called too large
        public const string SheetName = "Students";

        // key, header, width, required
        private static readonly (string Key, string Header, double Width)[] Columns =
        {
            ("adm", "Admission number", 18), ("name", "Full name", 28), ("gender", "Gender", 11), ("dob", "Date of birth", 15),
            ("cls", "Class", 14), ("sec", "Section", 10), ("roll", "Roll number", 12),
            ("fname", "Father name", 24), ("fphone", "Father phone", 15), ("mname", "Mother name", 24), ("mphone", "Mother phone", 15),
            ("gname", "Guardian name", 24), ("gphone", "Guardian phone", 15), ("addr", "Address", 36),
            ("admdate", "Admission date", 15),   // optional; appended so a file made from the older template (without it) still imports
        };
        private static readonly string[] RequiredKeys = { "name", "gender", "dob", "cls", "sec" };
        private static readonly Dictionary<string, string> HeaderAliases = new()
        {
            ["admissionnumber"] = "adm", ["admissionno"] = "adm", ["admno"] = "adm",
            ["fullname"] = "name", ["studentname"] = "name", ["name"] = "name",
            ["gender"] = "gender", ["sex"] = "gender",
            ["dateofbirth"] = "dob", ["dob"] = "dob", ["birthdate"] = "dob",
            ["class"] = "cls", ["section"] = "sec",
            ["rollnumber"] = "roll", ["rollno"] = "roll",
            ["fathername"] = "fname", ["fatherphone"] = "fphone", ["fathermobile"] = "fphone",
            ["mothername"] = "mname", ["motherphone"] = "mphone", ["mothermobile"] = "mphone",
            ["guardianname"] = "gname", ["guardianphone"] = "gphone", ["guardianmobile"] = "gphone",
            ["address"] = "addr",
            ["admissiondate"] = "admdate", ["dateofadmission"] = "admdate", ["dateofjoining"] = "admdate", ["joiningdate"] = "admdate",
        };
        private static readonly string[] DateFormats = { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd" };
        private static readonly Regex Mobile = new(Rx.Mobile, RegexOptions.Compiled);
        private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

        private const string StudentIdConstraint = "tb_students_pkey";
        private const string AdmissionNumberConstraint = "uq_tb_students_school_admno";

        private readonly IStudentImportRepository _repo;
        private readonly ILogger<StudentImportService> _logger;
        public StudentImportService(IStudentImportRepository repo, ILogger<StudentImportService> logger) { _repo = repo; _logger = logger; }

        // ============================================================================ models
        private sealed class RawRow
        {
            public int Row;
            public Dictionary<string, string> V = new();
            public DateTime? Date;               // when the DOB cell held a real Excel date
            public DateTime? AdmDate;            // when the admission date cell held a real Excel date
            public bool Formula;
        }

        private sealed class Guardian { public string Name = ""; public string Relationship = ""; public string Mobile = ""; }

        private sealed class VRow
        {
            public RawRow Raw = null!;
            public ImportRowDto Dto = new();
            public List<string> Errors = new(), Warnings = new();
            public long ClassId, SectionId;
            public string ClassName = "", SectionName = "";
            public int? GenderId;
            public DateTime? Dob;
            public DateTime? AdmDate;            // the optional admission date; null = the start of the current academic year (as before)
            public string First = "", Last = "";
            public string? Middle, Adm, Roll, Address;
            public List<Guardian> Guardians = new();
        }

        private sealed class Context
        {
            public int ActiveId;
            public TbAdmissionTypes Type = null!;
            public TbAcademicYears Year = null!;
            public string ShortCode = "";
            public List<ImportClassRow> Classes = new();
            public List<ImportSectionRow> Sections = new();
            public List<ImportGenderRow> Genders = new();
        }

        private sealed class Validation
        {
            public ImportPreviewDto Preview = new();
            public List<VRow> Rows = new();
            public Context? Ctx;
        }

        // ============================================================================ template
        public async Task<ImportFileResult> TemplateAsync(string schoolId, CancellationToken ct)
        {
            try
            {
                var active = await _repo.GetActiveStatusIdAsync(ct);
                if (active == null) return Fail(500, "The system is not configured correctly. Please contact support.");
                var classes = await _repo.ListClassesAsync(schoolId, active.Value, ct);
                var sections = await _repo.ListSectionsAsync(schoolId, active.Value, ct);
                if (classes.Count == 0 || sections.Count == 0) return Fail(409, "Add your classes and sections first (School Setup), then download the template: its Class and Section drop-downs are built from them.");
                var genders = await _repo.ListGendersAsync(ct);
                using var wb = BuildWorkbook(classes, sections, genders, null);
                return new ImportFileResult { Content = Save(wb), FileName = "student-import-template.xlsx" };
            }
            catch (Exception ex) { _logger.LogError(ex, "Error while building the student import template"); return Fail(500, "Something went wrong. Please try again later."); }
        }

        // ============================================================================ preview (dry run)
        public async Task<ApiResponse<ImportPreviewDto>> PreviewAsync(string schoolId, byte[] file, CancellationToken ct)
        {
            try
            {
                var bad = ExcelFileGuard.Inspect(file);
                if (bad != null) return Result<ImportPreviewDto>(400, bad, null);
                var v = await ValidateAsync(schoolId, file, ct);
                return Result(200, v.Preview.Summary.CanImport ? "The file is ready to import." : "The file has problems to fix before it can be imported.", v.Preview);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error while previewing the student import"); return Result<ImportPreviewDto>(500, "Something went wrong. Please try again later.", null); }
        }

        // ============================================================================ commit
        public async Task<ApiResponse<ImportResultDto>> CommitAsync(string schoolId, byte[] file, CancellationToken ct)
        {
            try
            {
                var bad = ExcelFileGuard.Inspect(file);
                if (bad != null) return Result<ImportResultDto>(400, bad, null);

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    _repo.ClearTracking();
                    await using var tx = await _repo.BeginTransactionAsync(ct);
                    try
                    {
                        await _repo.LockSchoolImportAsync(schoolId, ct);              // one import per school at a time; also guards admission-number allocation
                        var v = await ValidateAsync(schoolId, file, ct);              // validated AGAIN, inside the lock: the preview is never trusted
                        if (!v.Preview.Summary.CanImport)
                            return Result<ImportResultDto>(422, v.Preview.FileErrors.Count > 0 ? v.Preview.FileErrors[0] : $"The file has {v.Preview.Summary.Errors} row(s) with errors. Nothing was imported. Fix them and upload the file again.", null);

                        var ctx = v.Ctx!;
                        var now = DateTime.UtcNow;
                        var today = DateTime.UtcNow.Date;
                        var baseCount = await _repo.CountStudentsAsync(schoolId, ct);
                        var supplied = v.Rows.Where(r => r.Adm != null).Select(r => r.Adm!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var seq = new Dictionary<string, int>();
                        var all = new List<object>();
                        var created = new List<(VRow Row, string StudentId)>();

                        for (var n = 0; n < v.Rows.Count; n++)
                        {
                            var r = v.Rows[n];
                            var admDate = r.AdmDate ?? AdmissionDateFor(ctx.Year, r.Dob!.Value, today);   // the column when given, else the start of the academic year as before
                            var number = r.Adm;
                            if (number == null)
                            {
                                var prefix = $"ADMNO{admDate.Year}";
                                if (!seq.TryGetValue(prefix, out var cur)) cur = await _repo.MaxAdmissionNumberSequenceAsync(schoolId, prefix, ct);
                                do { cur++; number = $"{prefix}{cur:D4}"; } while (supplied.Contains(number));
                                seq[prefix] = cur;
                            }
                            var studentId = StudentIdHelper.Build(ctx.ShortCode, baseCount + n + 1);
                            var graph = StudentOnboardingEntities.Build(new OnboardingInput
                            {
                                SchoolId = schoolId, StudentId = studentId, AdmissionNumber = number,
                                FirstName = r.First, MiddleName = r.Middle, LastName = r.Last, DateOfBirth = r.Dob!.Value, GenderId = r.GenderId,
                                AdmissionTypeId = ctx.Type.AdmissionTypeId, AdmissionDate = admDate, AddressLine1 = r.Address,
                                ActiveStatusId = ctx.ActiveId, AcademicYearId = ctx.Year.AcademicYearId, ClassSectionId = r.SectionId, RollNumber = r.Roll,
                                Guardians = r.Guardians.Select(g => new OnboardingGuardianInput { Name = g.Name, Relationship = g.Relationship, Mobile = g.Mobile }).ToList(),
                                Now = now
                            });
                            all.Add(graph.Student); all.Add(graph.Enrollment);
                            all.AddRange(graph.Guardians); all.AddRange(graph.Links);
                            created.Add((r, studentId));
                        }

                        _repo.AddRange(all);
                        await _repo.SaveChangesAsync(ct);                              // everything or nothing
                        await tx.CommitAsync(ct);

                        var result = new ImportResultDto
                        {
                            Imported = created.Count,
                            AcademicYearName = ctx.Year.YearName,
                            WarningRows = v.Rows.Count(r => r.Warnings.Count > 0),
                            FirstStudentId = created[0].StudentId,
                            LastStudentId = created[^1].StudentId,
                            ByClass = created.GroupBy(c => (c.Row.ClassName, c.Row.SectionName)).Select(g => new ImportClassCountDto { ClassName = g.Key.ClassName, SectionName = g.Key.SectionName, Students = g.Count() })
                                             .OrderBy(g => g.ClassName, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.SectionName, StringComparer.OrdinalIgnoreCase).ToList(),
                            Notes = new List<string>
                            {
                                "No fee structure was assigned to these students. Assign fees from the fee screens or each student's Fees tab.",
                                "No student logins were created. Use Create Login on a student's profile when a student needs one."
                            }
                        };
                        return Result(201, $"{created.Count} students imported.", result);
                    }
                    catch (DbUpdateException ex) when (ConstraintOf(ex) == StudentIdConstraint)
                    {
                        _logger.LogWarning(ex, "Generated student id collided during an import for school {SchoolId}; retrying.", schoolId);   // tx rolls back on dispose
                    }
                    catch (DbUpdateException ex) when (ConstraintOf(ex) == AdmissionNumberConstraint)
                    {
                        _logger.LogWarning(ex, "An admission number was taken during an import for school {SchoolId}.", schoolId);
                        return Result<ImportResultDto>(409, "An admission number in the file was taken by another student while importing. Nothing was imported. Upload the file again.", null);
                    }
                }
                return Result<ImportResultDto>(409, "Could not allocate student IDs. Nothing was imported. Please try again.", null);
            }
            catch (Exception ex) { _logger.LogError(ex, "Error while importing students"); return Result<ImportResultDto>(500, "Something went wrong. Nothing was imported. Please try again later.", null); }
        }

        // ============================================================================ error report
        public async Task<ImportFileResult> ErrorReportAsync(string schoolId, byte[] file, CancellationToken ct)
        {
            try
            {
                var bad = ExcelFileGuard.Inspect(file);
                if (bad != null) return Fail(400, bad);
                var v = await ValidateAsync(schoolId, file, ct);
                if (v.Ctx == null) return Fail(400, v.Preview.FileErrors.FirstOrDefault() ?? "The file could not be checked.");
                using var wb = BuildWorkbook(v.Ctx.Classes, v.Ctx.Sections, v.Ctx.Genders, v.Rows);
                return new ImportFileResult { Content = Save(wb), FileName = "student-import-errors.xlsx" };
            }
            catch (Exception ex) { _logger.LogError(ex, "Error while building the student import error report"); return Fail(500, "Something went wrong. Please try again later."); }
        }

        // ============================================================================ validation (shared by preview, commit, error report)
        private async Task<Validation> ValidateAsync(string schoolId, byte[] file, CancellationToken ct)
        {
            var v = new Validation();
            v.Preview.Summary.MaxRows = MaxRows;
            void FileError(string m) => v.Preview.FileErrors.Add(m);

            // ---- the school's own set-up must be in place
            var active = await _repo.GetActiveStatusIdAsync(ct);
            if (active == null) { FileError("The system is not configured correctly. Please contact support."); return v; }
            var type = await _repo.GetAdmissionTypeByNameAsync(schoolId, StudentOnboardingService.ExistingStudentTypeName, ct);
            if (type == null) { FileError($"Add an '{StudentOnboardingService.ExistingStudentTypeName}' Admission Type before importing students."); return v; }
            if (type.StatusId != active) { FileError($"The '{StudentOnboardingService.ExistingStudentTypeName}' Admission Type is inactive. Activate it before importing students."); return v; }
            var year = await _repo.GetCurrentYearAsync(schoolId, active.Value, ct);
            if (year == null) { FileError("Set a current academic year first (School Setup). Students are imported into the current year."); return v; }
            var shortCode = await _repo.GetSchoolShortCodeAsync(schoolId, ct);
            if (string.IsNullOrWhiteSpace(shortCode)) { FileError(StudentIdHelper.NoShortCodeMessage); return v; }
            var ctx = new Context
            {
                ActiveId = active.Value, Type = type, Year = year, ShortCode = shortCode,
                Classes = await _repo.ListClassesAsync(schoolId, active.Value, ct),
                Sections = await _repo.ListSectionsAsync(schoolId, active.Value, ct),
                Genders = await _repo.ListGendersAsync(ct)
            };
            v.Ctx = ctx;
            v.Preview.AcademicYearName = year.YearName;

            // ---- read the sheet (values only)
            var raws = ReadSheet(file, FileError);
            if (raws == null || v.Preview.FileErrors.Count > 0) { Finish(v); return v; }
            if (raws.Count == 0) { FileError("The sheet has no students. Fill in the rows under the header and upload again."); Finish(v); return v; }

            // ---- lookups
            var classByName = ctx.Classes.GroupBy(c => Norm(c.ClassName)).ToDictionary(g => g.Key, g => g.First());
            var sectionsOfClass = ctx.Sections.GroupBy(s => s.ClassId).ToDictionary(g => g.Key, g => g.ToList());
            var genderByWord = new Dictionary<string, int>();
            foreach (var g in ctx.Genders)
            {
                genderByWord[Norm(g.GenderName)] = g.GenderId;
                genderByWord[Norm(g.GenderCode)] = g.GenderId;
            }
            foreach (var letter in ctx.Genders.GroupBy(g => Norm(g.GenderName)[..1]).Where(g => g.Count() == 1)) genderByWord.TryAdd(letter.Key, letter.First().GenderId);
            var today = DateTime.UtcNow.Date;

            foreach (var raw in raws) v.Rows.Add(ValidateRow(raw, classByName, sectionsOfClass, genderByWord, today));

            // ---- admission numbers: inside the file, then against the school
            foreach (var grp in v.Rows.Where(r => r.Adm != null).GroupBy(r => r.Adm!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var r in grp) r.Errors.Add($"Admission number {r.Adm} is used more than once in the file (rows {RowList(grp.Select(x => x.Raw.Row))}).");
            var existing = await _repo.ExistingAdmissionNumbersAsync(schoolId, v.Rows.Where(r => r.Adm != null).Select(r => r.Adm!).Distinct().ToList(), ct);
            foreach (var r in v.Rows.Where(r => r.Adm != null && existing.Contains(r.Adm!))) r.Errors.Add($"Admission number {r.Adm} already belongs to a student in this school.");

            // ---- roll numbers: unique within a section (this year)
            foreach (var grp in v.Rows.Where(r => r.Roll != null && r.SectionId != 0).GroupBy(r => (r.SectionId, Roll: r.Roll!.ToLowerInvariant())).Where(g => g.Count() > 1))
                foreach (var r in grp) r.Errors.Add($"Roll number {r.Roll} is used more than once in {r.ClassName} {r.SectionName} (rows {RowList(grp.Select(x => x.Raw.Row))}).");
            var rollsInDb = await _repo.ExistingRollsAsync(schoolId, year.AcademicYearId, v.Rows.Where(r => r.SectionId != 0).Select(r => r.SectionId).Distinct().ToList(), ct);
            var rollSet = rollsInDb.Select(x => (x.SectionId, x.RollNumber.ToLowerInvariant())).ToHashSet();
            foreach (var r in v.Rows.Where(r => r.Roll != null && r.SectionId != 0 && rollSet.Contains((r.SectionId, r.Roll!.ToLowerInvariant()))))
                r.Errors.Add($"Roll number {r.Roll} is already taken in {r.ClassName} {r.SectionName}.");

            // ---- possible duplicate person: same name + date of birth + a guardian phone (warning only)
            var dobs = v.Rows.Where(r => r.Dob != null).Select(r => r.Dob!.Value).Distinct().ToList();
            var people = await _repo.FindPeopleBornOnAsync(schoolId, dobs, ct);
            foreach (var r in v.Rows.Where(r => r.Dob != null && r.Guardians.Count > 0))
            {
                var phones = r.Guardians.Select(g => g.Mobile).ToHashSet();
                var hit = people.FirstOrDefault(p => p.DateOfBirth.Date == r.Dob!.Value.Date && Norm(p.FirstName) == Norm(r.First) && Norm(p.LastName) == Norm(r.Last) && p.GuardianMobiles.Any(phones.Contains));
                if (hit != null) r.Warnings.Add($"Looks like a student already in the school ({hit.FirstName} {hit.LastName}, {hit.StudentId}): same name, date of birth and guardian phone.");
            }
            foreach (var grp in v.Rows.Where(r => r.Dob != null && r.Guardians.Count > 0).GroupBy(r => (Norm(r.First), Norm(r.Last), r.Dob!.Value.Date)).Where(g => g.Count() > 1))
            {
                var list = grp.ToList();
                foreach (var r in list)
                {
                    var others = list.Where(o => o != r && o.Guardians.Any(g => r.Guardians.Any(m => m.Mobile == g.Mobile))).Select(o => o.Raw.Row).ToList();
                    if (others.Count > 0) r.Warnings.Add($"May be the same student as row {RowList(others)} (same name, date of birth and guardian phone).");
                }
            }

            Finish(v);
            return v;
        }

        private static void Finish(Validation v)
        {
            var s = v.Preview.Summary;
            foreach (var r in v.Rows)
            {
                r.Dto.RowNumber = r.Raw.Row;
                r.Dto.Status = r.Errors.Count > 0 ? "ERROR" : r.Warnings.Count > 0 ? "WARNING" : "OK";
                r.Dto.Messages = r.Errors.Concat(r.Warnings).Distinct().ToList();
                v.Preview.Rows.Add(r.Dto);
            }
            s.Total = v.Rows.Count;
            s.Errors = v.Rows.Count(r => r.Errors.Count > 0);
            s.Warnings = v.Rows.Count(r => r.Errors.Count == 0 && r.Warnings.Count > 0);
            s.Ok = s.Total - s.Errors - s.Warnings;
            s.CanImport = v.Preview.FileErrors.Count == 0 && s.Total > 0 && s.Errors == 0;
        }

        private VRow ValidateRow(RawRow raw, Dictionary<string, ImportClassRow> classByName, Dictionary<long, List<ImportSectionRow>> sectionsOfClass, Dictionary<string, int> genderByWord, DateTime today)
        {
            var r = new VRow { Raw = raw };
            string G(string k) => raw.V.TryGetValue(k, out var s) ? s : "";
            void Err(string m) => r.Errors.Add(m);

            if (raw.Formula) Err("This row contains a formula. Formulas are not read: paste the values instead (Paste Special > Values).");

            // ---- name
            var fullName = Spaces.Replace(G("name"), " ").Trim();
            if (fullName.Length == 0) Err("Full name is required.");
            else
            {
                var parts = fullName.Split(' ');
                if (parts.Length < 2) Err("Enter the first and the last name (for example Asha Rawat).");
                else
                {
                    r.First = parts[0]; r.Last = parts[^1];
                    r.Middle = parts.Length > 2 ? string.Join(' ', parts[1..^1]) : null;
                    if (r.First.Length > 100 || r.Last.Length > 100 || (r.Middle?.Length ?? 0) > 100) Err("A name part is longer than 100 characters.");
                }
            }

            // ---- gender
            var gender = G("gender");
            if (gender.Length == 0) Err("Gender is required (Male, Female or Other).");
            else if (genderByWord.TryGetValue(Norm(gender), out var gid)) { r.GenderId = gid; }
            else Err($"Gender '{Clip(gender)}' is not valid. Use Male, Female or Other.");

            // ---- date of birth
            string dobShown = "";
            if (raw.Date != null) { r.Dob = raw.Date.Value.Date; }
            else if (G("dob").Length == 0) Err("Date of birth is required.");
            else if (!DateTime.TryParseExact(G("dob"), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) Err($"Date of birth '{Clip(G("dob"))}' is not a date. Use dd/mm/yyyy, for example 27/08/2015.");
            else r.Dob = d.Date;
            if (r.Dob != null)
            {
                dobShown = r.Dob.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                if (r.Dob.Value >= today) { Err("Date of birth must be in the past."); r.Dob = null; }
                else if (r.Dob.Value.Year < 1990) { Err("Date of birth looks wrong (before 1990)."); r.Dob = null; }
                else
                {
                    var age = today.Year - r.Dob.Value.Year - (r.Dob.Value.Date > today.AddYears(-(today.Year - r.Dob.Value.Year)) ? 1 : 0);
                    if (age < 2 || age > 25) r.Warnings.Add($"Age {age} is unusual for a school student. Check the date of birth.");
                }
            }

            // ---- class and section (the section must belong to the class)
            var cls = G("cls"); var sec = G("sec");
            if (cls.Length == 0) Err("Class is required.");
            if (sec.Length == 0) Err("Section is required.");
            if (cls.Length > 0)
            {
                if (!classByName.TryGetValue(Norm(cls), out var c)) Err($"Class '{Clip(cls)}' does not exist in this school. Pick it from the drop-down.");
                else
                {
                    r.ClassId = c.ClassId; r.ClassName = c.ClassName;
                    if (sec.Length > 0)
                    {
                        var s = sectionsOfClass.GetValueOrDefault(c.ClassId)?.FirstOrDefault(x => Norm(x.SectionName) == Norm(sec));
                        if (s == null)
                        {
                            var have = string.Join(", ", (sectionsOfClass.GetValueOrDefault(c.ClassId) ?? new()).Select(x => x.SectionName));
                            Err($"Section '{Clip(sec)}' does not belong to {c.ClassName}" + (have.Length > 0 ? $" (its sections: {have})." : " (it has no sections)."));
                        }
                        else { r.SectionId = s.SectionId; r.SectionName = s.SectionName; }
                    }
                }
            }

            // ---- admission and roll number (optional)
            var adm = G("adm");
            if (adm.Length > 0) { if (adm.Length > 50) Err("Admission number is longer than 50 characters."); else r.Adm = adm; }
            var roll = G("roll");
            if (roll.Length > 0) { if (roll.Length > 30) Err("Roll number is longer than 30 characters."); else r.Roll = roll; }

            // ---- address (optional)
            var addr = G("addr");
            if (addr.Length > 200) Err("Address is longer than 200 characters."); else if (addr.Length > 0) r.Address = addr;

            // ---- admission date (optional)
            var admText = G("admdate");
            if (raw.AdmDate != null) r.AdmDate = raw.AdmDate.Value.Date;
            else if (admText.Length > 0)
            {
                if (DateTime.TryParseExact(admText, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ad)) r.AdmDate = ad.Date;
                else Err($"Admission date '{Clip(admText)}' is not a date. Use dd/mm/yyyy, for example 15/06/2024, or leave it blank.");
            }
            if (r.AdmDate != null)
            {
                if (r.AdmDate.Value > today) { Err("Admission date cannot be in the future."); r.AdmDate = null; }
                else if (r.Dob != null && r.AdmDate.Value < r.Dob.Value) { Err("Admission date cannot be before the date of birth."); r.AdmDate = null; }
                else if (r.AdmDate.Value.Year < 1990) { Err("Admission date looks wrong (before 1990)."); r.AdmDate = null; }
            }

            // ---- guardians: father, mother, guardian. A block needs a name AND a valid phone. The first complete block is the primary guardian.
            foreach (var (nameKey, phoneKey, label, relationship) in new[] { ("fname", "fphone", "Father", "FATHER"), ("mname", "mphone", "Mother", "MOTHER"), ("gname", "gphone", "Guardian", "GUARDIAN") })
            {
                var name = Spaces.Replace(G(nameKey), " ").Trim(); var phoneRaw = G(phoneKey);
                if (name.Length == 0 && phoneRaw.Length == 0) continue;
                if (name.Length == 0) { Err($"{label} phone is given but {label.ToLowerInvariant()} name is missing."); continue; }
                if (phoneRaw.Length == 0) { Err($"{label} name is given but {label.ToLowerInvariant()} phone is missing."); continue; }
                if (name.Length > 100) { Err($"{label} name is longer than 100 characters."); continue; }
                var phone = NormalizePhone(phoneRaw);
                if (!Mobile.IsMatch(phone)) { Err($"{label} phone '{Clip(phoneRaw)}' is not a valid 10-digit mobile number."); continue; }
                r.Guardians.Add(new Guardian { Name = name, Relationship = relationship, Mobile = phone });
            }
            if (r.Guardians.Count == 0 && !r.Errors.Any(e => e.Contains("phone") || e.Contains("name is given")))
                Err("Give at least one parent or guardian with a name and a phone number.");

            var primary = r.Guardians.FirstOrDefault();
            r.Dto = new ImportRowDto
            {
                AdmissionNumber = r.Adm ?? "", FullName = fullName, Gender = gender,
                DateOfBirth = dobShown, AdmissionDate = r.AdmDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "", ClassName = r.ClassName.Length > 0 ? r.ClassName : cls, SectionName = r.SectionName.Length > 0 ? r.SectionName : sec,
                RollNumber = r.Roll ?? "", Guardian = primary?.Name ?? "", GuardianMobile = primary?.Mobile ?? ""
            };
            return r;
        }

        // ============================================================================ reading the sheet
        private static List<RawRow>? ReadSheet(byte[] file, Action<string> fileError)
        {
            XLWorkbook wb;
            try { wb = new XLWorkbook(new MemoryStream(file, writable: false)); }
            catch (Exception) { fileError("The file could not be read as an Excel workbook. Download the template again and copy your data into it."); return null; }
            using (wb)
            {
                var ws = wb.Worksheets.FirstOrDefault(w => w.Name.Equals(SheetName, StringComparison.OrdinalIgnoreCase));
                if (ws == null) { fileError($"There is no sheet named '{SheetName}'. Use the template and keep its sheet names."); return null; }

                // header row 1 -> column keys
                var map = new Dictionary<string, int>();
                var lastCol = Math.Min(ws.LastColumnUsed(XLCellsUsedOptions.Contents)?.ColumnNumber() ?? 0, 60);
                for (var c = 1; c <= lastCol; c++)
                {
                    var key = HeaderAliases.GetValueOrDefault(Regex.Replace(Cell(ws.Cell(1, c)).Text.ToLowerInvariant(), "[^a-z]", ""));
                    if (key != null && !map.ContainsKey(key)) map[key] = c;
                }
                var missing = RequiredKeys.Where(k => !map.ContainsKey(k)).Select(k => Columns.First(c => c.Key == k).Header).ToList();
                if (missing.Count > 0) { fileError($"The header row is missing: {string.Join(", ", missing)}. Use the template and do not rename its columns."); return null; }

                var lastRow = ws.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1;
                if (lastRow - 1 > MaxRows + MaxBlankGap) { fileError($"The sheet is too large. A file can hold at most {MaxRows} students."); return null; }

                var rows = new List<RawRow>();
                for (var rn = 2; rn <= lastRow; rn++)
                {
                    var raw = new RawRow { Row = rn };
                    var any = false;
                    foreach (var (key, col) in map)
                    {
                        var cell = ws.Cell(rn, col);
                        var read = Cell(cell);
                        if (read.Formula) { raw.Formula = true; any = true; continue; }
                        if (key == "dob" && read.Date != null) { raw.Date = read.Date; any = true; continue; }
                        if (key == "admdate" && read.Date != null) { raw.AdmDate = read.Date; any = true; continue; }
                        if (read.Text.Length > 0) { raw.V[key] = ExcelFileGuard.Unneutralize(read.Text); any = true; }
                    }
                    if (!any) continue;
                    rows.Add(raw);
                    if (rows.Count > MaxRows) { fileError($"The file has more than {MaxRows} students. Split it into files of up to {MaxRows} and import them one after the other."); return null; }
                }
                return rows;
            }
        }

        private static (string Text, DateTime? Date, bool Formula) Cell(IXLCell cell)
        {
            if (cell.HasFormula) return ("", null, true);       // never evaluated, never read
            var val = cell.Value;
            if (val.IsBlank) return ("", null, false);
            if (val.IsDateTime) return ("", val.GetDateTime(), false);
            if (val.IsNumber)
            {
                var n = val.GetNumber();
                return (n == Math.Floor(n) && Math.Abs(n) < 1e15 ? ((long)n).ToString(CultureInfo.InvariantCulture) : n.ToString("R", CultureInfo.InvariantCulture), null, false);
            }
            if (val.IsBoolean) return (val.GetBoolean() ? "TRUE" : "FALSE", null, false);
            if (val.IsText) return (Strip(val.GetText()), null, false);
            return ("", null, false);                           // an Excel error value (#N/A ...) counts as empty
        }

        private static string Strip(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s) if (!char.IsControl(ch) || ch == ' ') sb.Append(ch);
            return sb.ToString().Trim();
        }

        // ============================================================================ building workbooks (template and error report)
        private static XLWorkbook BuildWorkbook(List<ImportClassRow> classes, List<ImportSectionRow> sections, List<ImportGenderRow> genders, List<VRow>? rows)
        {
            var wb = new XLWorkbook();
            var ins = wb.Worksheets.Add("Instructions");
            var ws = wb.Worksheets.Add(SheetName);
            var lists = wb.Worksheets.Add("Lists");

            // ---- Lists: what the drop-downs read from
            var classNames = classes.Select(c => c.ClassName).ToList();
            var sectionNames = sections.Select(s => s.SectionName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
            var genderNames = genders.Select(g => g.GenderName).ToList();
            lists.Cell(1, 1).Value = "Classes"; lists.Cell(1, 2).Value = "Sections (all)"; lists.Cell(1, 3).Value = "Gender";
            lists.Cell(1, 5).Value = "Class"; lists.Cell(1, 6).Value = "Its sections";
            for (var i = 0; i < classNames.Count; i++) lists.Cell(i + 2, 1).Value = classNames[i];
            for (var i = 0; i < sectionNames.Count; i++) lists.Cell(i + 2, 2).Value = sectionNames[i];
            for (var i = 0; i < genderNames.Count; i++) lists.Cell(i + 2, 3).Value = genderNames[i];
            var rowNo = 2;
            foreach (var c in classes)
            {
                lists.Cell(rowNo, 5).Value = c.ClassName;
                lists.Cell(rowNo, 6).Value = string.Join(", ", sections.Where(s => s.ClassId == c.ClassId).Select(s => s.SectionName));
                rowNo++;
            }
            lists.Row(1).Style.Font.Bold = true;
            lists.Columns().AdjustToContents();

            // ---- Students: header, formats, drop-downs
            for (var i = 0; i < Columns.Length; i++)
            {
                var h = ws.Cell(1, i + 1);
                h.Value = Columns[i].Header;
                h.Style.Font.Bold = true; h.Style.Font.FontColor = XLColor.White; h.Style.Fill.BackgroundColor = XLColor.FromHtml("#7C5CFC");
                h.Style.Alignment.WrapText = true; h.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Column(i + 1).Width = Columns[i].Width;
                if (Columns[i].Key != "dob" && Columns[i].Key != "admdate") ws.Column(i + 1).Style.NumberFormat.Format = "@";    // text: phones and numbers keep their zeros
            }
            ws.Column(4).Style.DateFormat.Format = "dd/mm/yyyy";
            var admCol = Array.FindIndex(Columns, c => c.Key == "admdate") + 1;
            ws.Column(admCol).Style.DateFormat.Format = "dd/mm/yyyy";
            ws.Row(1).Height = 30;
            ws.SheetView.FreezeRows(1);
            var last = MaxRows + 1;

            // Named ranges (not a direct cross-sheet range) so the drop-downs are classic data validations that every spreadsheet program reads
            if (genderNames.Count > 0) { wb.NamedRanges.Add("GenderList", lists.Range(2, 3, genderNames.Count + 1, 3)); AddList(ws.Range(2, 3, last, 3), "GenderList", "Pick Male, Female or Other from the list."); }
            wb.NamedRanges.Add("ClassList", lists.Range(2, 1, classNames.Count + 1, 1)); AddList(ws.Range(2, 5, last, 5), "ClassList", "Pick the class from the list.");
            if (sectionNames.Count > 0) { wb.NamedRanges.Add("SectionList", lists.Range(2, 2, sectionNames.Count + 1, 2)); AddList(ws.Range(2, 6, last, 6), "SectionList", "Pick the section from the list."); }
            var dv = ws.Range(2, 4, last, 4).CreateDataValidation();
            dv.Date.Between(new DateTime(1990, 1, 1), new DateTime(2100, 1, 1));
            dv.ShowErrorMessage = true; dv.ErrorTitle = "Date of birth"; dv.ErrorMessage = "Enter a date as dd/mm/yyyy.";
            var dva = ws.Range(2, admCol, last, admCol).CreateDataValidation();
            dva.Date.Between(new DateTime(1990, 1, 1), new DateTime(2100, 1, 1));
            dva.IgnoreBlanks = true; dva.ShowErrorMessage = true; dva.ErrorTitle = "Admission date"; dva.ErrorMessage = "Enter a date as dd/mm/yyyy, or leave it blank.";

            if (rows != null)
            {
                ws.Cell(1, Columns.Length + 1).Value = "Status"; ws.Cell(1, Columns.Length + 2).Value = "Messages";
                foreach (var c in new[] { Columns.Length + 1, Columns.Length + 2 })
                {
                    var h = ws.Cell(1, c); h.Style.Font.Bold = true; h.Style.Font.FontColor = XLColor.White; h.Style.Fill.BackgroundColor = XLColor.FromHtml("#B42318");
                    ws.Column(c).Style.NumberFormat.Format = "@";
                }
                ws.Column(Columns.Length + 1).Width = 11; ws.Column(Columns.Length + 2).Width = 80;
                var r = 2;
                foreach (var row in rows.OrderBy(x => x.Raw.Row))
                {
                    for (var i = 0; i < Columns.Length; i++)
                    {
                        var key = Columns[i].Key;
                        var cell = ws.Cell(r, i + 1);
                        if (key == "dob" && row.Raw.Date != null) { cell.Value = row.Raw.Date.Value.Date; continue; }
                        if (key == "admdate" && row.Raw.AdmDate != null) { cell.Value = row.Raw.AdmDate.Value.Date; continue; }
                        var text = row.Raw.V.GetValueOrDefault(key, "");
                        if (text.Length > 0) PutText(cell, text);
                    }
                    var statusCell = ws.Cell(r, Columns.Length + 1); var msgCell = ws.Cell(r, Columns.Length + 2);
                    PutText(statusCell, row.Dto.Status);
                    PutText(msgCell, string.Join(" ", row.Dto.Messages));
                    if (row.Errors.Count > 0) ws.Range(r, 1, r, Columns.Length + 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#FEE4E2");
                    else if (row.Warnings.Count > 0) ws.Range(r, 1, r, Columns.Length + 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF0C7");
                    r++;
                }
            }

            // ---- Instructions
            var lines = new (string Text, bool Bold)[]
            {
                ("Import students", true),
                ("Fill the Students sheet, one student per row, starting at row 2. Do not rename or move the header row. Save as Excel Workbook (.xlsx).", false),
                ("", false),
                ("Required columns", true),
                ("Full name (first and last name), Gender, Date of birth, Class, Section, and at least one of Father / Mother / Guardian with BOTH a name and a phone.", false),
                ("", false),
                ("Formats", true),
                ("Date of birth: dd/mm/yyyy, for example 27/08/2015 (a real Excel date also works).", false),
                ("Gender: pick Male, Female or Other from the drop-down.", false),
                ("Class and Section: pick from the drop-downs. The section must belong to the class (see the Lists sheet).", false),
                ("Phone: a 10-digit Indian mobile number starting 6-9. +91 or a leading 0 is removed for you.", false),
                ("Admission date: optional, dd/mm/yyyy (a real Excel date also works). It cannot be in the future or before the date of birth. Leave it blank and the start of the current academic year is used.", false),
                ("Admission number: optional. Leave it blank and the system gives the next number (ADMNO + year + 4 digits).", false),
                ("Roll number: optional, but it cannot repeat inside a section.", false),
                ($"At most {MaxRows} students per file and 1 MB. Only .xlsx; no macros, no formulas (paste values).", false),
                ("", false),
                ("What happens next", true),
                ("Upload the file to see a preview first. Nothing is saved until you confirm, and a single error stops the whole import, so you never end up with half a class.", false),
                ("Students are added to the current academic year as 'Existing Student'. No fees and no logins are created by the import.", false),
            };
            for (var i = 0; i < lines.Length; i++) { var c = ins.Cell(i + 1, 1); c.Value = lines[i].Text; c.Style.Font.Bold = lines[i].Bold; c.Style.Alignment.WrapText = true; }
            ins.Column(1).Width = 110;
            ins.Cell(1, 1).Style.Font.FontSize = 16;
            ws.SetTabActive();
            return wb;
        }

        /// <summary>
        /// Writes user-supplied text safely. Risky text (leading = + - @) gets a REAL leading apostrophe stored in the value, so it stays inert even
        /// when the sheet is saved as CSV. ClosedXML treats a single leading apostrophe as Excel's quote-prefix and drops it, hence the doubled one.
        /// </summary>
        private static void PutText(IXLCell cell, string text)
        {
            var safe = ExcelFileGuard.Neutralize(text);
            cell.SetValue(safe.Length > 0 && safe[0] == (char)39 ? "'" + safe : safe);
        }

        private static void AddList(IXLRange target, string namedRange, string message)
        {
            var dv = target.CreateDataValidation();
            dv.List(namedRange, true);
            dv.IgnoreBlanks = true;
            dv.ShowErrorMessage = true; dv.ErrorTitle = "Pick from the list"; dv.ErrorMessage = message;
        }

        private static byte[] Save(XLWorkbook wb) { using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray(); }

        // ============================================================================ small helpers
        private static string Norm(string s) => Spaces.Replace(s ?? "", " ").Trim().ToLowerInvariant();
        private static string Clip(string s) => s.Length > 40 ? s[..40] + "…" : s;
        private static string RowList(IEnumerable<int> rows) => string.Join(", ", rows.OrderBy(x => x).Take(8)) + (rows.Count() > 8 ? ", …" : "");

        /// <summary>Strips spaces, dashes, brackets, a leading +91 / 91 and a leading 0.</summary>
        public static string NormalizePhone(string raw)
        {
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
            else if (digits.Length == 11 && digits.StartsWith('0')) digits = digits[1..];
            return digits;
        }

        /// <summary>The historical date students are admitted on: the start of the current academic year, never in the future and never before the date of birth.</summary>
        private static DateTime AdmissionDateFor(TbAcademicYears year, DateTime dob, DateTime today)
        {
            var d = year.StartDate.ToDateTime(TimeOnly.MinValue);
            if (d < dob) d = dob;
            return d > today ? today : d;
        }

        private static string? ConstraintOf(DbUpdateException ex) =>
            (ex.InnerException as PostgresException) is { SqlState: PostgresErrorCodes.UniqueViolation } pg ? pg.ConstraintName : null;

        private static ImportFileResult Fail(int code, string message) => new() { StatusCode = code, Message = message };
        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
    }
}
