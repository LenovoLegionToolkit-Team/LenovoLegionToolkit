using System;
using System.Collections.Generic;
using System.IO;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class PCManagerDisabler : AbstractSoftwareDisabler
{
    protected override IEnumerable<string> ScheduledTasksPaths => [];

    protected override IEnumerable<string> ServiceNames =>
    [
        "HRWSCCtrl",
        "LAVService",
        "LenovoPcManagerService",
        "LnvSvcFdn"
    ];

    protected override IEnumerable<string> ProcessNames =>
    [
        "Appvant",
        "BatterySetting",
        "DesktopAssistant",
        "hotfixplatform",
        "LenovoAppupdate",
        "LenovoMonitorManager",
        "LenovoPcManager",
        "LenovoPCMKeyService",
        "LenovoTray",
        "LnvSvcFdn",
        "ShowDeskBand"
    ];

    protected override IEnumerable<string> StartupEntryRoots =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "PCManager")
    ];
}
