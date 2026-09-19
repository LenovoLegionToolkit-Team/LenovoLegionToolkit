using System;
using System.Collections.Generic;
using System.IO;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class SmartEngineDisabler : AbstractSoftwareDisabler
{
    protected override IEnumerable<string> ScheduledTasksPaths =>
    [
        "Lenovo\\SmartEngine"
    ];

    protected override IEnumerable<string> ServiceNames =>
    [
        "GAService",
        "LenovoLightingService",
        "LenovoSmartService"
    ];

    protected override IEnumerable<string> ProcessNames =>
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
    ];

    protected override IEnumerable<string> OwnershipRoots =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Lenovo", "SmartEngine"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "SmartEngine")
    ];

    protected override IEnumerable<string> OwnershipPathMarkers => ["SmartEngine"];
}
