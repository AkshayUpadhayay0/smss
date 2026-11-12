using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using smss_api_service_layer.@interface;

namespace smss_api_service_layer.helper
{
    /// <summary>appsettings LoginThrottle: nothing is hard-coded in the throttle itself.</summary>
    public sealed class LoginThrottleOptions
    {
        /// <summary>Failed attempts allowed per login id inside the window; the next one is answered 429.</summary>
        public int MaxFailedPerLoginId { get; set; } = 5;
        /// <summary>Failed attempts allowed per client address (an IPv6 /64) inside the window. Higher than the per-id limit: a school's pupils may share one address.</summary>
        public int MaxFailedPerIp { get; set; } = 30;
        public int WindowMinutes { get; set; } = 15;
        /// <summary>Upper bound on remembered login ids/addresses, so random ids cannot grow memory without limit (beyond it only the per-address limit applies to new ids).</summary>
        public int MaxTrackedKeys { get; set; } = 50_000;

        public static LoginThrottleOptions From(IConfiguration config)
        {
            var o = new LoginThrottleOptions();
            static int Read(IConfiguration c, string name, int fallback) => int.TryParse(c["LoginThrottle:" + name], out var v) ? v : fallback;
            o.MaxFailedPerLoginId = Read(config, nameof(MaxFailedPerLoginId), o.MaxFailedPerLoginId);
            o.MaxFailedPerIp = Read(config, nameof(MaxFailedPerIp), o.MaxFailedPerIp);
            o.WindowMinutes = Read(config, nameof(WindowMinutes), o.WindowMinutes);
            o.MaxTrackedKeys = Read(config, nameof(MaxTrackedKeys), o.MaxTrackedKeys);
            o.MaxFailedPerLoginId = Math.Max(1, o.MaxFailedPerLoginId);
            o.MaxFailedPerIp = Math.Max(1, o.MaxFailedPerIp);
            o.WindowMinutes = Math.Max(1, o.WindowMinutes);
            o.MaxTrackedKeys = Math.Max(100, o.MaxTrackedKeys);
            return o;
        }
    }

    /// <summary>
    /// In-memory sliding-window counter (single API instance; no Redis). A failure is remembered for the window and then forgotten, so nothing is ever locked for good, and a restart
    /// simply starts the counts again. Thread-safe.
    /// </summary>
    public sealed class LoginThrottle : ILoginThrottle
    {
        private sealed class Window { public readonly Queue<DateTimeOffset> Failures = new(); }

        private readonly LoginThrottleOptions _o;
        private readonly TimeProvider _clock;
        private readonly TimeSpan _window;
        private readonly ConcurrentDictionary<string, Window> _byLogin = new();
        private readonly ConcurrentDictionary<string, Window> _byClient = new();
        private long _lastSweepTicks;

        public LoginThrottle(LoginThrottleOptions options, TimeProvider? clock = null)
        {
            _o = options;
            _clock = clock ?? TimeProvider.System;
            _window = TimeSpan.FromMinutes(_o.WindowMinutes);
            _lastSweepTicks = _clock.GetUtcNow().UtcTicks;
        }

        private static string Key(string loginId)
        {
            var k = (loginId ?? "").Trim().ToLowerInvariant();
            return k.Length > 100 ? k[..100] : k;
        }

        public TimeSpan? RetryAfter(string loginId, string clientKey)
        {
            var now = _clock.GetUtcNow();
            var a = Wait(_byLogin, Key(loginId), _o.MaxFailedPerLoginId, now);
            var b = Wait(_byClient, clientKey ?? "", _o.MaxFailedPerIp, now);
            if (a == null) return b;
            if (b == null) return a;
            return a > b ? a : b;
        }

        public void RecordFailure(string loginId, string clientKey)
        {
            var now = _clock.GetUtcNow();
            Sweep(now);
            Add(_byLogin, Key(loginId), now);
            Add(_byClient, clientKey ?? "", now);
        }

        public void RecordSuccess(string loginId) => ClearLogin(loginId);

        public void ClearLogin(string loginId) => _byLogin.TryRemove(Key(loginId), out _);

        private TimeSpan? Wait(ConcurrentDictionary<string, Window> map, string key, int max, DateTimeOffset now)
        {
            if (!map.TryGetValue(key, out var w)) return null;
            lock (w)
            {
                Prune(w, now);
                if (w.Failures.Count < max) return null;
                // blocked until enough of the oldest failures have left the window for the count to drop below the limit
                var release = w.Failures.ElementAt(w.Failures.Count - max) + _window;
                var wait = release - now;
                return wait > TimeSpan.Zero ? wait : null;
            }
        }

        private void Add(ConcurrentDictionary<string, Window> map, string key, DateTimeOffset now)
        {
            if (!map.TryGetValue(key, out var w))
            {
                if (map.Count >= _o.MaxTrackedKeys) return;   // memory guard: new keys are not tracked past the cap
                w = map.GetOrAdd(key, _ => new Window());
            }
            lock (w) { Prune(w, now); w.Failures.Enqueue(now); }
        }

        private void Prune(Window w, DateTimeOffset now)
        {
            while (w.Failures.Count > 0 && now - w.Failures.Peek() >= _window) w.Failures.Dequeue();
        }

        // At most once a minute, drop keys whose failures have all expired
        private void Sweep(DateTimeOffset now)
        {
            var last = Interlocked.Read(ref _lastSweepTicks);
            if (now.UtcTicks - last < TimeSpan.TicksPerMinute) return;
            if (Interlocked.CompareExchange(ref _lastSweepTicks, now.UtcTicks, last) != last) return;
            foreach (var map in new[] { _byLogin, _byClient })
                foreach (var kv in map)
                    lock (kv.Value)
                    {
                        Prune(kv.Value, now);
                        if (kv.Value.Failures.Count == 0) map.TryRemove(kv.Key, out _);
                    }
        }
    }
}
