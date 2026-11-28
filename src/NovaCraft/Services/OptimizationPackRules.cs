namespace NovaCraft.Services;

internal sealed record OptimizationModSpec(
    string ProjectSlug,
    string Name,
    string Purpose,
    bool ClientOnly,
    bool Conservative);

internal static class OptimizationPackRules
{
    public const string TargetMinecraft = "1.21.11";
    public const string TargetLoader = "fabric";

    // Conservative client-first set. These are deliberately separated from
    // aggressive world-generation/networking optimizers because the goal is
    // to join existing modded servers without requiring server-side changes.
    public static IReadOnlyList<OptimizationModSpec> ConservativePack { get; } =
    [
        new("sodium", "Sodium", "Быстрый рендеринг чанков и меньше микрофризов.", true, true),
        new("lithium", "Lithium", "Оптимизация игровой логики и тиков.", false, true),
        new("ferrite-core", "FerriteCore", "Снижение потребления памяти.", false, true),
        new("immediatelyfast", "ImmediatelyFast", "Быстрее GUI, текста, сущностей и immediate-mode рендера.", true, true),
        new("entityculling", "Entity Culling", "Не рендерит невидимые сущности и block entities.", true, true),
        new("badoptimizations", "BadOptimizations", "Дополнительные оптимизации вне основного рендера.", true, true)
    ];

    public static bool IsTarget(string minecraftVersion, string loader)
        => string.Equals(minecraftVersion, TargetMinecraft, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(loader, TargetLoader, StringComparison.OrdinalIgnoreCase);

    public static bool LooksLikeOptimizationPackVersion(string? versionType, string? loader)
        => string.Equals(versionType, "release", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(loader, TargetLoader, StringComparison.OrdinalIgnoreCase);
}
