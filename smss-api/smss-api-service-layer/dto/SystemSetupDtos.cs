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
        [Required, StringLength(100, MinimumLength = 3)]
        public string Username { get; set; } = null!;

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
        public string OrgUserId { get; set; } = null!;     // new: e.g. SUAD001
        public string Username { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? MobileNumber { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
