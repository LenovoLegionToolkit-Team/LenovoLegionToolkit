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
        "DoudouAI",
        "LegionZone",
        "LZAgent",
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

    protected override IEnumerable<string> OwnershipRoots =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "LegionZone"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo", "LegionZone")
    ];

    protected override IEnumerable<string> OwnershipPathMarkers => ["LegionZone"];
}
