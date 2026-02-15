using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.RyzenAdj;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers;

/// <summary>
/// Controller for RyzenAdj (AMD CPU tuning via SMU).
/// Provides high-level interface for applying RyzenAdj settings.
/// </summary>
public class RyzenAdjController
{
    private readonly RyzenAdjSettings _settings;
    private readonly Lazy<RyzenCpuInfo> _cpuInfo;

    /// <summary>
    /// Gets the detected CPU name.
    /// </summary>
    public string CpuName => _cpuInfo.Value.CpuName;

    /// <summary>
    /// Gets the detected CPU family codename.
    /// </summary>
    public string CpuCodename => _cpuInfo.Value.Codename;

    /// <summary>
    /// Gets the detected CPU family.
    /// </summary>
    public RyzenFamily CpuFamily => _cpuInfo.Value.Family;

    /// <summary>
    /// Gets whether RyzenAdj is supported on this system.
    /// </summary>
    public bool IsSupported => _cpuInfo.Value.IsAmd && _cpuInfo.Value.IsSupported;

    /// <summary>
    /// Gets whether the CPU is AMD (for UI visibility).
    /// </summary>
    public bool IsAmdCpu => _cpuInfo.Value.IsAmd;

    /// <summary>
    /// Gets whether CPU undervolting is supported.
    /// </summary>
    public bool SupportsUndervolting => _cpuInfo.Value.SupportsUndervolting;

    /// <summary>
    /// Gets whether iGPU undervolting is supported.
    /// </summary>
    public bool SupportsIgpuUndervolting => _cpuInfo.Value.SupportsIgpuUndervolting;

    /// <summary>
    /// Gets the current settings store.
    /// </summary>
    public RyzenAdjSettings.RyzenAdjSettingsStore Settings => _settings.Store;

    /// <summary>
    /// Minimum CPU undervolt value.
    /// </summary>
    public int MinCpuUndervolt => _cpuInfo.Value.MinCpuUv;

    /// <summary>
    /// Maximum CPU undervolt value.
    /// </summary>
    public int MaxCpuUndervolt => _cpuInfo.Value.MaxCpuUv;

    /// <summary>
    /// Minimum iGPU undervolt value.
    /// </summary>
    public int MinIgpuUndervolt => _cpuInfo.Value.MinIgpuUv;

    /// <summary>
    /// Maximum iGPU undervolt value.
    /// </summary>
    public int MaxIgpuUndervolt => _cpuInfo.Value.MaxIgpuUv;

    /// <summary>
    /// Minimum temperature limit.
    /// </summary>
    public int MinTempLimit => RyzenCpuInfo.MinTemp;

    /// <summary>
    /// Maximum temperature limit.
    /// </summary>
    public int MaxTempLimit => RyzenCpuInfo.MaxTemp;

    /// <summary>
    /// Initializes a new instance of the RyzenAdjController class.
    /// </summary>
    /// <param name="settings">RyzenAdj settings instance.</param>
    public RyzenAdjController(RyzenAdjSettings settings)
    {
        _settings = settings;
        _cpuInfo = new Lazy<RyzenCpuInfo>(() =>
        {
            var info = new RyzenCpuInfo();
            info.Initialize();
            return info;
        });
    }

    /// <summary>
    /// Initializes CPU detection. Call this on application startup.
    /// </summary>
    public void Initialize()
    {
        _ = _cpuInfo.Value; // Force initialization
        Log.Instance.Trace($"[RyzenAdjController] Initialized: {CpuName} ({CpuCodename}), Supported={IsSupported}");
    }

