namespace NebulaRAG.Core.Services;

/// <summary>
/// Receives runtime activity and performance telemetry from API and MCP execution paths.
/// </summary>
public interface IRuntimeTelemetrySink
{
    /// <summary>
    /// Records one activity event for dashboard timelines.
    /// </summary>
    /// <param name="eventType">Event category (for example query, index, mcp, error).</param>
    /// <param name="description">Short human-readable activity description.</param>
    /// <param name="metadata">Optional event metadata values.</param>
    void RecordActivity(string eventType, string description, IReadOnlyDictionary<string, string?>? metadata = null);

    /// <summary>
    /// Records one query-latency sample in milliseconds.
    /// </summary>
    /// <param name="elapsedMilliseconds">Observed query duration.</param>
    void RecordQueryLatency(double elapsedMilliseconds);

    /// <summary>
    /// Records one indexing-throughput sample in documents per second.
    /// </summary>
    /// <param name="documentsPerSecond">Observed indexing throughput.</param>
    void RecordIndexingRate(double documentsPerSecond);
}

/// <summary>
/// No-op telemetry sink used by runtimes that do not expose dashboard telemetry.
/// </summary>
public sealed class NullRuntimeTelemetrySink : IRuntimeTelemetrySink
{
    /// <inheritdoc />
    public void RecordActivity(string eventType, string description, IReadOnlyDictionary<string, string?>? metadata = null)
    {
    }

    /// <inheritdoc />
    public void RecordQueryLatency(double elapsedMilliseconds)
    {
    }

    /// <inheritdoc />
    public void RecordIndexingRate(double documentsPerSecond)
    {
    }
}
