using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Bulk report card PDFs. School Admin, Principal (published cards only: a publication is always published) and the section's class teacher for their own section.
    // The school always comes from the token; another school's publication is a 404.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportCardController : ControllerBase
    {
        private readonly IReportResultService _service;
        public ReportCardController(IReportResultService service) => _service = service;

        // POST api/ReportCard/{publicationId}/bulk   Body (optional): { fromRoll, toRoll }
        // 200: the PDF (every card of the version in roll order, with cover and rules pages where switched on). 409 over the cap: data.parts lists the roll ranges to download instead.
        [HttpPost("{publicationId:long}/bulk")]
        public async Task<IActionResult> Bulk(long publicationId, [FromBody] BulkRequest? request, CancellationToken ct)
        {
            if (!User.CanUseReportInputs()) return this.Forbidden("You can't download report cards.");
            if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
            var r = await _service.BulkPdfAsync(User.GetSchoolId(), User.ToReportCaller(userId), publicationId, request, ct);
            if (r.Content != null) { Response.Headers["X-Card-Count"] = r.Cards.ToString(); return File(r.Content, "application/pdf", r.FileName); }
            return StatusCode(r.StatusCode, new ApiResponse<BulkTooLargeDto> { Status = false, StatusCode = r.StatusCode, Message = r.Message, Data = r.TooLarge });
        }
    }
}
