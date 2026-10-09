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
    public class ClassSubjectsController : ControllerBase
    {
        private readonly IClassSubjectService _service;
        public ClassSubjectsController(IClassSubjectService service) => _service = service;

        // GET api/ClassSubjects?classId=5   (classId is required)
        [HttpGet]
        public async Task<IActionResult> GetByClass([FromQuery] long? classId)
        {
            var response = await _service.GetByClassAsync(User.GetSchoolId(), classId);
            return StatusCode(response.StatusCode, response);
        }

        // GET api/ClassSubjects/5
        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById(long id)
        {
            var response = await _service.GetByIdAsync(User.GetSchoolId(), id);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/ClassSubjects
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateClassSubjectRequest request)
        {
            var response = await _service.CreateAsync(User.GetSchoolId(), request);
            return StatusCode(response.StatusCode, response);
        }

        // DELETE api/ClassSubjects/5   (hard delete: a mapping is a pure join row)
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            var response = await _service.DeleteAsync(User.GetSchoolId(), id);
            return StatusCode(response.StatusCode, response);
        }

        // POST api/ClassSubjects/bulk   { classId, subjectIds: [] }  -> replaces the class's whole subject set
        [HttpPost("bulk")]
        public async Task<IActionResult> BulkSet([FromBody] BulkSetClassSubjectsRequest request)
        {
            var response = await _service.BulkSetAsync(User.GetSchoolId(), request);
            return StatusCode(response.StatusCode, response);
        }
    }
}
