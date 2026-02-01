using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.View;

namespace LenovoLegionToolkit.Lib.Utils;

public class FanCurveManager : IDisposable
{
    public SensorsGroupController Sensors { get; }
    private readonly PowerModeListener _powerModeListener;
    private readonly PowerModeFeature _powerModeFeature;

    private IExtensionProvider? _extension;
    private bool _pluginLoaded;

    private readonly Dictionary<FanType, IFanControlView> _activeViewModels = new();

    public int LogicInterval { get; set; } = 500;

    public bool IsEnabled { get; private set; }

    public FanCurveManager(
        SensorsGroupController sensors,
        PowerModeListener powerModeListener,
        PowerModeFeature powerModeFeature)
    {
        Log.Instance.Trace($"FanCurveManager instance created.");
        Sensors = sensors;
        _powerModeListener = powerModeListener;
        _powerModeFeature = powerModeFeature;
    }

    public async void Initialize()
    {
        Log.Instance.Trace($"FanCurveManager.Initialize called.");

        if (IsEnabled)
        {
            Log.Instance.Trace($"FanCurveManager is already enabled. Skipping initialization.");
            return;
        }

        LoadPlugin();
        if (_extension != null)
        {
            IsEnabled = true;
            _extension.Initialize(this);
            Log.Instance.Trace($"FanCurveManager initialized with extension.");

            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            if (mi.LegionSeries != LegionSeries.ThinkBook)
            {
                _powerModeListener.Changed += OnPowerModeChanged;
                var currentState = await _powerModeFeature.GetStateAsync().ConfigureAwait(false);
                if (currentState != PowerModeState.GodMode)
                {
                    Log.Instance.Trace($"Initial state is {currentState}, disabling fan control register.");
                    await SetRegister(false).ConfigureAwait(false);
                }
            }
        }
        else
        {
            Log.Instance.Trace($"No extension found during Initialize.");
        }
    }

    private async void OnPowerModeChanged(object? sender, PowerModeListener.ChangedEventArgs e)
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        if (mi.LegionSeries == LegionSeries.ThinkBook) return;

        if (e.State == PowerModeState.GodMode)
        {
            Log.Instance.Trace($"PowerMode changed to GodMode. Enabling custom fan control.");
            await SetRegister(true).ConfigureAwait(false);
        }
        else
        {
            Log.Instance.Trace($"PowerMode changed to {e.State}. Disabling custom fan control.");
            await SetRegister(false).ConfigureAwait(false);
        }
    }

    private void LoadPlugin()
    {
        if (_pluginLoaded)
        {
            Log.Instance.Trace($"Plugin loaded.");
            return;
        }

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
        ResolveEventHandler resolver = (_, args) => 
            args.Name.Contains(typeof(IExtensionProvider).Assembly.GetName().Name!) ? typeof(IExtensionProvider).Assembly : null;

        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += resolver;
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
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
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
        if (_extension == null) return;
        foreach (var entry in entries)
        {
            AddEntry(entry);
            UpdateConfig(entry.Type, entry);
        }

        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        if (mi.LegionSeries == LegionSeries.ThinkBook)
        {
             await SetRegister(true).ConfigureAwait(false);
        }
        else
        {
            var currentState = await _powerModeFeature.GetStateAsync().ConfigureAwait(false);
            if (currentState == PowerModeState.GodMode)
            {
                await SetRegister(true).ConfigureAwait(false);
            }
            else
            {
                 Log.Instance.Trace($"Not in GodMode, applying config but keeping register disabled.");
                 await SetRegister(false).ConfigureAwait(false);
            }
        }
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
        _powerModeListener.Changed -= OnPowerModeChanged;
    }
}
