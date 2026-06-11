using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Station.Core;
using LenovoLegionToolkit.Lib.Station.Logging;
using LenovoLegionToolkit.Lib.Station.Services;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Station.Core;

public sealed class ExtensionContext : IExtensionContext
{
    private static readonly string PluginsBasePath = Path.Combine(Folders.AppData, "Plugins", "Configs");

    private readonly string _pluginId;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private Dictionary<string, JsonElement>? _settings;

    public ExtensionContext(string pluginId, INavigationService navigation, IAutomationStepRegistry automationSteps, IAutomationTriggerRegistry automationTriggers, IUiDispatcher uiDispatcher, IExtensionLogger logger)
    {
        _pluginId = pluginId;
        Navigation = navigation;
        AutomationSteps = automationSteps;
        AutomationTriggers = automationTriggers;
        UiDispatcher = uiDispatcher;
        Logger = logger;
    }

    public INavigationService Navigation { get; }
    public IAutomationStepRegistry AutomationSteps { get; }
    public IAutomationTriggerRegistry AutomationTriggers { get; }
    public IUiDispatcher UiDispatcher { get; }
    public IExtensionLogger Logger { get; }

    public string GetPluginStoragePath(string pluginId)
    {
        var path = Path.Combine(PluginsBasePath, pluginId);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        return path;
    }

    public async Task<T?> GetSettingAsync<T>(string key)
    {
        var settings = await LoadSettingsAsync().ConfigureAwait(false);

        if (settings.TryGetValue(key, out var element))
        {
            try
            {
                return element.Deserialize<T>();
            }
            catch
            {

            }
        }

        return default;
    }

    public async Task<bool> SetSettingAsync<T>(string key, T value)
    {
        try
        {
            var settings = await LoadSettingsAsync().ConfigureAwait(false);
            settings[key] = JsonSerializer.SerializeToElement(value);
            await SaveSettingsAsync(settings).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<Dictionary<string, JsonElement>> LoadSettingsAsync()
    {
        if (_settings is not null)
            return _settings;

        await _settingsLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_settings is not null)
                return _settings;

            var settingsFile = GetSettingsFilePath();

            if (!File.Exists(settingsFile))
            {
                _settings = [];
                return _settings;
            }

            try
            {
                var json = await File.ReadAllTextAsync(settingsFile).ConfigureAwait(false);
                _settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
            }
            catch
            {
                _settings = [];
            }

            return _settings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private async Task SaveSettingsAsync(Dictionary<string, JsonElement> settings)
    {
        await _settingsLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var settingsFile = GetSettingsFilePath();
            var dir = Path.GetDirectoryName(settingsFile)!;

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(settingsFile, json).ConfigureAwait(false);
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private string GetSettingsFilePath()
    {
        return Path.Combine(PluginsBasePath, _pluginId, "plugin.json");
    }
}
