using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeripheralBattery.Core;

public sealed class UserSettings
{
    public bool LaunchAtStartup { get; set; }
    public bool DemoMode { get; set; }
    public bool TaskbarWidgetEnabled { get; set; }
    public string AnchorMonitor { get; set; } = TaskbarPlacement.PrimaryMonitor;
    public double AnchorFraction { get; set; } = TaskbarPlacement.DefaultFraction;

    public void Normalize()
    {
        AnchorFraction = TaskbarPlacement.ClampFraction(
            double.IsFinite(AnchorFraction) ? AnchorFraction : TaskbarPlacement.DefaultFraction);
        if (string.IsNullOrWhiteSpace(AnchorMonitor))
            AnchorMonitor = TaskbarPlacement.PrimaryMonitor;
    }
}

public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static UserSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new UserSettings();
            var parsed = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), Options);
            if (parsed is null)
                return new UserSettings();
            parsed.Normalize();
            return parsed;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }

    public static void Save(string path, UserSettings settings)
    {
        settings.Normalize();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(settings, Options);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, overwrite: true);
    }
}

public sealed class JsonIdentityOverrideStore : IIdentityOverrideStore
{
    private readonly string _path;
    private Dictionary<string, IdentityOverride> _map = new(StringComparer.OrdinalIgnoreCase);

    public JsonIdentityOverrideStore(string path)
    {
        _path = path;
        Reload();
    }

    public void Reload()
    {
        _map = Load(_path);
    }

    public IdentityOverride? Find(string stableKey)
    {
        return _map.TryGetValue(stableKey, out var value) ? value : null;
    }

    public static Dictionary<string, IdentityOverride> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new Dictionary<string, IdentityOverride>(StringComparer.OrdinalIgnoreCase);
            var entries = JsonSerializer.Deserialize<Dictionary<string, OverrideEntry>>(File.ReadAllText(path), JsonOptions());
            var map = new Dictionary<string, IdentityOverride>(StringComparer.OrdinalIgnoreCase);
            if (entries is null)
                return map;
            foreach (var (key, entry) in entries)
            {
                if (string.IsNullOrWhiteSpace(key) || entry is null)
                    continue;
                DeviceKind? kind = DeviceIdentity.TryParseKind(entry.Kind, out var parsed) ? parsed : null;
                var stable = NormalizeKey(key);
                map[stable] = new IdentityOverride(
                    string.IsNullOrWhiteSpace(entry.DisplayName) ? null : entry.DisplayName.Trim(),
                    kind);
            }

            return map;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Dictionary<string, IdentityOverride>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static string NormalizeKey(string key)
    {
        var trimmed = key.Trim();
        if (trimmed.StartsWith("bt:", StringComparison.OrdinalIgnoreCase))
        {
            var address = DeviceIdentity.NormalizeBluetoothAddress(trimmed[3..]);
            if (address is not null)
                return "bt:" + address;
        }

        return trimmed;
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class OverrideEntry
    {
        public string? DisplayName { get; set; }
        public string? Kind { get; set; }
    }
}
