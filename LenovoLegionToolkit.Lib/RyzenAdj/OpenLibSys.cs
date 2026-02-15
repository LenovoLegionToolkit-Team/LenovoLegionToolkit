//-----------------------------------------------------------------------------
//     Author : hiyohiyo
//       Mail : hiyohiyo@crystalmark.info
//        Web : http://openlibsys.org/
//    License : The modified BSD license
//
//                     Copyright 2007-2009 OpenLibSys.org. All rights reserved.
//-----------------------------------------------------------------------------
// This is support library for WinRing0 1.3.x.
// Adapted for LenovoLegionToolkit by Antigravity Agent

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace LenovoLegionToolkit.Lib.RyzenAdj;

/// <summary>
/// Wrapper class for WinRing0 driver providing low-level hardware access.
/// Used for SMU communication on AMD Ryzen processors.
/// </summary>
public class OpenLibSys : IDisposable
{
    private const string DllNameX64 = "WinRing0x64.dll";
    private const string DllName = "WinRing0.dll";

    /// <summary>
    /// Status codes for this support library.
    /// </summary>
    public enum Status
    {
        NoError = 0,
        DllNotFound = 1,
        DllIncorrectVersion = 2,
        DllInitializeError = 3,
    }

    /// <summary>
    /// Status codes returned by WinRing0 DLL.
    /// </summary>
    public enum OlsDllStatus
    {
        OlsDllNoError = 0,
        OlsDllUnsupportedPlatform = 1,
        OlsDllDriverNotLoaded = 2,
        OlsDllDriverNotFound = 3,
        OlsDllDriverUnloaded = 4,
        OlsDllDriverNotLoadedOnNetwork = 5,
        OlsDllUnknownError = 9
    }

    /// <summary>
    /// Status codes returned by WinRing0 DLL.
    /// </summary>
    public enum OlsDriverType
    {
        OlsDriverTypeUnknown = 0,
        OlsDriverTypeWin9X = 1,
        OlsDriverTypeWinNt = 2,
        OlsDriverTypeWinNt4 = 3,
        OlsDriverTypeWinNtX64 = 4,
        OlsDriverTypeWinNtIa64 = 5
    }

    /// <summary>
    /// PCI error codes.
    /// </summary>
    public enum OlsErrorPci : uint
    {
        OlsErrorPciBusNotExist = 0xE0000001,
        OlsErrorPciNoDevice = 0xE0000002,
        OlsErrorPciWriteConfig = 0xE0000003,
        OlsErrorPciReadConfig = 0xE0000004
    }

