using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;

namespace NebulaRAG.Tests;

public sealed class MemorySearchResultRankerTests
{
    [Fact]
    public void ShouldUseLexicalFallback_ReturnsTrue_WhenSemanticResultsAreMissing()
    {
        var shouldFallback = MemorySearchResultRanker.ShouldUseLexicalFallback([], requestedCount: 3);

        Assert.True(shouldFallback);
    }

    [Fact]
    public void ShouldUseLexicalFallback_ReturnsTrue_WhenTopSemanticScoreIsWeak()
    {
        var semanticResults = new[]
        {
            new MemorySearchResult(1, "s1", "p1", "semantic", "weak semantic hit", ["architecture"], DateTimeOffset.UtcNow, 0.20d, MemoryTier.ShortTerm, null),
            new MemorySearchResult(2, "s1", "p1", "semantic", "second weak hit", ["design"], DateTimeOffset.UtcNow.AddMinutes(-1), 0.18d, MemoryTier.ShortTerm, null),
            new MemorySearchResult(3, "s1", "p1", "semantic", "third weak hit", ["notes"], DateTimeOffset.UtcNow.AddMinutes(-2), 0.14d, MemoryTier.ShortTerm, null)
        };

        var shouldFallback = MemorySearchResultRanker.ShouldUseLexicalFallback(semanticResults, requestedCount: 3);

        Assert.True(shouldFallback);
    }

    [Fact]
    public void MergePrimaryWithFallback_PreservesSemanticResults_AndAppendsUniqueLexicalMatches()
    {
        var semanticResults = new[]
        {
            new MemorySearchResult(1, "session-a", "project-a", "semantic", "semantic memory", ["plan"], DateTimeOffset.UtcNow, 0.81d, MemoryTier.ShortTerm, null),
            new MemorySearchResult(2, "session-a", "project-a", "semantic", "second semantic memory", ["decision"], DateTimeOffset.UtcNow.AddMinutes(-1), 0.73d, MemoryTier.ShortTerm, null)
        };

        var lexicalResults = new[]
        {
            new MemorySearchResult(1, "session-a", "project-a", "semantic", "duplicate semantic memory", ["plan"], DateTimeOffset.UtcNow, 0.95d, MemoryTier.ShortTerm, null),
            new MemorySearchResult(3, "session-b", "project-a", "episodic", "lexical fill", ["hook"], DateTimeOffset.UtcNow.AddMinutes(-2), 0.55d, MemoryTier.ShortTerm, null)
        };

        var merged = MemorySearchResultRanker.MergePrimaryWithFallback("semantic memory", semanticResults, lexicalResults, requestedCount: 3);

        Assert.Collection(
            merged,
            first => Assert.Equal(1, first.Id),
            second => Assert.Equal(2, second.Id),
            third => Assert.Equal(3, third.Id));
    }

    [Fact]
    public void MergePrimaryWithFallback_BoostsExactTagMatches()
    {
        var semanticResults = new[]
        {
            new MemorySearchResult(10, "session-a", "project-a", "semantic", "general planning note", ["notes"], DateTimeOffset.UtcNow, 0.28d, MemoryTier.ShortTerm, null)
        };

        var lexicalResults = new[]
        {
            new MemorySearchResult(11, "session-b", "project-a", "procedural", "installer workflow note", ["nebula-rag"], DateTimeOffset.UtcNow.AddMinutes(-2), 0.22d, MemoryTier.ShortTerm, null),
            new MemorySearchResult(12, "session-c", "project-a", "semantic", "another general note", ["misc"], DateTimeOffset.UtcNow.AddMinutes(-1), 0.44d, MemoryTier.ShortTerm, null)
        };

        var merged = MemorySearchResultRanker.MergePrimaryWithFallback("nebula-rag", semanticResults, lexicalResults, requestedCount: 3);

        Assert.Equal(11, merged[0].Id);
    }
}
