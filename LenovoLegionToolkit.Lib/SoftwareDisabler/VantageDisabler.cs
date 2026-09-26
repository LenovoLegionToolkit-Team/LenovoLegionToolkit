using System;
using System.IO;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class VantageDisabler : AbstractSoftwareDisabler
{
    protected override SoftwareDisablerPolicy Policy { get; } = new()
    {
        ScheduledTaskPaths =
        [
            "Lenovo\\BatteryGauge",
            "Lenovo\\ImController",
            "Lenovo\\ImController\\Plugins",
            "Lenovo\\ImController\\TimeBasedEvents",
            "Lenovo\\UDC",
            "Lenovo\\Vantage",
            "Lenovo\\Vantage\\Schedule"
        ],
        ServiceNames = ["ImControllerService", "LenovoVantageService"],
        ProcessNames =
        [
            "BGHelper",
            "Lenovo.Modern.ImController",
            "Lenovo.Vantage",
            "LenovoVantage",
            "QSHelper",
            "ScheduleEventAction"
        ],
        StartupEntryNames = ["LenovoVantageToolbar", "LenovoVantage"],
        StartupEntryRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lenovo", "Vantage")
        ],
        OwnershipRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "VantageService"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lenovo", "Vantage"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Lenovo", "ImController")
        ],
        OwnershipPathMarkers = ["Vantage", "ImController"]
    };
}
