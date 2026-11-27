using System.IO.Compression;
using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;

namespace NovaCraft.Services;

public sealed record PackImportProgress(
    string Stage,
    int CompletedFiles,
    int TotalFiles,
    string CurrentFile,
    double FileProgress);

public static class MinecraftPackImportService
{
    private const long MaxModFileBytes = 250L * 1024 * 1024;
    private const long MaxOverrideFileBytes = 256L * 1024 * 1024;
    private const long MaxOverrideTotalBytes = 1024L * 1024 * 1024;
    private const int MaxOverrideEntries = 20_000;

    private static readonly HashSet<string> AllowedModrinthExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jar", ".zip", ".toml", ".json", ".json5", ".cfg", ".txt", ".properties",
        ".png", ".jpg", ".jpeg", ".yml", ".yaml", ".mcmeta", ".dat", ".snbt",
        ".xml", ".js", ".mjs", ".ogg", ".lang", ".ini", ".shader"
    };

    public static async Task ImportAsync(
        string archivePath,
        BuildProfile profile,
        HttpClient http,
        string? curseForgeApiKey,
        IProgress<PackImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(http);

        var descriptor = MinecraftPackManifestReader.Read(archivePath);
        profile.Name = descriptor.Name;
        profile.MinecraftVersion = descriptor.MinecraftVersion;
        profile.Loader = descriptor.Loader;
        profile.LoaderVersion = descriptor.LoaderVersion;
        profile.SourcePlatform = descriptor.Platform;
        profile.SourceProjectId = descriptor.ProjectId;

        if (descriptor.Loader == "neoforge")
            throw new NotSupportedException("Этот импорт определил NeoForge. Для него нужен отдельный проверенный установщик; NovaCraft не будет подменять его на Forge.");

        if (descriptor.Platform == "curseforge" && string.IsNullOrWhiteSpace(curseForgeApiKey))
            throw new InvalidOperationException("Для загрузки файлов CurseForge нужен персональный API-ключ. Ключ используется только в памяти и не записывается в настройки.");

        Directory.CreateDirectory(profile.GameDirectory);

        using var packStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var packArchive = new ZipArchive(packStream, ZipArchiveMode.Read);

        var requiredFiles = descriptor.Files.Where(f => f.Required).ToList();
        var total = requiredFiles.Count;
        var completed = 0;

        progress?.Report(new PackImportProgress("Подготовка файлов сборки", completed, total, "", 0));

        if (descriptor.Platform == "modrinth")
        {
            foreach (var item in requiredFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = MinecraftPackManifestReader.SafeDestination(profile.GameDirectory, item.RelativePath);
                var extension = Path.GetExtension(destination);
                if (!AllowedModrinthExtensions.Contains(extension))
                    throw new InvalidDataException("Сборка содержит неподдерживаемый тип файла: " + extension);
                if (string.IsNullOrWhiteSpace(item.DownloadUrl))
                    throw new InvalidDataException("В манифесте Modrinth отсутствует ссылка для " + item.RelativePath);
                if (string.IsNullOrWhiteSpace(item.Sha512) || item.Sha512.Length != 128 || !item.Sha512.All(Uri.IsHexDigit))
                    throw new InvalidDataException("В манифесте Modrinth отсутствует корректный SHA-512 для " + item.RelativePath);
                if (item.FileSize is < 0 or > MaxModFileBytes)
                    throw new InvalidDataException("Недопустимый размер файла в сборке: " + item.RelativePath);

                if (!Uri.TryCreate(item.DownloadUrl, UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps ||
                    !string.Equals(uri.Host, "cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
                    throw new SecurityException("NovaCraft разрешает импорт Modrinth только с HTTPS cdn.modrinth.com. Заблокирован файл: " + item.RelativePath);

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (File.Exists(destination))
                    throw new IOException("Файл уже существует в этой новой сборке: " + item.RelativePath);

                var fileProgress = new Progress<double>(value =>
                    progress?.Report(new PackImportProgress("Загрузка Modrinth", completed, total, Path.GetFileName(destination), value)));

                await DownloadService.DownloadVerifiedAsync(
                    http,
                    uri,
                    item.Sha512,
                    Path.GetDirectoryName(destination)!,
                    Path.GetFileName(destination),
                    new[] { extension },
                    MaxModFileBytes,
                    fileProgress,
                    cancellationToken);

                completed++;
                progress?.Report(new PackImportProgress("Файл проверен и установлен", completed, total, Path.GetFileName(destination), 1));
            }
        }
        else if (descriptor.Platform == "curseforge")
        {
            using var curseForgeHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromMinutes(3)
            };

            foreach (var item in requiredFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.CurseForgeProjectId is not long projectId || item.CurseForgeFileId is not long fileId)
                    throw new InvalidDataException("В CurseForge manifest отсутствует ID проекта или файла.");

                progress?.Report(new PackImportProgress("Получение метаданных CurseForge", completed, total, projectId + " / " + fileId, 0));
                var metadata = await GetCurseForgeFileAsync(curseForgeHttp, projectId, fileId, curseForgeApiKey!, cancellationToken);
                var fileName = metadata.FileName;
                if (string.IsNullOrWhiteSpace(fileName) ||
                    !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
                    !fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("CurseForge вернул некорректное имя JAR для " + projectId + " / " + fileId);
                if (string.IsNullOrWhiteSpace(metadata.DownloadUrl))
                    throw new InvalidOperationException("CurseForge не разрешил стороннюю загрузку файла " + fileName + ". Он пропущен политикой автора или недоступен для этой программы.");
                if (string.IsNullOrWhiteSpace(metadata.Sha1) || metadata.Sha1.Length != 40 || !metadata.Sha1.All(Uri.IsHexDigit))
                    throw new InvalidDataException("CurseForge не предоставил корректный SHA-1 для " + fileName);
                if (metadata.FileSize is < 0 or > MaxModFileBytes)
                    throw new InvalidDataException("Недопустимый размер файла CurseForge: " + fileName);

                var modsDirectory = Path.Combine(profile.GameDirectory, "mods");
                Directory.CreateDirectory(modsDirectory);
                var destination = MinecraftPackManifestReader.SafeDestination(profile.GameDirectory, "mods/" + fileName);
                if (File.Exists(destination))
                    throw new IOException("Файл уже существует в этой сборке: " + fileName);

                var fileProgress = new Progress<double>(value =>
                    progress?.Report(new PackImportProgress("Загрузка CurseForge", completed, total, fileName, value)));

                await DownloadCurseForgeFileAsync(
                    curseForgeHttp,
                    metadata.DownloadUrl,
                    curseForgeApiKey!,
                    metadata.Sha1,
                    destination,
                    fileProgress,
                    cancellationToken);

                completed++;
                progress?.Report(new PackImportProgress("Файл проверен и установлен", completed, total, fileName, 1));
            }
        }
        else
        {
            throw new InvalidDataException("Неизвестный источник сборки.");
        }

        progress?.Report(new PackImportProgress("Установка конфигураций и файлов overrides", completed, total, "", 0));
        await ApplyOverridesAsync(packArchive, profile.GameDirectory, descriptor.OverridesPrefix, cancellationToken);

        if (descriptor.Platform == "modrinth")
            await ApplyOverridesAsync(packArchive, profile.GameDirectory, "client-overrides/", cancellationToken);

        progress?.Report(new PackImportProgress("Сборка импортирована", total, total, "", 1));
    }

    private sealed record CurseForgeFileMetadata(string FileName, string DownloadUrl, string Sha1, long? FileSize);

    private static async Task<CurseForgeFileMetadata> GetCurseForgeFileAsync(
        HttpClient http,
        long projectId,
        long fileId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url = new Uri($"https://api.curseforge.com/v1/mods/{projectId}/files/{fileId}");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new SecurityException("CurseForge API вернул перенаправление. Запрос остановлен, чтобы не передать API-ключ другому сайту.");
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("CurseForge API вернул неожиданный ответ.");

        var fileName = data.TryGetProperty("fileName", out var nameElement) ? nameElement.GetString() ?? "" : "";
        var downloadUrl = data.TryGetProperty("downloadUrl", out var urlElement) ? urlElement.GetString() ?? "" : "";
        long? length = data.TryGetProperty("fileLength", out var lengthElement) && lengthElement.TryGetInt64(out var parsedLength)
            ? parsedLength
            : null;

        var sha1 = "";
        if (data.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Array)
        {
            foreach (var hash in hashes.EnumerateArray())
            {
                if (hash.TryGetProperty("algo", out var algo) && algo.TryGetInt32(out var algorithm) && algorithm == 1)
                {
                    sha1 = hash.TryGetProperty("value", out var value) ? value.GetString() ?? "" : "";
                    break;
                }
            }
        }

        return new CurseForgeFileMetadata(fileName, downloadUrl, sha1, length);
    }

    private static async Task DownloadCurseForgeFileAsync(
        HttpClient http,
        string downloadUrl,
        string apiKey,
        string expectedSha1,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !IsCurseForgeCdn(uri.Host))
            throw new SecurityException("Заблокирован небезопасный адрес загрузки CurseForge.");

        var temporary = destination + ".download";
        try
        {
            using var response = await SendCurseForgeDownloadAsync(http, uri, apiKey, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > MaxModFileBytes))
                throw new InvalidDataException("CurseForge-файл имеет недопустимый размер.");

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            var buffer = new byte[128 * 1024];
            long total = 0;
            var lastPercent = -1;

            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > MaxModFileBytes)
                    throw new InvalidDataException("CurseForge-файл превышает лимит 250 МБ.");

                sha1.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                if (response.Content.Headers.ContentLength is long declared && declared > 0)
                {
                    var percent = (int)Math.Clamp(total * 100 / declared, 0, 100);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(percent / 100d);
                    }
                }
            }

            await output.FlushAsync(cancellationToken);
            var actual = Convert.ToHexString(sha1.GetHashAndReset());
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expectedSha1)))
                throw new CryptographicException("SHA-1 CurseForge не совпадает. Файл отклонён.");

            await ValidateJarSignatureAsync(temporary, cancellationToken);
            File.Move(temporary, destination, false);
            progress?.Report(1d);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static async Task<HttpResponseMessage> SendCurseForgeDownloadAsync(
        HttpClient http,
        Uri initialUri,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var current = initialUri;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (current.Scheme != Uri.UriSchemeHttps || !IsCurseForgeCdn(current.Host))
                throw new SecurityException("Перенаправление CurseForge вышло за пределы разрешённого CDN.");

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location == null)
                    throw new HttpRequestException("CurseForge CDN вернул пустое перенаправление.");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            return response;
        }

        throw new HttpRequestException("Слишком много перенаправлений CurseForge CDN.");
    }

    private static bool IsCurseForgeCdn(string host)
        => host.Equals("forgecdn.net", StringComparison.OrdinalIgnoreCase) ||
           host.EndsWith(".forgecdn.net", StringComparison.OrdinalIgnoreCase);

    private static async Task ValidateJarSignatureAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var signature = new byte[4];
        var read = await stream.ReadAsync(signature.AsMemory(0, 4), cancellationToken);
        if (read != 4 ||
            signature[0] != (byte)'P' ||
            signature[1] != (byte)'K' ||
            !((signature[2] == 3 && signature[3] == 4) ||
              (signature[2] == 5 && signature[3] == 6) ||
              (signature[2] == 7 && signature[3] == 8)))
            throw new InvalidDataException("Файл CurseForge не имеет корректной ZIP/JAR сигнатуры.");
    }

    private static async Task ApplyOverridesAsync(
        ZipArchive archive,
        string destinationRoot,
        string prefix,
        CancellationToken cancellationToken)
    {
        var entries = archive.Entries
            .Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        if (entries.Count > MaxOverrideEntries)
            throw new InvalidDataException("В overrides слишком много файлов.");

        long totalBytes = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = entry.FullName[prefix.Length..];
            if (string.IsNullOrEmpty(relativePath) || relativePath.EndsWith("/", StringComparison.Ordinal))
                continue;

            ValidateOverridePath(relativePath);

            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000)
                throw new InvalidDataException("Символическая ссылка в overrides запрещена.");

            if (entry.Length < 0 || entry.Length > MaxOverrideFileBytes)
                throw new InvalidDataException("Один файл overrides превышает 256 МБ.");

            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaxOverrideTotalBytes)
                throw new InvalidDataException("Общий размер overrides превышает 1 ГБ.");

            var destination = MinecraftPackManifestReader.SafeDestination(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".novacraft-import";

            try
            {
                await using var input = entry.Open();
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                File.Move(temporary, destination, true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }

    private static void ValidateOverridePath(string path)
    {
        if (path.Contains('\\') || path.Contains('\0') || path.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(path) || path.Split('/').Any(s => s is "" or "." or ".." || s.Contains(':')))
            throw new InvalidDataException("В архиве overrides обнаружен небезопасный путь.");
    }
}
