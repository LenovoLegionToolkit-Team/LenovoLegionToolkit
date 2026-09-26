using System;
using System.IO;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class SmartEngineDisabler : AbstractSoftwareDisabler
{
    protected override SoftwareDisablerPolicy Policy { get; } = new()
    {
        ScheduledTaskPaths = ["Lenovo\\SmartEngine"],
        ServiceNames =
        [
            "GAService",
            "LenovoLightingService",
            "LenovoSmartService"
        ],
        ProcessNames =
        [
            "GACapture",
            "GAController",
            "GAEditor",
            "GAHighlight",
            "GAInference",
            "GAInferCV",
            "GAInferOCR",
            "GAService",
            "GAWalkthrough",
            "GAWorker",
            "LenovoLighting",
            "SEGameTool",
            "seworker",
            "SmartEngineHost"
        ],
        DriverNamePrefixes = ["SENetFilter"],
        DriverPackageRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo", "SENetFilter"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "LegionZone")
        ],
        StartupEntryRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo", "SmartEngine"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "SmartEngine"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo", "LegionZone"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "LegionZone")
        ],
        AppxPackageNames = ["LegionLightingController"],
        RepairBlockingServiceNames = ["LZService"],
        RepairBlockingProcessNames = ["LZInstall", "LZTray", "LZUpdate"],
        OwnershipRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo", "SmartEngine"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "SmartEngine")
        ],
        OwnershipPathMarkers = ["SmartEngine"],
        OwnershipPriorityPathFragments = [@"\SESDK\", @"\SEGamingAI\", "LegionLightingController_"]
    };
}
