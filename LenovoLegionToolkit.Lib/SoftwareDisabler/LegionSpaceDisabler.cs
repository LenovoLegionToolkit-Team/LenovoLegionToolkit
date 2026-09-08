using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class LegionSpaceDisabler : AbstractSoftwareDisabler
{
    protected override IEnumerable<string> ScheduledTasksPaths =>
    [
        "Lenovo\\LegionSpace",
        "Lenovo\\SmartEngine"
    ];

    protected override IEnumerable<string> ServiceNames =>
    [
        "DAService",
        "GAService",
        "LenovoLightingService",
        "LenovoSmartService"
    ];

    protected override IEnumerable<string> ProcessNames =>
    [
        "Bino3D",
        "GACapture",
        "GAController",
        "GAHighlight",
        "GAInference",
        "GAInferOCR",
        "GAService",
        "GAWorker",
        "LegionGameWidget",
        "LegionSpace",
        "LenovoLighting",
        "LSDaemon",
        "SEGameHLEditorWorker",
        "SEGameTool",
        "seworker",
        "SmartEngineHost"
    ];
}
