using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NovaCraft.Services;

internal sealed record NovaBoostInstallResult(string FilePath, string Version, string Sha256, long SizeBytes);

internal static class NovaBoostReleaseInstaller
{
    private const string ReleaseAssetBase =
        "https://github.com/iliakuzminoh-prog/NovaCraft-1.14/releases/download/novaboost-latest/";
    private const string JarFileName = "NovaBoost.jar";
    private const long MaximumJarSizeBytes = 25L * 1024L * 1024L;

    public static async Task<NovaBoostInstallResult> InstallAsync(
        HttpClient http,
        string gameDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);

        var gameRoot = Path.GetFullPath(gameDirectory);
        EnsureNoReparsePointAncestors(gameRoot);
        Directory.CreateDirectory(gameRoot);
        EnsureNoReparsePointAncestors(gameRoot);

        var modsDirectory = Path.Combine(gameRoot, "mods");
        EnsureNoReparsePointAncestors(modsDirectory);
        Directory.CreateDirectory(modsDirectory);
        EnsureNoReparsePointAncestors(modsDirectory);

        var destination = Path.Combine(modsDirectory, JarFileName);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("NovaBoost.jar уже существует. Файл не будет перезаписан.");

        foreach (var existingJar in Directory.EnumerateFiles(modsDirectory, "*.jar", SearchOption.TopDirectoryOnly))
        {
            var attributes = File.GetAttributes(existingJar);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("В папке mods найден файл-ссылка. Установка остановлена.");

            if (string.Equals(ReadFabricModId(existingJar), "novaboost", StringComparison.OrdinalIgnoreCase))
                throw new IOException("В папке mods уже найден NovaBoost. Установщик не заменяет и не отключает существующие моды.");
        }

        // Staging stays on the same volume as the destination, allowing a final rename
        // without exposing a partially downloaded .jar to Fabric.
        var staging = Path.Combine(modsDirectory, ".novaboost-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var expectedHash = await DownloadExpectedHashAsync(http, cancellationToken).ConfigureAwait(false);
            var (actualHash, size) = await DownloadJarAsync(http, staging, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedHash),
                    Convert.FromHexString(actualHash)))
            {
                throw new InvalidDataException("SHA-256 NovaBoost.jar не совпал с опубликованной контрольной суммой.");
            }

            var manifest = ReadManifest(staging);
            var version = ValidateManifest(manifest);

            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException("NovaBoost.jar появился во время загрузки. Ничего не перезаписано.");

            File.Move(staging, destination, overwrite: false);
            return new NovaBoostInstallResult(destination, version, actualHash, size);
        }
        finally
        {
            try
            {
                if (File.Exists(staging))
                    File.Delete(staging);
            }
            catch
            {
                // A failed cleanup must not hide the original installation error.
            }
        }
    }

    internal static string ValidateManifest(string manifestJson)
    {
        using var document = JsonDocument.Parse(manifestJson);
        var root = document.RootElement;

        var id = RequiredString(root, "id");
        var version = RequiredString(root, "version");
        var environment = RequiredString(root, "environment");

        if (!string.Equals(id, "novaboost", StringComparison.Ordinal))
            throw new InvalidDataException("У загруженного JAR неверный Fabric mod ID.");

        if (!string.Equals(environment, "client", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("NovaBoost должен быть помечен как client-only мод.");

        if (!root.TryGetProperty("depends", out var dependencies) ||
            dependencies.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("В fabric.mod.json отсутствует блок зависимостей.");

        var minecraft = RequiredString(dependencies, "minecraft");
        var loader = RequiredString(dependencies, "fabricloader");
        var java = RequiredString(dependencies, "java");

        if (!string.Equals(minecraft, "~1.21.11", StringComparison.Ordinal))
            throw new InvalidDataException("JAR собран не для Minecraft 1.21.11.");

        if (!string.Equals(loader, ">=0.20.1", StringComparison.Ordinal))
            throw new InvalidDataException("JAR требует неожиданную версию Fabric Loader.");

        if (!string.Equals(java, ">=21", StringComparison.Ordinal))
            throw new InvalidDataException("JAR требует неожиданную версию Java.");

        return version;
    }

    internal static string ParseSha256(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"\A\s*([a-fA-F0-9]{64})(?:\s+.*)?\s*\z");
        if (!match.Success)
            throw new InvalidDataException("Файл контрольной суммы имеет неверный формат.");

        return match.Groups[1].Value.ToLowerInvariant();
    }

    private static async Task<string> DownloadExpectedHashAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            ReleaseAssetBase + JarFileName + ".sha256",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        EnsureSecureSuccessfulResponse(response, "контрольную сумму");
        if (response.Content.Headers.ContentLength is > 1024)
            throw new InvalidDataException("Файл контрольной суммы неожиданно большой.");

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (text.Length > 1024)
            throw new InvalidDataException("Файл контрольной суммы превышает допустимый размер.");

        return ParseSha256(text);
    }

    private static async Task<(string Hash, long Size)> DownloadJarAsync(
        HttpClient http,
        string destination,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
            ReleaseAssetBase + JarFileName,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        EnsureSecureSuccessfulResponse(response, "JAR-файл");
        if (response.Content.Headers.ContentLength is long declaredLength &&
            (declaredLength <= 0 || declaredLength > MaximumJarSizeBytes))
            throw new InvalidDataException("Размер NovaBoost.jar не соответствует допустимому диапазону.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            total += read;
            if (total > MaximumJarSizeBytes)
                throw new InvalidDataException("NovaBoost.jar превышает ограничение 25 МБ.");

            hash.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
            throw new InvalidDataException("Сервер вернул пустой JAR-файл.");

        return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), total);
    }

    private static void EnsureSecureSuccessfulResponse(HttpResponseMessage response, string description)
    {
        if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Загрузка остановлена: соединение не использует HTTPS.");

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new HttpRequestException(
                    "Опубликованная сборка NovaBoost пока не найдена. Проверь статус GitHub Actions и повтори установку после публикации.");
            throw new HttpRequestException(
                "Не удалось загрузить " + description + ": HTTP " + (int)response.StatusCode + ".");
        }
    }

    private static string ReadManifest(string jarPath)
    {
        using var archive = ZipFile.OpenRead(jarPath);
        var entry = archive.GetEntry("fabric.mod.json")
            ?? throw new InvalidDataException("В JAR отсутствует fabric.mod.json.");
        if (entry.Length > 1024 * 1024)
            throw new InvalidDataException("fabric.mod.json неожиданно большой.");

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? ReadFabricModId(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var entry = archive.GetEntry("fabric.mod.json");
            if (entry == null)
                return null;

            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("В fabric.mod.json отсутствует строковое поле '" + propertyName + "'.");

        var result = value.GetString();
        if (string.IsNullOrWhiteSpace(result))
            throw new InvalidDataException("Поле '" + propertyName + "' в fabric.mod.json пустое.");

        return result;
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
                    throw new InvalidDataException("Установка остановлена: путь содержит ссылку или точку повторной обработки: " + current);
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
    }
}
