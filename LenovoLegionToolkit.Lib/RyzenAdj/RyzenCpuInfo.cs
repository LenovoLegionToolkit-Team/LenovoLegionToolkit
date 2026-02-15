//
// AMD Ryzen CPU Detection and Configuration
// Based on Universal-x86-Tuning-Utility and g-helper
// Adapted for LenovoLegionToolkit by Antigravity Agent
//

using System;
using System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.RyzenAdj;

/// <summary>
/// AMD Ryzen CPU family identifiers.
/// Used to determine correct SMU addresses and command codes.
/// </summary>
public enum RyzenFamily
{
    Unknown = -2,
    Unsupported = -1,
    Zen1Desktop = 0,     // Zen1/+ Desktop
    Raven = 1,           // Raven Ridge
    Picasso = 2,         // Picasso
    Dali = 3,            // Dali
    Renoir = 4,          // Renoir/Lucienne
    Matisse = 5,         // Matisse (Desktop)
    VanGogh = 6,         // Van Gogh (Steam Deck)
    Vermeer = 7,         // Vermeer (Desktop)
    Cezanne = 8,         // Cezanne/Barcelo
    Rembrandt = 9,       // Rembrandt
    Phoenix = 10,        // Phoenix
    Raphael = 11,        // Raphael/Dragon Range (Desktop)
    Mendocino = 12,      // Mendocino
    HawkPoint = 13,      // Hawk Point
    StrixPoint = 14,     // Strix Point
    StrixHalo = 15,      // Strix Halo
    FireRange = 16,      // Fire Range
}

/// <summary>
/// Provides AMD Ryzen CPU detection and SMU address configuration.
/// </summary>
public class RyzenCpuInfo
{
    /// <summary>
    /// Minimum UV value for CPU (Curve Optimizer).
    /// </summary>
    public int MinCpuUv { get; private set; } = -40;

    /// <summary>
    /// Maximum UV value for CPU (Curve Optimizer).
    /// </summary>
    public int MaxCpuUv { get; private set; } = 0;

    /// <summary>
    /// Minimum UV value for iGPU (Curve Optimizer).
    /// </summary>
    public int MinIgpuUv { get; private set; } = -30;

    /// <summary>
    /// Maximum UV value for iGPU (Curve Optimizer).
    /// </summary>
    public int MaxIgpuUv { get; private set; } = 0;

    /// <summary>
    /// Minimum CPU temperature limit.
    /// </summary>
    public const int MinTemp = 75;

    /// <summary>
    /// Maximum CPU temperature limit.
    /// </summary>
    public const int MaxTemp = 98;

    /// <summary>
    /// Default CPU temperature limit.
    /// </summary>
    public const int DefaultTemp = 96;

    /// <summary>
    /// Detected CPU family ID.
    /// </summary>
    public RyzenFamily Family { get; private set; } = RyzenFamily.Unknown;

    /// <summary>
    /// Full CPU name from WMI.
    /// </summary>
    public string CpuName { get; private set; } = string.Empty;

