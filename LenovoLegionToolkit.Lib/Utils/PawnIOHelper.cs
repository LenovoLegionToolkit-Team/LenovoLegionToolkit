using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using Microsoft.Win32;

namespace LenovoLegionToolkit.Lib.Utils;

public static class PawnIOHelper
{
    private static readonly ApplicationSettings ApplicationSettings = IoCContainer.Resolve<ApplicationSettings>();

    private const string REG_KEY_PAWN_IO = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private const string REG_VAL_INSTALL_LOC = "InstallLocation";
    private const string REG_VAL_DISPLAY_VERSION = "DisplayVersion";
    private const string REG_KEY_PAWN_IO_WOW64 = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\PawnIO";
    private const string REG_VAL_INSTALL_DIR = "Install_Dir";
    private const string FOLDER_PAWN_IO = "PawnIO";
    private const string FILE_PAWN_IO_DRIVER = "PawnIO.sys";
    private const string FILE_PAWN_IO_LIB = "PawnIOLib.dll";

    private static readonly Version MinimumPawnIOVersion = new(2, 2, 0, 0);

    public static Version RequiredPawnIOVersion => MinimumPawnIOVersion;

    public static Func<PawnIOState, Task<bool>>? RequestShowDialogAsync;

    public static void OpenPawnIODownloadPage()
    {
        Process.Start("explorer.exe", $"\"https://pawnio.eu/\"");
    }

    public static async Task TryShowPawnIODialogAsync(PawnIOState state, bool disableHardwareSensors = true)
    {
        if (RequestShowDialogAsync == null)
        {
            return;
        }

        bool userClickedYes = await RequestShowDialogAsync.Invoke(state).ConfigureAwait(false);

        if (userClickedYes)
        {
            OpenPawnIODownloadPage();
            return;
        }

        if (disableHardwareSensors)
        {
            ApplicationSettings.Store.EnableHardwareSensors = false;
            ApplicationSettings.Store.UseNewSensorDashboard = false;
            ApplicationSettings.SynchronizeStore();
        }
    }

    public static async Task TryShowPawnIONotFoundDialogAsync(bool disableHardwareSensors = true)
    {
        await TryShowPawnIODialogAsync(PawnIOState.NotInstalled, disableHardwareSensors).ConfigureAwait(false);
    }

    public static void ShowPawnIONotify()
    {
        var state = GetPawnIOState();
        if (state != PawnIOState.Installed)
        {
            TryShowPawnIODialogAsync(state).ConfigureAwait(false);
        }
    }

    public static string? GetInstallPath()
    {
        return Registry.GetValue(REG_KEY_PAWN_IO, REG_VAL_INSTALL_LOC, null) as string
               ?? Registry.GetValue(REG_KEY_PAWN_IO_WOW64, REG_VAL_INSTALL_DIR, null) as string
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), FOLDER_PAWN_IO);
    }

    public static bool IsPawnIOInstalled()
    {
        var path = GetInstallPath();
        return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
    }

    public static Version? GetInstalledVersion()
    {
        var displayVersion = Registry.GetValue(REG_KEY_PAWN_IO, REG_VAL_DISPLAY_VERSION, null) as string;
        if (Version.TryParse(displayVersion, out var version))
        {
            return version;
        }

        var installPath = GetInstallPath();
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        var driverPath = Path.Combine(installPath, FILE_PAWN_IO_DRIVER);
        if (File.Exists(driverPath) && Version.TryParse(FileVersionInfo.GetVersionInfo(driverPath).FileVersion, out version))
        {
            return version;
        }

        var libPath = Path.Combine(installPath, FILE_PAWN_IO_LIB);
        if (File.Exists(libPath) && Version.TryParse(FileVersionInfo.GetVersionInfo(libPath).FileVersion, out version))
        {
            return version;
        }

        return null;
    }

    public static PawnIOState GetPawnIOState()
    {
        if (!IsPawnIOInstalled())
        {
            return PawnIOState.NotInstalled;
        }

        var version = GetInstalledVersion();
        if (version is null)
        {
            return PawnIOState.Installed;
        }

        if (IsOlderThan(version, MinimumPawnIOVersion))
        {
            return PawnIOState.UpdateRequired;
        }

        return IsPawnIOServiceRunning() ? PawnIOState.Installed : PawnIOState.ServiceNotRunning;
    }

    public static bool IsPawnIOServiceRunning()
    {
        try
        {
            var startInfo = new ProcessStartInfo("sc.exe", "query PawnIO")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsOlderThan(Version installed, Version minimum)
    {
        for (var i = 0; i < 4; i++)
        {
            var a = Component(installed, i);
            var b = Component(minimum, i);

            if (a != b)
            {
                return a < b;
            }
        }
        return false;
    }

    private static int Component(Version version, int index) => index switch
    {
        0 => version.Major,
        1 => version.Minor,
        2 => Math.Max(version.Build, 0),
        _ => Math.Max(version.Revision, 0),
    };
}
