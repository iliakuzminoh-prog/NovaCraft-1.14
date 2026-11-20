using System.Security;
using System.Security.Cryptography;

namespace NovaCraft.Services;

public static class DownloadService
{
    public static async Task<string> DownloadVerifiedAsync(
        HttpClient http,
        Uri uri,
        string expectedSha512Hex,
        string destinationDir,
        string suggestedFileName,
        string[] allowedExtensions,
        long maxBytes,
        IProgress<double>? progress,
        CancellationToken ct,
        Func<string, bool>? confirm = null)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new SecurityException("Разрешены только HTTPS-загрузки.");

        if (!string.Equals(uri.Host, "cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
            throw new SecurityException("Загрузка разрешена только с cdn.modrinth.com.");

        if (string.IsNullOrWhiteSpace(expectedSha512Hex) ||
            expectedSha512Hex.Length != 128 ||
            !expectedSha512Hex.All(Uri.IsHexDigit))
            throw new InvalidDataException("Некорректный SHA-512.");

        var fileName = Path.GetFileName(suggestedFileName);
        if (!string.Equals(fileName, suggestedFileName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(fileName) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Некорректное имя загружаемого файла.");

        var extension = Path.GetExtension(fileName);
        if (!allowedExtensions.Any(x => string.Equals(x, extension, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Недопустимое расширение файла.");

        Directory.CreateDirectory(destinationDir);
        var fullDir = Path.GetFullPath(destinationDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(destinationDir, fileName));
        if (!destination.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase))
            throw new SecurityException("Путь загрузки выходит за пределы целевой папки.");

        if (File.Exists(destination))
            throw new IOException("Файл уже установлен: " + fileName);

        if (confirm != null && !confirm(fileName))
            throw new OperationCanceledException("Установка отменена пользователем.");

        var temporary = destination + ".download";
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long length && length > maxBytes)
                throw new InvalidDataException($"Файл превышает лимит {maxBytes / (1024 * 1024)} МБ.");

            using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);

            // The write stream must be closed before the file is re-opened for signature
            // validation and moved; a still-open FileShare.None handle makes both fail on Windows.
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = new FileStream(
                temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                var lastReport = -1;

                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read == 0) break;

                    total += read;
                    if (total > maxBytes)
                        throw new InvalidDataException($"Файл превышает лимит {maxBytes / (1024 * 1024)} МБ.");

                    sha512.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);

                    if (response.Content.Headers.ContentLength is long declared && declared > 0)
                    {
                        var current = (int)(total * 100 / declared);
                        if (current != lastReport)
                        {
                            lastReport = current;
                            progress?.Report(Math.Clamp(current / 100d, 0d, 1d));
                        }
                    }
                }

                await output.FlushAsync(ct);
            }

            progress?.Report(1d);

            var actual = Convert.ToHexString(sha512.GetHashAndReset());
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actual),
                    Convert.FromHexString(expectedSha512Hex)))
                throw new CryptographicException("SHA-512 не совпадает с метаданными Modrinth.");

            await ValidateArchiveSignatureAsync(temporary, extension, ct);
            File.Move(temporary, destination, false);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    static async Task ValidateArchiveSignatureAsync(string path, string extension, CancellationToken ct)
    {
        if (!extension.Equals(".jar", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var signature = new byte[4];
        var read = await stream.ReadAsync(signature.AsMemory(0, 4), ct);
        if (read != 4 ||
            signature[0] != (byte)'P' ||
            signature[1] != (byte)'K' ||
            !((signature[2] == 3 && signature[3] == 4) ||
              (signature[2] == 5 && signature[3] == 6) ||
              (signature[2] == 7 && signature[3] == 8)))
            throw new InvalidDataException("Файл не имеет корректной ZIP/JAR сигнатуры.");
    }
}
