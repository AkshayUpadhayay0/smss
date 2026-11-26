using smss_api_service_layer.service;

namespace smss_api.Helpers
{
    /// <summary>Runs what BackgroundTaskQueue holds, one item at a time, each in its own DI scope. A failure is logged by type only and never stops the loop.</summary>
    public class QueuedWorkRunner : BackgroundService
    {
        private readonly BackgroundTaskQueue _queue;
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<QueuedWorkRunner> _logger;

        public QueuedWorkRunner(BackgroundTaskQueue queue, IServiceScopeFactory scopes, ILogger<QueuedWorkRunner> logger)
        {
            _queue = queue; _scopes = scopes; _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var work in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    await work(scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogWarning("A queued background task failed ({Error})", ex.GetType().Name); }
            }
        }
    }
}
