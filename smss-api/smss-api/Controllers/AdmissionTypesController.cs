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
    public class AdmissionTypesController : ControllerBase
    {
        private readonly IAdmissionTypeService _service;
        public AdmissionTypesController(IAdmissionTypeService service) => _service = service;

        // GET api/AdmissionTypes
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var response = await _service.GetAllAsync(User.GetSchoolId());
            return StatusCode(response.StatusCode, response);
        }

        // GET api/AdmissionTypes/5
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var response = await _service.GetByIdAsync(User.GetSchoolId(), id);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/AdmissionTypes
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateAdmissionTypeRequest request)
        {
            var response = await _service.CreateAsync(User.GetSchoolId(), request);
            return StatusCode(response.StatusCode, response);
        }

        // PUT api/AdmissionTypes/5
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] UpdateAdmissionTypeRequest request)
        {
            var response = await _service.UpdateAsync(User.GetSchoolId(), id, request);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/AdmissionTypes/5/toggle-status
        [HttpPost("{id:long}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(long id)
        {
            var response = await _service.ToggleStatusAsync(User.GetSchoolId(), id);
            return StatusCode(response.StatusCode, response);
        }
    }
}
