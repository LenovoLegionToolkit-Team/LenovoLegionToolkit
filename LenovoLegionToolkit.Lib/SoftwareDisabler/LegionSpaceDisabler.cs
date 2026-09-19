using System;
using System.Collections.Generic;
using System.IO;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class LegionSpaceDisabler : AbstractSoftwareDisabler
{
    protected override IEnumerable<string> ScheduledTasksPaths => [];

    protected override IEnumerable<string> ServiceNames =>
    [
        "DAService"
    ];

    protected override IEnumerable<string> ProcessNames =>
    [
        "Bino3D",
        "LegionGameWidget",
        "LegionSpace",
        "LegionSpaceComponent",
        "LegionSpaceToast",
        "LSDaemon"
    ];

    protected override IEnumerable<string> OwnershipRoots =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo", "LegionSpace"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "LegionSpace")
    ];

    protected override IEnumerable<string> OwnershipPathMarkers => ["LegionSpace", "LSDaemon"];
}
