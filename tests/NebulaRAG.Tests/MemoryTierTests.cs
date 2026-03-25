using Xunit;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Configuration;

namespace NebulaRAG.Tests;

public class MemoryTierTests
{
    [Fact]
    public void MemoryTier_KnownValues_AreCorrect()
    {
        Assert.Equal("short_term", MemoryTier.ShortTerm);
        Assert.Equal("long_term", MemoryTier.LongTerm);
    }

    [Theory]
    [InlineData("short_term", true)]
    [InlineData("long_term", true)]
    [InlineData("medium", false)]
    [InlineData("LONG_TERM", false)]
    [InlineData("", false)]
    public void MemoryTier_IsValid_CorrectlyClassifies(string value, bool expected)
    {
        Assert.Equal(expected, MemoryTier.IsValid(value));
    }

    [Fact]
    public void AutoMemorySettings_RetentionDaysAlias_MapsToShortTerm()
    {
        var settings = new AutoMemorySettings { RetentionDays = 14 };
        Assert.Equal(14, settings.ResolvedShortTermRetentionDays);
    }

    [Fact]
    public void AutoMemorySettings_Defaults_AreCorrect()
    {
        var settings = new AutoMemorySettings();
        Assert.Equal(30, settings.ShortTermRetentionDays);
        Assert.Equal(90, settings.LongTermReviewIntervalDays);
    }

    [Fact]
    public void MemoryRecord_HasTierAndReviewFields()
    {
        var record = new MemoryRecord(1, "s1", null, "episodic", "content",
            [], DateTimeOffset.UtcNow, MemoryTier.ShortTerm, null);
        Assert.Equal(MemoryTier.ShortTerm, record.Tier);
        Assert.Null(record.LastReviewedAtUtc);
    }
}
