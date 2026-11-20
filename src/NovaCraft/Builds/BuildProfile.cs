using System.Text.Json.Serialization;

namespace NovaCraft.Builds;

public sealed class BuildProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Новая сборка";
    public string MinecraftVersion { get; set; } = "1.21.1";
    public string Loader { get; set; } = "vanilla";
    public string GameDirectory { get; set; } = "";
    public int MaximumRamMb { get; set; } = 2048;
    public bool FpsBoost { get; set; }
    public bool Favorite { get; set; }

    [JsonIgnore]
    public string DisplayLoader => Loader switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "quilt" => "Quilt",
        "neoforge" => "NeoForge",
        _ => "Vanilla"
    };
}
