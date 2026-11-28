using System.IO.Compression;

namespace NovaCraft.Services;

internal sealed class BuildCopyOptions
{
    public bool IncludeMods { get; init; } = true;
    public bool IncludeConfig { get; init; } = true;
    public bool IncludeWorlds { get; init; }
    public bool IncludeGameSettings { get; init; } = true;
    public bool IncludeResourcePacks { get; init; } = true;
    public bool IncludeShaderPacks { get; init; } = true;
    public bool IncludeScreenshots { get; init; }

    public bool HasSelection =>
        IncludeMods || IncludeConfig || IncludeWorlds || IncludeGameSettings ||
        IncludeResourcePacks || IncludeShaderPacks || IncludeScreenshots;
}

internal sealed record BuildCopyProgress(
    int CompletedFiles,
    int TotalFiles,
    long CopiedBytes,
    long TotalBytes,
    string CurrentItem);

internal sealed record BuildCopyResult(int FilesCopied, long BytesCopied);

internal sealed record WorldBackupResult(string ArchivePath, int FilesCopied, long BytesCopied);

internal static class BuildCopyService
{
    private const int BufferSize = 128 * 1024;

    public static BuildCopyResult CopySelectedContent(
        string sourceDirectory,
        string destinationDirectory,
        BuildCopyOptions options,
        IProgress<BuildCopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.HasSelection)
            throw new InvalidOperationException("Выбери хотя бы одну категорию данных для копирования.");

        var sourceRoot = NormalizeDirectoryPath(sourceDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory);

        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException("Папка исходной сборки не найдена: " + sourceRoot);

        EnsureNoReparsePointAncestors(sourceRoot);
        RejectOverlappingPaths(sourceRoot, destinationRoot);

        var files = new List<CopyItem>();
        AddDirectory("mods", options.IncludeMods);
        AddDirectory("config", options.IncludeConfig);
        AddDirectory("saves", options.IncludeWorlds);
        AddDirectory("resourcepacks", options.IncludeResourcePacks);
        AddDirectory("shaderpacks", options.IncludeShaderPacks);
        AddDirectory("screenshots", options.IncludeScreenshots);
        AddRootFile("options.txt", options.IncludeGameSettings);
        AddRootFile("servers.dat", options.IncludeGameSettings);

        if (files.Count == 0)
            throw new InvalidOperationException("В выбранных категориях нет файлов для копирования.");

        var totalBytes = files.Sum(item => new FileInfo(item.SourcePath).Length);
        var parent = Path.GetDirectoryName(destinationRoot);
        if (string.IsNullOrWhiteSpace(parent))
            throw new InvalidOperationException("Не удалось определить папку для новой сборки.");

        Directory.CreateDirectory(parent);
        EnsureNoReparsePointAncestors(parent);

        if (Directory.Exists(destinationRoot) || File.Exists(destinationRoot))
            throw new IOException("Папка назначения уже существует. Исходные данные не перезаписаны.");

