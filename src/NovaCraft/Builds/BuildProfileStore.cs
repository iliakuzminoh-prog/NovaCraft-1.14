using System.Text.Json;

namespace NovaCraft.Builds;

public sealed class BuildProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string filePath;

    public BuildProfileStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Path is required.", nameof(filePath));

        this.filePath = Path.GetFullPath(filePath);
    }

    public IReadOnlyList<BuildProfile> Load()
    {
        if (!File.Exists(filePath))
            return Array.Empty<BuildProfile>();

        try
        {
            var json = File.ReadAllText(filePath);
            var builds = JsonSerializer.Deserialize<List<BuildProfile>>(json, JsonOptions);
            return builds ?? new List<BuildProfile>();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Build profile file is invalid: {filePath}", ex);
        }
    }

    public void Save(IEnumerable<BuildProfile> builds)
    {
        ArgumentNullException.ThrowIfNull(builds);

        var normalized = builds.Select(Normalize).ToList();
        var duplicateIds = normalized
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateIds.Length > 0)
            throw new InvalidDataException($"Duplicate build IDs: {string.Join(", ", duplicateIds)}");

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = filePath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(tempPath, filePath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public BuildProfile Add(ICollection<BuildProfile> builds, BuildProfile build)
    {
        ArgumentNullException.ThrowIfNull(builds);
        ArgumentNullException.ThrowIfNull(build);

        var normalized = Normalize(build);
        if (builds.Any(x => string.Equals(x.Id, normalized.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Build ID already exists: {normalized.Id}");

        builds.Add(normalized);
        return normalized;
    }

    public BuildProfile Duplicate(ICollection<BuildProfile> builds, string sourceId, string? requestedName = null)
    {
        ArgumentNullException.ThrowIfNull(builds);

        var source = builds.FirstOrDefault(x =>
            string.Equals(x.Id, sourceId, StringComparison.OrdinalIgnoreCase));

        if (source is null)
            throw new KeyNotFoundException($"Build not found: {sourceId}");

        var copy = new BuildProfile
        {
            Name = string.IsNullOrWhiteSpace(requestedName) ? source.Name + " — копия" : requestedName.Trim(),
            MinecraftVersion = source.MinecraftVersion,
            Loader = source.Loader,
            GameDirectory = source.GameDirectory,
            MaximumRamMb = source.MaximumRamMb,
            FpsBoost = source.FpsBoost,
            Favorite = false
        };

        return Add(builds, copy);
    }

    public bool Remove(ICollection<BuildProfile> builds, string id)
    {
        ArgumentNullException.ThrowIfNull(builds);

        var existing = builds.FirstOrDefault(x =>
            string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

        return existing is not null && builds.Remove(existing);
    }

    private static BuildProfile Normalize(BuildProfile source)
    {
        var name = source.Name.Trim();
        var version = source.MinecraftVersion.Trim();
        var loader = source.Loader.Trim().ToLowerInvariant();
        var gameDirectory = source.GameDirectory.Trim();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Build name cannot be empty.", nameof(source));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Minecraft version cannot be empty.", nameof(source));

        var allowedLoaders = new[] { "vanilla", "fabric", "forge", "quilt", "neoforge" };
        if (!allowedLoaders.Contains(loader, StringComparer.Ordinal))
            throw new ArgumentException($"Unsupported loader: {source.Loader}", nameof(source));

        if (source.MaximumRamMb < 512)
            throw new ArgumentOutOfRangeException(nameof(source.MaximumRamMb), "RAM limit must be at least 512 MB.");

        if (string.IsNullOrWhiteSpace(source.Id))
            throw new ArgumentException("Build ID cannot be empty.", nameof(source));

        return new BuildProfile
        {
            Id = source.Id.Trim(),
            Name = name,
            MinecraftVersion = version,
            Loader = loader,
            GameDirectory = gameDirectory,
            MaximumRamMb = source.MaximumRamMb,
            FpsBoost = source.FpsBoost,
            Favorite = source.Favorite
        };
    }
}
