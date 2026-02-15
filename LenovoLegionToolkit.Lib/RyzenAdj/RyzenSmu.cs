//
// This is an optimised/simplified version of Ryzen System Management Unit
// from https://github.com/JamesCJ60/Universal-x86-Tuning-Utility
// Adapted for LenovoLegionToolkit by Antigravity Agent
//

using System;
using System.Threading;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.RyzenAdj;

/// <summary>
/// Provides low-level communication with AMD SMU (System Management Unit).
/// Used for sending commands to control power limits, temperature limits, and undervolting.
/// </summary>
public class RyzenSmu : IDisposable
{
    /// <summary>
    /// SMU command response status codes.
    /// </summary>
    public enum Status : int
    {
        Bad = 0x0,
        Ok = 0x1,
        Failed = 0xFF,
        UnknownCmd = 0xFE,
        CmdRejectedPrereq = 0xFD,
        CmdRejectedBusy = 0xFC
    }

    private readonly OpenLibSys _ols;
    private readonly Mutex _smuMutex;
    private bool _isInitialized;
    private bool _disposed;

    private const ushort SmuTimeout = 8192;

    // SMU register addresses (set by RyzenCpuInfo based on CPU family)
    public uint SmuPciAddr { get; set; }
    public uint SmuOffsetAddr { get; set; }
    public uint SmuOffsetData { get; set; }

    public uint Mp1AddrMsg { get; set; }
    public uint Mp1AddrRsp { get; set; }
    public uint Mp1AddrArg { get; set; }

    public uint PsmuAddrMsg { get; set; }
    public uint PsmuAddrRsp { get; set; }
    public uint PsmuAddrArg { get; set; }

    /// <summary>
    /// Gets whether the SMU communication is ready.
    /// </summary>
    public bool IsReady => _ols.IsReady && _isInitialized;

    /// <summary>
    /// Initializes a new instance of the RyzenSmu class.
    /// </summary>
    /// <exception cref="ApplicationException">Thrown when WinRing0 driver fails to load.</exception>
    public RyzenSmu()
    {
        _ols = new OpenLibSys();
        _smuMutex = new Mutex();

        // Check WinRing0 status
        var dllStatus = _ols.GetDllStatus?.Invoke() ?? (uint)OpenLibSys.OlsDllStatus.OlsDllUnknownError;

        switch (dllStatus)
        {
            case (uint)OpenLibSys.OlsDllStatus.OlsDllNoError:
                Log.Instance.Trace($"[RyzenSmu] WinRing0 DLL loaded successfully");
                break;
            case (uint)OpenLibSys.OlsDllStatus.OlsDllDriverNotLoaded:
                throw new ApplicationException("WinRing0: Driver not loaded");
            case (uint)OpenLibSys.OlsDllStatus.OlsDllUnsupportedPlatform:
                throw new ApplicationException("WinRing0: Unsupported platform");
            case (uint)OpenLibSys.OlsDllStatus.OlsDllDriverNotFound:
                throw new ApplicationException("WinRing0: Driver not found");
            case (uint)OpenLibSys.OlsDllStatus.OlsDllDriverUnloaded:
                throw new ApplicationException("WinRing0: Driver unloaded");
            case (uint)OpenLibSys.OlsDllStatus.OlsDllDriverNotLoadedOnNetwork:
                throw new ApplicationException("WinRing0: Driver not loaded on network");
            default:
                throw new ApplicationException("WinRing0: Unknown error");
        }
    }

    /// <summary>
    /// Initializes the SMU communication subsystem.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized)
            return;

        _ols.InitializeOls?.Invoke();