    /// <summary>
    /// CPU model caption from WMI (e.g., "Family 25 Model 116").
    /// </summary>
    public string CpuModel { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether an AMD CPU was detected.
    /// </summary>
    public bool IsAmd => CpuName.Contains("AMD") || CpuName.Contains("Ryzen") || 
                         CpuName.Contains("Athlon") || CpuName.Contains("Radeon") ||
                         CpuName.Contains("AMD Custom APU");

    /// <summary>
    /// Gets whether an Intel CPU was detected.
    /// </summary>
    public bool IsIntel => CpuName.Contains("Intel") || CpuName.Contains("Core") || 
                           CpuName.Contains("Pentium") || CpuName.Contains("Celeron") || 
                           CpuName.Contains("Atom");

    /// <summary>
    /// Gets whether CPU undervolting is supported.
    /// </summary>
    public bool SupportsUndervolting => CpuName.Contains("RYZEN AI MAX") || 
                                         CpuName.Contains("Ryzen AI 9") ||
                                         CpuName.Contains("Ryzen 9") || 
                                         CpuName.Contains("4900H") || 
                                         CpuName.Contains("4800H") || 
                                         CpuName.Contains("4600H");

    /// <summary>
    /// Gets whether iGPU undervolting is supported.
    /// </summary>
    public bool SupportsIgpuUndervolting => CpuName.Contains("RYZEN AI MAX") ||
                                             CpuName.Contains("6900H") || 
                                             CpuName.Contains("7945H") || 
                                             CpuName.Contains("7845H");

    /// <summary>
    /// Gets whether the CPU is supported for RyzenAdj.
    /// </summary>
    public bool IsSupported => Family != RyzenFamily.Unknown && Family != RyzenFamily.Unsupported;

    /// <summary>
    /// Gets the human-readable codename for the CPU family.
    /// </summary>
    public string Codename => Family switch
    {
        RyzenFamily.Zen1Desktop => "Zen1/+ Desktop",
        RyzenFamily.Raven => "Raven Ridge",
        RyzenFamily.Picasso => "Picasso",
        RyzenFamily.Dali => "Dali",
        RyzenFamily.Renoir => "Renoir/Lucienne",
        RyzenFamily.Matisse => "Matisse",
        RyzenFamily.VanGogh => "Van Gogh",
        RyzenFamily.Vermeer => "Vermeer",
        RyzenFamily.Cezanne => "Cezanne/Barcelo",
        RyzenFamily.Rembrandt => "Rembrandt",
        RyzenFamily.Phoenix => "Phoenix",
        RyzenFamily.Raphael => "Raphael/Dragon Range",
        RyzenFamily.Mendocino => "Mendocino",
        RyzenFamily.HawkPoint => "Hawk Point",
        RyzenFamily.StrixPoint => "Strix Point",
        RyzenFamily.StrixHalo => "Strix Halo",
        RyzenFamily.FireRange => "Fire Range",
        _ => "Unknown"
    };

    /// <summary>
    /// Initializes CPU detection.
    /// </summary>
    public void Initialize()
    {
        DetectCpu();
        Family = DetectFamily();
        Log.Instance.Trace($"[RyzenCpuInfo] CPU: {CpuName} | Model: {CpuModel} | Family: {Family} ({Codename})");
    }

    /// <summary>
    /// Configures SMU addresses on the provided RyzenSmu instance based on detected CPU family.
    /// </summary>
    /// <param name="smu">The RyzenSmu instance to configure.</param>
    public void ConfigureSmuAddresses(RyzenSmu smu)
    {
        smu.SmuPciAddr = 0x00000000;
        smu.SmuOffsetAddr = 0xB8;
        smu.SmuOffsetData = 0xBC;

        switch (Family)
        {
            case RyzenFamily.Zen1Desktop:
                smu.Mp1AddrMsg = 0x3B10528;
                smu.Mp1AddrRsp = 0x3B10564;
                smu.Mp1AddrArg = 0x3B10598;
                smu.PsmuAddrMsg = 0x3B1051C;
                smu.PsmuAddrRsp = 0x3B10568;
                smu.PsmuAddrArg = 0x3B10590;
                break;

            case RyzenFamily.Raven:
            case RyzenFamily.Picasso:
            case RyzenFamily.Dali:
            case RyzenFamily.Renoir:
            case RyzenFamily.Cezanne:
                smu.Mp1AddrMsg = 0x3B10528;
                smu.Mp1AddrRsp = 0x3B10564;
                smu.Mp1AddrArg = 0x3B10998;
                smu.PsmuAddrMsg = 0x3B10A20;
                smu.PsmuAddrRsp = 0x3B10A80;
                smu.PsmuAddrArg = 0x3B10A88;
                break;

            case RyzenFamily.VanGogh:
            case RyzenFamily.Rembrandt:
            case RyzenFamily.Phoenix:
            case RyzenFamily.Mendocino:
            case RyzenFamily.HawkPoint:
            case RyzenFamily.StrixHalo:
                smu.Mp1AddrMsg = 0x3B10528;
                smu.Mp1AddrRsp = 0x3B10578;
                smu.Mp1AddrArg = 0x3B10998;
                smu.PsmuAddrMsg = 0x3B10A20;
                smu.PsmuAddrRsp = 0x3B10A80;
                smu.PsmuAddrArg = 0x3B10A88;
                break;

            case RyzenFamily.StrixPoint:
                smu.Mp1AddrMsg = 0x3B10928;
                smu.Mp1AddrRsp = 0x3B10978;
                smu.Mp1AddrArg = 0x3B10998;
                smu.PsmuAddrMsg = 0x3B10A20;
                smu.PsmuAddrRsp = 0x3B10A80;
                smu.PsmuAddrArg = 0x3B10A88;
                break;

            case RyzenFamily.Matisse:
            case RyzenFamily.Vermeer:
                smu.Mp1AddrMsg = 0x3B10530;
                smu.Mp1AddrRsp = 0x3B1057C;
                smu.Mp1AddrArg = 0x3B109C4;
                smu.PsmuAddrMsg = 0x3B10524;
                smu.PsmuAddrRsp = 0x3B10570;
                smu.PsmuAddrArg = 0x3B10A40;
                break;

            case RyzenFamily.Raphael:
            case RyzenFamily.FireRange:
                smu.Mp1AddrMsg = 0x3B10530;
                smu.Mp1AddrRsp = 0x3B1057C;
                smu.Mp1AddrArg = 0x3B109C4;
                smu.PsmuAddrMsg = 0x03B10524;
                smu.PsmuAddrRsp = 0x03B10570;
                smu.PsmuAddrArg = 0x03B10A40;
                break;

            default:
                // Unknown CPU - zero out addresses
                smu.Mp1AddrMsg = 0;
                smu.Mp1AddrRsp = 0;
                smu.Mp1AddrArg = 0;
                smu.PsmuAddrMsg = 0;
                smu.PsmuAddrRsp = 0;
                smu.PsmuAddrArg = 0;
                break;
        }

        Log.Instance.Trace($"[RyzenCpuInfo] SMU addresses configured for {Family}");
    }

    private void DetectCpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("select * from Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                CpuName = obj["Name"]?.ToString() ?? string.Empty;
                CpuModel = obj["Caption"]?.ToString() ?? string.Empty;
                break; // We only need the first processor
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[RyzenCpuInfo] Failed to detect CPU: {ex.Message}");
        }
    }

