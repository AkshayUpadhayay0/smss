using Microsoft.Extensions.Logging;
using smss_api_db_layer.entity;
using smss_api_db_layer.@interface;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>The exam schedule: a date per exam, optionally a different date per class. Dates only: no seating, no invigilation.</summary>
    public class ReportScheduleService : IReportScheduleService
    {
        private readonly IReportSetupRepository _repo;
        private readonly ILogger<ReportScheduleService> _logger;

        public ReportScheduleService(IReportSetupRepository repo, ILogger<ReportScheduleService> logger) { _repo = repo; _logger = logger; }

        public async Task<ApiResponse<List<ScheduleSchemeDto>>> GetAsync(string? schoolId, long? academicYearId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<List<ScheduleSchemeDto>>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (active == null) return Result<List<ScheduleSchemeDto>>(500, "The system is not configured correctly. Please contact support.", null);
                var list = new List<ScheduleSchemeDto>();
                foreach (var row in await _repo.ListSchemesAsync(schoolId, academicYearId, false, active.Value, ct))
                    list.Add(await BuildAsync(schoolId, row.Scheme.SchemeId, row.YearName, active.Value, ct));
                return Result(200, "Successfully fetched", list);
            }
            catch (Exception ex) { return Error<List<ScheduleSchemeDto>>(ex, "fetching the exam schedule"); }
        }

        public async Task<ApiResponse<ScheduleSchemeDto>> SaveAsync(string? schoolId, SaveScheduleRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(schoolId)) return Result<ScheduleSchemeDto>(400, TenantMessages.NoSchool, null);
            try
            {
                var active = await _repo.GetStatusIdByNameAsync(StatusNames.Active, StatusNames.GeneralType, ct);
                if (active == null) return Result<ScheduleSchemeDto>(500, "The system is not configured correctly. Please contact support.", null);
                var g = await _repo.LoadGraphAsync(schoolId, req.SchemeId!.Value, true, ct);
                if (g == null || g.Scheme.StatusId != active.Value) return Result<ScheduleSchemeDto>(404, "Report scheme not found.", null);
                var year = await _repo.GetYearAsync(schoolId, g.Scheme.AcademicYearId, ct);
                if (year == null) return Result<ScheduleSchemeDto>(500, "Something went wrong. Please try again later.", null);
                var classIds = g.Classes.Select(c => c.ClassId).ToHashSet();

                bool InYear(DateOnly d) => d >= year.StartDate && d <= year.EndDate;
                foreach (var e in req.Exams!)
                {
                    if (g.Exams.All(x => x.ExamId != e.ExamId)) return Result<ScheduleSchemeDto>(400, "An exam in the request does not belong to this scheme.", null);
                    if (e.ExamDate != null && !InYear(e.ExamDate.Value)) return Result<ScheduleSchemeDto>(400, $"Exam dates must be inside the academic year ({year.StartDate:dd MMM yyyy} to {year.EndDate:dd MMM yyyy}).", null);
                    foreach (var d in e.ClassDates ?? new())
                    {
                        if (!classIds.Contains(d.ClassId!.Value)) return Result<ScheduleSchemeDto>(400, "A class date was given for a class that is not in this scheme.", null);
                        if (!InYear(d.ExamDate!.Value)) return Result<ScheduleSchemeDto>(400, "Exam dates must be inside the academic year.", null);
                    }
                    if ((e.ClassDates ?? new()).Select(d => d.ClassId).Distinct().Count() != (e.ClassDates ?? new()).Count) return Result<ScheduleSchemeDto>(400, "A class was listed twice for the same exam.", null);
                }
                if (req.Exams!.Select(e => e.ExamId).Distinct().Count() != req.Exams!.Count) return Result<ScheduleSchemeDto>(400, "An exam was listed twice.", null);

                await using var tx = await _repo.BeginTransactionAsync(ct);
                var touched = req.Exams!.Select(e => e.ExamId!.Value).ToHashSet();
                _repo.RemoveRange(g.ClassDates.Where(d => touched.Contains(d.ExamId)).Cast<object>());
                await _repo.SaveChangesAsync(ct);                  // delete first: a class date may be re-added with the same key
                foreach (var e in req.Exams!)
                {
                    g.Exams.First(x => x.ExamId == e.ExamId).ExamDate = e.ExamDate;
                    _repo.AddRange((e.ClassDates ?? new()).Select(d => (object)new TbReportExamClassDate { ExamId = e.ExamId!.Value, SchemeId = g.Scheme.SchemeId, SchoolId = schoolId, ClassId = d.ClassId!.Value, ExamDate = d.ExamDate!.Value }));
                }
                await _repo.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Result(200, "Exam schedule saved.", await BuildAsync(schoolId, g.Scheme.SchemeId, year.YearName, active.Value, ct));
            }
            catch (Exception ex) { return Error<ScheduleSchemeDto>(ex, "saving the exam schedule"); }
        }

        private async Task<ScheduleSchemeDto> BuildAsync(string schoolId, long schemeId, string yearName, int activeId, CancellationToken ct)
        {
            var g = (await _repo.LoadGraphAsync(schoolId, schemeId, false, ct))!;
            var names = await _repo.ClassNamesAsync(schoolId, g.Classes.Select(c => c.ClassId).ToList(), activeId, ct);
            string Cls(long id) => names.GetValueOrDefault(id, $"Class #{id}");
            return new ScheduleSchemeDto
            {
                SchemeId = schemeId, SchemeName = g.Scheme.SchemeName, AcademicYearName = yearName,
                ClassNames = g.Classes.Select(c => Cls(c.ClassId)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
                Exams = g.Terms.SelectMany(t => g.Exams.Where(e => e.TermId == t.TermId).OrderBy(e => e.Sequence).Select(e => new ScheduleExamDto
                {
                    ExamId = e.ExamId, TermName = t.TermName, ExamName = e.ExamName, ExamDate = e.ExamDate,
                    ClassDates = g.ClassDates.Where(d => d.ExamId == e.ExamId).Select(d => new ScheduleClassDateDto { ClassId = d.ClassId, ClassName = Cls(d.ClassId), ExamDate = d.ExamDate }).OrderBy(d => d.ClassName, StringComparer.OrdinalIgnoreCase).ToList()
                })).ToList()
            };
        }

        private static ApiResponse<T> Result<T>(int code, string message, T? data) => new() { Status = code < 400, StatusCode = code, Message = message, Data = data };
        private ApiResponse<T> Error<T>(Exception ex, string action) { _logger.LogError(ex, "Error while {Action}", action); return Result<T>(500, "Something went wrong. Please try again later.", default); }
    }
}
