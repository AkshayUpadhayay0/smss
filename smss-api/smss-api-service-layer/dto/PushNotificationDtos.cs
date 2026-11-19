using System.ComponentModel.DataAnnotations;

namespace smss_api_service_layer.dto
{
    /// <summary>POST api/Device/register. The school and the user come from the token, never from here.</summary>
    public class RegisterDeviceRequest
    {
        [Required][StringLength(1024)] public string FcmToken { get; set; } = null!;
        /// <summary>ANDROID or IOS (any case).</summary>
        [Required][StringLength(10)] public string Platform { get; set; } = null!;
        [StringLength(100)] public string? DeviceLabel { get; set; }
        [StringLength(30)] public string? AppVersion { get; set; }
    }

    public class UnregisterDeviceRequest
    {
        [Required][StringLength(1024)] public string FcmToken { get; set; } = null!;
    }

    /// <summary>Answer to register / unregister. Carries nothing about the token.</summary>
    public class DeviceResultDto
    {
        public bool Registered { get; set; }
    }

    public class NotificationSettingsDto
    {
        public bool FeeReminderEnabled { get; set; }
        /// <summary>HH:mm, India time.</summary>
        public string FeeReminderSendTime { get; set; } = "09:00";
        public int FeeReminderGraceDays { get; set; }
        public DateOnly? LastFeeReminderRunDate { get; set; }
    }

    public class NotificationSettingsRequest
    {
        public bool FeeReminderEnabled { get; set; }
        /// <summary>HH:mm, India time.</summary>
        [Required] public string? FeeReminderSendTime { get; set; }
        public int FeeReminderGraceDays { get; set; }
    }

    /// <summary>The dry run: who would be reminded today. Sends nothing, writes nothing.</summary>
    public class FeeReminderPreviewDto
    {
        public DateOnly Date { get; set; }
        public int GraceDays { get; set; }
        /// <summary>Students with an overdue installment and an Active Student login.</summary>
        public int StudentsToRemind { get; set; }
        public int WithDevice { get; set; }
        public int WithoutDevice { get; set; }
    }

    public class PushLogItemDto
    {
        public long NotificationId { get; set; }
        public string? StudentId { get; set; }
        public string NotificationType { get; set; } = null!;
        public DateOnly NotifyDate { get; set; }
        public string Title { get; set; } = null!;
        public string Status { get; set; } = null!;
        public int AttemptCount { get; set; }
        public int DevicesTargeted { get; set; }
        public int DevicesSucceeded { get; set; }
        public string? ErrorSummary { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }
}
