using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Business.SmbClient.Session
{
    /// <inheritdoc cref="ISmbClientSessionPool"/>
    internal class SmbClientSessionPool : ISmbClientSessionPool
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<SmbClientSessionPool> _logger;
        private readonly IInteractiveProcessFactory _processFactory;
        private readonly bool _useKerberos;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _domain;
        private readonly bool _useWsl;
        private readonly int _poolSizePerShare;
        private readonly TimeSpan _idleTimeout;
        private readonly int _sessionInitMaxAttempts;
        private readonly TimeSpan _sessionInitRetryDelay;
        private readonly TimeSpan? _sessionInitTimeout;

        private readonly ConcurrentDictionary<string, ShareBucket> _buckets = new();
        private readonly Timer _evictionTimer;
        private bool _disposed;

        public SmbClientSessionPool(ILoggerFactory loggerFactory, IInteractiveProcessFactory processFactory,
            bool useKerberos, string? username = null, string? password = null, string? domain = null,
            bool useWsl = false, int poolSizePerShare = 3, TimeSpan? idleTimeout = null,
            int sessionInitMaxAttempts = 3, TimeSpan? sessionInitRetryDelay = null, TimeSpan? sessionInitTimeout = null)
        {
            if (poolSizePerShare < 1)
                throw new ArgumentOutOfRangeException(nameof(poolSizePerShare), "Pool size must be at least 1.");
            if (sessionInitMaxAttempts < 1)
                throw new ArgumentOutOfRangeException(nameof(sessionInitMaxAttempts), "Must be at least 1.");

            _sessionInitMaxAttempts = sessionInitMaxAttempts;
            _sessionInitRetryDelay = sessionInitRetryDelay ?? TimeSpan.FromSeconds(1);
            _sessionInitTimeout = sessionInitTimeout;

            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<SmbClientSessionPool>();
            _processFactory = processFactory;
            _useKerberos = useKerberos;
            _username = username;
            _password = password;
            _domain = domain;
            _useWsl = useWsl;
            _poolSizePerShare = poolSizePerShare;
            _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(15);

            var checkInterval = TimeSpan.FromSeconds(Math.Max(30, _idleTimeout.TotalSeconds / 2));
            _evictionTimer = new Timer(_ => EvictIdleBuckets(), null, checkInterval, checkInterval);
        }

        public async Task<string> ExecuteAsync(string server, string share, string command, string contextPath,
            CancellationToken cancellationToken = default)
        {
            var key = $"{server}/{share}".ToLowerInvariant();
            var bucket = _buckets.GetOrAdd(key, _ => new ShareBucket(_poolSizePerShare, server, share));
            var slotIndex = SelectSlot(bucket);

            var session = await GetOrCreateSessionAsync(bucket, slotIndex, cancellationToken);

            try
            {
                return await session.ExecuteAsync(command, contextPath, cancellationToken);
            }
            catch (SmbSessionBrokenException ex)
            {
                _logger.LogWarning(ex,
                    "smbclient session for {ContextPath} was broken; recreating and retrying once.", contextPath);

                await RecreateSlotAsync(bucket, slotIndex, cancellationToken);
                var retrySession = await GetOrCreateSessionAsync(bucket, slotIndex, cancellationToken);
                return await retrySession.ExecuteAsync(command, contextPath, cancellationToken);
            }
        }

        private int SelectSlot(ShareBucket bucket)
        {
            var start = (int)((uint)Interlocked.Increment(ref bucket.RoundRobinCounter) % (uint)_poolSizePerShare);

            // Prefer an already-authenticated session that is free, so sequential calls reuse one
            // connection instead of authenticating every slot. New sessions are only opened when
            // all live sessions are busy (i.e. real concurrency).
            for (var i = 0; i < _poolSizePerShare; i++)
            {
                var index = (start + i) % _poolSizePerShare;
                var session = bucket.Slots[index];
                if (session is { IsAlive: true, IsBusy: false })
                    return index;
            }

            return start;
        }

        private async Task<ISmbClientSession> GetOrCreateSessionAsync(ShareBucket bucket, int slotIndex,
            CancellationToken cancellationToken)
        {
            var existing = bucket.Slots[slotIndex];
            if (existing != null && existing.IsAlive)
                return existing;

            await bucket.SlotLocks[slotIndex].WaitAsync(cancellationToken);
            try
            {
                existing = bucket.Slots[slotIndex];
                if (existing != null && existing.IsAlive)
                    return existing;

                // Clear the slot before attempting to reconnect so a failed attempt never leaves a
                // disposed session behind for later callers.
                existing?.Dispose();
                bucket.Slots[slotIndex] = null;

                var newSession = await CreateInitializedSessionAsync(bucket, cancellationToken);
                bucket.Slots[slotIndex] = newSession;
                return newSession;
            }
            finally
            {
                bucket.SlotLocks[slotIndex].Release();
            }
        }

        private async Task<ISmbClientSession> CreateInitializedSessionAsync(ShareBucket bucket,
            CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                var session = new SmbClientSession(_loggerFactory.CreateLogger<SmbClientSession>(),
                    _processFactory, bucket.Server, bucket.Share, _useKerberos, _username, _password, _domain,
                    _useWsl, _sessionInitTimeout);
                try
                {
                    await session.InitializeAsync(cancellationToken);
                    return session;
                }
                catch (Exception ex)
                {
                    // Always release the half-started process and its temp credentials file.
                    session.Dispose();

                    // Only a dropped/timed-out connection is transient. Auth and not-found errors are
                    // deterministic and must not be retried (retrying bad credentials risks lockout).
                    if (ex is not SmbSessionBrokenException || attempt >= _sessionInitMaxAttempts)
                        throw;

                    var delay = ComputeRetryDelay(attempt);
                    _logger.LogWarning(ex,
                        "Failed to establish smbclient session for //{Server}/{Share} (attempt {Attempt}/{MaxAttempts}); retrying in {DelayMs}ms.",
                        bucket.Server, bucket.Share, attempt, _sessionInitMaxAttempts, (int)delay.TotalMilliseconds);

                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, cancellationToken);
                }
            }
        }

        private TimeSpan ComputeRetryDelay(int attempt)
        {
            if (_sessionInitRetryDelay <= TimeSpan.Zero)
                return TimeSpan.Zero;

            // Exponential backoff with jitter so pods/slots that failed together don't reconnect in lockstep.
            var baseMs = _sessionInitRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            var jitterMs = NextJitter() * _sessionInitRetryDelay.TotalMilliseconds;
            return TimeSpan.FromMilliseconds(baseMs + jitterMs);
        }

