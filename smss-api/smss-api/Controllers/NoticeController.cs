using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;
using System.Security.Claims;

namespace smss_api.Controllers
{
    // Notices (in-app only, published immediately). The school and the acting user always come from the token. Who may do what:
    //   create / update / delete / admin list:  CanManageNotices (School Admin or Principal); update and delete: a School Admin, or the Principal who created it
    //   GET {id}:   anyone the notice is addressed to (its audience tier), or a School Admin / Principal;  GET my, GET unread-count:  every signed-in user of a school
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NoticeController : ControllerBase
    {
        private readonly INoticeService _service;
        public NoticeController(INoticeService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> response) => StatusCode(response.StatusCode, response);
        private bool TryCaller(out NoticeCaller caller)
        {
            var ok = long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
            caller = new NoticeCaller(userId, User.CanManageNotices(), User.IsSchoolAdmin());
            return ok;
        }
        private IActionResult? RefuseUnlessManager() => User.CanManageNotices() ? null : this.Forbidden("Only a School Admin or a Principal can manage notices.");

        // POST api/Notice   Body: { title, body, audience: ALL | STAFF | STUDENTS_GUARDIANS }   (published immediately)
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveNoticeRequest request, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.CreateAsync(User.GetSchoolId(), caller, request, ct));
        }

        // PUT api/Notice/{id}
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] SaveNoticeRequest request, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.UpdateAsync(User.GetSchoolId(), caller, id, request, ct));
        }

        // DELETE api/Notice/{id}   (soft: hidden, status Inactive)
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.DeleteAsync(User.GetSchoolId(), caller, id, ct));
        }

        // GET api/Notice/{id}   (opening it marks it read for the caller)
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct)
        {
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.GetAsync(User.GetSchoolId(), caller, id, ct));
        }

        // GET api/Notice/my?unreadOnly=&from=&to=   (the notices addressed to the caller, most recently published first)
        [HttpGet("my")]
        public async Task<IActionResult> My([FromQuery] bool unreadOnly, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.GetMyAsync(User.GetSchoolId(), caller, unreadOnly, from, to, ct));
        }

        // GET api/Notice/unread-count   (for the bell badge)
        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount(CancellationToken ct)
        {
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.GetUnreadCountAsync(User.GetSchoolId(), caller, ct));
        }

        // GET api/Notice?audience=&from=&to=   (School Admin / Principal: every notice of the school)
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? audience, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            if (RefuseUnlessManager() is { } refused) return refused;
            if (!TryCaller(out var caller)) return Unauthorized();
            return Reply(await _service.ListAsync(User.GetSchoolId(), caller, audience, from, to, ct));
        }
    }
}
