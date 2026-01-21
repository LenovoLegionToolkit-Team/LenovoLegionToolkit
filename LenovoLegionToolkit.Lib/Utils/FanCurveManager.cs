using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.View;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using UniversalFanControl.Lib;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Features;
using UniversalFanControl.Lib.Generic.Api;

namespace LenovoLegionToolkit.Lib.Utils;

public class FanCurveManager : IDisposable
{
    private readonly SensorsGroupController _sensors;
    private readonly FanCurveSettings _settings;
    private readonly PowerModeFeature _powerModeFeature;
    private readonly Func<FanCurveConfig, FanCurveController> _controllerFactory;
    
    private readonly FanControl _fanHardware;
    private readonly Dictionary<FanType, FanControlViewModel> _activeViewModels = new();
    private readonly Dictionary<FanType, FanCurveController> _activeControllers = new();
    
    private CancellationTokenSource? _cts;

    public int LogicInterval { get; set; } = 500;

    public FanCurveManager(
        SensorsGroupController sensors,
        FanCurveSettings settings,
        PowerModeFeature powerModeFeature,
        Func<FanCurveConfig, FanCurveController> controllerFactory)
    {
        _sensors = sensors;
        _settings = settings;
        _powerModeFeature = powerModeFeature;
        _controllerFactory = controllerFactory;

        _fanHardware = new FanControl();
        if (!_fanHardware.InitiliazeFanControl())
        {
            Log.Instance.Trace($"FanCurveManager: Failed to initialize hardware.");
        }

        StartControlLoop();
    }

    public void RegisterViewModel(FanType type, FanControlViewModel vm)
    {
        lock (_activeViewModels)
        {
            _activeViewModels[type] = vm;
        }
    }

    public void UnregisterViewModel(FanType type)
    {
        lock (_activeViewModels)
        {
            _activeViewModels.Remove(type);
        }
    }

    public FanCurveEntry? GetEntry(FanType type)
    {
        return _settings.Store.Entries.FirstOrDefault(e => e.Type == type);
    }

    public void AddEntry(FanCurveEntry entry)
    {
        var existing = GetEntry(entry.Type);
        if (existing == null)
        {
            _settings.Store.Entries.Add(entry);
            _settings.Save();
        }
    }

    public void UpdateGlobalSettings(FanCurveEntry sourceEntry)
    {
        foreach (var entry in _settings.Store.Entries)
        {
            if (entry == sourceEntry) continue;

            bool changed = false;
            
            if (entry.IsLegion != sourceEntry.IsLegion)
            {
                entry.IsLegion = sourceEntry.IsLegion;
                changed = true;
            }
            if (entry.CriticalTemp != sourceEntry.CriticalTemp)
            {
                entry.CriticalTemp = sourceEntry.CriticalTemp;
                changed = true;
            }
            if (Math.Abs(entry.LegionLowTempThreshold - sourceEntry.LegionLowTempThreshold) > 0.01f)
            {
                entry.LegionLowTempThreshold = sourceEntry.LegionLowTempThreshold;
                changed = true;
            }
            if (entry.AccelerationDcrReduction != sourceEntry.AccelerationDcrReduction)
            {
                entry.AccelerationDcrReduction = sourceEntry.AccelerationDcrReduction;
                changed = true;
            }
            if (entry.DecelerationDcrReduction != sourceEntry.DecelerationDcrReduction)
            {
                entry.DecelerationDcrReduction = sourceEntry.DecelerationDcrReduction;
                changed = true;
            }

            if (changed)
            {
                UpdateConfig(entry.Type, entry);
                
                // Update ViewModel if exists
                lock (_activeViewModels)
                {
                    if (_activeViewModels.TryGetValue(entry.Type, out var vm))
                    {
                        vm.NotifyGlobalSettingsChanged();
                    }
                }
            }
        }
        _settings.Save();
    }

    public void UpdateConfig(FanType type, FanCurveEntry entry)
    {
        lock (_activeControllers)
        {
            if (_activeControllers.TryGetValue(type, out var controller))
            {
                controller.UpdateConfig(entry.ToConfig());
            }
            else
            {
                _activeControllers[type] = _controllerFactory(entry.ToConfig());
            }
        }
        _settings.Save();
    }

    private void StartControlLoop()
    {
        _cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // Check GodMode (PowerModeState.GodMode is 254)
                    var powerState = await _powerModeFeature.GetStateAsync().ConfigureAwait(false);
                    if (powerState != PowerModeState.GodMode)
                    {
                        await Task.Delay(1000, _cts.Token);
                        continue;
                    }

                    await _sensors.UpdateAsync().ConfigureAwait(false);
                    float cpuTemp = await _sensors.GetCpuTemperatureAsync().ConfigureAwait(false);
                    float gpuTemp = await _sensors.GetGpuTemperatureAsync().ConfigureAwait(false);

                    // Iterate over settings entries instead of ViewModels
                    var entries = _settings.Store.Entries.ToList();

                    foreach (var entry in entries)
                    {
                         // Ensure controller exists
                         FanCurveController? controller;
                         lock (_activeControllers)
                         {
                             if (!_activeControllers.TryGetValue(entry.Type, out controller))
                             {
                                 controller = _controllerFactory(entry.ToConfig());
                                 _activeControllers[entry.Type] = controller;
                             }
                         }

                         if (controller == null) continue;

                         float temp = entry.Type == FanType.Gpu ? gpuTemp : cpuTemp;
                         if (temp < 0) temp = 0;

                         // Calculate PWM
                         byte? pwm = controller.Update(temp, entry.CurveNodes);
                         
                         int rpm = 0;
                         if (pwm.HasValue && _fanHardware != null)
                         {
                             var regs = GetRegisters(entry.Type);
                             _fanHardware.SetFanPwm(regs.pwm, pwm.Value);
                             rpm = _fanHardware.GetFanRpm(regs.rpmLow, regs.rpmHigh);
                         }

                         // Update ViewModel if present
                         lock (_activeViewModels)
                         {
                             if (_activeViewModels.TryGetValue(entry.Type, out var vm))
                             {
                                 vm.UpdateMonitoring(temp, rpm, pwm ?? 0);
                             }
                         }
                    }

                    await Task.Delay(LogicInterval, _cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log.Instance.Trace($"Manager Loop Error: {ex.Message}"); }
            }
        }, _cts.Token);
    }

    private (ushort pwm, ushort rpmLow, ushort rpmHigh) GetRegisters(FanType type) => type switch
    {
        FanType.Gpu => (0x1806, 0x1820, 0x1821),
        FanType.System => (0x1808, 0x1845, 0x1846),
        _ => (0x1807, 0x181E, 0x181F)
    };

    public void Dispose() => _cts?.Cancel();
}