    [DllImport("kernel32")]
    private static extern nint LoadLibrary(string lpFileName);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(nint hModule);

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = false)]
    private static extern nint GetProcAddress(nint hModule, [MarshalAs(UnmanagedType.LPStr)] string lpProcName);

    [DllImport("kernel32")]
    private static extern uint GetLastError();

    private nint _module = nint.Zero;
    private uint _status = (uint)Status.NoError;
    private bool _disposed;

    /// <summary>
    /// Gets the full path to the loaded DLL.
    /// </summary>
    public string? LoadedDllPath { get; private set; }

    /// <summary>
    /// Gets the last Windows error code.
    /// </summary>
    public uint LastError { get; private set; }

    // Delegate definitions
    public delegate uint GetDllStatusDelegate();
    public delegate uint GetDllVersionDelegate(ref byte major, ref byte minor, ref byte revision, ref byte release);
    public delegate uint GetDriverVersionDelegate(ref byte major, ref byte minor, ref byte revision, ref byte release);
    public delegate uint GetDriverTypeDelegate();
    public delegate int InitializeOlsDelegate();
    public delegate void DeinitializeOlsDelegate();

    public delegate int ReadPciConfigByteExDelegate(uint pciAddress, uint regAddress, ref byte value);
    public delegate int ReadPciConfigWordExDelegate(uint pciAddress, uint regAddress, ref ushort value);
    public delegate int ReadPciConfigDwordExDelegate(uint pciAddress, uint regAddress, ref uint value);
    public delegate int WritePciConfigByteExDelegate(uint pciAddress, uint regAddress, byte value);
    public delegate int WritePciConfigWordExDelegate(uint pciAddress, uint regAddress, ushort value);
    public delegate int WritePciConfigDwordExDelegate(uint pciAddress, uint regAddress, uint value);

    // Function pointers
    public GetDllStatusDelegate? GetDllStatus { get; private set; }
    public GetDllVersionDelegate? GetDllVersion { get; private set; }
    public GetDriverVersionDelegate? GetDriverVersion { get; private set; }
    public GetDriverTypeDelegate? GetDriverType { get; private set; }
    public InitializeOlsDelegate? InitializeOls { get; private set; }
    public DeinitializeOlsDelegate? DeinitializeOls { get; private set; }

    public ReadPciConfigByteExDelegate? ReadPciConfigByteEx { get; private set; }
    public ReadPciConfigWordExDelegate? ReadPciConfigWordEx { get; private set; }
    public ReadPciConfigDwordExDelegate? ReadPciConfigDwordEx { get; private set; }
    public WritePciConfigByteExDelegate? WritePciConfigByteEx { get; private set; }
    public WritePciConfigWordExDelegate? WritePciConfigWordEx { get; private set; }
    public WritePciConfigDwordExDelegate? WritePciConfigDwordEx { get; private set; }

    /// <summary>
    /// Gets whether the driver is properly loaded and ready.
    /// </summary>
    public bool IsReady => _status == (uint)Status.NoError && _module != nint.Zero;

    /// <summary>
    /// Initializes a new instance of the OpenLibSys class.
    /// Loads the WinRing0 driver.
    /// </summary>
    public OpenLibSys()
    {
        var dllName = nint.Size == 8 ? DllNameX64 : DllName;
        
        // Try to find DLL in application directory first
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var fullPath = Path.Combine(appDir, dllName);
        
        if (File.Exists(fullPath))
        {
            LoadedDllPath = fullPath;
            _module = LoadLibrary(fullPath);
        }
        else
        {
            // Fallback to just filename (searches PATH)
            LoadedDllPath = dllName;
            _module = LoadLibrary(dllName);
        }

        if (_module == nint.Zero)
        {
            LastError = GetLastError();
            _status = (uint)Status.DllNotFound;
            return;
        }

        try
        {
            GetDllStatus = GetDelegate<GetDllStatusDelegate>("GetDllStatus");
            GetDllVersion = GetDelegate<GetDllVersionDelegate>("GetDllVersion");
            GetDriverVersion = GetDelegate<GetDriverVersionDelegate>("GetDriverVersion");
            GetDriverType = GetDelegate<GetDriverTypeDelegate>("GetDriverType");
            InitializeOls = GetDelegate<InitializeOlsDelegate>("InitializeOls");
            DeinitializeOls = GetDelegate<DeinitializeOlsDelegate>("DeinitializeOls");

            ReadPciConfigByteEx = GetDelegate<ReadPciConfigByteExDelegate>("ReadPciConfigByteEx");
            ReadPciConfigWordEx = GetDelegate<ReadPciConfigWordExDelegate>("ReadPciConfigWordEx");
            ReadPciConfigDwordEx = GetDelegate<ReadPciConfigDwordExDelegate>("ReadPciConfigDwordEx");
            WritePciConfigByteEx = GetDelegate<WritePciConfigByteExDelegate>("WritePciConfigByteEx");
            WritePciConfigWordEx = GetDelegate<WritePciConfigWordExDelegate>("WritePciConfigWordEx");
            WritePciConfigDwordEx = GetDelegate<WritePciConfigDwordExDelegate>("WritePciConfigDwordEx");

            if (!ValidateDelegates())
            {
                _status = (uint)Status.DllIncorrectVersion;
                return;
            }

            if (InitializeOls!() == 0)
            {
                _status = (uint)Status.DllInitializeError;
            }
        }
        catch
        {
            _status = (uint)Status.DllIncorrectVersion;
        }
    }

    /// <summary>
    /// Gets the current status of the library.
    /// </summary>
    /// <returns>Status code.</returns>
    public uint GetStatus() => _status;

    /// <summary>
    /// Converts Bus, Device, Function to PCI address.
    /// </summary>
    public static uint PciBusDevFunc(uint bus, uint dev, uint func)
    {
        return ((bus & 0xFF) << 8) | ((dev & 0x1F) << 3) | (func & 7);
    }

    /// <summary>
    /// Extracts Bus number from PCI address.
    /// </summary>
    public static uint PciGetBus(uint address) => (address >> 8) & 0xFF;

    /// <summary>
    /// Extracts Device number from PCI address.
    /// </summary>
    public static uint PciGetDev(uint address) => (address >> 3) & 0x1F;

    /// <summary>
    /// Extracts Function number from PCI address.
    /// </summary>
    public static uint PciGetFunc(uint address) => address & 7;

    private T? GetDelegate<T>(string procName) where T : Delegate
    {
        var ptr = GetProcAddress(_module, procName);
        if (ptr == nint.Zero)
            return null;

        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    private bool ValidateDelegates()
    {
        return GetDllStatus != null
            && GetDllVersion != null
            && GetDriverVersion != null
            && GetDriverType != null
            && InitializeOls != null
            && DeinitializeOls != null
            && ReadPciConfigByteEx != null
            && ReadPciConfigWordEx != null
            && ReadPciConfigDwordEx != null
            && WritePciConfigByteEx != null
            && WritePciConfigWordEx != null
            && WritePciConfigDwordEx != null;
    }

    /// <summary>
    /// Releases the WinRing0 driver resources.
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

        if (_module != nint.Zero)
        {
            DeinitializeOls?.Invoke();
            FreeLibrary(_module);
            _module = nint.Zero;
        }

        _disposed = true;
    }

    ~OpenLibSys()
    {
        Dispose(false);
    }
}
