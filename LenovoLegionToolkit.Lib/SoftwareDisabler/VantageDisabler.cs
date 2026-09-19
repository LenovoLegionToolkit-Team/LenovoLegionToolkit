using System;
using System.Collections.Generic;
using System.IO;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class VantageDisabler : AbstractSoftwareDisabler
{
    protected override IEnumerable<string> ScheduledTasksPaths =>
    [
        "Lenovo\\BatteryGauge",
        "Lenovo\\ImController",
        "Lenovo\\ImController\\Plugins",
        "Lenovo\\ImController\\TimeBasedEvents",
        "Lenovo\\UDC",
        "Lenovo\\Vantage",
        "Lenovo\\Vantage\\Schedule"
    ];

    protected override IEnumerable<string> ServiceNames =>
    [
        "ImControllerService",
        "LenovoVantageService"
    ];

    protected override IEnumerable<string> ProcessNames =>
    [
        "BGHelper",
        "Lenovo.Modern.ImController",
        "Lenovo.Vantage",
        "LenovoVantage",
        "QSHelper",
        "ScheduleEventAction"
    ];

    protected override IEnumerable<string> StartupEntryNames => ["LenovoVantageToolbar", "LenovoVantage"];

    protected override IEnumerable<string> StartupEntryRoots =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lenovo", "Vantage")
    ];

    protected override IEnumerable<string> OwnershipRoots =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Lenovo", "VantageService"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lenovo", "Vantage"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Lenovo", "ImController")
    ];

    protected override IEnumerable<string> OwnershipPathMarkers => ["Vantage", "ImController"];
}
