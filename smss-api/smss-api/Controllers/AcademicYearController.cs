using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Everything here is scoped to the caller's own school (from the token); no school id is ever accepted.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AcademicYearController : ControllerBase
    {
        private readonly IAcademicYearService _service;
        public AcademicYearController(IAcademicYearService service) => _service = service;

        // GET api/AcademicYear
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var response = await _service.GetAllAsync(User.GetSchoolId());
            return StatusCode(response.StatusCode, response);
        }

        // GET api/AcademicYear/5
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var response = await _service.GetByIdAsync(User.GetSchoolId(), id);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/AcademicYear
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateAcademicYearRequest request)
        {
            var response = await _service.CreateAsync(User.GetSchoolId(), request);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/AcademicYear/5
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateAcademicYearRequest request)
        {
            var response = await _service.UpdateAsync(User.GetSchoolId(), id, request);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/AcademicYear/5/toggle-status
        [HttpPost("{id:long}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(long id)
        {
            var response = await _service.ToggleStatusAsync(User.GetSchoolId(), id);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/AcademicYear/5/set-current
        [HttpPost("{id:long}/set-current")]
        public async Task<IActionResult> SetCurrent(long id)
        {
            var response = await _service.SetCurrentAsync(User.GetSchoolId(), id);
            return StatusCode(response.StatusCode, response);
        }
    }
}