    /// <summary>
    /// Checks if the WinRing0 driver is available.
    /// </summary>
    /// <returns>True if driver is available.</returns>
    public bool CheckDriverAvailable()
    {
        try
        {
            using var ols = new OpenLibSys();
            
            Log.Instance.Trace($"[RyzenAdjController] WinRing0 LoadedPath: {ols.LoadedDllPath}");
            Log.Instance.Trace($"[RyzenAdjController] WinRing0 IsReady: {ols.IsReady}");
            Log.Instance.Trace($"[RyzenAdjController] WinRing0 Status: {ols.GetStatus()}");
            Log.Instance.Trace($"[RyzenAdjController] WinRing0 LastError: {ols.LastError}");
            
            if (ols.IsReady && ols.GetDllStatus != null)
            {
                var dllStatus = ols.GetDllStatus();
                Log.Instance.Trace($"[RyzenAdjController] WinRing0 DllStatus: {dllStatus} ({(OpenLibSys.OlsDllStatus)dllStatus})");
                return dllStatus == (uint)OpenLibSys.OlsDllStatus.OlsDllNoError;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[RyzenAdjController] Driver check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Applies all configured RyzenAdj settings.
    /// </summary>
    /// <returns>True if all settings were applied successfully.</returns>
    public async Task<bool> ApplySettingsAsync()
    {
        if (!IsSupported)
        {
            Log.Instance.Trace($"[RyzenAdjController] Cannot apply - CPU not supported");
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                using var commands = new RyzenAdjCommands();
                var store = _settings.Store;
                bool allSuccess = true;

                // Power Limits
                if (store.StapmLimitEnabled && store.StapmLimit.HasValue)
                {
                    var result = commands.SetStapmLimit(store.StapmLimit.Value);
                    allSuccess &= result == RyzenSmu.Status.Ok;
                }

                if (store.SlowLimitEnabled && store.SlowLimit.HasValue)
                {
                    var result = commands.SetSlowLimit(store.SlowLimit.Value);
                    allSuccess &= result == RyzenSmu.Status.Ok;
                }

                if (store.FastLimitEnabled && store.FastLimit.HasValue)
                {
                    var result = commands.SetFastLimit(store.FastLimit.Value);
                    allSuccess &= result == RyzenSmu.Status.Ok;
                }

                // Temperature Limit
                if (store.CpuTempLimitEnabled && store.CpuTempLimit.HasValue)
                {
                    var result = commands.SetTctlTemp(store.CpuTempLimit.Value);
                    allSuccess &= result == RyzenSmu.Status.Ok;
                }

                // Undervolting
                if (store.CpuUndervoltEnabled && store.CpuUndervolt.HasValue && SupportsUndervolting)
                {
                    var result = commands.SetAllCoreCurveOptimizer(store.CpuUndervolt.Value);
                    allSuccess &= result == RyzenSmu.Status.Ok;
                }

                if (store.IgpuUndervoltEnabled && store.IgpuUndervolt.HasValue && SupportsIgpuUndervolting)
                {
                    var result = commands.SetIgpuCurveOptimizer(store.IgpuUndervolt.Value);
                    allSuccess &= result == RyzenSmu.Status.Ok;
                }

                Log.Instance.Trace($"[RyzenAdjController] Applied settings: Success={allSuccess}");
                return allSuccess;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"[RyzenAdjController] Apply failed: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Applies a single STAPM limit value.
    /// </summary>
    public async Task<bool> SetStapmLimitAsync(int watts)
    {
        return await RunCommandAsync(cmd => cmd.SetStapmLimit(watts));
    }

    /// <summary>
    /// Applies a single Slow PPT limit value.
    /// </summary>
    public async Task<bool> SetSlowLimitAsync(int watts)
    {
        return await RunCommandAsync(cmd => cmd.SetSlowLimit(watts));
    }

    /// <summary>
    /// Applies a single Fast PPT limit value.
    /// </summary>
    public async Task<bool> SetFastLimitAsync(int watts)
    {
        return await RunCommandAsync(cmd => cmd.SetFastLimit(watts));
    }

    /// <summary>
    /// Applies a single CPU temperature limit value.
    /// </summary>
    public async Task<bool> SetTempLimitAsync(int celsius)
    {
        return await RunCommandAsync(cmd => cmd.SetTctlTemp(celsius));
    }

    /// <summary>
    /// Applies a single CPU undervolt value.
    /// </summary>
    public async Task<bool> SetCpuUndervoltAsync(int offset)
    {
        return await RunCommandAsync(cmd => cmd.SetAllCoreCurveOptimizer(offset));
    }

    /// <summary>
    /// Applies a single iGPU undervolt value.
    /// </summary>
    public async Task<bool> SetIgpuUndervoltAsync(int offset)
    {
        return await RunCommandAsync(cmd => cmd.SetIgpuCurveOptimizer(offset));
    }

    /// <summary>
    /// Restores system default SMU values (stock power limits).
    /// This applies immediately without requiring a reboot.
    /// </summary>
    /// <returns>True if all defaults were applied successfully.</returns>
    public async Task<bool> RestoreSystemDefaultsAsync()
    {
        if (!IsSupported)
        {
            Log.Instance.Trace($"[RyzenAdjController] Cannot restore defaults - CPU not supported");
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                using var commands = new RyzenAdjCommands();
                bool allSuccess = true;

                // Default power limits (typical stock values for Dragon Range/Raphael)
                // These can be adjusted based on specific CPU model
                const int defaultStapm = 65;  // 65W sustained
                const int defaultSlow = 80;   // 80W slow limit
                const int defaultFast = 95;   // 95W boost
                const int defaultTemp = 95;   // 95°C temp limit (stock)
                const int defaultUv = 0;      // No undervolt

                Log.Instance.Trace($"[RyzenAdjController] Restoring system defaults...");

                // Apply default power limits
                var r1 = commands.SetStapmLimit(defaultStapm);
                allSuccess &= r1 == RyzenSmu.Status.Ok;

                var r2 = commands.SetSlowLimit(defaultSlow);
                allSuccess &= r2 == RyzenSmu.Status.Ok;

                var r3 = commands.SetFastLimit(defaultFast);
                allSuccess &= r3 == RyzenSmu.Status.Ok;

                // Apply default temp limit
                var r4 = commands.SetTctlTemp(defaultTemp);
                allSuccess &= r4 == RyzenSmu.Status.Ok;

                // Reset undervolting to 0
                if (SupportsUndervolting)
                {
                    var r5 = commands.SetAllCoreCurveOptimizer(defaultUv);
                    allSuccess &= r5 == RyzenSmu.Status.Ok;
                }

                if (SupportsIgpuUndervolting)
                {
                    var r6 = commands.SetIgpuCurveOptimizer(defaultUv);
                    allSuccess &= r6 == RyzenSmu.Status.Ok;
                }

                Log.Instance.Trace($"[RyzenAdjController] Restored defaults: Success={allSuccess}");
                return allSuccess;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"[RyzenAdjController] Restore defaults failed: {ex.Message}");
                return false;
            }
        });
    }

    /// <summary>
    /// Saves current settings to disk.
    /// </summary>
    public void SaveSettings()
    {
        _settings.SynchronizeStore();
        Log.Instance.Trace($"[RyzenAdjController] Settings saved");
    }

    private async Task<bool> RunCommandAsync(Func<RyzenAdjCommands, RyzenSmu.Status?> action)
    {
        if (!IsSupported)
            return false;

        return await Task.Run(() =>
        {
            try
            {
                using var commands = new RyzenAdjCommands();
                var result = action(commands);
                return result == RyzenSmu.Status.Ok;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"[RyzenAdjController] Command failed: {ex.Message}");
                return false;
            }
        });
    }
}
