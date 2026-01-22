using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.View;
using UniversalFanControl.Lib;
using UniversalFanControl.Lib.Generic.Api;
using UniversalFanControl.Lib.Generic.Utils;

namespace LenovoLegionToolkit.Lib.Utils;

public class FanCurveManager : IDisposable
{
    private readonly SensorsGroupController _sensors;
    private readonly FanCurveSettings _settings;
    private readonly PowerModeFeature _powerModeFeature;

    private readonly FanControl _fanHardware;
    private readonly Dictionary<FanType, IFanControlView> _activeViewModels = new();
    private readonly Dictionary<FanType, FanCurveController> _activeControllers = new();

    private CancellationTokenSource? _cts;

    public int LogicInterval { get; set; } = 500;

    public FanCurveManager(
        SensorsGroupController sensors,
        FanCurveSettings settings,
        PowerModeFeature powerModeFeature)
    {
        _sensors = sensors;
        _settings = settings;
        _powerModeFeature = powerModeFeature;

        _fanHardware = new FanControl();
        if (!_fanHardware.InitiliazeFanControl())
        {
            Log.Instance.Trace($"FanCurveManager: Failed to initialize hardware.");
        }

        SetRegister(true).ConfigureAwait(false);

        StartControlLoop();
    }

    public void RegisterViewModel(FanType type, IFanControlView vm)
    {
        lock (_activeViewModels)
        {
            _activeViewModels[type] = vm;
        }
    }

    public void UnregisterViewModel(FanType type, IFanControlView vm)
    {
        lock (_activeViewModels)
        {
            if (_activeViewModels.TryGetValue(type, out var current) && current == vm)
            {
                _activeViewModels.Remove(type);
            }
        }
    }

