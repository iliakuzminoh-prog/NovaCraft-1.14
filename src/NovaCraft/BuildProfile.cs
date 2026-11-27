namespace NovaCraft;

internal sealed class BuildProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Основная сборка";
    public string MinecraftVersion { get; set; } = "1.21.1";
    public string Loader { get; set; } = "fabric";
    public string LoaderVersion { get; set; } = "";
    public string GameDirectory { get; set; } = "";
    public string SourcePlatform { get; set; } = "local";
    public string SourceProjectId { get; set; } = "";
    public string InstallState { get; set; } = "Пустая";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastPlayedUtc { get; set; }

    public override string ToString()
        => $"{Name}  ·  {MinecraftVersion}  ·  {Loader.ToUpperInvariant()}";
}

internal static class BuildProfileRules
{
    public static string CreateInstanceDirectory(string currentGameDirectory, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new InvalidDataException("У сборки некорректный идентификатор.");

        var current = Path.GetFullPath(currentGameDirectory);
        var parent = Directory.GetParent(current)?.FullName
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaCraft");
        var root = string.Equals(Path.GetFileName(parent), "NovaCraft-instances", StringComparison.OrdinalIgnoreCase)
            ? parent
            : Path.Combine(parent, "NovaCraft-instances");

        return Path.GetFullPath(Path.Combine(root, id));
    }

    public static void EnsureProfiles(LauncherSettings settings)
    {
        settings.BuildProfiles ??= new List<BuildProfile>();
        settings.SavedBuilds ??= new List<string>();

        if (settings.BuildProfiles.Count == 0)
        {
            var primary = new BuildProfile
            {
                Name = "Основная сборка",
                MinecraftVersion = string.IsNullOrWhiteSpace(settings.LastVersion) ? "1.21.1" : settings.LastVersion,
                Loader = NormalizeLoader(settings.ModLoader),
                GameDirectory = Path.GetFullPath(settings.GameDirectory),
                SourcePlatform = "local"
            };
            settings.BuildProfiles.Add(primary);

            foreach (var legacy in settings.SavedBuilds)
            {
                var parts = (legacy ?? "").Split('|');
                if (parts.Length < 3)
                    continue;

                var id = Guid.NewGuid().ToString("N");
                settings.BuildProfiles.Add(new BuildProfile
                {
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(parts[0]) ? "Импортированная сборка" : parts[0].Trim(),
                    MinecraftVersion = string.IsNullOrWhiteSpace(parts[1]) ? settings.LastVersion : parts[1].Trim(),
                    Loader = NormalizeLoader(parts[2]),
                    GameDirectory = CreateInstanceDirectory(settings.GameDirectory, id),
                    SourcePlatform = "legacy"
                });
            }
        }

        settings.BuildProfiles = settings.BuildProfiles
            .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Id) && Guid.TryParseExact(p.Id, "N", out _))
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (settings.BuildProfiles.Count == 0)
        {
            var id = Guid.NewGuid().ToString("N");
            settings.BuildProfiles.Add(new BuildProfile
            {
                Id = id,
                Name = "Основная сборка",
                MinecraftVersion = "1.21.1",
                Loader = "fabric",
                GameDirectory = Path.GetFullPath(settings.GameDirectory)
            });
        }

        var active = settings.BuildProfiles.FirstOrDefault(p =>
            string.Equals(p.Id, settings.ActiveBuildId, StringComparison.OrdinalIgnoreCase))
            ?? settings.BuildProfiles[0];

        foreach (var profile in settings.BuildProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
                profile.Name = "Сборка";
            profile.Loader = NormalizeLoader(profile.Loader);
            if (string.IsNullOrWhiteSpace(profile.MinecraftVersion))
                profile.MinecraftVersion = "1.21.1";
            if (string.IsNullOrWhiteSpace(profile.GameDirectory))
                profile.GameDirectory = CreateInstanceDirectory(settings.GameDirectory, profile.Id);
            profile.GameDirectory = Path.GetFullPath(profile.GameDirectory);
        }

        settings.ActiveBuildId = active.Id;
        settings.LastVersion = active.MinecraftVersion;
        settings.ModLoader = active.Loader;
        settings.GameDirectory = active.GameDirectory;
    }

    public static string NormalizeLoader(string? loader)
    {
        var normalized = (loader ?? "").Trim().ToLowerInvariant();
        return normalized is "vanilla" or "fabric" or "forge" or "quilt" or "neoforge"
            ? normalized
            : "fabric";
    }
}
