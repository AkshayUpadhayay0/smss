using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Everything is scoped to the caller's own school (from the token); no school id is ever accepted.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FeeStructureController : ControllerBase
    {
        private readonly IFeeStructureService _service;
        public FeeStructureController(IFeeStructureService service) => _service = service;

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
            var response = await _service.CreateAsync(User.GetSchoolId(), request);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/FeeStructure/5   (name + the full set of lines; academic year and class do not change)
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateFeeStructureRequest request)
        {
            var response = await _service.UpdateAsync(User.GetSchoolId(), id, request);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/FeeStructure/5/toggle-status
        [HttpPost("{id:long}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(long id)
        {
            var response = await _service.ToggleStatusAsync(User.GetSchoolId(), id);
            return StatusCode(response.StatusCode, response);
        }
    }
}
