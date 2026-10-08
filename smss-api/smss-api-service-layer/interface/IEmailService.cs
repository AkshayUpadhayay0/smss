using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.@interface
{
    public interface IEmailService
    {
        // Throws on failure — the caller decides whether that should block its own flow
        Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
    }
}
