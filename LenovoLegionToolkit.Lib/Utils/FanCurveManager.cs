using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.View;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using UniversalFanControl.Lib;

public class FanCurveManager : IDisposable
{
    public ObservableCollection<FanCurveEntry> Curves { get; } = new();
    public bool IsLegionMode { get; set; }
    public float LegionLowTempThreshold { get; set; } = 40f;
    public int AccelerationDcrReduction { get; set; } = 1;
    public int DecelerationDcrReduction { get; set; } = 2;
    public int LogicInterval { get; set; } = 500;
    public int UiUpdateInterval { get; set; } = 500;

    private readonly SensorsGroupController _sensors;
    private readonly FanControl _fanHardware;
    private readonly Dictionary<FanType, FanControlViewModel> _activeViewModels = new();
    private CancellationTokenSource? _cts;

    public FanCurveManager(SensorsGroupController sensors)
    {
        _sensors = sensors;
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

    private void StartControlLoop()
    {
        _cts = new CancellationTokenSource();
        Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await _sensors.UpdateAsync();
                    float cpuTemp = await _sensors.GetCpuTemperatureAsync();
                    float gpuTemp = await _sensors.GetGpuTemperatureAsync();

                    List<FanControlViewModel> vms;
                    lock (_activeViewModels)
                    {
                        vms = _activeViewModels.Values.ToList();
                    }

                    foreach (var vm in vms)
                    {
                        float temp = vm.FanType == FanType.Gpu ? gpuTemp : cpuTemp;
                        if (temp < 0) temp = 0;

                        byte? pwm = vm.CalculateTargetPWM(temp);
                        if (pwm.HasValue && _fanHardware != null)
                        {
                            var regs = GetRegisters(vm.FanType);
                            _fanHardware.SetFanPwm(regs.pwm, pwm.Value);

                            int rpm = _fanHardware.GetFanRpm(regs.rpmLow, regs.rpmHigh);
                            vm.UpdateMonitoring(temp, rpm, pwm.Value);
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
        FanType.Gpu => (0x1806, 0x181E, 0x181F),
        FanType.System => (0x1808, 0x1845, 0x1846),
        _ => (0x1807, 0x1820, 0x1821)
    };

    public void Dispose() => _cts?.Cancel();
}
