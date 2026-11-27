using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class PerformanceProfileRulesTests
{
    [Fact]
    public void RecommendsHeapWithinAvailableMemoryAndEightGbCap()
    {
        var result = PerformanceProfileRules.RecommendedHeapMb(new SystemMemorySnapshot(16 * 1024, 8 * 1024));
        Assert.Equal<int?>(4864, result);
        Assert.True(result.HasValue);
        Assert.True(result.Value <= 8 * 1024);
        Assert.True(result.Value < 8 * 1024);
    }

    [Fact]
    public void DeclinesAutomaticRecommendationWhenMemoryHeadroomIsTooLow()
    {
        var memory = new SystemMemorySnapshot(4 * 1024, 1536);
        Assert.Null(PerformanceProfileRules.RecommendedHeapMb(memory));
        Assert.True(PerformanceProfileRules.IsLowAvailableMemory(memory));
        Assert.Contains("закрой тяжёлые программы", PerformanceProfileRules.Describe(memory), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FallsBackWhenWindowsMemoryInformationIsUnavailable()
    {
        Assert.Null(PerformanceProfileRules.RecommendedHeapMb(new SystemMemorySnapshot(0, 0)));
    }
}
