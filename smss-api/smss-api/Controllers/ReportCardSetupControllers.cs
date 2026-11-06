using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Examination / Report Card, STAGE 1 (setup). Who may do what (the school always comes from the token; another school's data is 404):
    //   configure (every POST / PUT / DELETE):  School Admin only (CanConfigureReportCards)
    //   read the setup:                         School Admin and Principal (CanViewReportSetup, read-only)
    //   read the exam schedule:                 School Admin, Principal and Teacher (CanViewExamSchedule)
    //   everyone else: 403.   [Authorize(Roles)] is switched off project-wide, so the roles are checked here, from the token.

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class GradeScaleController : ControllerBase
    {
        private readonly IGradeScaleService _service;
        public GradeScaleController(IGradeScaleService service) => _service = service;

        private IActionResult? CannotView() => User.CanViewReportSetup() ? null : this.Forbidden("Only a School Admin or a Principal can see grade scales.");
        private IActionResult? CannotConfigure() => User.CanConfigureReportCards() ? null : this.Forbidden("Only a School Admin can change grade scales.");
        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);

        // GET api/GradeScale?includeInactive=false
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
            CannotView() ?? Reply(await _service.ListAsync(User.GetSchoolId(), includeInactive, ct));

        // GET api/GradeScale/presets   (code constants: CBSE Class IX-style absolute, standard A-E, A-B-C skills, descriptive A-E)
        [HttpGet("presets")]
        public IActionResult Presets() => CannotView() ?? Reply(_service.Presets());

        // GET api/GradeScale/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct) => CannotView() ?? Reply(await _service.GetAsync(User.GetSchoolId(), id, ct));

        // POST api/GradeScale   Body: { name, kind: PERCENT_BANDS | RATING, bands: [{ gradeCode, label, minPercent?, gradePoint?, isFail, description? }] }
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveGradeScaleRequest request, CancellationToken ct) =>
            CannotConfigure() ?? Reply(await _service.CreateAsync(User.GetSchoolId(), request, ct));

        // POST api/GradeScale/from-preset   Body: { presetCode, name? }
        [HttpPost("from-preset")]
        public async Task<IActionResult> FromPreset([FromBody] GradeScaleFromPresetRequest request, CancellationToken ct) =>
            CannotConfigure() ?? Reply(await _service.FromPresetAsync(User.GetSchoolId(), request, ct));

        // PUT api/GradeScale/{id}
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] SaveGradeScaleRequest request, CancellationToken ct) =>
            CannotConfigure() ?? Reply(await _service.UpdateAsync(User.GetSchoolId(), id, request, ct));

        // DELETE api/GradeScale/{id}   (soft: Inactive; 409 while a scheme or activity area uses it)
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id, CancellationToken ct) =>
            CannotConfigure() ?? Reply(await _service.DeleteAsync(User.GetSchoolId(), id, ct));
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportSchemeController : ControllerBase
    {
        private readonly IReportSchemeService _service;
        public ReportSchemeController(IReportSchemeService service) => _service = service;

        private IActionResult? CannotView() => User.CanViewReportSetup() ? null : this.Forbidden("Only a School Admin or a Principal can see report schemes.");
        private IActionResult? CannotConfigure() => User.CanConfigureReportCards() ? null : this.Forbidden("Only a School Admin can change report schemes.");
        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);
        private bool TryUser(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

        // GET api/ReportScheme?academicYearId=&includeInactive=false
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] long? academicYearId, [FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
            CannotView() ?? Reply(await _service.ListAsync(User.GetSchoolId(), academicYearId, includeInactive, ct));

        // GET api/ReportScheme/presets   (the three real card shapes) and /templates (the six card layouts)
        [HttpGet("presets")]
        public IActionResult Presets() => CannotView() ?? Reply(_service.Presets());

        [HttpGet("templates")]
        public IActionResult Templates() => CannotView() ?? Reply(_service.Templates());

        // GET api/ReportScheme/{id}   (the whole scheme: classes, terms and exams, subjects, matrix, skills, warnings)
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct) => CannotView() ?? Reply(await _service.GetAsync(User.GetSchoolId(), id, ct));

        // POST api/ReportScheme   Body: the header only (name, year, mode, scale, rules, template)
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveSchemeRequest request, CancellationToken ct)
        {
            if (CannotConfigure() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.CreateAsync(User.GetSchoolId(), userId, request, ct));
        }

        // POST api/ReportScheme/from-preset   Body: { presetCode, academicYearId, classIds, schemeName?, termDates?, ... }   (the three-step path)
        [HttpPost("from-preset")]
        public async Task<IActionResult> FromPreset([FromBody] SchemeFromPresetRequest request, CancellationToken ct)
        {
            if (CannotConfigure() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.FromPresetAsync(User.GetSchoolId(), userId, request, ct));
        }

        // PUT api/ReportScheme/{id}
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] SaveSchemeRequest request, CancellationToken ct)
        {
            if (CannotConfigure() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.UpdateAsync(User.GetSchoolId(), userId, id, request, ct));
        }

        // DELETE api/ReportScheme/{id}   (soft; frees its classes; 409 once anything has been entered)
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id, CancellationToken ct) => CannotConfigure() ?? Reply(await _service.DeleteAsync(User.GetSchoolId(), id, ct));

        // POST api/ReportScheme/{id}/clone   Body: { name?, academicYearId? }
        [HttpPost("{id:long}/clone")]
        public async Task<IActionResult> Clone(long id, [FromBody] CloneSchemeRequest request, CancellationToken ct)
        {
            if (CannotConfigure() is { } refused) return refused;
            if (!TryUser(out var userId)) return Unauthorized();
            return Reply(await _service.CloneAsync(User.GetSchoolId(), userId, id, request, ct));
        }

        // The parts of a scheme are saved as whole sets: what is not listed is removed (refused once marks exist).
        [HttpPut("{id:long}/classes")]
        public async Task<IActionResult> SetClasses(long id, [FromBody] SetSchemeClassesRequest request, CancellationToken ct) =>
            CannotConfigure() ?? Reply(await _service.SetClassesAsync(User.GetSchoolId(), id, request, ct));

        [HttpPut("{id:long}/terms")]
        public async Task<IActionResult> SaveTerms(long id, [FromBody] SaveTermsRequest request, CancellationToken ct) =>
            CannotConfigure() ?? Reply(await _service.SaveTermsAsync(User.GetSchoolId(), id, request, ct));

        [HttpPut("{id:long}/subjects")]
        public async Task<IActionResult> SaveSubjects(long id, [FromBody] SaveSubjectsRequest request, CancellationToken ct) =>
            CannotConfigure() ?? Reply(await _service.SaveSubjectsAsync(User.GetSchoolId(), id, request, ct));

        [HttpPut("{id:long}/matrix")]
        public async Task<IActionResult> SaveMatrix(long id, [FromBody] SaveMatrixRequest request, CancellationToken ct) =>
            CannotConfigure() ?? Reply(await _service.SaveMatrixAsync(User.GetSchoolId(), id, request, ct));

        [HttpPut("{id:long}/skills")]
        public async Task<IActionResult> SaveSkills(long id, [FromBody] SaveSkillsRequest request, CancellationToken ct) =>
            CannotConfigure() ?? Reply(await _service.SaveSkillsAsync(User.GetSchoolId(), id, request, ct));
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportActivityAreaController : ControllerBase
    {
        private readonly IReportActivityAreaService _service;
        public ReportActivityAreaController(IReportActivityAreaService service) => _service = service;

        private IActionResult? CannotView() => User.CanViewReportSetup() ? null : this.Forbidden("Only a School Admin or a Principal can see activity areas.");
        private IActionResult? CannotConfigure() => User.CanConfigureReportCards() ? null : this.Forbidden("Only a School Admin can change activity areas.");
        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);

        // GET api/ReportActivityArea?includeInactive=false
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
            CannotView() ?? Reply(await _service.ListAsync(User.GetSchoolId(), includeInactive, ct));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveActivityAreaRequest request, CancellationToken ct) =>
            CannotConfigure() ?? Reply(await _service.CreateAsync(User.GetSchoolId(), request, ct));

        // POST api/ReportActivityArea/starter   (Discipline, Cleanliness, Punctuality, Art & Craft, Music, Physical Education, Computer Skills: the missing ones)
        [HttpPost("starter")]
        public async Task<IActionResult> Starter(CancellationToken ct) => CannotConfigure() ?? Reply(await _service.AddStarterAreasAsync(User.GetSchoolId(), ct));

        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] SaveActivityAreaRequest request, CancellationToken ct) =>
            CannotConfigure() ?? Reply(await _service.UpdateAsync(User.GetSchoolId(), id, request, ct));

        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id, CancellationToken ct) => CannotConfigure() ?? Reply(await _service.DeleteAsync(User.GetSchoolId(), id, ct));
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportScheduleController : ControllerBase
    {
        private readonly IReportScheduleService _service;
        public ReportScheduleController(IReportScheduleService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);

        // GET api/ReportSchedule?academicYearId=   (School Admin, Principal and every Teacher may read the dates)
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] long? academicYearId, CancellationToken ct) =>
            User.CanViewExamSchedule() ? Reply(await _service.GetAsync(User.GetSchoolId(), academicYearId, ct)) : this.Forbidden("You can't see the exam schedule.");

        // PUT api/ReportSchedule   Body: { schemeId, exams: [{ examId, examDate?, classDates?: [{ classId, examDate }] }] }   (School Admin only)
        [HttpPut]
        public async Task<IActionResult> Save([FromBody] SaveScheduleRequest request, CancellationToken ct) =>
            User.CanConfigureReportCards() ? Reply(await _service.SaveAsync(User.GetSchoolId(), request, ct)) : this.Forbidden("Only a School Admin can change the exam schedule.");
    }
}
