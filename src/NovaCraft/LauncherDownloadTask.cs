namespace NovaCraft;

internal sealed class LauncherDownloadTask
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Source { get; set; } = "";
    public string BuildName { get; set; } = "";
    public string Status { get; set; } = "В очереди";
    public string Detail { get; set; } = "";
    public double Progress { get; set; }
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}
