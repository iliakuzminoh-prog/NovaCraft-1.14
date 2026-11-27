using System.IO.Compression;
using System.Text.Json;

namespace NovaCraft.Services;

public sealed record PackFileReference(
    string RelativePath,
    string? DownloadUrl,
    string? Sha512,
    string? Sha1,
    long? FileSize,
    long? CurseForgeProjectId,
    long? CurseForgeFileId,
    bool Required);

public sealed class MinecraftPackDescriptor
{
    public string Name { get; init; } = "Импортированная сборка";
    public string Summary { get; init; } = "";
    public string MinecraftVersion { get; init; } = "1.21.1";
    public string Loader { get; init; } = "fabric";
    public string LoaderVersion { get; init; } = "";
    public string Platform { get; init; } = "local";
    public string ProjectId { get; init; } = "";
    public List<PackFileReference> Files { get; init; } = new();
    public string OverridesPrefix { get; init; } = "";
}

public static class MinecraftPackManifestReader
{
    private const int MaxManifestBytes = 4 * 1024 * 1024;
    private const int MaxFiles = 5000;

    public static MinecraftPackDescriptor Read(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);

        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);

        var mrpack = archive.GetEntry("modrinth.index.json");
        if (mrpack != null)
            return ReadModrinth(mrpack);

        var curseForge = archive.GetEntry("manifest.json");
        if (curseForge != null)
            return ReadCurseForge(curseForge);

        throw new InvalidDataException("В архиве не найден modrinth.index.json или manifest.json. Нужен экспорт сборки, а не произвольный ZIP.");
    }

    private static MinecraftPackDescriptor ReadModrinth(ZipArchiveEntry entry)
    {
        if (entry.Length <= 0 || entry.Length > MaxManifestBytes)
            throw new InvalidDataException("Файл modrinth.index.json имеет недопустимый размер.");

        using var stream = entry.Open();
        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 48 });
        var root = json.RootElement;

        if (GetInt(root, "formatVersion") != 1)
            throw new InvalidDataException("Не поддерживается версия формата Modrinth modpack.");

        if (!string.Equals(GetString(root, "game"), "minecraft", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Архив не относится к Minecraft.");

        if (!root.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("В Modrinth manifest отсутствуют dependencies.");

        var gameVersion = GetString(dependencies, "minecraft");
        if (string.IsNullOrWhiteSpace(gameVersion))
            throw new InvalidDataException("В Modrinth manifest не указана версия Minecraft.");

        var loader = "vanilla";
        var loaderVersion = "";
        foreach (var candidate in new[] { "forge", "neoforge", "fabric-loader", "quilt-loader" })
        {
            var value = GetString(dependencies, candidate);
            if (!string.IsNullOrWhiteSpace(value))
            {
                loader = candidate switch
                {
                    "fabric-loader" => "fabric",
                    "quilt-loader" => "quilt",
                    _ => candidate
                };
                loaderVersion = value;
                break;
            }
        }

        var files = new List<PackFileReference>();
        if (root.TryGetProperty("files", out var fileArray))
        {
            if (fileArray.ValueKind != JsonValueKind.Array || fileArray.GetArrayLength() > MaxFiles)
                throw new InvalidDataException("В Modrinth manifest слишком много файлов.");

            foreach (var item in fileArray.EnumerateArray())
            {
                var path = GetString(item, "path");
                ValidateRelativePath(path);

                var environment = item.TryGetProperty("env", out var env) ? env : default;
                if (environment.ValueKind == JsonValueKind.Object)
                {
                    var client = GetString(environment, "client");
                    if (string.Equals(client, "unsupported", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var downloads = item.TryGetProperty("downloads", out var urls) && urls.ValueKind == JsonValueKind.Array
                    ? urls.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                    : new List<string?>();

                var hash = item.TryGetProperty("hashes", out var hashes) ? GetString(hashes, "sha512") : null;
                long? size = item.TryGetProperty("fileSize", out var sizeElement) && sizeElement.TryGetInt64(out var length)
                    ? length
                    : null;

                files.Add(new PackFileReference(
                    path!,
                    downloads.FirstOrDefault(),
                    hash,
                    item.TryGetProperty("hashes", out hashes) ? GetString(hashes, "sha1") : null,
                    size,
                    null,
                    null,
                    true));
            }
        }

        return new MinecraftPackDescriptor
        {
            Name = SafeName(GetString(root, "name"), "Modrinth-сборка"),
            Summary = GetString(root, "summary") ?? "",
            MinecraftVersion = gameVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            Platform = "modrinth",
            ProjectId = GetString(root, "versionId") ?? "",
            Files = files,
            OverridesPrefix = "overrides/"
        };
    }

    private static MinecraftPackDescriptor ReadCurseForge(ZipArchiveEntry entry)
    {
        if (entry.Length <= 0 || entry.Length > MaxManifestBytes)
            throw new InvalidDataException("Файл manifest.json имеет недопустимый размер.");

        using var stream = entry.Open();
        using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 48 });
        var root = json.RootElement;

        if (!string.Equals(GetString(root, "manifestType"), "minecraftModpack", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Этот ZIP не является Minecraft modpack-экспортом CurseForge.");

        if (!root.TryGetProperty("minecraft", out var minecraft) || minecraft.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("В CurseForge manifest отсутствует раздел minecraft.");

        var gameVersion = GetString(minecraft, "version");
        if (string.IsNullOrWhiteSpace(gameVersion))
            throw new InvalidDataException("В CurseForge manifest не указана версия Minecraft.");

        var loader = "vanilla";
        var loaderVersion = "";
        if (minecraft.TryGetProperty("modLoaders", out var loaders) && loaders.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in loaders.EnumerateArray())
            {
                var id = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var prefix = id.Split('-', 2)[0].ToLowerInvariant();
                if (prefix is "forge" or "fabric" or "neoforge" or "quilt")
                {
                    loader = prefix;
                    loaderVersion = id[(prefix.Length + 1)..];
                    break;
                }
            }
        }

        var files = new List<PackFileReference>();
        if (root.TryGetProperty("files", out var fileArray))
        {
            if (fileArray.ValueKind != JsonValueKind.Array || fileArray.GetArrayLength() > MaxFiles)
                throw new InvalidDataException("В CurseForge manifest слишком много файлов.");

            foreach (var item in fileArray.EnumerateArray())
            {
                var projectId = GetLong(item, "projectID");
                var fileId = GetLong(item, "fileID");
                if (projectId is null or <= 0 || fileId is null or <= 0)
                    throw new InvalidDataException("В CurseForge manifest обнаружена некорректная запись мода.");

                var required = !item.TryGetProperty("required", out var requiredElement) ||
                               requiredElement.ValueKind != JsonValueKind.False;

                files.Add(new PackFileReference(
                    Path.Combine("mods", projectId.Value.ToString() + "-" + fileId.Value.ToString() + ".jar").Replace(Path.DirectorySeparatorChar, '/'),
                    null,
                    null,
                    null,
                    null,
                    projectId,
                    fileId,
                    required));
            }
        }

        return new MinecraftPackDescriptor
        {
            Name = SafeName(GetString(root, "name"), "CurseForge-сборка"),
            Summary = GetString(root, "version") ?? "",
            MinecraftVersion = gameVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            Platform = "curseforge",
            ProjectId = GetString(root, "author") ?? "",
            Files = files.Where(f => f.Required).ToList(),
            OverridesPrefix = "overrides/"
        };
    }

    public static string SafeDestination(string rootDirectory, string relativePath)
    {
        ValidateRelativePath(relativePath);
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Путь файла выходит за пределы папки сборки.");
        return destination;
    }

    private static void ValidateRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Contains('\\') ||
            path.Contains('\0') ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            Path.IsPathRooted(path) ||
            (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'))
            throw new InvalidDataException("В манифесте обнаружен небезопасный путь.");

        var parts = path.Split('/');
        if (parts.Any(p => p is "" or "." or ".." || p.Contains(':')))
            throw new InvalidDataException("В манифесте обнаружен путь с выходом за пределы сборки.");
    }

    private static string SafeName(string? value, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '-');
        return name.Length > 60 ? name[..60] : name;
    }

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.TryGetInt32(out var number)
            ? number
            : -1;

    private static long? GetLong(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.TryGetInt64(out var number)
            ? number
            : null;
}
