using System.Text.Json;
using System.Text.RegularExpressions;
using CmlLib.Core;

namespace NovaCraft.Services;

/// <summary>Loader identifiers used in settings and instance.json (lower-case) and their UI names.</summary>
public static class LoaderIds
{
    public static string FromDisplay(string? display) => display switch
    {
        "Forge" => "forge",
        "Quilt" => "quilt",
        "Vanilla" => "vanilla",
        _ => "fabric"
    };

    public static string ToDisplay(string? id) => id switch
    {
        "forge" => "Forge",
        "quilt" => "Quilt",
        "vanilla" => "Vanilla",
        _ => "Fabric"
    };

    public static string Normalize(string? id) => FromDisplay(ToDisplay(id));
}

/// <summary>Persistent description of one isolated game instance (instance.json).</summary>
public sealed class InstanceInfo
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string MinecraftVersion { get; set; } = "";
    public string Loader { get; set; } = "vanilla";

    /// <summary>
    /// True: libraries, versions, assets and Java runtime live in the shared folder.
    /// False: everything stays inside the game folder (used for adopted legacy folders).
    /// </summary>
    public bool UsesSharedStorage { get; set; } = true;

    /// <summary>Absolute path of an existing game folder adopted from an older NovaCraft. Never moved or deleted.</summary>
    public string? CustomGameDir { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Layout and persistence of instances:
/// <code>
/// {Root}\shared\{libraries,versions,assets,runtime}   one copy for every instance
/// {Root}\instances\{id}\instance.json
/// {Root}\instances\{id}\game\                         mods, saves, config, resourcepacks, ...
/// </code>
/// </summary>
public sealed class InstanceStore
{
    static readonly Regex IdPattern = new(@"^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.Compiled);
    static readonly Regex ReservedName = new(@"^(con|prn|aux|nul|com[1-9]|lpt[1-9])(\..*)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Root { get; }
    public string InstancesDir => Path.Combine(Root, "instances");
    public string SharedDir => Path.Combine(Root, "shared");

    public InstanceStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
            throw new ArgumentException("Папка данных NovaCraft должна быть абсолютным путём.", nameof(root));
        Root = Path.GetFullPath(root);
    }

    // ---------- ids and paths ----------

    public static bool IsValidId(string? id) =>
        id is not null && IdPattern.IsMatch(id) && !id.Contains("..") && !id.EndsWith('.') && !ReservedName.IsMatch(id);

    /// <summary>Lower-case file-system-safe slug; never empty.</summary>
    public static string Slug(string? text)
    {
        var chars = (text ?? "").Trim().ToLowerInvariant()
            .Select(c => (c is >= 'a' and <= 'z') || (c is >= '0' and <= '9') || c is '.' or '_' ? c : '-')
            .ToArray();
        var slug = Regex.Replace(new string(chars), "-{2,}", "-").Trim('-', '.', '_');
        if (slug.Length > 48) slug = slug[..48].TrimEnd('-', '.', '_');
        if (slug.Length == 0) slug = "instance";
        if (ReservedName.IsMatch(slug)) slug = "i-" + slug;
        return slug;
    }

    /// <summary>Deterministic id for the automatic instance of a version/loader pair.</summary>
    public static string MakeId(string loader, string version) => Slug(LoaderIds.Normalize(loader) + "-" + version);

    public string InstanceDir(string id)
    {
        if (!IsValidId(id)) throw new ArgumentException("Недопустимый идентификатор экземпляра: " + id, nameof(id));
        var dir = Path.GetFullPath(Path.Combine(InstancesDir, id));
        var prefix = Path.GetFullPath(InstancesDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Путь экземпляра выходит за пределы папки instances.");
        return dir;
    }

    public string RecordPath(string id) => Path.Combine(InstanceDir(id), "instance.json");

    public string GameDir(InstanceInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!string.IsNullOrWhiteSpace(info.CustomGameDir))
        {
            if (!Path.IsPathRooted(info.CustomGameDir))
                throw new InvalidDataException("Папка игры экземпляра должна быть абсолютным путём.");
            return Path.GetFullPath(info.CustomGameDir);
        }
        return Path.Combine(InstanceDir(info.Id), "game");
    }

    /// <summary>
    /// MinecraftPath for CmlLib.Core 4.1.0: BasePath is the instance game folder; for shared-storage instances
    /// Library/Versions/Assets/Runtime point to the shared folder (all four are settable in 4.1.0).
    /// </summary>
    public MinecraftPath CreateMinecraftPath(InstanceInfo info)
    {
        var path = new MinecraftPath(GameDir(info));
        if (info.UsesSharedStorage)
        {
            path.Library = Path.Combine(SharedDir, "libraries");
            path.Versions = Path.Combine(SharedDir, "versions");
            path.Assets = Path.Combine(SharedDir, "assets");
            path.Runtime = Path.Combine(SharedDir, "runtime");
        }
        return path;
    }

    // ---------- persistence ----------

    public bool Exists(string id) => IsValidId(id) && File.Exists(RecordPath(id));

    public InstanceInfo? Load(string id)
    {
        if (!IsValidId(id)) return null;
        var file = RecordPath(id);
        if (!File.Exists(file)) return null;
        try
        {
            var info = JsonSerializer.Deserialize<InstanceInfo>(File.ReadAllText(file));
            if (info is null || !string.Equals(info.Id, id, StringComparison.Ordinal)) return null;
            info.Loader = LoaderIds.Normalize(info.Loader);
            return info;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<InstanceInfo> List()
    {
        if (!Directory.Exists(InstancesDir)) return Array.Empty<InstanceInfo>();
        var result = new List<InstanceInfo>();
        foreach (var dir in Directory.EnumerateDirectories(InstancesDir))
        {
            var id = Path.GetFileName(dir);
            if (!IsValidId(id)) continue;
            var info = Load(id);
            if (info is not null) result.Add(info);
        }
        return result.OrderBy(x => x.CreatedUtc).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Writes instance.json atomically (temp file + move).</summary>
    public void Save(InstanceInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var dir = InstanceDir(info.Id);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "instance.json");
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(info, JsonOptions));
        File.Move(temp, file, true);
    }

    public void EnsureSaved(InstanceInfo info)
    {
        if (!Exists(info.Id)) Save(info);
    }

    /// <summary>Returns baseId, or baseId-2, baseId-3 ... for the first id without an instance.json.</summary>
    public string UniqueId(string baseId)
    {
        var b = Slug(baseId);
        var id = b;
        for (var n = 2; Exists(id); n++)
            id = b + "-" + n;
        return id;
    }

    public InstanceInfo Create(string name, string version, string loader)
    {
        var cleanName = string.IsNullOrWhiteSpace(name) ? "Экземпляр" : name.Trim();
        var info = new InstanceInfo
        {
            Id = UniqueId(cleanName),
            Name = cleanName,
            MinecraftVersion = version,
            Loader = LoaderIds.Normalize(loader)
        };
        Save(info);
        return info;
    }

    /// <summary>Removes the instance record and its isolated game folder. An adopted CustomGameDir is never touched.</summary>
    public void Delete(string id)
    {
        var dir = InstanceDir(id);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    // ---------- selection ----------

    public static bool Matches(InstanceInfo info, string version, string loader) =>
        string.Equals(info.MinecraftVersion, version, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(LoaderIds.Normalize(info.Loader), LoaderIds.Normalize(loader), StringComparison.Ordinal);

    /// <summary>
    /// Picks the instance for a version/loader pair so that mods of different loaders or versions never share a folder:
    /// the active instance if it matches, else the oldest matching instance, else a new (not yet saved) automatic one.
    /// </summary>
    public InstanceInfo Resolve(string? activeId, string version, string loader)
    {
        var all = List();
        var active = all.FirstOrDefault(x => x.Id == activeId);
        if (active is not null && Matches(active, version, loader)) return active;

        var existing = all.FirstOrDefault(x => Matches(x, version, loader));
        if (existing is not null) return existing;

        var normalized = LoaderIds.Normalize(loader);
        return new InstanceInfo
        {
            Id = UniqueId(MakeId(normalized, version)),
            Name = LoaderIds.ToDisplay(normalized) + " " + version,
            MinecraftVersion = version,
            Loader = normalized
        };
    }

    // ---------- migration ----------

    /// <summary>
    /// Adopts a non-empty legacy game folder as the instance "Основной" without moving or copying anything.
    /// Returns null when there is nothing to adopt. Safe to call repeatedly.
    /// </summary>
    public InstanceInfo? MigrateLegacy(string? legacyDir, string version, string loader)
    {
        if (string.IsNullOrWhiteSpace(legacyDir) || !Path.IsPathRooted(legacyDir) || !Directory.Exists(legacyDir))
            return null;

        var full = Path.GetFullPath(legacyDir);
        if (IsInside(full, InstancesDir) || IsInside(full, SharedDir)) return null;
        if (!Directory.EnumerateFileSystemEntries(full).Any()) return null;

        var existing = List().FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(x.CustomGameDir) &&
            string.Equals(Path.GetFullPath(x.CustomGameDir), full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var info = new InstanceInfo
        {
            Id = UniqueId("main"),
            Name = "Основной",
            MinecraftVersion = version,
            Loader = LoaderIds.Normalize(loader),
            UsesSharedStorage = false,
            CustomGameDir = full
        };
        Save(info);
        return info;
    }

    static bool IsInside(string path, string dir)
    {
        var d = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).Length >= d.Length - 1 &&
               (Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
                   .StartsWith(d, StringComparison.OrdinalIgnoreCase);
    }
}
