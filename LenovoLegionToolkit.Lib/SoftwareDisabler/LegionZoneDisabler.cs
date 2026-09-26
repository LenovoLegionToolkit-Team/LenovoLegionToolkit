using System;
using System.IO;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class LegionZoneDisabler : AbstractSoftwareDisabler
{
    protected override SoftwareDisablerPolicy Policy { get; } = new()
    {
        ServiceNames = ["LZService", "StreamingService"],
        ProcessNames =
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
        ],
        DriverNamePrefixes = ["AMDRyzenMasterDriver"],
        DriverPackageRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "LegionZone")
        ],
        StartupEntryRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "LegionZone"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo", "LegionZone")
        ],
        AppxPackageNames = ["LegionZoneExt"],
        OwnershipRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "LegionZone"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo", "LegionZone")
        ],
        OwnershipPathMarkers = ["LegionZone"],
        OwnershipPriorityPathFragments = ["LegionZoneExt_"]
    };
}
