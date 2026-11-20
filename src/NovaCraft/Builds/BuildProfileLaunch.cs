namespace NovaCraft.Builds;

public static class BuildProfileLaunch
{
    internal static void Apply(LauncherSettings settings, BuildProfile build)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(build);

        settings.LastVersion = build.MinecraftVersion;
        settings.ModLoader = build.Loader;
        settings.GameDirectory = build.GameDirectory;
        settings.MaximumRamMb = build.MaximumRamMb;
        settings.FpsBoost = build.FpsBoost;
    }
}
