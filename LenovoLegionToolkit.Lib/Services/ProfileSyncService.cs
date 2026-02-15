using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// Service for exporting/importing all application settings as a single JSON file.
/// </summary>
public class ProfileSyncService
{
    private readonly ApplicationSettings _applicationSettings;
    private readonly RyzenAdjSettings _ryzenAdjSettings;
    private readonly GodModeSettings _godModeSettings;
    private readonly BalanceModeSettings _balanceModeSettings;
    private readonly RGBKeyboardSettings _rgbSettings;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ProfileSyncService(
        ApplicationSettings applicationSettings,
        RyzenAdjSettings ryzenAdjSettings,
        GodModeSettings godModeSettings,
        BalanceModeSettings balanceModeSettings,
        RGBKeyboardSettings rgbSettings)
    {
        _applicationSettings = applicationSettings;
        _ryzenAdjSettings = ryzenAdjSettings;
        _godModeSettings = godModeSettings;
        _balanceModeSettings = balanceModeSettings;
        _rgbSettings = rgbSettings;
    }

    /// <summary>
    /// Export all settings to a JSON file.
    /// </summary>
    public async Task<bool> ExportAsync(string filePath)
    {
        try
        {
            var profile = new ProfileExport
            {
                ExportedAt = DateTime.UtcNow,
                Version = "1.0",
                ApplicationSettings = _applicationSettings.Store,
                RyzenAdjSettings = _ryzenAdjSettings.Store,
                GodModeSettings = _godModeSettings.Store,
                BalanceModeSettings = _balanceModeSettings.Store,
                RGBKeyboardSettings = _rgbSettings.Store
            };

            var json = JsonSerializer.Serialize(profile, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);
            
            Log.Instance.Trace($"[ProfileSync] Exported settings to: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[ProfileSync] Export failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Import settings from a JSON file.
    /// </summary>
    public async Task<bool> ImportAsync(string filePath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var profile = JsonSerializer.Deserialize<ProfileExport>(json, JsonOptions);

            if (profile == null)
            {
                Log.Instance.Trace($"[ProfileSync] Import failed: invalid JSON");
                return false;
            }

            // Restore settings (null-safe)
            if (profile.ApplicationSettings != null)
            {
                // Copy non-null properties
                CopySettings(_applicationSettings.Store, profile.ApplicationSettings);
                _applicationSettings.SynchronizeStore();
            }
            
            if (profile.RyzenAdjSettings != null)
            {
                CopySettings(_ryzenAdjSettings.Store, profile.RyzenAdjSettings);
                _ryzenAdjSettings.SynchronizeStore();
            }
            
            if (profile.GodModeSettings != null)
            {
                CopySettings(_godModeSettings.Store, profile.GodModeSettings);
                _godModeSettings.SynchronizeStore();
            }
            
            if (profile.BalanceModeSettings != null)
            {
                CopySettings(_balanceModeSettings.Store, profile.BalanceModeSettings);
                _balanceModeSettings.SynchronizeStore();
            }
            
            if (profile.RGBKeyboardSettings != null)
            {
                CopySettings(_rgbSettings.Store, profile.RGBKeyboardSettings);
                _rgbSettings.SynchronizeStore();
            }

            Log.Instance.Trace($"[ProfileSync] Imported settings from: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[ProfileSync] Import failed: {ex.Message}");
            return false;
        }
    }
    
    private static void CopySettings<T>(T target, T source) where T : class
    {
        // Use reflection to copy all public properties
        foreach (var prop in typeof(T).GetProperties())
        {
            if (!prop.CanWrite || !prop.CanRead) continue;
            
            var value = prop.GetValue(source);
            if (value != null)
                prop.SetValue(target, value);
        }
    }
}

/// <summary>
/// Container for exported profile data.
/// </summary>
public class ProfileExport
{
    public DateTime ExportedAt { get; set; }
    public string Version { get; set; } = "1.0";
    
    public ApplicationSettings.ApplicationSettingsStore? ApplicationSettings { get; set; }
    public RyzenAdjSettings.RyzenAdjSettingsStore? RyzenAdjSettings { get; set; }
    public GodModeSettings.GodModeSettingsStore? GodModeSettings { get; set; }
    public BalanceModeSettings.BalanceModeSettingsStore? BalanceModeSettings { get; set; }
    public RGBKeyboardSettings.RGBKeyboardSettingsStore? RGBKeyboardSettings { get; set; }
}
