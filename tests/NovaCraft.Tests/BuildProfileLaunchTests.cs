using NovaCraft.Builds;

namespace NovaCraft.Tests;

public sealed class BuildProfileLaunchTests
{
    [Fact]
    public void Apply_UsesSelectedBuildDirectoryAndRuntimeSettings()
    {
        var settings = new LauncherSettings
        {
            GameDirectory = "old",
            LastVersion = "old-version",
            ModLoader = "vanilla",
            MaximumRamMb = 1024,
            FpsBoost = false
        };

        var build = new BuildProfile
        {
            Name = "Forge test",
            MinecraftVersion = "1.20.1",
            Loader = "forge",
            GameDirectory = Path.Combine(Path.GetTempPath(), "NovaCraft-test-instance"),
            MaximumRamMb = 4096,
            FpsBoost = true
        };

        BuildProfileLaunch.Apply(settings, build);

        Assert.Equal(build.GameDirectory, settings.GameDirectory);
        Assert.Equal(build.MinecraftVersion, settings.LastVersion);
        Assert.Equal(build.Loader, settings.ModLoader);
        Assert.Equal(build.MaximumRamMb, settings.MaximumRamMb);
        Assert.Equal(build.FpsBoost, settings.FpsBoost);
    }
}
