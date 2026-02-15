//
// RyzenAdj Commands - High-level API for AMD SMU commands
// Based on Universal-x86-Tuning-Utility and g-helper
// Adapted for LenovoLegionToolkit by Antigravity Agent
//

using System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.RyzenAdj;

/// <summary>
/// Provides high-level API for sending RyzenAdj commands to AMD SMU.
/// Handles power limits, temperature limits, and undervolting.
/// </summary>
public class RyzenAdjCommands : IDisposable
{
    private readonly RyzenSmu _smu;
    private readonly RyzenCpuInfo _cpuInfo;
    private bool _disposed;

    /// <summary>
    /// Gets whether the RyzenAdj commands are available.
    /// </summary>
    public bool IsAvailable => _cpuInfo.IsAmd && _cpuInfo.IsSupported && _smu.IsReady;

    /// <summary>
    /// Gets the CPU information.
    /// </summary>
    public RyzenCpuInfo CpuInfo => _cpuInfo;

    /// <summary>
    /// Initializes a new instance of the RyzenAdjCommands class.
    /// </summary>
    public RyzenAdjCommands()
    {
        _cpuInfo = new RyzenCpuInfo();
        _cpuInfo.Initialize();

        _smu = new RyzenSmu();
        _cpuInfo.ConfigureSmuAddresses(_smu);
    }

    /// <summary>
    /// Initializes SMU communication. Must be called before sending commands.
    /// </summary>
    public void Initialize()
    {
        _smu.Initialize();
    }

    /// <summary>
    /// Deinitializes SMU communication. Call after sending commands.
    /// </summary>
    public void Deinitialize()
    {
        _smu.Deinitialize();
    }

