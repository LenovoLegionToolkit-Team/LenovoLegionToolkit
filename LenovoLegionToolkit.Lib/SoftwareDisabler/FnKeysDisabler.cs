using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class FnKeysDisabler : AbstractSoftwareDisabler
{
    protected override SoftwareDisablerPolicy Policy { get; } = new()
    {
        ServiceNames = ["LenovoFnAndFunctionKeys"],
        ProcessNames = ["LenovoUtilityUI", "LenovoUtilityService", "LenovoSmartKey"],
        OwnershipPathMarkers = ["LenovoUtilityService", "LenovoUtilityUI", "LenovoSmartKey", "LenovoFnAndFunctionKeys"]
    };

    private const string UWP_STARTUP_STATE_SNAPSHOT_KEY = "FnKeys.UwpStartupState";

    private protected override void CaptureAdditionalSnapshot(SoftwareDisablerSnapshot snapshot)
    {
        if (TryGetUwpStartupKey("LenovoUtility", "LenovoUtilityID", out var startupKey))
        {
            var state = Registry.GetValue("HKEY_CURRENT_USER", startupKey, "State", 0x2);
            snapshot.AdditionalValues[UWP_STARTUP_STATE_SNAPSHOT_KEY] = state.ToString(CultureInfo.InvariantCulture);
        }
    }

    private protected override IEnumerable<string> GetAdditionalEnabledResources()
    {
        if (!TryGetUwpStartupKey("LenovoUtility", "LenovoUtilityID", out var startupKey))
        {
            return [];
        }

        var state = Registry.GetValue("HKEY_CURRENT_USER", startupKey, "State", 0x2);
        return state == 0x1 ? [] : ["UWP startup: LenovoUtility"];
    }

    private protected override Task ApplyAdditionalStateAsync(bool enabled, SoftwareDisablerSnapshot? snapshot)
    {
        var state = enabled &&
                    snapshot?.AdditionalValues.TryGetValue(UWP_STARTUP_STATE_SNAPSHOT_KEY, out var value) == true &&
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var capturedState)
            ? capturedState
            : enabled ? 0x2 : 0x1;

        SetUwpStartup("LenovoUtility", "LenovoUtilityID", state);
        return Task.CompletedTask;
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
            catch { }
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
            catch { }
        }
    }

    private static bool TryGetUwpStartupKey(string appPattern, string subKeyName, out string startupKey)
    {
        const string hive = "HKEY_CURRENT_USER";
        const string subKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

        var key = Registry.GetSubKeys(hive, subKey).FirstOrDefault(s => s.Contains(appPattern, StringComparison.CurrentCultureIgnoreCase));
        if (key is null)
        {
            startupKey = string.Empty;
            return false;
        }

        startupKey = Path.Combine(key, subKeyName);
        return true;
    }

    private static void SetUwpStartup(string appPattern, string subKeyName, int state)
    {
        if (!TryGetUwpStartupKey(appPattern, subKeyName, out var startupKey))
        {
            return;
        }

        Registry.SetValue("HKEY_CURRENT_USER", startupKey, "State", state);
    }
}
