using System;
using System.Collections.Generic;
using System.IO;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class LegionZoneDisabler : AbstractSoftwareDisabler
{
    protected override IEnumerable<string> ScheduledTasksPaths => [];

    protected override IEnumerable<string> ServiceNames =>
    [
        "LZService",
        "StreamingService"
    ];

    protected override IEnumerable<string> ProcessNames =>
    [
        "BorderlessSpace",
        "ContextMenuInstaller",
        "DoudouAI",
        "LegionZone",
        "LZAgent",
        "LZInstall",
        "LZMain",
        "lzolhelp64",
        "LZService",
        "LZStrategy",
        "LZTray",
        "LZUpdate",
        "NvOcScanner",
        "StreamingDiagnosis",
        "StreamingHost",
        "StreamingService"
    ];

    protected override IEnumerable<string> DriverNamePrefixes => ["AMDRyzenMasterDriver"];

    protected override IEnumerable<string> DriverPackageRoots =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "LegionZone")
    ];

    protected override IEnumerable<string> StartupEntryRoots =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "LegionZone"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo", "LegionZone")
    ];
}