#if NET6_0_OR_GREATER
        private static double NextJitter() => Random.Shared.NextDouble();
#else
        private static readonly ThreadLocal<Random> JitterRandom =
            new(() => new Random(Interlocked.Increment(ref _jitterSeed)));
        private static int _jitterSeed = Environment.TickCount;
        private static double NextJitter() => JitterRandom.Value!.NextDouble();
#endif

        private async Task RecreateSlotAsync(ShareBucket bucket, int slotIndex, CancellationToken cancellationToken)
        {
            await bucket.SlotLocks[slotIndex].WaitAsync(cancellationToken);
            try
            {
                bucket.Slots[slotIndex]?.Dispose();
                bucket.Slots[slotIndex] = null;
            }
            finally
            {
                bucket.SlotLocks[slotIndex].Release();
            }
        }

        private void EvictIdleBuckets()
        {
            if (_disposed)
                return;

            var cutoff = DateTime.UtcNow - _idleTimeout;
            foreach (var kvp in _buckets)
            {
                if (kvp.Value.LastUsedUtc >= cutoff)
                    continue;

                if (_buckets.TryRemove(kvp.Key, out var bucket))
                {
                    _logger.LogDebug("Evicting idle smbclient session pool for {Server}/{Share}", bucket.Server,
                        bucket.Share);
                    foreach (var slot in bucket.Slots)
                    {
                        slot?.Dispose();
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _evictionTimer.Dispose();

            foreach (var bucket in _buckets.Values)
            {
                foreach (var slot in bucket.Slots)
                {
                    slot?.Dispose();
                }
            }

            _buckets.Clear();
        }

        private class ShareBucket
        {
            public readonly ISmbClientSession?[] Slots;
            public readonly SemaphoreSlim[] SlotLocks;
            public readonly string Server;
            public readonly string Share;
            public int RoundRobinCounter;

            public ShareBucket(int size, string server, string share)
            {
                Slots = new ISmbClientSession?[size];
                SlotLocks = new SemaphoreSlim[size];
                for (var i = 0; i < size; i++)
                {
                    SlotLocks[i] = new SemaphoreSlim(1, 1);
                }

                Server = server;
                Share = share;
            }

            public DateTime LastUsedUtc =>
                Slots.Where(s => s != null).Select(s => s!.LastUsedUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        }
    }
}
