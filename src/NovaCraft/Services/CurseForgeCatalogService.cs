using System.Net;
using System.Security;
using System.Text.Json;

namespace NovaCraft.Services;

internal sealed record CurseForgeProject(
    long Id,
    string Name,
    string Slug,
    string Summary,
    string WebsiteUrl,
    string ThumbnailUrl,
    long DownloadCount,
    bool AllowsDistribution,
    bool IsAvailable);

internal sealed record CurseForgeProjectPage(
    IReadOnlyList<CurseForgeProject> Projects,
    long TotalCount);

internal sealed record CurseForgeCompatibleFile(
    long Id,
    string FileName,
    string DisplayName,
    string DownloadUrl,
    string Sha1,
    long FileSize,
    int ReleaseType,
    bool IsAvailable,
    IReadOnlyList<string> GameVersions);

/// <summary>
/// Read-only client for the official CurseForge Core API.
/// API keys are supplied by the UI per call and are never persisted or logged.
/// </summary>
internal static class CurseForgeCatalogService
{
    private const int MinecraftGameId = 432;
    private const int PageSize = 20;
    private static readonly Uri ApiBase = new("https://api.curseforge.com/");

    public static async Task<CurseForgeProjectPage> SearchModsAsync(
        HttpClient http,
        string apiKey,
        string searchFilter,
        string minecraftVersion,
        string loader,
        int index,
        int sortField,
        CancellationToken cancellationToken)
    {
        ValidateInputs(http, apiKey);
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            throw new InvalidOperationException("Сначала выбери версию Minecraft для активной сборки.");
        if (index < 0 || index >= 10_000 || index % PageSize != 0)
            throw new ArgumentOutOfRangeException(nameof(index), "Индекс страницы должен быть кратен размеру страницы и меньше 10 000.");

        var loaderType = ToLoaderType(loader);
        var classId = await GetModsClassIdAsync(http, apiKey, cancellationToken);

        var query = new List<string>
        {
            "gameId=" + MinecraftGameId,
            "classId=" + classId,
            "gameVersion=" + Uri.EscapeDataString(minecraftVersion.Trim()),
            "modLoaderType=" + loaderType,
            "sortField=" + (sortField is 2 or 3 or 11 ? sortField : 2),
            "sortOrder=desc",
            "index=" + index,
            "pageSize=" + PageSize
        };

        if (!string.IsNullOrWhiteSpace(searchFilter))
            query.Add("searchFilter=" + Uri.EscapeDataString(searchFilter.Trim()));

        using var document = await GetJsonAsync(
            http,
            apiKey,
            new Uri(ApiBase, "v1/mods/search?" + string.Join("&", query)),
            cancellationToken);

        var root = document.RootElement;
        var projects = new List<CurseForgeProject>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                if (!TryReadInt64(item, "id", out var id) || id <= 0)
                    continue;

                var name = ReadString(item, "name");
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var links = GetProperty(item, "links");
                var logo = GetProperty(item, "logo");
                var canDistribute = TryReadBoolean(item, "allowModDistribution", out var allowed) && allowed;
                var available = TryReadBoolean(item, "isAvailable", out var isAvailable) && isAvailable;

                projects.Add(new CurseForgeProject(
                    id,
                    name,
                    ReadString(item, "slug"),
                    ReadString(item, "summary"),
                    ReadString(links, "websiteUrl"),
                    ReadString(logo, "thumbnailUrl"),
                    TryReadInt64(item, "downloadCount", out var downloads) ? downloads : 0,
                    canDistribute,
                    available));
            }
        }

        var total = 0L;
        var pagination = GetProperty(root, "pagination");
        if (TryReadInt64(pagination, "totalCount", out var totalCount))
            total = totalCount;

        return new CurseForgeProjectPage(projects, total);
    }

    public static async Task<IReadOnlyList<CurseForgeCompatibleFile>> GetCompatibleFilesAsync(
        HttpClient http,
        string apiKey,
        long projectId,
        string minecraftVersion,
        string loader,
        CancellationToken cancellationToken)
    {
        ValidateInputs(http, apiKey);
        if (projectId <= 0)
            throw new ArgumentOutOfRangeException(nameof(projectId));
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            throw new InvalidOperationException("Не выбрана версия Minecraft.");

        var loaderType = ToLoaderType(loader);
        var url = new Uri(ApiBase,
            $"v1/mods/{projectId}/files?gameVersion={Uri.EscapeDataString(minecraftVersion.Trim())}&modLoaderType={loaderType}&index=0&pageSize=50");

        using var document = await GetJsonAsync(http, apiKey, url, cancellationToken);
        var results = new List<CurseForgeCompatibleFile>();
        var data = GetProperty(document.RootElement, "data");
        if (data.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var item in data.EnumerateArray())
        {
            if (!TryReadInt64(item, "id", out var id) || id <= 0)
                continue;

            var fileName = ReadString(item, "fileName");
            if (string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                continue;

            var hash = "";
            var hashes = GetProperty(item, "hashes");
            if (hashes.ValueKind == JsonValueKind.Array)
            {
                foreach (var candidate in hashes.EnumerateArray())
                {
                    if (TryReadInt64(candidate, "algo", out var algorithm) && algorithm == 1)
                    {
                        hash = ReadString(candidate, "value");
                        break;
                    }
                }
            }

            var gameVersions = new List<string>();
            var versions = GetProperty(item, "gameVersions");
            if (versions.ValueKind == JsonValueKind.Array)
            {
                foreach (var value in versions.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                        gameVersions.Add(value.GetString()!);
                }
            }

            results.Add(new CurseForgeCompatibleFile(
                id,
                fileName,
                ReadString(item, "displayName"),
                ReadString(item, "downloadUrl"),
                hash,
                TryReadInt64(item, "fileLength", out var length) ? length : 0,
                TryReadInt64(item, "releaseType", out var releaseType) ? (int)releaseType : 0,
                TryReadBoolean(item, "isAvailable", out var isAvailable) && isAvailable,
                gameVersions));
        }

        // Prefer stable releases, then newest files as returned by the official API.
        return results.OrderBy(x => x.ReleaseType == 1 ? 0 : x.ReleaseType == 2 ? 1 : 2).ToList();
    }

    public static async Task<string> GetFileDownloadUrlAsync(
        HttpClient http,
        string apiKey,
        long projectId,
        long fileId,
        CancellationToken cancellationToken)
    {
        ValidateInputs(http, apiKey);
        if (projectId <= 0 || fileId <= 0)
            throw new ArgumentOutOfRangeException(nameof(projectId));

        var url = new Uri(ApiBase, $"v1/mods/{projectId}/files/{fileId}/download-url");
        using var document = await GetJsonAsync(http, apiKey, url, cancellationToken);
        var data = GetProperty(document.RootElement, "data");
        var value = data.ValueKind == JsonValueKind.String ? data.GetString() : null;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("CurseForge не вернул ссылку загрузки этого файла.");
        return value;
    }

    private static async Task<int> GetModsClassIdAsync(HttpClient http, string apiKey, CancellationToken cancellationToken)
    {
        var url = new Uri(ApiBase, $"v1/categories?gameId={MinecraftGameId}&classesOnly=true");
        using var document = await GetJsonAsync(http, apiKey, url, cancellationToken);
        var data = GetProperty(document.RootElement, "data");
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var name = ReadString(item, "name");
                var slug = ReadString(item, "slug");
                var isClass = TryReadBoolean(item, "isClass", out var value) && value;
                if (isClass &&
                    (string.Equals(name, "Mods", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(slug, "mc-mods", StringComparison.OrdinalIgnoreCase)))
                {
                    if (TryReadInt64(item, "id", out var id) && id > 0 && id <= int.MaxValue)
                        return (int)id;
                }
            }
        }

        throw new InvalidDataException("Официальный API CurseForge не вернул категорию Minecraft Mods. Поиск остановлен.");
    }

    private static async Task<JsonDocument> GetJsonAsync(
        HttpClient http,
        string apiKey,
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, ApiBase.Host, StringComparison.OrdinalIgnoreCase))
            throw new SecurityException("Запрос CurseForge разрешён только к официальному HTTPS API.");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new SecurityException("CurseForge API вернул перенаправление. Запрос остановлен, чтобы не передать API-ключ другому сайту.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException("CurseForge отклонил API-ключ. Проверь, что он действующий и одобрен для стороннего приложения.");
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > 8 * 1024 * 1024))
            throw new InvalidDataException("Ответ CurseForge имеет недопустимый размер.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static void ValidateInputs(HttpClient http, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 512 || apiKey.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Введи действующий API-ключ CurseForge без пробелов. Ключ не сохраняется в файле настроек.");
    }

    private static int ToLoaderType(string loader)
        => loader.Trim().ToLowerInvariant() switch
        {
            "forge" => 1,
            "fabric" => 4,
            "quilt" => 5,
            _ => throw new NotSupportedException("Каталог CurseForge поддерживает в этой версии Fabric, Forge и Quilt.")
        };

    private static JsonElement GetProperty(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value
            : default;

    private static string ReadString(JsonElement element, string propertyName)
    {
        var value = GetProperty(element, propertyName);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }

    private static bool TryReadInt64(JsonElement element, string propertyName, out long value)
    {
        var property = GetProperty(element, propertyName);
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value)
            ? true
            : AssignZero(out value);
    }

    private static bool AssignZero(out long value)
    {
        value = 0;
        return false;
    }

    private static bool TryReadBoolean(JsonElement element, string propertyName, out bool value)
    {
        var property = GetProperty(element, propertyName);
        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        value = false;
        return false;
    }
}
