using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.View;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Utils;

public class FanCurveManager : IDisposable
{
    public SensorsGroupController Sensors { get; }

    private IExtensionProvider? _extension;
    private bool _pluginLoaded;

    private readonly Dictionary<FanType, IFanControlView> _activeViewModels = new();

    public int LogicInterval { get; set; } = 500;

    public bool IsEnabled { get; private set; }

    public FanCurveManager(SensorsGroupController sensors)
    {
        Log.Instance.Trace($"FanCurveManager instance created.");
        Sensors = sensors;
    }

    public void Initialize()
    {
        Log.Instance.Trace($"FanCurveManager.Initialize called.");
        if (IsEnabled) return;

        LoadPlugin();
        if (_extension != null)
        {
            IsEnabled = true;
            _extension.Initialize(this);
            Log.Instance.Trace($"FanCurveManager initialized with extension.");
        }
        else
        {
            Log.Instance.Trace($"No extension found during Initialize.");
        }
    }

    private void LoadPlugin()
    {
        if (_pluginLoaded) return;
        _pluginLoaded = true;

        try
        {
            var pluginDir = Path.Combine(Folders.Program, "Plugins");
            Log.Instance.Trace($"Scanning for plugins in: {pluginDir} (Full: {Path.GetFullPath(pluginDir)})");
            
            if (!Directory.Exists(pluginDir))
            {
                Log.Instance.Trace($"Plugin directory does not exist.");
                return;
            }

            var dlls = Directory.GetFiles(pluginDir, "*.dll");
            Log.Instance.Trace($"Found {dlls.Length} DLL(s) in plugin directory.");

            foreach (var dll in dlls)
            {
                if (TryLoadPlugin(dll))
                {
                    Log.Instance.Trace($"Successfully loaded extension from {dll}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error during plugin scanning: {ex}");
        }
    }

    private bool TryLoadPlugin(string path)
    {
        try
        {
            Log.Instance.Trace($"Attempting to load: {path}");
            var assembly = Assembly.LoadFrom(path);
            var types = assembly.GetTypes();
            Log.Instance.Trace($"Assembly loaded. Found {types.Length} types.");

            var type = types.FirstOrDefault(t => typeof(IExtensionProvider).IsAssignableFrom(t) && !t.IsInterface);
            if (type != null)
            {
                Log.Instance.Trace($"Found provider type: {type.FullName}");
                _extension = (IExtensionProvider?)Activator.CreateInstance(type);
                return _extension != null;
            }
            
            Log.Instance.Trace($"No valid IExtensionProvider implementation found in this assembly.");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to load plugin assembly: {path}. Error: {ex}");
        }
        return false;
    }

    public void RegisterViewModel(FanType type, IFanControlView vm)
    {
        if (!IsEnabled) return;
        lock (_activeViewModels)
        {
            _activeViewModels[type] = vm;
        }
    }

    public void UnregisterViewModel(FanType type, IFanControlView vm)
    {
        if (!IsEnabled) return;
        lock (_activeViewModels)
        {
            if (_activeViewModels.TryGetValue(type, out var current) && current == vm) _activeViewModels.Remove(type);
        }
    }

    public void UpdateMonitoring(FanType type, float temp, int rpm, int pwm)
    {
        lock (_activeViewModels)
        {
            if (_activeViewModels.TryGetValue(type, out var vm))
            {
                vm.UpdateMonitoring(temp, rpm, (byte)pwm);
            }
        }
    }

    public async Task LoadAndApply(List<FanCurveEntry> entries)
    {
        if (!IsEnabled || _extension == null) return;
        foreach (var entry in entries)
        {
            AddEntry(entry);
            UpdateConfig(entry.Type, entry);
        }
        await SetRegister(true).ConfigureAwait(false);
    }

    public async Task SetRegister(bool flag = false)
    {
        if (!IsEnabled) return;
        if (_extension != null)
        {
            await _extension.ExecuteAsync("SetRegister", flag).ConfigureAwait(false);
        }
    }

    public FanCurveEntry? GetEntry(FanType type) => _extension?.GetData($"Entry_{type}") as FanCurveEntry;

    public void AddEntry(FanCurveEntry entry) => _extension?.ExecuteAsync("AddEntry", entry);

    public void UpdateGlobalSettings(FanCurveEntry sourceEntry) => _extension?.ExecuteAsync("UpdateGlobal", sourceEntry);

    public void UpdateConfig(FanType type, FanCurveEntry entry) => _extension?.ExecuteAsync("UpdateConfig", type, entry);

    public void Dispose()
    {
        _extension?.Dispose();
    }
}
