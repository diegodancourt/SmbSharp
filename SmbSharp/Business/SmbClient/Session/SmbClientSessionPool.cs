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

        private readonly ConcurrentDictionary<string, ShareBucket> _buckets = new();
        private readonly Timer _evictionTimer;
        private bool _disposed;

        public SmbClientSessionPool(ILoggerFactory loggerFactory, IInteractiveProcessFactory processFactory,
            bool useKerberos, string? username = null, string? password = null, string? domain = null,
            bool useWsl = false, int poolSizePerShare = 3, TimeSpan? idleTimeout = null)
        {
            if (poolSizePerShare < 1)
                throw new ArgumentOutOfRangeException(nameof(poolSizePerShare), "Pool size must be at least 1.");

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
            var slotIndex = (int)((uint)Interlocked.Increment(ref bucket.RoundRobinCounter) % (uint)_poolSizePerShare);

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

                existing?.Dispose();

                var newSession = new SmbClientSession(_loggerFactory.CreateLogger<SmbClientSession>(),
                    _processFactory, bucket.Server, bucket.Share, _useKerberos, _username, _password, _domain,
                    _useWsl);
                await newSession.InitializeAsync(cancellationToken);
                bucket.Slots[slotIndex] = newSession;
                return newSession;
            }
            finally
            {
                bucket.SlotLocks[slotIndex].Release();
            }
        }

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
