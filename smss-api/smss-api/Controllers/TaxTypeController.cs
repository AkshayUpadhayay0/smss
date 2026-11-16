using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api.Helpers;
using smss_api_service_layer.dto;
using smss_api_service_layer.@interface;

namespace smss_api.Controllers
{
    // Tax types of the caller's own school (from the token; no school id is ever accepted).
    // Reads: School Admin, Accountant and Principal (CanViewFees; the Principal is read-only). Writes: School Admin only (CanConfigureFees).
    // includeInactive is honoured for a School Admin only. No hard delete: a type is deactivated, never removed (payments keep pointing at it).
    // Activate / deactivate are POSTs, not PATCHes: PATCH is blocked in this dev environment.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TaxTypeController : ControllerBase
    {
        private readonly ITaxTypeService _service;
        public TaxTypeController(ITaxTypeService service) => _service = service;

        private IActionResult Reply<T>(ApiResponse<T> r) => StatusCode(r.StatusCode, r);

        // GET api/TaxType?includeInactive=false
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] bool includeInactive, CancellationToken ct)
        {
            if (!User.CanViewFees()) return this.Forbidden("Only a School Admin, Accountant or Principal can view tax types.");
            return Reply(await _service.GetAllAsync(User.GetSchoolId(), includeInactive && User.CanConfigureFees(), ct));
        }

        // POST api/TaxType
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TaxTypeRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can create tax types.");
            return Reply(await _service.CreateAsync(User.GetSchoolId(), request, ct));
        }

        // PUT api/TaxType/5
        [HttpPut("{id:long}")]
        public async Task<IActionResult> Update(long id, [FromBody] TaxTypeRequest request, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can change tax types.");
            return Reply(await _service.UpdateAsync(User.GetSchoolId(), id, request, ct));
        }

        // POST api/TaxType/5/activate
        [HttpPost("{id:long}/activate")]
        public async Task<IActionResult> Activate(long id, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can activate tax types.");
            return Reply(await _service.SetActiveAsync(User.GetSchoolId(), id, true, ct));
        }

        // POST api/TaxType/5/deactivate
        [HttpPost("{id:long}/deactivate")]
        public async Task<IActionResult> Deactivate(long id, CancellationToken ct)
        {
            if (!User.CanConfigureFees()) return this.Forbidden("Only a School Admin can deactivate tax types.");
            return Reply(await _service.SetActiveAsync(User.GetSchoolId(), id, false, ct));
        }
    }
}
