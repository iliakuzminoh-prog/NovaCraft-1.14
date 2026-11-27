using System.IO.Compression;
using System.Text.Json;

namespace NovaCraft.Services;

/// <summary>
/// Validates Modrinth ZIP packages before they are moved into a Minecraft directory.
/// Archives are inspected in place and are never extracted by NovaCraft.
/// </summary>
public static class CatalogArchiveValidator
{
    private const int MaxEntries = 20_000;
    private const long MaxExpandedBytes = 1_073_741_824;
    private const long MaxSingleFileBytes = 268_435_456;
    private const long MaxMetadataBytes = 1_048_576;

    public static void Validate(string archivePath, string projectType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectType);

        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);

        if (archive.Entries.Count is 0 or > MaxEntries)
            throw new InvalidDataException($"Архив содержит недопустимое количество элементов (максимум {MaxEntries}).");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasPackMetadata = false;
        var hasAssets = false;
        var hasData = false;
        var hasShaders = false;
        long expandedBytes = 0;

        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            ValidateEntryName(name);

            if (!names.Add(name))
                throw new InvalidDataException("В архиве обнаружены повторяющиеся пути: " + name);

            // Unix symlinks are represented in the upper 16 bits of ExternalAttributes.
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000)
                throw new InvalidDataException("Символические ссылки в архивах не поддерживаются.");

            if (entry.Length < 0 || entry.Length > MaxSingleFileBytes)
                throw new InvalidDataException("Один из файлов архива превышает допустимый размер 256 МБ.");

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaxExpandedBytes)
                throw new InvalidDataException("Распакованный размер архива превышает 1 ГБ.");

            if (entry.Length > 0 && entry.CompressedLength == 0)
                throw new InvalidDataException("Некорректный ZIP: размер сжатого файла равен нулю.");

            if (entry.CompressedLength > 0 &&
                entry.Length > 64L * 1024 * 1024 &&
                entry.Length / Math.Max(1L, entry.CompressedLength) > 500)
                throw new InvalidDataException("Архив имеет подозрительно высокий коэффициент сжатия.");

            var normalized = name.TrimEnd('/');
            if (normalized.Equals("pack.mcmeta", StringComparison.Ordinal))
            {
                if (entry.Length > MaxMetadataBytes)
                    throw new InvalidDataException("Файл pack.mcmeta слишком большой.");

                ValidatePackMetadata(entry);
                hasPackMetadata = true;
            }

            hasAssets |= name.StartsWith("assets/", StringComparison.Ordinal);
            hasData |= name.StartsWith("data/", StringComparison.Ordinal);
            hasShaders |= name.StartsWith("shaders/", StringComparison.Ordinal);
        }

        switch (projectType)
        {
            case "resourcepack":
                if (!hasPackMetadata || !hasAssets)
                    throw new InvalidDataException("Это не похоже на корректный ресурс-пак: в корне нужны pack.mcmeta и папка assets/.");
                break;
            case "datapack":
                if (!hasPackMetadata || !hasData)
                    throw new InvalidDataException("Это не похоже на корректный дата-пак: в корне нужны pack.mcmeta и папка data/.");
                break;
            case "shader":
                if (!hasShaders)
                    throw new InvalidDataException("В архиве шейдера не найдена корневая папка shaders/. Проверь ZIP-файл на Modrinth.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(projectType), projectType, "Неизвестный тип каталога.");
        }
    }

    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Contains('\\') ||
            name.Contains('\0') ||
            name.StartsWith("/", StringComparison.Ordinal) ||
            name.StartsWith("//", StringComparison.Ordinal) ||
            Path.IsPathRooted(name) ||
            (name.Length >= 2 && char.IsAsciiLetter(name[0]) && name[1] == ':'))
            throw new InvalidDataException("Архив содержит небезопасный путь.");

        var segments = name.Split('/');
        if (segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
            throw new InvalidDataException("Архив содержит путь с переходом за пределы папки.");
    }

    private static void ValidatePackMetadata(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("pack", out var pack) ||
            pack.ValueKind != JsonValueKind.Object ||
            !pack.TryGetProperty("pack_format", out var format) ||
            format.ValueKind != JsonValueKind.Number ||
            !pack.TryGetProperty("description", out _))
            throw new InvalidDataException("Файл pack.mcmeta не содержит обязательные поля pack_format и description.");
    }
}
