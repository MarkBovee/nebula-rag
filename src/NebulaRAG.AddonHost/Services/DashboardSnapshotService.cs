using System.Diagnostics;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;

namespace NebulaRAG.AddonHost.Services;

/// <summary>
/// Provides cached dashboard snapshots to reduce repetitive database load from polling clients.
/// </summary>
public sealed class DashboardSnapshotService : IRuntimeTelemetrySink
{
    private const int MaxQueryLatencySamples = 240;
    private const int MaxIndexingRateSamples = 240;
    private const int MaxPerformanceSamples = 2880;
    private const int MaxActivityEntries = 500;
    private readonly RagManagementService _managementService;
    private readonly TimedCache<HealthCheckResult> _healthCache;
    private readonly TimedCache<IndexStats> _statsCache;
    private readonly TimedCache<IndexStats> _statsWithSizeCache;
    private readonly TimedCache<MemoryDashboardStats> _memoryStatsCache;
    private readonly object _performanceSync = new();
    private readonly Queue<double> _queryLatenciesMs;
    private readonly Queue<double> _indexingRatesDocsPerSec;
    private readonly Queue<PerformanceMetricPoint> _performanceHistory;
    private readonly Queue<DashboardActivityEvent> _activityHistory;
    private DateTime _lastSampleAtUtc;
    private TimeSpan _lastProcessCpuTime;
    private int _lastDocumentCount;

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
        _memoryStatsCache = new TimedCache<MemoryDashboardStats>(TimeSpan.FromSeconds(30));
        _queryLatenciesMs = new Queue<double>();
        _indexingRatesDocsPerSec = new Queue<double>();
        _performanceHistory = new Queue<PerformanceMetricPoint>();
        _activityHistory = new Queue<DashboardActivityEvent>();
        _lastSampleAtUtc = DateTime.MinValue;
        _lastProcessCpuTime = TimeSpan.Zero;
        _lastDocumentCount = 0;
    }

    /// <summary>
    /// Records one semantic query latency sample for rolling dashboard averages.
    /// </summary>
    /// <param name="elapsedMilliseconds">Observed query latency in milliseconds.</param>
    public void RecordQueryLatency(double elapsedMilliseconds)
    {
        if (elapsedMilliseconds <= 0)
        {
            return;
        }

        lock (_performanceSync)
        {
            _queryLatenciesMs.Enqueue(elapsedMilliseconds);
            while (_queryLatenciesMs.Count > MaxQueryLatencySamples)
            {
                _queryLatenciesMs.Dequeue();
            }
        }
    }

    /// <summary>
    /// Records one indexing throughput sample in documents per second.
    /// </summary>
    /// <param name="documentsPerSecond">Observed indexing throughput.</param>
    public void RecordIndexingRate(double documentsPerSecond)
    {
        if (documentsPerSecond <= 0)
        {
            return;
        }

        lock (_performanceSync)
        {
            _indexingRatesDocsPerSec.Enqueue(documentsPerSecond);
            while (_indexingRatesDocsPerSec.Count > MaxIndexingRateSamples)
            {
                _indexingRatesDocsPerSec.Dequeue();
            }
        }
    }

    /// <summary>
    /// Records one dashboard activity entry.
    /// </summary>
    /// <param name="eventType">Activity category.</param>
    /// <param name="description">Short activity description.</param>
    /// <param name="metadata">Optional metadata values.</param>
    public void RecordActivity(string eventType, string description, IReadOnlyDictionary<string, string?>? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(eventType) || string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        lock (_performanceSync)
        {
            _activityHistory.Enqueue(new DashboardActivityEvent(DateTime.UtcNow, eventType, description, metadata));
            while (_activityHistory.Count > MaxActivityEntries)
            {
                _activityHistory.Dequeue();
            }
        }
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
    /// Gets memory analytics with short-lived caching.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current memory analytics snapshot.</returns>
    public async Task<MemoryDashboardStats> GetMemoryStatsAsync(CancellationToken cancellationToken = default)
    {
        if (_memoryStatsCache.TryGet(out var cachedMemoryStats))
        {
            return cachedMemoryStats;
        }

        var freshMemoryStats = await _managementService.GetMemoryStatsAsync(cancellationToken: cancellationToken);
        _memoryStatsCache.Set(freshMemoryStats);
        return freshMemoryStats;
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
        var memoryStats = await GetMemoryStatsAsync(cancellationToken);
        var performanceMetrics = CapturePerformanceSample(stats);
        var activity = GetRecentActivity(limit: 200);
        var sources = await _managementService.ListSourcesAsync(normalizedLimit, cancellationToken);

        return new DashboardSnapshotResponse(health, stats, sources, memoryStats, activity, performanceMetrics, DateTime.UtcNow);
    }

    /// <summary>
    /// Returns recent activity entries ordered newest-first.
    /// </summary>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <returns>Recent dashboard activity entries.</returns>
    public IReadOnlyList<DashboardActivityEvent> GetRecentActivity(int limit = 100)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 500);
        lock (_performanceSync)
        {
            return _activityHistory
                .TakeLast(normalizedLimit)
                .Reverse()
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Captures one performance sample from current index stats and process telemetry.
    /// </summary>
    /// <param name="stats">Current index statistics snapshot.</param>
    /// <returns>Recent performance history list ordered by time.</returns>
    private IReadOnlyList<PerformanceMetricPoint> CapturePerformanceSample(IndexStats stats)
    {
        lock (_performanceSync)
        {
            var sampleAtUtc = DateTime.UtcNow;
            var processCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
            var queryLatencyAverageMs = _queryLatenciesMs.Count == 0 ? 0 : _queryLatenciesMs.Average();

            var indexingRateDocsPerSec = 0d;
            var cpuUsagePercent = 0d;

            if (_lastSampleAtUtc != DateTime.MinValue)
            {
                var elapsedSeconds = (sampleAtUtc - _lastSampleAtUtc).TotalSeconds;
                if (elapsedSeconds > 0)
                {
                    var documentDelta = stats.DocumentCount - _lastDocumentCount;
                    indexingRateDocsPerSec = Math.Max(0, documentDelta / elapsedSeconds);
                    if (indexingRateDocsPerSec > 0)
                    {
                        _indexingRatesDocsPerSec.Enqueue(indexingRateDocsPerSec);
                        while (_indexingRatesDocsPerSec.Count > MaxIndexingRateSamples)
                        {
                            _indexingRatesDocsPerSec.Dequeue();
                        }
                    }

                    var cpuDeltaSeconds = (processCpuTime - _lastProcessCpuTime).TotalSeconds;
                    var cpuCapacitySeconds = elapsedSeconds * Environment.ProcessorCount;
                    if (cpuCapacitySeconds > 0)
                    {
                        cpuUsagePercent = Math.Clamp((cpuDeltaSeconds / cpuCapacitySeconds) * 100d, 0d, 100d);
                    }
                }
            }

            // Prefer event-driven throughput when available so short indexing bursts remain visible.
            if (_indexingRatesDocsPerSec.Count > 0)
            {
                indexingRateDocsPerSec = _indexingRatesDocsPerSec.Average();
            }

            var sample = new PerformanceMetricPoint(sampleAtUtc, queryLatencyAverageMs, indexingRateDocsPerSec, cpuUsagePercent);
            _performanceHistory.Enqueue(sample);
            _lastSampleAtUtc = sampleAtUtc;
            _lastProcessCpuTime = processCpuTime;
            _lastDocumentCount = stats.DocumentCount;

            TrimPerformanceHistory(sampleAtUtc);
            return _performanceHistory.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Trims performance history to 24 hours and bounded sample size.
    /// </summary>
    /// <param name="nowUtc">Current UTC timestamp.</param>
    private void TrimPerformanceHistory(DateTime nowUtc)
    {
        while (_performanceHistory.Count > 0 && (nowUtc - _performanceHistory.Peek().TimestampUtc) > TimeSpan.FromHours(24))
        {
            _performanceHistory.Dequeue();
        }

        while (_performanceHistory.Count > MaxPerformanceSamples)
        {
            _performanceHistory.Dequeue();
        }
    }
}

/// <summary>
/// Combined payload returned to dashboard clients.
/// </summary>
/// <param name="Health">Health status data.</param>
/// <param name="Stats">Index statistics data.</param>
/// <param name="Sources">Recent indexed sources.</param>
/// <param name="MemoryStats">Aggregated memory analytics.</param>
/// <param name="Activity">Recent activity entries.</param>
/// <param name="PerformanceMetrics">Recent sampled performance metrics.</param>
/// <param name="GeneratedAtUtc">UTC timestamp when this payload was generated.</param>
public sealed record DashboardSnapshotResponse(HealthCheckResult Health, IndexStats Stats, IReadOnlyList<SourceInfo> Sources, MemoryDashboardStats MemoryStats, IReadOnlyList<DashboardActivityEvent> Activity, IReadOnlyList<PerformanceMetricPoint> PerformanceMetrics, DateTime GeneratedAtUtc);

/// <summary>
/// One activity timeline entry displayed in the dashboard.
/// </summary>
/// <param name="Timestamp">UTC timestamp when this event was captured.</param>
/// <param name="EventType">Activity category.</param>
/// <param name="Description">Short activity description.</param>
/// <param name="Metadata">Optional metadata values.</param>
public sealed record DashboardActivityEvent(DateTime Timestamp, string EventType, string Description, IReadOnlyDictionary<string, string?>? Metadata = null);

/// <summary>
/// One sampled runtime point displayed in dashboard performance timeline.
/// </summary>
/// <param name="TimestampUtc">UTC timestamp when this sample was captured.</param>
/// <param name="QueryLatencyMs">Rolling average query latency in milliseconds.</param>
/// <param name="IndexingRateDocsPerSec">Observed indexing throughput in documents per second.</param>
/// <param name="CpuUsagePercent">Observed process CPU utilization percentage across available cores.</param>
public sealed record PerformanceMetricPoint(DateTime TimestampUtc, double QueryLatencyMs, double IndexingRateDocsPerSec, double CpuUsagePercent);

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
