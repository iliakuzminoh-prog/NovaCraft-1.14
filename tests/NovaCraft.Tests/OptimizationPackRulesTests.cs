using NovaCraft.Services;

namespace NovaCraft.Tests;

public sealed class OptimizationPackRulesTests
{
    [Fact]
    public void ConservativePackTargetsFabric12111AndAvoidsUnsupportedRendererReplacement()
    {
        Assert.True(OptimizationPackRules.IsTarget("1.21.11", "fabric"));
        Assert.False(OptimizationPackRules.IsTarget("1.21.10", "fabric"));
        Assert.False(OptimizationPackRules.IsTarget("1.21.11", "forge"));

        var names = OptimizationPackRules.ConservativePack
            .Select(x => x.ProjectSlug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("lithium", names);
        Assert.Contains("ferrite-core", names);
        Assert.Contains("immediatelyfast", names);
        Assert.Contains("entityculling", names);
        Assert.DoesNotContain("sodium", names);
        Assert.DoesNotContain("badoptimizations", names);
        Assert.Equal(4, names.Count);
    }

    [Fact]
    public void VersionMetadataMustMatchExactGameAndLoader()
    {
        Assert.True(OptimizationPackRules.IsCompatibleVersion(
            new[] { "1.21.11" },
            new[] { "fabric", "quilt" },
            "1.21.11",
            "fabric"));

        Assert.False(OptimizationPackRules.IsCompatibleVersion(
            new[] { "1.21.10" },
            new[] { "fabric" },
            "1.21.11",
            "fabric"));

        Assert.False(OptimizationPackRules.IsCompatibleVersion(
            new[] { "1.21.11" },
            new[] { "forge" },
            "1.21.11",
            "fabric"));

        Assert.False(OptimizationPackRules.IsCompatibleVersion(
            null,
            new[] { "fabric" },
            "1.21.11",
            "fabric"));
    }

    [Fact]
    public void OnlyStableVersionsForTheExactTargetAreAccepted()
    {
        Assert.True(OptimizationPackRules.LooksLikeOptimizationPackVersion(
            "release",
            new[] { "1.21.11" },
            new[] { "fabric" }));

        Assert.False(OptimizationPackRules.LooksLikeOptimizationPackVersion(
            "beta",
            new[] { "1.21.11" },
            new[] { "fabric" }));

        Assert.False(OptimizationPackRules.LooksLikeOptimizationPackVersion(
            "release",
            new[] { "1.21.10" },
            new[] { "fabric" }));

        Assert.False(OptimizationPackRules.LooksLikeOptimizationPackVersion(
            "release",
            new[] { "1.21.11" },
            new[] { "forge" }));
    }
}
