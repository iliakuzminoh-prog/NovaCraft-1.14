using NovaCraft;

namespace NovaCraft.Tests;

public sealed class BuildProfileRulesTests
{
    [Fact]
    public void MigratesLegacySavedBuildsIntoSeparateDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "novacraft-build-test-" + Guid.NewGuid().ToString("N"));
        var existingGame = Path.Combine(root, "MainMinecraft");
        var settings = new LauncherSettings
        {
            GameDirectory = existingGame,
            LastVersion = "1.21.1",
            ModLoader = "fabric",
            SavedBuilds = new List<string>
            {
                "Forge Survival|1.20.1|Forge",
                "Vanilla Test|1.19.4|Vanilla"
            }
        };

        BuildProfileRules.EnsureProfiles(settings);

        Assert.Equal(3, settings.BuildProfiles.Count);
        var main = settings.BuildProfiles.Single(p => p.Name == "Основная сборка");
        Assert.Equal(Path.GetFullPath(existingGame), main.GameDirectory);

        var legacyProfiles = settings.BuildProfiles.Where(p => p.SourcePlatform == "legacy").ToList();
        Assert.Equal(2, legacyProfiles.Count);
        Assert.All(legacyProfiles, profile =>
        {
            Assert.NotEqual(main.GameDirectory, profile.GameDirectory);
            Assert.StartsWith(Path.Combine(root, "NovaCraft-instances"), profile.GameDirectory, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Equal(legacyProfiles.Count, legacyProfiles.Select(p => p.GameDirectory).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("forge", legacyProfiles.Single(p => p.Name == "Forge Survival").Loader);
        Assert.Equal("vanilla", legacyProfiles.Single(p => p.Name == "Vanilla Test").Loader);
        Assert.Equal(main.Id, settings.ActiveBuildId);
    }

    [Fact]
    public void RepairsDuplicateGameDirectoriesWithoutDeletingData()
    {
        var root = Path.Combine(Path.GetTempPath(), "novacraft-build-test-" + Guid.NewGuid().ToString("N"));
        var shared = Path.Combine(root, "Shared");
        var settings = new LauncherSettings
        {
            GameDirectory = shared,
            BuildProfiles = new List<BuildProfile>
            {
                new() { Id = Guid.NewGuid().ToString("N"), Name = "One", GameDirectory = shared },
                new() { Id = Guid.NewGuid().ToString("N"), Name = "Two", GameDirectory = shared }
            }
        };

        BuildProfileRules.EnsureProfiles(settings);

        Assert.Equal(2, settings.BuildProfiles.Select(p => p.GameDirectory).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(shared, settings.BuildProfiles[0].GameDirectory);
        Assert.False(Directory.Exists(shared));
    }

    [Theory]
    [InlineData("Forge", "forge")]
    [InlineData("fabric", "fabric")]
    [InlineData("Vanilla", "vanilla")]
    [InlineData("Quilt", "quilt")]
    [InlineData("unknown", "fabric")]
    public void NormalizesLoaderNames(string input, string expected)
        => Assert.Equal(expected, BuildProfileRules.NormalizeLoader(input));
}
