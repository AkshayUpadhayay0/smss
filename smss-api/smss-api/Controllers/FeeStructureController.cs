using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using smss_api_service_layer.service;

namespace smss_api.Controllers
{
    // Reads: any signed-in user of the school. Writes: School Admin only (CanConfigureFees). Everything is scoped to the caller's own school (from the token); no school id is ever accepted.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FeeStructureController : ControllerBase
    {
        private readonly IFeeStructureService _service;
        private readonly IFeeBackdatingService _backdating;
        public FeeStructureController(IFeeStructureService service, IFeeBackdatingService backdating)
        {
            _service = service;
            _backdating = backdating;
        }

        // GET api/FeeStructure?academicYearId=1&classId=2   (both filters optional)
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] long? academicYearId, [FromQuery] long? classId)
        {
            var response = await _service.GetAllAsync(User.GetSchoolId(), academicYearId, classId);
            return StatusCode(response.StatusCode, response);
        }

        // GET api/FeeStructure/frequencies   (the global fee frequency list, for the line builder's dropdown)
        [HttpGet("frequencies")]
        public async Task<IActionResult> GetFrequencies()
        {
            var response = await _service.GetFrequenciesAsync();
            return StatusCode(response.StatusCode, response);
        }

        // GET api/FeeStructure/5   (the header AND its lines, with fee type and frequency names joined)
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var response = await _service.GetByIdAsync(User.GetSchoolId(), id);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/FeeStructure
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateFeeStructureRequest request)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can create fee structures.");
            var response = await _service.CreateAsync(User.GetSchoolId(), request);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/FeeStructure/5   (name + the full set of lines; academic year and class do not change)
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateFeeStructureRequest request)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can change fee structures.");
            var response = await _service.UpdateAsync(User.GetSchoolId(), id, request);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/FeeStructure/5/lines/12/backdating/preview   Body: { fromDate }
        // What "Apply to earlier months" would create for that fee line (per student: how many dues and what amount). Writes nothing. School Admin only.
        [HttpPost("{id:long}/lines/{feeTypeId:long}/backdating/preview")]
        public async Task<IActionResult> PreviewBackdating(long id, long feeTypeId, [FromBody] BackdatingPreviewRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can apply a fee to earlier months.");
            var response = await _backdating.PreviewAsync(User.GetSchoolId(), id, feeTypeId, request.FromDate, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/FeeStructure/5/lines/12/backdating/apply   Body: { fromDate, reason, confirm: true, expectedDuesCount? }
        // Creates the dues (source BACKDATED) and a log row, in one transaction. Current academic year only; running it again creates nothing new. School Admin only.
        [HttpPost("{id:long}/lines/{feeTypeId:long}/backdating/apply")]
        public async Task<IActionResult> ApplyBackdating(long id, long feeTypeId, [FromBody] ApplyBackdatingRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can apply a fee to earlier months.");
            if (!long.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId)) return Unauthorized();
            var response = await _backdating.ApplyAsync(User.GetSchoolId(), userId, id, feeTypeId, request, ct);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/FeeStructure/5/toggle-status
        [HttpPost("{id:long}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(long id)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can activate or deactivate fee structures.");
            var response = await _service.ToggleStatusAsync(User.GetSchoolId(), id);
            return StatusCode(response.StatusCode, response);
        }
    }
}