        var stagingDirectory = destinationRoot + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagingDirectory);

        var copiedBytes = 0L;
        var completedFiles = 0;

        try
        {
            foreach (var item in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoReparsePointAncestors(item.SourcePath);

                var targetPath = Path.GetFullPath(Path.Combine(stagingDirectory, item.RelativePath));
                if (!IsSameOrWithin(stagingDirectory, targetPath))
                    throw new InvalidDataException("Путь копируемого файла выходит за пределы новой сборки.");

                var targetDirectory = Path.GetDirectoryName(targetPath)
                    ?? throw new InvalidDataException("Не удалось определить папку назначения файла.");

                Directory.CreateDirectory(targetDirectory);
                EnsureNoReparsePointAncestors(targetDirectory);

                copiedBytes += CopyFile(item.SourcePath, targetPath, cancellationToken);
                completedFiles++;

                progress?.Report(new BuildCopyProgress(
                    completedFiles,
                    files.Count,
                    copiedBytes,
                    totalBytes,
                    item.RelativePath));
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingDirectory, destinationRoot);
            return new BuildCopyResult(completedFiles, copiedBytes);
        }
        catch
        {
            TryDeleteTree(stagingDirectory);
            throw;
        }

        void AddDirectory(string directoryName, bool include)
        {
            if (!include)
                return;

            var selectedDirectory = Path.Combine(sourceRoot, directoryName);
            if (!Directory.Exists(selectedDirectory))
                return;

            EnsureNoReparsePointAncestors(selectedDirectory);
            foreach (var file in EnumerateFilesSafely(selectedDirectory))
            {
                var relativeFile = Path.GetRelativePath(selectedDirectory, file);
                files.Add(new CopyItem(file, Path.Combine(directoryName, relativeFile)));
            }
        }

        void AddRootFile(string fileName, bool include)
        {
            if (!include)
                return;

            var file = Path.Combine(sourceRoot, fileName);
            if (!File.Exists(file))
                return;

            EnsureNoReparsePointAncestors(file);
            files.Add(new CopyItem(file, fileName));
        }
    }

    public static WorldBackupResult CreateWorldBackup(
        string gameDirectory,
        IProgress<BuildCopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        var gameRoot = NormalizeDirectoryPath(gameDirectory);
        if (!Directory.Exists(gameRoot))
            throw new DirectoryNotFoundException("Папка сборки не найдена: " + gameRoot);

        EnsureNoReparsePointAncestors(gameRoot);

        var savesDirectory = Path.Combine(gameRoot, "saves");
        if (!Directory.Exists(savesDirectory))
            throw new DirectoryNotFoundException("В этой сборке ещё нет папки миров saves.");

        EnsureNoReparsePointAncestors(savesDirectory);
        var files = EnumerateFilesSafely(savesDirectory);
        if (files.Count == 0)
            throw new InvalidOperationException("Папка saves пуста. Сохранять пока нечего.");

        var totalBytes = files.Sum(file => new FileInfo(file).Length);
        var backupDirectory = Path.Combine(gameRoot, "backups");
        Directory.CreateDirectory(backupDirectory);
        EnsureNoReparsePointAncestors(backupDirectory);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        var archivePath = Path.Combine(backupDirectory, "worlds_" + timestamp + ".zip");
        if (File.Exists(archivePath) || Directory.Exists(archivePath))
            archivePath = Path.Combine(backupDirectory, "worlds_" + timestamp + "_" + Guid.NewGuid().ToString("N") + ".zip");

        var temporaryArchive = Path.Combine(backupDirectory, ".worlds_" + Guid.NewGuid().ToString("N") + ".tmp");
        var copiedBytes = 0L;
        var completedFiles = 0;

        try
        {
            using (var output = new FileStream(
                temporaryArchive,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                BufferSize,
                FileOptions.SequentialScan))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureNoReparsePointAncestors(file);

                    var relative = Path.GetRelativePath(savesDirectory, file);
                    if (Path.IsPathRooted(relative) ||
                        relative == ".." ||
                        relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException("Путь мира выходит за пределы папки saves.");

                    var entryName = "saves/" + relative.Replace(Path.DirectorySeparatorChar, '/');
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                    using var input = new FileStream(
                        file,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        BufferSize,
                        FileOptions.SequentialScan);
                    using var entryStream = entry.Open();

                    var buffer = new byte[BufferSize];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        entryStream.Write(buffer, 0, read);
                        copiedBytes += read;
                    }

                    completedFiles++;
                    progress?.Report(new BuildCopyProgress(
                        completedFiles,
                        files.Count,
                        copiedBytes,
                        totalBytes,
                        relative));
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryArchive, archivePath, overwrite: false);
            return new WorldBackupResult(archivePath, completedFiles, copiedBytes);
        }
        catch
        {
            TryDeleteFile(temporaryArchive);
            throw;
        }
    }

    private static long CopyFile(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);
        using var output = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.SequentialScan);

        var buffer = new byte[BufferSize];
        var copiedBytes = 0L;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            copiedBytes += read;
        }

        return copiedBytes;
    }

    private static List<string> EnumerateFilesSafely(string rootDirectory)
    {
        EnsureNoReparsePointAncestors(rootDirectory);

        var files = new List<string>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(Path.GetFullPath(rootDirectory));

        while (pendingDirectories.Count > 0)
        {
            var current = pendingDirectories.Pop();
            EnsureNoReparsePointAncestors(current);

            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Копирование остановлено: обнаружена символическая ссылка или точка повторной обработки: " + entry);

                if ((attributes & FileAttributes.Directory) != 0)
                    pendingDirectories.Push(entry);
                else
                    files.Add(Path.GetFullPath(entry));
            }
        }

        return files;
    }

    private static void RejectOverlappingPaths(string sourceRoot, string destinationRoot)
    {
        if (IsSameOrWithin(sourceRoot, destinationRoot) || IsSameOrWithin(destinationRoot, sourceRoot))
            throw new InvalidOperationException("Исходная папка и папка копии не должны пересекаться.");
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

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var volumeRoot = Path.GetPathRoot(fullPath);
        if (!string.Equals(fullPath, volumeRoot, StringComparison.OrdinalIgnoreCase))
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath;
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
                    throw new InvalidDataException("Операция остановлена: путь содержит символическую ссылку или точку повторной обработки: " + current);
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;

            current = parent;
        }
    }

    private static void TryDeleteTree(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    if ((attributes & FileAttributes.Directory) != 0)
                        Directory.Delete(entry, recursive: false);
                    else
                        File.Delete(entry);
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    TryDeleteTree(entry);
                    if (Directory.Exists(entry))
                        Directory.Delete(entry, recursive: false);
                }
                else
                {
                    File.Delete(entry);
                }
            }

            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: false);
        }
        catch
        {
            // Cleanup must not hide the original copy/archive exception.
        }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        catch
        {
            // A temporary archive is not the source world data.
        }
    }

    private sealed record CopyItem(string SourcePath, string RelativePath);
}
