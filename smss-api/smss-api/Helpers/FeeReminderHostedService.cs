using smss_api_service_layer.@interface;

namespace smss_api.Helpers
{
    /// <summary>
    /// The daily fee-reminder job: wakes every 5 minutes and asks <see cref="IFeeReminderService"/> what is due (after a school's send time, not yet run today).
    /// An in-process BackgroundService, not a scheduler framework (project decision: Docs/push-notification-design.md section 1). Nothing in here can stop the API:
    /// every tick is wrapped, so missing credentials, a database that is not migrated yet or a Firebase outage only produce a log line.
    /// </summary>
    public class FeeReminderHostedService : BackgroundService
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<FeeReminderHostedService> _logger;

        public FeeReminderHostedService(IServiceScopeFactory scopes, ILogger<FeeReminderHostedService> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let the API finish starting before the first tick
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }

            using var timer = new PeriodicTimer(Interval);
            do { await TickAsync(stoppingToken); }
            while (await WaitAsync(timer, stoppingToken));
        }

        private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
        {
            try { return await timer.WaitForNextTickAsync(ct); }
            catch (OperationCanceledException) { return false; }
        }

        /// <summary>One tick. Public so a test can drive it without waiting five minutes.</summary>
        public async Task TickAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<IFeeReminderService>().RunTickAsync(ct);
                if (result.SchoolsRun + result.SchoolsRetried + result.SchoolsFailed + result.SchoolsSkippedLocked > 0)
                    _logger.LogDebug("Fee reminder tick: {Run} run, {Retried} retried, {Locked} locked elsewhere, {FailedSchools} school(s) failed; {Sent} sent, {NoDevice} without a device, {Failed} failed",
                        result.SchoolsRun, result.SchoolsRetried, result.SchoolsSkippedLocked, result.SchoolsFailed, result.Sent, result.NoDevice, result.Failed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fee reminder tick failed; it will be tried again at the next tick");
            }
        }
    }
}