    public async Task SetRegister(bool flag = false)
    {
        if (_fanHardware == null)
        {
            return;
        }

        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        var (addr, val, _) = GetHardwareConfig(mi, FanType.Cpu, true);

        if (addr != 0)
        {
            if (flag)
            {
                _fanHardware.SetRegister(addr, (byte)val);
            }
            else
            {
                _fanHardware.SetRegister(addr, 0x00);
            }
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
                controller.SetCurve(entry.CurveNodes.Select(n => new UniversalFanControl.Lib.Generic.Utils.CurveNode { Temperature = n.Temperature, TargetPercent = n.TargetPercent }));
            }
            else
            {
                var newController = new FanCurveController(entry.ToConfig(), msg => Log.Instance.Trace($"{msg}"));
                newController.SetCurve(entry.CurveNodes.Select(n => new UniversalFanControl.Lib.Generic.Utils.CurveNode { Temperature = n.Temperature, TargetPercent = n.TargetPercent }));
                _activeControllers[type] = newController;
            }
        }
        _settings.Save();
    }

    private void StartControlLoop()
    {
        _cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var powerState = await _powerModeFeature.GetStateAsync().ConfigureAwait(false);
                    if (powerState != PowerModeState.GodMode)
                    {
                        await Task.Delay(1000, _cts.Token);
                        continue;
                    }

                    await _sensors.UpdateAsync().ConfigureAwait(false);
                    float cpuTemp = await _sensors.GetCpuTemperatureAsync().ConfigureAwait(false);
                    float gpuTemp = await _sensors.GetGpuTemperatureAsync().ConfigureAwait(false);

                    var entries = _settings.Store.Entries.ToList();

                    foreach (var entry in entries)
                    {
                        FanCurveController? controller;
                        lock (_activeControllers)
                        {
                            if (!_activeControllers.TryGetValue(entry.Type, out controller))
                            {
                                controller = new FanCurveController(entry.ToConfig(), msg => Log.Instance.Trace($"{msg}"));
                                _activeControllers[entry.Type] = controller;
                            }
                        }

                        if (controller == null) continue;

                        float temp = entry.Type == FanType.Gpu ? gpuTemp : cpuTemp;
                        if (temp < 0) temp = 0;

                        controller.SetCurve(entry.CurveNodes.Select(n => new UniversalFanControl.Lib.Generic.Utils.CurveNode { Temperature = n.Temperature, TargetPercent = n.TargetPercent }));
                        byte? pwm = controller.Update(temp);

                        int rpm = 0;
                        if (pwm.HasValue && _fanHardware != null)
                        {
                            var (regPwm, regRpmL, regRpmH) = GetHardwareConfig(mi, entry.Type, false);
                            if (regPwm != 0)
                            {
                                _fanHardware.SetFanPwm((ushort)regPwm, pwm.Value);
                                rpm = _fanHardware.GetFanRpm((ushort)regRpmL, (ushort)regRpmH);
                            }
                        }

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

    private (ushort Val1, ushort Val2, ushort Val3) GetHardwareConfig(MachineInformation? mi, FanType fanType, bool isControlConfig)
    {
        var mapping = new (LegionSeries? Series, int MinGen, int MaxGen, bool IsCtrl, FanType? Type, ushort var1, ushort var2, ushort? var3)[]
        {
            (LegionSeries.Legion_Pro_7, 10, 10, true, null, 0xDFF7, 0x01, null),
            (LegionSeries.Legion_Pro_5, 10, 10, true, null, 0xDFF7, 0x01, null),
            (null, 0, int.MaxValue, true, null, 0x00, 0x00, 0x00),

            // ================================================================================

            (LegionSeries.Legion_Pro_7, 8, 9, false, FanType.Cpu, 0x1807, 0x181E, 0x181F),
            (LegionSeries.Legion_Pro_7, 8, 9, false, FanType.Gpu, 0x1806, 0x1820, 0x1821),

            (LegionSeries.Legion_Pro_7, 10, 10, false, FanType.Cpu, 0x1807, 0x181E, 0x181F),
            (LegionSeries.Legion_Pro_7, 10, 10, false, FanType.Gpu, 0x1806, 0x1820, 0x1821),
            (LegionSeries.Legion_Pro_7, 10, 10, false, FanType.System, 0x1808, 0x1845, 0x1846),

            (LegionSeries.Legion_Pro_5, 10, 10, false, FanType.Cpu, 0x1807, 0x181E, 0x181F),
            (LegionSeries.Legion_Pro_5, 10, 10, false, FanType.Gpu, 0x1806, 0x1820, 0x1821),

            (LegionSeries.ThinkBook, 8, 9, false, FanType.Cpu, 0x1806, 0x1820, 0x1821),
            (LegionSeries.ThinkBook, 8, 9, false, FanType.Gpu, 0x1808, 0x1845, 0x1846),
            (LegionSeries.ThinkBook, 8, 9, false, FanType.System, 0x1807, 0x181E, 0x181F),

            (null, 0, int.MaxValue, false, FanType.Gpu,    0x1806, 0x1820, 0x1821),
            (null, 0, int.MaxValue, false, FanType.System, 0x1808, 0x1845, 0x1846),
            (null, 0, int.MaxValue, false, FanType.Cpu,    0x1807, 0x181E, 0x181F),
        };

        var match = mapping.FirstOrDefault(cfg =>
            cfg.IsCtrl == isControlConfig &&

            (isControlConfig || cfg.Type == fanType) &&

            (cfg.Series == null || (mi != null && cfg.Series == mi.Value.LegionSeries)) &&

            (mi == null ? cfg.MinGen == 0 : (mi.Value.Generation >= cfg.MinGen && mi.Value.Generation <= cfg.MaxGen))
        );

        if (match.var1 != 0) return (match.var1, match.var2, match.var3 ?? 0);

        return (0x00, 0x00, 0x00);
    }

    public void Dispose()
    {
        SetRegister().ConfigureAwait(false);
        _cts?.Cancel();
    }
}