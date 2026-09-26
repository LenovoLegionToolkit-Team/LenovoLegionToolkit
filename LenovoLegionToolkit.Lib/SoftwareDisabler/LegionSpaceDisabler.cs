using System;
using System.IO;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class LegionSpaceDisabler : AbstractSoftwareDisabler
{
    protected override SoftwareDisablerPolicy Policy { get; } = new()
    {
        ServiceNames = ["DAService"],
        ProcessNames =
        [
            "Bino3D",
            "LegionGameWidget",
            "LegionSpace",
            "LegionSpaceComponent",
            "LegionSpaceToast",
            "LSDaemon"
        ],
        OwnershipRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo", "LegionSpace"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "LegionSpace")
        ],
        OwnershipPathMarkers = ["LegionSpace", "LSDaemon"]
    };
}
