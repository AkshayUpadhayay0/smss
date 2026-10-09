using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.helper;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DocumentTypeController : ControllerBase
    {
        private readonly IDocumentTypeService _service;

        public DocumentTypeController(IDocumentTypeService service)
        {
            _service = service;
        }

        // The routes carry {schoolId}, but it is never trusted: the caller can only reach their OWN school (the JWT
        // "school_id" claim). Anything else is refused, so one school can't read or change another's document types.
        // The service is always called with the token's value. TODO: drop {schoolId} from the routes once the
        // frontend stops sending it (same pending item as the other Core School Masters).
        private IActionResult? RejectForeignSchool(string routeSchoolId, out string schoolId)
        {
            schoolId = User.GetSchoolId() ?? string.Empty;
            if (schoolId.Length == 0)
                return Respond(400, TenantMessages.NoSchool);
            if (!string.Equals(routeSchoolId, schoolId, StringComparison.OrdinalIgnoreCase))
                return Respond(403, "You can only manage your own school's document types.");
            return null;
        }

        private IActionResult Respond(int code, string message) =>
            StatusCode(code, new ApiResponse<object> { Status = false, StatusCode = code, Message = message, Data = null });

        [HttpGet("{schoolId}")]
        public async Task<IActionResult> GetAll(string schoolId, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            var response = await _service.GetAllAsync(own, ct);
            return StatusCode(response.StatusCode, response);
        }

        [HttpPost("{schoolId}")]
        public async Task<IActionResult> Create(string schoolId, [FromBody] CreateDocumentTypeRequestDto request, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            var response = await _service.CreateAsync(own, request, ct);
            return StatusCode(response.StatusCode, response);
        }

        [HttpPost("{schoolId}/update")]
        public async Task<IActionResult> Update(string schoolId, [FromBody] UpdateDocumentTypeRequestDto request, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            var response = await _service.UpdateAsync(own, request, ct);
            return StatusCode(response.StatusCode, response);
        }

        [HttpPost("{schoolId}/{documentTypeId:long}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(string schoolId, long documentTypeId, CancellationToken ct)
        {
            if (RejectForeignSchool(schoolId, out var own) is { } rejected) return rejected;
            var response = await _service.ToggleStatusAsync(own, documentTypeId, ct);
            return StatusCode(response.StatusCode, response);
        }
    }
}
