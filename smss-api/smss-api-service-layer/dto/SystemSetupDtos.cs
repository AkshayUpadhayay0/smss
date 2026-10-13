using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace smss_api_service_layer.dto
{
    internal class SystemSetupDtos
    {
    }
    public class CreateSuperAdminRequest
    {
        /// <summary>The admin's name, stored as display_name. The login id is the generated SUADnnn, not this.</summary>
        [Required, StringLength(100, MinimumLength = 3)]
        public string DisplayName { get; set; } = null!;

        [Required, EmailAddress, StringLength(150)]
        public string Email { get; set; } = null!;

        [RegularExpression(Rx.Mobile, ErrorMessage = "Enter a valid 10-digit mobile number.")]
        public string? MobileNumber { get; set; }

        [Required, StringLength(100, MinimumLength = 8)]
        public string Password { get; set; } = null!;
    }

    public class SuperAdminResponse
    {
        public long UserId { get; set; }
        public string OrgUserId { get; set; } = null!;     // the login id, e.g. SUAD001
        public string DisplayName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? MobileNumber { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
