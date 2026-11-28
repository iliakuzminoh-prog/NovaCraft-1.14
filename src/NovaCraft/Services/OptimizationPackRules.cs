namespace NovaCraft.Services;

internal sealed record OptimizationModSpec(
    string ProjectSlug,
    string FabricModId,
    string Name,
    string Purpose,
    bool ClientOnly,
    bool Conservative);

internal static class OptimizationPackRules
{
    public const string TargetMinecraft = "1.21.11";
    public const string TargetLoader = "fabric";

    // Deliberately excludes renderer replacements and optional micro-tweaks.
    // The base set aims for broad client-side compatibility with existing
    // Fabric servers and third-party modpacks, but cannot guarantee every pack.
    public static IReadOnlyList<OptimizationModSpec> ConservativePack { get; } =
    [
        new("lithium", "lithium", "Lithium", "Оптимизация части игровой логики на клиенте.", false, true),
        new("ferrite-core", "ferritecore", "FerriteCore", "Снижение расхода памяти.", false, true),
        new("immediatelyfast", "immediatelyfast", "ImmediatelyFast", "Оптимизация отрисовки интерфейса и immediate-mode элементов.", true, true),
        new("entityculling", "entityculling", "Entity Culling", "Пропуск отрисовки скрытых сущностей и block entities.", true, true)
    ];

    public static bool IsTarget(string minecraftVersion, string loader)
        => string.Equals(minecraftVersion, TargetMinecraft, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(loader, TargetLoader, StringComparison.OrdinalIgnoreCase);

    public static bool IsCompatibleVersion(
        IEnumerable<string>? gameVersions,
        IEnumerable<string>? loaders,
        string minecraftVersion,
        string loader)
        => gameVersions != null &&
           loaders != null &&
           gameVersions.Contains(minecraftVersion, StringComparer.OrdinalIgnoreCase) &&
           loaders.Contains(loader, StringComparer.OrdinalIgnoreCase);

    public static bool LooksLikeOptimizationPackVersion(
        string? versionType,
        IEnumerable<string>? gameVersions,
        IEnumerable<string>? loaders)
        => string.Equals(versionType, "release", StringComparison.OrdinalIgnoreCase) &&
           IsCompatibleVersion(gameVersions, loaders, TargetMinecraft, TargetLoader);
}
