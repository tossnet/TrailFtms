using System.Text.Json;
using System.Text.Json.Serialization;
using TrailFtms.Services.Bluetooth;
using TrailFtms.Services.Ride;
using TrailFtms.Services.Storage;

namespace TrailFtms.Services.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> in the browser storage and applies the ceilings to the treadmill.</summary>
public sealed class SettingsStore(IBrowserStorage storage, IFtmsTreadmill treadmill)
{
    private const string SettingsKey = "trailftms.settings";
    private const string SafetyKey = "trailftms.safety-accepted";

    private Task? _loading;

    public AppSettings Current { get; private set; } = new();

    /// <summary>The user acknowledged the safety warning (first launch).</summary>
    public bool SafetyAccepted { get; private set; }

    public event Action? Changed;

    public Task LoadAsync() => _loading ??= LoadCoreAsync();

    public async Task SaveAsync(AppSettings settings)
    {
        Apply(settings);
        await storage.SetAsync(SettingsKey, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
        Changed?.Invoke();
    }

    public async Task AcceptSafetyAsync()
    {
        SafetyAccepted = true;
        await storage.SetAsync(SafetyKey, "true");
        Changed?.Invoke();
    }

    private async Task LoadCoreAsync()
    {
        SafetyAccepted = await storage.GetAsync(SafetyKey) == "true";

        var stored = await storage.GetAsync(SettingsKey);
        var settings = new AppSettings();
        if (stored is not null)
        {
            try
            {
                settings = JsonSerializer.Deserialize(stored, SettingsJsonContext.Default.AppSettings) ?? settings;
            }
            catch (JsonException)
            {
                // Corrupted or outdated: back to defaults.
            }
        }
        Apply(settings);
        Changed?.Invoke();
    }

    private void Apply(AppSettings settings)
    {
        Current = settings;
        treadmill.Limits = new TreadmillLimits(settings.MaxSpeed, settings.MaxIncline);
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
