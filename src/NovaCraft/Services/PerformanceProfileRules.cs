using System.Runtime.InteropServices;

namespace NovaCraft.Services;

internal readonly record struct SystemMemorySnapshot(ulong TotalMb, ulong AvailableMb);

internal static class SystemMemoryStatus
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailablePhys;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public static SystemMemorySnapshot Read()
    {
        if (!OperatingSystem.IsWindows())
            return new SystemMemorySnapshot(0, 0);

        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
            return new SystemMemorySnapshot(0, 0);

        const ulong bytesPerMb = 1024 * 1024;
        return new SystemMemorySnapshot(
            status.TotalPhys / bytesPerMb,
            status.AvailablePhys / bytesPerMb);
    }
}

internal static class PerformanceProfileRules
{
    /// <summary>
    /// Leaves headroom for Windows and other processes, then caps the Java heap
    /// by currently available physical memory. The result is a starting point,
    /// not a guarantee of FPS or compatibility with every modpack.
    /// </summary>
    public static int RecommendedHeapMb(SystemMemorySnapshot memory)
    {
        if (memory.TotalMb == 0 || memory.AvailableMb == 0)
            return 1024;

        var reserveForWindows = memory.TotalMb >= 8192 ? 2048UL : 1536UL;
        var totalBudget = memory.TotalMb > reserveForWindows
            ? memory.TotalMb - reserveForWindows
            : 1024UL;
        var availableBudget = (ulong)Math.Floor(memory.AvailableMb * 0.60d);

        var budget = Math.Min(totalBudget, availableBudget);
        var rounded = budget / 256UL * 256UL;
        return (int)Math.Clamp(rounded, 1024UL, 8192UL);
    }

    public static bool IsLowAvailableMemory(SystemMemorySnapshot memory)
        => memory.AvailableMb > 0 && memory.AvailableMb < 2048;

    public static string Describe(SystemMemorySnapshot memory)
    {
        if (memory.TotalMb == 0 || memory.AvailableMb == 0)
            return "Не удалось получить данные о физической памяти Windows.";

        var recommended = RecommendedHeapMb(memory);
        var warning = IsLowAvailableMemory(memory)
            ? " Свободной памяти мало: закрой тяжёлые программы перед запуском. Даже этот лимит не гарантирует запуск тяжёлых сборок."
            : " Лимит оставляет запас для Windows и фоновых программ.";

        return $"ОЗУ: {memory.TotalMb / 1024.0:0.0} ГБ всего, {memory.AvailableMb / 1024.0:0.0} ГБ свободно. Рекомендуемый лимит Java: {recommended} МБ.{warning}";
    }
}
