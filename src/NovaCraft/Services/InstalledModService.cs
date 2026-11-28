namespace NovaCraft.Services;

internal sealed record InstalledModEntry(string FileName, long SizeBytes, bool Enabled);

internal static class InstalledModService
{
    public static IReadOnlyList<InstalledModEntry> GetInstalledMods(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        var gameRoot = NormalizeDirectoryPath(gameDirectory);
        if (!Directory.Exists(gameRoot))
            return Array.Empty<InstalledModEntry>();

        EnsureNoReparsePointAncestors(gameRoot);
        var modsDirectory = Path.Combine(gameRoot, "mods");
        if (!Directory.Exists(modsDirectory))
            return Array.Empty<InstalledModEntry>();

        EnsureNoReparsePointAncestors(modsDirectory);
        var entries = new List<InstalledModEntry>();

        foreach (var path in Directory.EnumerateFileSystemEntries(modsDirectory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Просмотр модов остановлен: найден файл или папка-ссылка: " + path);

            if ((attributes & FileAttributes.Directory) != 0)
                continue;

            var fileName = Path.GetFileName(path);
            var isDisabled = fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
            var isEnabled = fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            if (!isEnabled && !isDisabled)
                continue;

            var info = new FileInfo(path);
            entries.Add(new InstalledModEntry(fileName, info.Length, isEnabled));
        }

        return entries
            .OrderBy(entry => entry.Enabled ? 0 : 1)
            .ThenBy(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static InstalledModEntry SetEnabled(string gameDirectory, string fileName, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ValidateModFileName(fileName);

        var gameRoot = NormalizeDirectoryPath(gameDirectory);
        if (!Directory.Exists(gameRoot))
            throw new DirectoryNotFoundException("Папка активной сборки не найдена.");

        EnsureNoReparsePointAncestors(gameRoot);
        var modsDirectory = Path.Combine(gameRoot, "mods");
        if (!Directory.Exists(modsDirectory))
            throw new DirectoryNotFoundException("В этой сборке ещё нет папки mods.");

        EnsureNoReparsePointAncestors(modsDirectory);

        var sourcePath = Path.GetFullPath(Path.Combine(modsDirectory, fileName));
        if (!IsSameOrWithin(modsDirectory, sourcePath))
            throw new InvalidDataException("Файл мода находится за пределами папки mods.");

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Файл мода больше не найден.", sourcePath);

        EnsureNoReparsePointAncestors(sourcePath);
        var attributes = File.GetAttributes(sourcePath);
        if ((attributes & FileAttributes.Directory) != 0)
            throw new InvalidDataException("Вместо JAR-файла обнаружена папка.");

        var currentlyDisabled = fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
        var currentlyEnabled = fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
        if (!currentlyDisabled && !currentlyEnabled)
            throw new InvalidDataException("Поддерживаются только JAR-файлы и файлы с суффиксом .jar.disabled.");

        if (currentlyEnabled == enabled)
            return new InstalledModEntry(fileName, new FileInfo(sourcePath).Length, enabled);

        var destinationName = enabled
            ? fileName.Substring(0, fileName.Length - ".disabled".Length)
            : fileName + ".disabled";
        var destinationPath = Path.GetFullPath(Path.Combine(modsDirectory, destinationName));

        if (!IsSameOrWithin(modsDirectory, destinationPath))
            throw new InvalidDataException("Новое имя файла выходит за пределы папки mods.");

        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            throw new IOException("Файл с целевым именем уже существует. Ничего не перезаписано.");

        File.Move(sourcePath, destinationPath, overwrite: false);
        var movedInfo = new FileInfo(destinationPath);
        return new InstalledModEntry(destinationName, movedInfo.Length, enabled);
    }

    private static void ValidateModFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            Path.IsPathRooted(fileName) ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            (!fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
             !fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Передано некорректное имя JAR-файла.");
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var volumeRoot = Path.GetPathRoot(fullPath);
        if (!string.Equals(fullPath, volumeRoot, StringComparison.OrdinalIgnoreCase))
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath;
    }

    private static bool IsSameOrWithin(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var normalizedCandidate = Path.GetFullPath(candidate);
        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootPrefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePointAncestors(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(current) || File.Exists(current))
            {
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Операция с модом остановлена: путь содержит ссылку или точку повторной обработки: " + current);
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
    }
}