        var status = _ols.GetStatus();
        switch (status)
        {
            case (uint)OpenLibSys.Status.NoError:
                Log.Instance.Trace($"[RyzenSmu] OLS initialized successfully");
                _isInitialized = true;
                break;
            case (uint)OpenLibSys.Status.DllNotFound:
                throw new ApplicationException("WinRing0: DLL not found");
            case (uint)OpenLibSys.Status.DllIncorrectVersion:
                throw new ApplicationException("WinRing0: Incorrect DLL version");
            case (uint)OpenLibSys.Status.DllInitializeError:
                throw new ApplicationException("WinRing0: Initialization error");
        }
    }

    /// <summary>
    /// Deinitializes the SMU communication subsystem.
    /// </summary>
    public void Deinitialize()
    {
        if (!_isInitialized)
            return;

        _ols.DeinitializeOls?.Invoke();
        _isInitialized = false;
    }

    /// <summary>
    /// Sends a command to MP1 (Microprocessor 1) mailbox.
    /// </summary>
    /// <param name="message">The command message.</param>
    /// <param name="arguments">Command arguments (modified in place with response).</param>
    /// <returns>SMU response status.</returns>
    public Status SendMp1(uint message, ref uint[] arguments)
    {
        var result = SendMsg(Mp1AddrMsg, Mp1AddrRsp, Mp1AddrArg, message, ref arguments);
        Log.Instance.Trace($"[RyzenSmu] MP1 msg=0x{message:X} arg[0]={arguments[0]} result={result}");
        return result;
    }

    /// <summary>
    /// Sends a command to PSMU (Platform SMU) mailbox.
    /// </summary>
    /// <param name="message">The command message.</param>
    /// <param name="arguments">Command arguments (modified in place with response).</param>
    /// <returns>SMU response status.</returns>
    public Status SendPsmu(uint message, ref uint[] arguments)
    {
        var result = SendMsg(PsmuAddrMsg, PsmuAddrRsp, PsmuAddrArg, message, ref arguments);
        Log.Instance.Trace($"[RyzenSmu] PSMU msg=0x{message:X} arg[0]={arguments[0]} result={result}");
        return result;
    }

    /// <summary>
    /// Sends a message to SMU via specified mailbox registers.
    /// </summary>
    private Status SendMsg(uint addrMsg, uint addrRsp, uint addrArg, uint msg, ref uint[] args)
    {
        ushort timeout = SmuTimeout;
        var cmdArgs = new uint[6];
        var argsLength = Math.Min(args.Length, cmdArgs.Length);
        uint status = 0;

        for (int i = 0; i < argsLength; ++i)
            cmdArgs[i] = args[i];

        if (_smuMutex.WaitOne(5000))
        {
            try
            {
                // Clear response register
                bool temp;
                do
                {
                    temp = SmuWriteReg(addrRsp, 0);
                } while (!temp && --timeout > 0);

                if (timeout == 0)
                {
                    SmuReadReg(addrRsp, ref status);
                    return (Status)status;
                }

                // Write data
                for (int i = 0; i < cmdArgs.Length; ++i)
                    SmuWriteReg(addrArg + (uint)(i * 4), cmdArgs[i]);

                // Send message
                SmuWriteReg(addrMsg, msg);

                // Wait for completion
                if (!SmuWaitDone(addrRsp))
                {
                    SmuReadReg(addrRsp, ref status);
                    return (Status)status;
                }

                // Read back args
                for (int i = 0; i < args.Length; ++i)
                    SmuReadReg(addrArg + (uint)(i * 4), ref args[i]);

                SmuReadReg(addrRsp, ref status);
            }
            finally
            {
                _smuMutex.ReleaseMutex();
            }
        }

        return (Status)status;
    }

    private bool SmuWaitDone(uint addrRsp)
    {
        ushort timeout = SmuTimeout;
        uint data = 0;
        bool res;

        do
        {
            res = SmuReadReg(addrRsp, ref data);
        } while ((!res || data != 1) && --timeout > 0);

        return timeout != 0 && data == 1;
    }

    private bool SmuWriteReg(uint addr, uint data)
    {
        if (_ols.WritePciConfigDwordEx?.Invoke(SmuPciAddr, SmuOffsetAddr, addr) == 1)
        {
            return _ols.WritePciConfigDwordEx?.Invoke(SmuPciAddr, SmuOffsetData, data) == 1;
        }
        return false;
    }

    private bool SmuReadReg(uint addr, ref uint data)
    {
        if (_ols.WritePciConfigDwordEx?.Invoke(SmuPciAddr, SmuOffsetAddr, addr) == 1)
        {
            return _ols.ReadPciConfigDwordEx?.Invoke(SmuPciAddr, SmuOffsetData, ref data) == 1;
        }
        return false;
    }

    /// <summary>
    /// Releases all resources used by the RyzenSmu.
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
            Deinitialize();
            _ols.Dispose();
            _smuMutex.Dispose();
        }

        _disposed = true;
    }

    ~RyzenSmu()
    {
        Dispose(false);
    }
}
