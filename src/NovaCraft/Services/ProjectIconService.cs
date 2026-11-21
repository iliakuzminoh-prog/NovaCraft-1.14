using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace NovaCraft.Services;

/// <summary>
/// Загружает иконки проектов Modrinth: дисковый кэш, скачивание с ограничением размера,
/// декодирование. GDI+ не умеет WebP, поэтому такие иконки конвертируются через ImageSharp в PNG.
/// </summary>
internal static class ProjectIconService
{
    internal const int MaxDownloadBytes = 5 * 1024 * 1024;
    internal const int MaxDimension = 2048;

    static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NovaCraft", "cache", "icons");

    public static string CachePathFor(Uri uri)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri));
        return Path.Combine(CacheDirectory, Convert.ToHexString(hash, 0, 16).ToLowerInvariant() + ".icon");
    }

    public static async Task<Bitmap?> LoadAsync(HttpClient http, Uri uri, CancellationToken cancellationToken = default)
    {
        var cachePath = CachePathFor(uri);
        var cached = await TryReadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);
        if (cached is not null) return cached;

        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > MaxDownloadBytes))
            return null;

        var data = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (data.Length == 0) return null;

        var bitmap = DecodeWithGdi(data);
        if (bitmap is null)
        {
            data = ConvertToPng(data);
            bitmap = DecodeWithGdi(data)
                ?? throw new InvalidDataException("PNG после конвертации не читается GDI+.");
        }

        await TryWriteCacheAsync(cachePath, data, cancellationToken).ConfigureAwait(false);
        return bitmap;
    }

    static async Task<Bitmap?> TryReadCacheAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return DecodeWithGdi(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static async Task TryWriteCacheAsync(string path, byte[] data, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var temp = path + "." + Environment.ProcessId + ".tmp";
            await File.WriteAllBytesAsync(temp, data, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Кэш — только ускорение. Если записать нельзя, иконка всё равно показывается.
        }
    }

    static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > MaxDownloadBytes)
                throw new InvalidDataException("Иконка превышает лимит размера.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return output.ToArray();
    }

    /// <summary>Возвращает null, если GDI+ не распознал формат (например, WebP).</summary>
    internal static Bitmap? DecodeWithGdi(byte[] data)
    {
        if (data.Length == 0) return null;
        try
        {
            using var stream = new MemoryStream(data);
            using var image = Image.FromStream(stream);
            if (image.Width > MaxDimension || image.Height > MaxDimension) return null;
            return new Bitmap(image);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException)
        {
            return null;
        }
    }

    /// <summary>Декодирует WebP/PNG/JPEG/GIF через ImageSharp и возвращает PNG-байты.</summary>
    internal static byte[] ConvertToPng(byte[] data)
    {
        var info = SixLabors.ImageSharp.Image.Identify(data);
        if (info.Width > MaxDimension || info.Height > MaxDimension)
            throw new InvalidDataException("Иконка слишком большая: " + info.Width + "x" + info.Height);

        using var image = SixLabors.ImageSharp.Image.Load(data);
        using var output = new MemoryStream();
        image.Save(output, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return output.ToArray();
    }
}

/// <summary>Заглушка вместо иконки: цветной градиент и первая буква названия.</summary>
internal static class ProjectIconPlaceholder
{
    static readonly (Color From, Color To)[] Palette =
    {
        (Color.FromArgb(108, 70, 235), Color.FromArgb(62, 38, 150)),
        (Color.FromArgb(30, 170, 120), Color.FromArgb(16, 96, 82)),
        (Color.FromArgb(232, 98, 120), Color.FromArgb(150, 54, 96)),
        (Color.FromArgb(46, 140, 230), Color.FromArgb(28, 78, 160)),
        (Color.FromArgb(235, 150, 52), Color.FromArgb(160, 88, 36)),
        (Color.FromArgb(150, 90, 220), Color.FromArgb(88, 52, 150)),
        (Color.FromArgb(40, 180, 190), Color.FromArgb(24, 102, 120)),
    };

    public static Bitmap Create(string? title, int size = 140)
    {
        var text = string.IsNullOrWhiteSpace(title) ? "?" : title.Trim();
        var (from, to) = Palette[(int)(Hash(text) % (uint)Palette.Length)];
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var brush = new LinearGradientBrush(new Rectangle(0, 0, size, size), from, to, 45f))
            graphics.FillRectangle(brush, 0, 0, size, size);

        using var font = new Font("Segoe UI Semibold", size * 0.44f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(FirstLetter(text), font, Brushes.White, new RectangleF(0, 0, size, size), format);
        return bitmap;
    }

    static string FirstLetter(string text)
    {
        foreach (var ch in text)
            if (char.IsLetterOrDigit(ch))
                return char.ToUpperInvariant(ch).ToString();
        return "◆";
    }

    static uint Hash(string text)
    {
        var hash = 2166136261u;
        foreach (var ch in text)
            hash = unchecked((hash ^ ch) * 16777619u);
        return hash;
    }
}