    /// <summary>
    /// Sets the STAPM (Sustained) power limit.
    /// </summary>
    /// <param name="watts">Power limit in watts.</param>
    /// <returns>SMU response status.</returns>
    public RyzenSmu.Status? SetStapmLimit(int watts)
    {
        _smu.Initialize();
        try
        {
            var args = new uint[6];
            args[0] = (uint)(watts * 1000); // Convert to mW
            RyzenSmu.Status? result = null;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.Raven:
                case RyzenFamily.Picasso:
                case RyzenFamily.Dali:
                    result = _smu.SendMp1(0x1A, ref args);
                    break;

                case RyzenFamily.Renoir:
                case RyzenFamily.VanGogh:
                case RyzenFamily.Cezanne:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                    result = _smu.SendMp1(0x14, ref args);
                    result = _smu.SendPsmu(0x31, ref args);
                    break;

                case RyzenFamily.Raphael:
                case RyzenFamily.FireRange:
                    result = _smu.SendMp1(0x3E, ref args);
                    // Note: PSMU 0x53 rejected with CmdRejectedPrereq on Dragon Range
                    break;

                default:
                    Log.Instance.Trace($"[RyzenAdj] STAPM not supported for {_cpuInfo.Family}");
                    break;
            }

            Log.Instance.Trace($"[RyzenAdj] SetStapmLimit({watts}W) = {result}");
            return result;
        }
        finally
        {
            _smu.Deinitialize();
        }
    }

    /// <summary>
    /// Sets the Fast PPT (boost) power limit.
    /// </summary>
    /// <param name="watts">Power limit in watts.</param>
    /// <returns>SMU response status.</returns>
    public RyzenSmu.Status? SetFastLimit(int watts)
    {
        _smu.Initialize();
        try
        {
            var args = new uint[6];
            args[0] = (uint)(watts * 1000);
            RyzenSmu.Status? result = null;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.Raven:
                case RyzenFamily.Picasso:
                case RyzenFamily.Dali:
                    result = _smu.SendMp1(0x1B, ref args);
                    break;

                case RyzenFamily.Renoir:
                case RyzenFamily.VanGogh:
                case RyzenFamily.Cezanne:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                    result = _smu.SendMp1(0x15, ref args);
                    result = _smu.SendPsmu(0x32, ref args);
                    break;

                case RyzenFamily.Raphael:
                case RyzenFamily.FireRange:
                    result = _smu.SendMp1(0x40, ref args);
                    // Note: PSMU 0x55 rejected with CmdRejectedPrereq on Dragon Range
                    break;

                default:
                    Log.Instance.Trace($"[RyzenAdj] FastLimit not supported for {_cpuInfo.Family}");
                    break;
            }

            Log.Instance.Trace($"[RyzenAdj] SetFastLimit({watts}W) = {result}");
            return result;
        }
        finally
        {
            _smu.Deinitialize();
        }
    }

    /// <summary>
    /// Sets the Slow PPT (sustained) power limit.
    /// </summary>
    /// <param name="watts">Power limit in watts.</param>
    /// <returns>SMU response status.</returns>
    public RyzenSmu.Status? SetSlowLimit(int watts)
    {
        _smu.Initialize();
        try
        {
            var args = new uint[6];
            args[0] = (uint)(watts * 1000);
            RyzenSmu.Status? result = null;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.Raven:
                case RyzenFamily.Picasso:
                case RyzenFamily.Dali:
                    result = _smu.SendMp1(0x1C, ref args);
                    break;

                case RyzenFamily.Renoir:
                case RyzenFamily.VanGogh:
                case RyzenFamily.Cezanne:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                    result = _smu.SendMp1(0x16, ref args);
                    result = _smu.SendPsmu(0x33, ref args);
                    result = _smu.SendPsmu(0x34, ref args);
                    break;

                case RyzenFamily.Raphael:
                case RyzenFamily.FireRange:
                    result = _smu.SendMp1(0x3D, ref args);
                    // Note: PSMU 0x54 rejected with CmdRejectedPrereq on Dragon Range
                    break;

                default:
                    Log.Instance.Trace($"[RyzenAdj] SlowLimit not supported for {_cpuInfo.Family}");
                    break;
            }

            Log.Instance.Trace($"[RyzenAdj] SetSlowLimit({watts}W) = {result}");
            return result;
        }
        finally
        {
            _smu.Deinitialize();
        }
    }

    /// <summary>
    /// Sets the CPU temperature limit (TCTL).
    /// </summary>
    /// <param name="celsius">Temperature limit in Celsius (75-98).</param>
    /// <returns>SMU response status.</returns>
    public RyzenSmu.Status? SetTctlTemp(int celsius)
    {
        _smu.Initialize();
        try
        {
            var args = new uint[6];
            args[0] = (uint)celsius;
            RyzenSmu.Status? result = null;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.Zen1Desktop:
                    result = _smu.SendPsmu(0x68, ref args);
                    break;

                case RyzenFamily.Raven:
                case RyzenFamily.Picasso:
                case RyzenFamily.Dali:
                    result = _smu.SendMp1(0x1F, ref args);
                    break;

                case RyzenFamily.Renoir:
                case RyzenFamily.VanGogh:
                case RyzenFamily.Cezanne:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                case RyzenFamily.StrixHalo:
                    result = _smu.SendMp1(0x19, ref args);
                    break;

                case RyzenFamily.Matisse:
                case RyzenFamily.Vermeer:
                    result = _smu.SendMp1(0x23, ref args);
                    result = _smu.SendPsmu(0x56, ref args);
                    break;

                case RyzenFamily.Raphael:
                case RyzenFamily.FireRange:
                    result = _smu.SendMp1(0x3F, ref args);
                    result = _smu.SendPsmu(0x59, ref args);
                    break;

                default:
                    Log.Instance.Trace($"[RyzenAdj] TctlTemp not supported for {_cpuInfo.Family}");
                    break;
            }

            Log.Instance.Trace($"[RyzenAdj] SetTctlTemp({celsius}°C) = {result}");
            return result;
        }
        finally
        {
            _smu.Deinitialize();
        }
    }

    /// <summary>
    /// Sets the All-Core Curve Optimizer value (CPU undervolting).
    /// </summary>
    /// <param name="offset">Curve Optimizer offset (-40 to 0).</param>
    /// <returns>SMU response status.</returns>
    public RyzenSmu.Status? SetAllCoreCurveOptimizer(int offset)
    {
        _smu.Initialize();
        try
        {
            // Convert signed offset to unsigned value for SMU
            uint uvalue = Convert.ToUInt32(0x100000 - (uint)(-1 * offset));

            var args = new uint[6];
            args[0] = uvalue;
            RyzenSmu.Status? result = null;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.Renoir:
                case RyzenFamily.Cezanne:
                    result = _smu.SendMp1(0x55, ref args);
                    result = _smu.SendPsmu(0xB1, ref args);
                    break;

                case RyzenFamily.Matisse:
                case RyzenFamily.Vermeer:
                    result = _smu.SendMp1(0x36, ref args);
                    result = _smu.SendPsmu(0x0B, ref args);
                    break;

                case RyzenFamily.VanGogh:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                    result = _smu.SendPsmu(0x5D, ref args);
                    break;

                case RyzenFamily.StrixHalo:
                    result = _smu.SendMp1(0x4C, ref args);
                    result = _smu.SendPsmu(0x5D, ref args);
                    break;

                case RyzenFamily.Raphael:
                case RyzenFamily.FireRange:
                    result = _smu.SendPsmu(0x07, ref args);
                    break;

                default:
                    Log.Instance.Trace($"[RyzenAdj] AllCoreCO not supported for {_cpuInfo.Family}");
                    break;
            }

            Log.Instance.Trace($"[RyzenAdj] SetAllCoreCurveOptimizer({offset}) = {result}");
            return result;
        }
        finally
        {
            _smu.Deinitialize();
        }
    }

    /// <summary>
    /// Sets the iGPU Curve Optimizer value (iGPU undervolting).
    /// </summary>
    /// <param name="offset">Curve Optimizer offset (-30 to 0).</param>
    /// <returns>SMU response status.</returns>
    public RyzenSmu.Status? SetIgpuCurveOptimizer(int offset)
    {
        _smu.Initialize();
        try
        {
            uint uvalue = Convert.ToUInt32(0x100000 - (uint)(-1 * offset));

            var args = new uint[6];
            args[0] = uvalue;
            RyzenSmu.Status? result = null;

            switch (_cpuInfo.Family)
            {
                case RyzenFamily.Renoir:
                case RyzenFamily.Cezanne:
                    result = _smu.SendMp1(0x64, ref args);
                    result = _smu.SendPsmu(0x57, ref args);
                    break;

                case RyzenFamily.VanGogh:
                case RyzenFamily.Rembrandt:
                case RyzenFamily.Phoenix:
                case RyzenFamily.Mendocino:
                case RyzenFamily.HawkPoint:
                case RyzenFamily.StrixPoint:
                case RyzenFamily.StrixHalo:
                    result = _smu.SendPsmu(0xB7, ref args);
                    break;

                default:
                    Log.Instance.Trace($"[RyzenAdj] iGPU CO not supported for {_cpuInfo.Family}");
                    break;
            }

            Log.Instance.Trace($"[RyzenAdj] SetIgpuCurveOptimizer({offset}) = {result}");
            return result;
        }
        finally
        {
            _smu.Deinitialize();
        }
    }

    /// <summary>
    /// Releases all resources used by the RyzenAdjCommands.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _smu.Dispose();
        }

        _disposed = true;
    }

    ~RyzenAdjCommands()
    {
        Dispose(false);
    }
}
