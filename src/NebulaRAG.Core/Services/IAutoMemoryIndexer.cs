namespace NebulaRAG.Core.Services;

/// <summary>Indexing abstraction used by AutoMemorySyncService (enables unit testing).</summary>
public interface IAutoMemoryIndexer
{
    /// <summary>Ingests a single auto-memory file into the RAG index under the given project slug.</summary>
    /// <param name="filePath">Absolute path of the Markdown file to ingest.</param>
    /// <param name="projectSlug">Project identifier used to namespace the indexed content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task IngestFileAsync(string filePath, string projectSlug, CancellationToken cancellationToken = default);

    /// <summary>Deletes existing chunks for the given source path and re-indexes the file from disk.</summary>
    /// <param name="sourcePath">Absolute path of the source file to reindex.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ReindexSourceAsync(string sourcePath, CancellationToken cancellationToken = default);
}
