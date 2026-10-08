using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.helper
{
    public class EmailSettings
    {
        public string SmtpHost { get; set; } = null!;
        public int SmtpPort { get; set; }
        public string SenderEmail { get; set; } = null!;
        public string SenderName { get; set; } = null!;
        public string SenderAppPassword { get; set; } = null!;
        public string? LoginUrl { get; set; }
    }
}
