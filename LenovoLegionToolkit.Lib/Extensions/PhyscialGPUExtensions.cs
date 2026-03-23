using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class NVAPIExtensions
{
    private static readonly string[] Exclusions =
    [
        "dwm.exe",
        "explorer.exe",
    ];

    public static (List<Process> All, List<Process> Filtered) GetActiveProcesses(PhysicalGPU gpu)
    {
        var allProcesses = new List<Process>();
        var filteredProcesses = new List<Process>();
        var apps = GPUApi.QueryActiveApps(gpu.Handle);
        
        foreach (var app in apps)
        {
            try
            {
                var process = Process.GetProcessById(app.ProcessId);
                allProcesses.Add(process);
                
                if (!Exclusions.Contains(app.ProcessName, StringComparer.InvariantCultureIgnoreCase))
                {
                    filteredProcesses.Add(process);
                }
            }
            catch (ArgumentException) { }
        }

        return (allProcesses, filteredProcesses);
    }
}
