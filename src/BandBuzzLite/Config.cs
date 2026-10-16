using System.Text.Json;
using System.Text.Json.Serialization;

namespace BandBuzzLite;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Config))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext
{
}

internal sealed class Config
{
    // Placeholders on purpose: real values live in appsettings.json (or CLI
    // --mac/--key) so no band credentials are baked into the binary.
    public string Mac { get; set; } = "";
    public string AuthKey { get; set; } = "";
    public List<string> Filters { get; set; } = ["Chrome", "Teams"];
    public int DedupSeconds { get; set; } = 10;
    public int ReconnectSeconds { get; set; } = 5;
    public int MaxWrite { get; set; } = 244;
    // xiaomi (FE95: Band 7 Pro, 8 Active/8/9...) or huami (Zepp OS: Band 7 plain, Amazfit family)
    public string DeviceFamily { get; set; } = "xiaomi";
    public string NormalizedFamily => DeviceFamily.Trim().ToLowerInvariant();

    public static Config Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            return new Config();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, ConfigJsonContext.Default.Config) ?? new Config();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"warn: ignoring {path}: {ex.Message}");
            return new Config();
        }
    }
}
