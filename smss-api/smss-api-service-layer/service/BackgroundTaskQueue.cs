using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.service
{
    /// <summary>The in-process queue behind IBackgroundTaskQueue: a bounded channel read by QueuedWorkRunner. When it is full the item is dropped and logged (the caller's answer never waits or fails).</summary>
    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
        private readonly ILogger<BackgroundTaskQueue> _logger;
        public BackgroundTaskQueue(ILogger<BackgroundTaskQueue> logger) => _logger = logger;

        public void Enqueue(Func<IServiceProvider, CancellationToken, Task> work)
        {
            if (!_channel.Writer.TryWrite(work)) _logger.LogWarning("The background queue is full: a queued message was dropped");
        }

        public ChannelReader<Func<IServiceProvider, CancellationToken, Task>> Reader => _channel.Reader;
    }
}
