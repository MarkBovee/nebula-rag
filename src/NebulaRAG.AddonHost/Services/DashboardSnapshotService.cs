using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;

namespace NebulaRAG.AddonHost.Services;

/// <summary>
/// Provides cached dashboard snapshots to reduce repetitive database load from polling clients.
/// </summary>
public sealed class DashboardSnapshotService
{
    private readonly RagManagementService _managementService;
    private readonly TimedCache<HealthCheckResult> _healthCache;
    private readonly TimedCache<IndexStats> _statsCache;
    private readonly TimedCache<IndexStats> _statsWithSizeCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardSnapshotService"/> class.
    /// </summary>
    /// <param name="managementService">Management service used for health, stats, and sources reads.</param>
    public DashboardSnapshotService(RagManagementService managementService)
    {
        _managementService = managementService ?? throw new ArgumentNullException(nameof(managementService));
        _healthCache = new TimedCache<HealthCheckResult>(TimeSpan.FromSeconds(30));
        _statsCache = new TimedCache<IndexStats>(TimeSpan.FromSeconds(30));
        _statsWithSizeCache = new TimedCache<IndexStats>(TimeSpan.FromMinutes(2));
    }

    /// <summary>
    /// Gets backend health status with short-lived caching.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current health status.</returns>
    public async Task<HealthCheckResult> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_healthCache.TryGet(out var cachedHealth))
        {
            return cachedHealth;
        }

        var freshHealth = await _managementService.HealthCheckAsync(cancellationToken);
        _healthCache.Set(freshHealth);
        return freshHealth;
    }

    /// <summary>
    /// Gets index statistics with optional index-size computation.
    /// </summary>
    /// <param name="includeIndexSize">When true, includes expensive size calculation and bypasses cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current index statistics.</returns>
    public async Task<IndexStats> GetStatsAsync(bool includeIndexSize, CancellationToken cancellationToken = default)
    {
        if (includeIndexSize)
        {
            if (_statsWithSizeCache.TryGet(out var cachedStatsWithSize))
            {
                return cachedStatsWithSize;
            }

            var freshStatsWithSize = await _managementService.GetStatsAsync(includeIndexSize: true, cancellationToken);
            _statsWithSizeCache.Set(freshStatsWithSize);
            return freshStatsWithSize;
        }

        if (_statsCache.TryGet(out var cachedStats))
        {
            return cachedStats;
        }

        var freshStats = await _managementService.GetStatsAsync(includeIndexSize: false, cancellationToken);
        _statsCache.Set(freshStats);
        return freshStats;
    }

    /// <summary>
    /// Builds one consolidated dashboard payload in a single orchestration call.
    /// </summary>
    /// <param name="limit">Maximum number of sources to include.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dashboard payload with health, stats, and sources.</returns>
    public async Task<DashboardSnapshotResponse> GetDashboardAsync(int limit, CancellationToken cancellationToken = default)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 500);
        var health = await GetHealthAsync(cancellationToken);
        var stats = await GetStatsAsync(includeIndexSize: true, cancellationToken);
        var sources = await _managementService.ListSourcesAsync(normalizedLimit, cancellationToken);

        return new DashboardSnapshotResponse(health, stats, sources, DateTime.UtcNow);
    }
}

/// <summary>
/// Combined payload returned to dashboard clients.
/// </summary>
/// <param name="Health">Health status data.</param>
/// <param name="Stats">Index statistics data.</param>
/// <param name="Sources">Recent indexed sources.</param>
/// <param name="GeneratedAtUtc">UTC timestamp when this payload was generated.</param>
public sealed record DashboardSnapshotResponse(HealthCheckResult Health, IndexStats Stats, IReadOnlyList<SourceInfo> Sources, DateTime GeneratedAtUtc);

/// <summary>
/// Thread-safe in-memory cache with fixed TTL.
/// </summary>
/// <typeparam name="T">Cached value type.</typeparam>
internal sealed class TimedCache<T>
{
    private readonly object _syncRoot = new();
    private readonly TimeSpan _timeToLive;
    private DateTime _expiresAtUtc;
    private T? _value;

    /// <summary>
    /// Initializes a new cache instance.
    /// </summary>
    /// <param name="timeToLive">Duration before items expire.</param>
    public TimedCache(TimeSpan timeToLive)
    {
        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "Cache TTL must be greater than zero.");
        }

        _timeToLive = timeToLive;
        _expiresAtUtc = DateTime.MinValue;
    }

    /// <summary>
    /// Attempts to read a valid cached value.
    /// </summary>
    /// <param name="value">Cached value when available.</param>
    /// <returns>True when cache hit; false otherwise.</returns>
    public bool TryGet(out T value)
    {
        lock (_syncRoot)
        {
            if (_value is null || DateTime.UtcNow >= _expiresAtUtc)
            {
                value = default!;
                return false;
            }

            value = _value;
            return true;
        }
    }

    /// <summary>
    /// Stores a value and refreshes expiration.
    /// </summary>
    /// <param name="value">Value to cache.</param>
    public void Set(T value)
    {
        lock (_syncRoot)
        {
            _value = value;
            _expiresAtUtc = DateTime.UtcNow.Add(_timeToLive);
        }
    }
}
