using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class OptimizationPackRulesTests
{
    [Fact]
    public void ConservativePackTargetsFabric12111AndContainsClientFirstCore()
    {
        Assert.True(OptimizationPackRules.IsTarget("1.21.11", "fabric"));
        Assert.False(OptimizationPackRules.IsTarget("1.21.10", "fabric"));
        Assert.False(OptimizationPackRules.IsTarget("1.21.11", "forge"));

        var names = OptimizationPackRules.ConservativePack.Select(x => x.ProjectSlug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("sodium", names);
        Assert.Contains("lithium", names);
        Assert.Contains("ferrite-core", names);
        Assert.Contains("immediatelyfast", names);
        Assert.Contains("entityculling", names);
        Assert.Contains("badoptimizations", names);
    }

    [Fact]
    public void PackDoesNotPretendToSupportEveryMinecraftVersion()
    {
        Assert.False(OptimizationPackRules.IsTarget("1.21.1", "fabric"));
        Assert.False(OptimizationPackRules.IsTarget("26.1", "fabric"));
    }
}
