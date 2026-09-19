using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class FnKeysDisabler : AbstractSoftwareDisabler
{
    protected override IEnumerable<string> ScheduledTasksPaths => [];
    protected override IEnumerable<string> ServiceNames => ["LenovoFnAndFunctionKeys"];
    protected override IEnumerable<string> ProcessNames => ["LenovoUtilityUI", "LenovoUtilityService", "LenovoSmartKey"];

    protected override IEnumerable<string> OwnershipPathMarkers => ["LenovoUtilityService", "LenovoUtilityUI", "LenovoSmartKey", "LenovoFnAndFunctionKeys"];

    protected override async Task ApplyStateAsync(bool enabled)
    {
        await base.ApplyStateAsync(enabled).ConfigureAwait(false);

        SetUwpStartup("LenovoUtility", "LenovoUtilityID", enabled);
    }

    protected override IEnumerable<string> RunningProcesses()
    {
        var result = base.RunningProcesses().ToList();

        foreach (var process in Process.GetProcessesByName("utility"))
        {
            try
            {
                using (process)
                {
                    var description = process.MainModule?.FileVersionInfo.FileDescription;
                    if (description is null)
                    {
                        continue;
                    }

                    if (description.Equals("Lenovo Hotkeys", StringComparison.InvariantCultureIgnoreCase))
                    {
                        result.Add(process.ProcessName);
                    }
                }
            }
            catch {  /* Ignore */ }
        }

        return result;
    }

    protected override async Task KillProcessesAsync()
    {
        await base.KillProcessesAsync().ConfigureAwait(false);

        foreach (var process in Process.GetProcessesByName("utility"))
        {
            try
            {
                using (process)
                {
                    var description = process.MainModule?.FileVersionInfo.FileDescription;
                    if (description is null)
                    {
                        continue;
                    }

                    if (!description.Equals("Lenovo Hotkeys", StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }

                    process.Kill();
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch {  /* Ignore */ }
        }
    }

    private static void SetUwpStartup(string appPattern, string subKeyName, bool enabled)
    {
        const string hive = "HKEY_CURRENT_USER";
        const string subKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";
        const string valueName = "State";

        var startupKey = Registry.GetSubKeys(hive, subKey).FirstOrDefault(s => s.Contains(appPattern, StringComparison.CurrentCultureIgnoreCase));
        if (startupKey is null)
        {
            return;
        }

        startupKey = Path.Combine(startupKey, subKeyName);

        Registry.SetValue(hive, startupKey, valueName, enabled ? 0x2 : 0x1);
    }
}
