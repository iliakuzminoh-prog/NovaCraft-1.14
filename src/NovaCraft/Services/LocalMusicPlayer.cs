using System.Runtime.InteropServices;
using System.Text;

namespace NovaCraft.Services;

/// <summary>
/// Validates and normalizes the user-owned local playlist.
/// No network access or file copying is performed.
/// </summary>
internal static class MusicPlaylistRules
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav" };

    public static bool IsSupported(string? path)
        => !string.IsNullOrWhiteSpace(path) &&
           SupportedExtensions.Contains(Path.GetExtension(path.Trim()));

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Путь к музыкальному файлу пуст.");

        var fullPath = Path.GetFullPath(path.Trim());
        if (!IsSupported(fullPath))
            throw new InvalidDataException("Поддерживаются только локальные файлы MP3 и WAV.");

        return fullPath;
    }

    public static List<string> Normalize(IEnumerable<string>? paths)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in paths ?? Enumerable.Empty<string>())
        {
            try
            {
                var fullPath = NormalizePath(candidate);
                if (seen.Add(fullPath))
                    normalized.Add(fullPath);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                // Invalid legacy entries are skipped; they are never opened.
            }
        }

        return normalized;
    }

    public static string ValidateExistingFile(string path)
    {
        var fullPath = NormalizePath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Музыкальный файл не найден. Удали его из плейлиста или добавь заново.", fullPath);
        return fullPath;
    }
}

/// <summary>
/// Small Windows-native player based on Media Control Interface (MCI).
/// It plays files chosen by the user and creates no child processes.
/// </summary>
internal sealed class LocalMusicPlayer : IDisposable
{
    private const string Alias = "novacraft_local_music";
    private string? openPath;
    private bool disposed;

    [DllImport("winmm.dll", EntryPoint = "mciSendStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr callback);

    [DllImport("winmm.dll", EntryPoint = "mciGetErrorStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool MciGetErrorString(int errorCode, StringBuilder errorText, int errorTextSize);

    public bool IsOpen => !string.IsNullOrWhiteSpace(openPath);
    public string? CurrentPath => openPath;

    public void Open(string path)
    {
        ThrowIfDisposed();
        var fullPath = MusicPlaylistRules.ValidateExistingFile(path);
        CloseCore();

        var type = Path.GetExtension(fullPath).Equals(".wav", StringComparison.OrdinalIgnoreCase)
            ? "waveaudio"
            : "mpegvideo";

        Send($"open \"{fullPath}\" type {type} alias {Alias}");
        openPath = fullPath;
    }

    public void Play()
    {
        EnsureOpen();
        Send($"play {Alias}");
    }

    public void Pause()
    {
        EnsureOpen();
        Send($"pause {Alias}");
    }

    public void Resume()
    {
        EnsureOpen();
        Send($"resume {Alias}");
    }

    public void Stop()
    {
        if (IsOpen)
            Send($"stop {Alias}");
    }

    public void SetVolume(int volumePercent)
    {
        EnsureOpen();
        var value = Math.Clamp(volumePercent, 0, 100) * 10;
        Send($"setaudio {Alias} volume to {value}");
    }

    public string GetMode()
    {
        EnsureOpen();
        var result = new StringBuilder(64);
        var code = MciSendString($"status {Alias} mode", result, result.Capacity, IntPtr.Zero);
        if (code != 0)
            throw CreateMciException(code);
        return result.ToString().Trim();
    }

    private static void Send(string command)
    {
        var code = MciSendString(command, null, 0, IntPtr.Zero);
        if (code != 0)
            throw CreateMciException(code);
    }

    private static InvalidOperationException CreateMciException(int errorCode)
    {
        var buffer = new StringBuilder(256);
        var message = MciGetErrorString(errorCode, buffer, buffer.Capacity)
            ? buffer.ToString()
            : $"Windows MCI error {errorCode}.";
        return new InvalidOperationException("Не удалось управлять аудио: " + message);
    }

    private void EnsureOpen()
    {
        ThrowIfDisposed();
        if (!IsOpen)
            throw new InvalidOperationException("Сначала выбери музыкальный файл.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void CloseCore()
    {
        if (!IsOpen)
            return;

        try { MciSendString($"stop {Alias}", null, 0, IntPtr.Zero); } catch { }
        try { MciSendString($"close {Alias}", null, 0, IntPtr.Zero); } catch { }
        openPath = null;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        CloseCore();
        disposed = true;
        GC.SuppressFinalize(this);
    }
}

internal sealed record MusicTrackEntry(string FullPath)
{
    public override string ToString() => Path.GetFileName(FullPath);
}
