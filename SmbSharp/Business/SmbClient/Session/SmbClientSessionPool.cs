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
        private readonly TimeSpan _commandTimeout;
        private readonly string? _wslDistribution;

        private readonly ConcurrentDictionary<string, ShareBucket> _buckets = new();
        private readonly object _lifecycleLock = new();
        private readonly Timer _evictionTimer;
        private volatile bool _disposed;

        public SmbClientSessionPool(ILoggerFactory loggerFactory, IInteractiveProcessFactory processFactory,
            bool useKerberos, string? username = null, string? password = null, string? domain = null,
            bool useWsl = false, int poolSizePerShare = 3, TimeSpan? idleTimeout = null,
            int sessionInitMaxAttempts = 3, TimeSpan? sessionInitRetryDelay = null, TimeSpan? sessionInitTimeout = null,
            TimeSpan? commandTimeout = null, string? wslDistribution = null)
        {
            if (loggerFactory == null)
                throw new ArgumentNullException(nameof(loggerFactory));
            if (processFactory == null)
                throw new ArgumentNullException(nameof(processFactory));
            if (poolSizePerShare < 1)
                throw new ArgumentOutOfRangeException(nameof(poolSizePerShare), "Pool size must be at least 1.");
            if (sessionInitMaxAttempts < 1)
                throw new ArgumentOutOfRangeException(nameof(sessionInitMaxAttempts), "Must be at least 1.");
            if (idleTimeout is { } configuredIdleTimeout && configuredIdleTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(idleTimeout), "Idle timeout must be positive.");

            _sessionInitMaxAttempts = sessionInitMaxAttempts;
            _sessionInitRetryDelay = sessionInitRetryDelay ?? TimeSpan.FromSeconds(1);
            _sessionInitTimeout = sessionInitTimeout;
            _commandTimeout = commandTimeout ?? TimeSpan.FromMinutes(2);
            if (_commandTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(commandTimeout), "Command timeout must be positive.");
            _wslDistribution = wslDistribution;

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
            ShareBucket bucket;
            lock (_lifecycleLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(SmbClientSessionPool));
                var key = $"{server}/{share}".ToLowerInvariant();
                bucket = _buckets.GetOrAdd(key, _ => new ShareBucket(_poolSizePerShare, server, share));
                bucket.ActiveOperations++;
            }
            try
            {
                var slotIndex = SelectSlot(bucket);
                var session = await GetOrCreateSessionAsync(bucket, slotIndex, cancellationToken);
                return await session.ExecuteAsync(command, contextPath, cancellationToken);
            }
            finally
            {
                lock (_lifecycleLock)
                    bucket.ActiveOperations--;
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
                lock (_lifecycleLock)
                {
                    if (_disposed)
                    {
                        newSession.Dispose();
                        throw new ObjectDisposedException(nameof(SmbClientSessionPool));
                    }
                    bucket.Slots[slotIndex] = newSession;
                }
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
                    _useWsl, _sessionInitTimeout, _commandTimeout, _wslDistribution);
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
                        "Failed to establish smbclient session for //{server}/{share} (attempt {attempt}/{maxAttempts}); retrying in {delayMs}ms.",
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

        private void EvictIdleBuckets()
        {
            var cutoff = DateTime.UtcNow - _idleTimeout;
            List<ShareBucket> expired = new();
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;

                foreach (var kvp in _buckets)
                {
                    if (kvp.Value.ActiveOperations > 0 || kvp.Value.LastUsedUtc >= cutoff ||
                        kvp.Value.Slots.Any(session => session?.IsBusy == true))
                        continue;
                    if (_buckets.TryRemove(kvp.Key, out var bucket))
                        expired.Add(bucket);
                }
            }

            foreach (var bucket in expired)
            {
                if (bucket != null)
                {
                    _logger.LogDebug("Evicting idle smbclient session pool for {server}/{share}", bucket.Server,
                        bucket.Share);
                    DisposeBucket(bucket);
                }
            }
        }

        public void Dispose()
        {
            ShareBucket[] buckets;
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                buckets = _buckets.Values.ToArray();
                _buckets.Clear();
            }
            _evictionTimer.Dispose();

            foreach (var bucket in buckets)
                DisposeBucket(bucket);
        }

        private static void DisposeBucket(ShareBucket bucket)
        {
            foreach (var slot in bucket.Slots)
                slot?.Dispose();
        }

        private class ShareBucket
        {
            public readonly ISmbClientSession?[] Slots;
            public readonly SemaphoreSlim[] SlotLocks;
            public readonly string Server;
            public readonly string Share;
            public int RoundRobinCounter;
            public int ActiveOperations;

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
