using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using smss_api_service_layer.service;

namespace smss_api.Controllers
{
    // Meta's delivery-status webhook. ANONYMOUS by necessity (Meta calls it) and therefore guarded by the app-secret signature on every POST, and DISABLED (404) unless WhatsApp:Enabled and WhatsApp:AppSecret are configured.
    // It answers Meta's raw contract (the challenge text / an empty 200), not the usual API envelope. Thin: everything is in WhatsAppWebhookService.
    [ApiController]
    [Route("api/webhooks/whatsapp")]
    [AllowAnonymous]
    public class WhatsAppWebhookController : ControllerBase
    {
        private const int MaxBodyBytes = 256 * 1024;
        private readonly IWhatsAppWebhookService _service;
        public WhatsAppWebhookController(IWhatsAppWebhookService service) => _service = service;

        // GET api/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=...&hub.challenge=...
        [HttpGet]
        public IActionResult Verify([FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.verify_token")] string? verifyToken, [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            var r = _service.Verify(mode, verifyToken, challenge);
            return r.StatusCode == 200 ? Content(r.Challenge!, "text/plain") : StatusCode(r.StatusCode);
        }

        // POST api/webhooks/whatsapp
        [HttpPost]
        public async Task<IActionResult> Receive(CancellationToken ct)
        {
            if (Request.ContentLength is > MaxBodyBytes) return StatusCode(413);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await Request.Body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes) return StatusCode(413);
                buffer.Write(chunk, 0, read);
            }
            var code = await _service.HandleAsync(buffer.ToArray(), Request.Headers["X-Hub-Signature-256"].FirstOrDefault(), ct);
            return StatusCode(code);
        }
    }
}
