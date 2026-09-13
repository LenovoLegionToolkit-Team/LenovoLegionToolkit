using System.Collections.Generic;

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
}
