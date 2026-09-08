using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class LegionZoneDisabler : AbstractSoftwareDisabler
{
    protected override IEnumerable<string> ScheduledTasksPaths =>
    [
        "Lenovo\\LegionZone",
        "Lenovo\\SmartEngine"
    ];

    protected override IEnumerable<string> ServiceNames =>
    [
        "GAService",
        "LenovoLightingService",
        "LenovoSmartService",
        "LZService",
        "StreamingService",
        "UtilityService"
    ];

    protected override IEnumerable<string> ProcessNames =>
    [
        "BorderlessSpace",
        "DoudouAI",
        "EMDriverAssist",
        "GACapture",
        "GAController",
        "GAEditor",
        "GAHighlight",
        "GAInferCV",
        "GAInference",
        "GAInferOCR",
        "GAService",
        "GAWalkthrough",
        "GAWorker",
        "LegionZone",
        "LenovoLighting",
        "LZAgent",
        "LZMain",
        "lzolhelp64",
        "LZService",
        "LZStrategy",
        "LZTray",
        "LZUpdate",
        "NvOcScanner",
        "SEGameTool",
        "seworker",
        "SmartEngineHost",
        "StreamingDiagnosis",
        "StreamingHost",
        "StreamingService"
    ];
}
