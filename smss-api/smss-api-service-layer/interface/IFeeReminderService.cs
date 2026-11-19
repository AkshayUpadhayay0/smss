using smss_api_service_layer.dto;

namespace smss_api_service_layer.@interface
{
    /// <summary>What one pass of the job did, for the log line and the tests. No student or token detail.</summary>
    public class FeeReminderTickResult
    {
        public int SchoolsChecked { get; set; }
        public int SchoolsRun { get; set; }
        public int SchoolsRetried { get; set; }
        public int SchoolsSkippedLocked { get; set; }
        public int SchoolsFailed { get; set; }
        public int Sent { get; set; }
        public int NoDevice { get; set; }
        public int Failed { get; set; }
        /// <summary>Rows validated by Firebase but not delivered (Firebase:DryRun is on).</summary>
        public int DryRun { get; set; }
        /// <summary>True when there was work but Firebase was not usable, so nothing was sent.</summary>
        public bool NotReady { get; set; }
    }

    public interface IFeeReminderService
    {
        /// <summary>
        /// One tick of the background job (every 5 minutes). For each school with reminders on: after its send time (India) and not yet run today -> the full daily run;
        /// already run today -> only a retry of the transient failures. Before the send time -> nothing. One school failing never stops the others.
        /// </summary>
        Task<FeeReminderTickResult> RunTickAsync(CancellationToken ct);

        /// <summary>School Admin dry run: how many students would be reminded today and how many of them have a device. Sends and writes nothing.</summary>
        Task<ApiResponse<FeeReminderPreviewDto>> PreviewAsync(string? schoolId, CancellationToken ct);
    }
}