    private RyzenFamily DetectFamily()
    {
        if (string.IsNullOrEmpty(CpuModel))
            return RyzenFamily.Unknown;

        // Zen1/+ Desktop
        if (CpuModel.Contains("Model 1") || CpuModel.Contains("Model 8"))
            return RyzenFamily.Zen1Desktop;

        // Raven
        if (CpuModel.Contains("Model 17"))
            return RyzenFamily.Raven;

        // Picasso
        if (CpuModel.Contains("Model 24"))
            return RyzenFamily.Picasso;

        // Dali
        if (CpuModel.Contains("Family 23") && CpuModel.Contains("Model 32"))
            return RyzenFamily.Dali;

        // Vermeer
        if (CpuModel.Contains("Model 33"))
            return RyzenFamily.Vermeer;

        // Renoir/Lucienne
        if (CpuModel.Contains("Model 96") || CpuModel.Contains("Model 104"))
            return RyzenFamily.Renoir;

        // Van Gogh
        if (CpuModel.Contains("Model 144"))
            return RyzenFamily.VanGogh;

        // Cezanne/Barcelo
        if (CpuModel.Contains("Model 80"))
            return RyzenFamily.Cezanne;

        // Rembrandt
        if (CpuModel.Contains("Family 25") && (CpuModel.Contains("Model 63") || CpuModel.Contains("Model 68")))
            return RyzenFamily.Rembrandt;

        // Phoenix
        if (CpuModel.Contains("Model 116") || CpuModel.Contains("Model 120"))
            return RyzenFamily.Phoenix;

        // Raphael/Dragon Range
        if (CpuModel.Contains("Model 97"))
            return RyzenFamily.Raphael;

        // Mendocino
        if (CpuModel.Contains("Model 160"))
            return RyzenFamily.Mendocino;

        // Hawk Point
        if (CpuModel.Contains("Model 117"))
            return RyzenFamily.HawkPoint;

        // Strix Point
        if (CpuModel.Contains("Family 26") && CpuModel.Contains("Model 36"))
            return RyzenFamily.StrixPoint;

        // Strix Halo
        if (CpuModel.Contains("Family 26") && CpuModel.Contains("Model 112"))
            return RyzenFamily.StrixHalo;

        // Fire Range
        if (CpuModel.Contains("Family 26") && CpuModel.Contains("Model 68") && CpuName.Contains("HX"))
            return RyzenFamily.FireRange;

        return RyzenFamily.Unsupported;
    }
}
