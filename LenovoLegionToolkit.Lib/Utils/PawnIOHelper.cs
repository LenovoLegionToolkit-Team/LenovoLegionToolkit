using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using Microsoft.Win32;

namespace LenovoLegionToolkit.Lib.Utils;

public static class PawnIOHelper
{
    private static readonly ApplicationSettings ApplicationSettings = IoCContainer.Resolve<ApplicationSettings>();

    private const string REG_KEY_PAWN_IO = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    private const string REG_VAL_INSTALL_LOC = "InstallLocation";
    private const string REG_KEY_PAWN_IO_WOW64 = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\PawnIO";
    private const string REG_VAL_INSTALL_DIR = "Install_Dir";
    private const string FOLDER_PAWN_IO = "PawnIO";

    public static Func<Task<bool>>? RequestShowDialogAsync;

    public static void OpenPawnIODownloadPage()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://pawnio.eu/",
            UseShellExecute = true
        });
    }

    public static async Task TryShowPawnIONotFoundDialogAsync(bool disableHardwareSensors = true)
    {
        if (RequestShowDialogAsync != null)
        {
            bool userClickedYes = await RequestShowDialogAsync.Invoke().ConfigureAwait(false);

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
    }

    public static void ShowPawnIONotify()
    {
        TryShowPawnIONotFoundDialogAsync().ConfigureAwait(false);
    }

    public static bool IsPawnIOInnstalled()
    {
        string? path = Registry.GetValue(REG_KEY_PAWN_IO, REG_VAL_INSTALL_LOC, null) as string
                       ?? Registry.GetValue(REG_KEY_PAWN_IO_WOW64, REG_VAL_INSTALL_DIR, null) as string
                       ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), FOLDER_PAWN_IO);
        return Directory.Exists(path);
    }
}