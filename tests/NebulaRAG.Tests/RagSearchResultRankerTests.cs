using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;

namespace NebulaRAG.Tests;

public sealed class RagSearchResultRankerTests
{
    [Fact]
    public void ShouldUseLexicalFallback_ReturnsTrue_WhenSemanticResultsAreMissing()
    {
        var shouldFallback = RagSearchResultRanker.ShouldUseLexicalFallback([], requestedCount: 3);

        Assert.True(shouldFallback);
    }

    [Fact]
    public void ShouldUseLexicalFallback_ReturnsTrue_WhenTopSemanticScoreIsWeak()
    {
        var semanticResults = new[]
        {
            new RagSearchResult("src/alpha.cs", 0, "weak semantic hit", 0.21d),
            new RagSearchResult("src/beta.cs", 0, "second weak hit", 0.18d),
            new RagSearchResult("src/gamma.cs", 0, "third weak hit", 0.12d)
        };

        var shouldFallback = RagSearchResultRanker.ShouldUseLexicalFallback(semanticResults, requestedCount: 3);

        Assert.True(shouldFallback);
    }

    [Fact]
    public void MergePrimaryWithFallback_PreservesSemanticOrder_AndAppendsUniqueLexicalMatches()
    {
        var semanticResults = new[]
        {
            new RagSearchResult("src/alpha.cs", 0, "semantic 1", 0.82d),
            new RagSearchResult("src/beta.cs", 1, "semantic 2", 0.74d)
        };

        var lexicalResults = new[]
        {
            new RagSearchResult("SRC/ALPHA.cs", 0, "duplicate exact chunk", 0.99d),
            new RagSearchResult("src/gamma.cs", 0, "lexical fill", 0.67d),
            new RagSearchResult("src/delta.cs", 0, "overflow lexical fill", 0.51d)
        };

        var merged = RagSearchResultRanker.MergePrimaryWithFallback("semantic fill", semanticResults, lexicalResults, requestedCount: 3);

        Assert.Collection(
            merged,
            first => Assert.Equal(("src/alpha.cs", 0), (first.SourcePath, first.ChunkIndex)),
            second => Assert.Equal(("src/beta.cs", 1), (second.SourcePath, second.ChunkIndex)),
            third => Assert.Equal(("src/gamma.cs", 0), (third.SourcePath, third.ChunkIndex)));
    }

    /// <summary>
    /// Validates that exact source-path matches outrank generic lexical or weak semantic candidates.
    /// </summary>
    [Fact]
    public void MergePrimaryWithFallback_BoostsExactSourcePathMatches()
    {
        var semanticResults = new[]
        {
            new RagSearchResult("src/other-service.cs", 0, "generic service implementation", 0.28d)
        };

        var lexicalResults = new[]
        {
            new RagSearchResult("src/RagQueryService.cs", 0, "query service implementation details", 0.22d),
            new RagSearchResult("src/query-notes.md", 0, "rag query service notes", 0.45d)
        };

        var merged = RagSearchResultRanker.MergePrimaryWithFallback("RagQueryService.cs", semanticResults, lexicalResults, requestedCount: 3);

        Assert.Equal("src/RagQueryService.cs", merged[0].SourcePath);
    }
}