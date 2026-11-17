using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Windows.Osd;

public partial class OsdPanelWindow : OsdWindowBase
{
    private Style? _originalLabelStyle;
    private Style? _originalValueStyle;

    public OsdPanelWindow()
    {
        InitializeComponent();

        _itemsMap = new()
        {
            // Game
            { OsdItem.Fps, _fps },
            { OsdItem.LowFps, _lowFps },
            { OsdItem.FrameTime, _frameTime },

            // CPU
            { OsdItem.CpuFrequency, _cpuFrequency },
            { OsdItem.CpuPCoreFrequency, _cpuPFrequency },
            { OsdItem.CpuECoreFrequency, _cpuEFrequency },
            { OsdItem.CpuUtilization, _cpuUsage },
            { OsdItem.CpuTemperature, _cpuTemperature },
            { OsdItem.CpuPower, _cpuPower },
            { OsdItem.CpuVoltage, _cpuVoltage },
            { OsdItem.CpuFan, _cpuFanSpeed },

            // GPU
            { OsdItem.GpuFrequency, _gpuFrequency },
            { OsdItem.GpuUtilization, _gpuUsage },
            { OsdItem.GpuTemperature, _gpuTemperature },
            { OsdItem.GpuVramUtilization, _gpuVramUsage },
            { OsdItem.GpuVramTemperature, _gpuVramTemperature },
            { OsdItem.GpuPower, _gpuPower },
            { OsdItem.GpuFan, _gpuFanSpeed },

            // RAM
            { OsdItem.MemoryUtilization, _memUsage },
            { OsdItem.MemoryTemperature, _memTemperature },

            // Storage
            { OsdItem.Disk1Temperature, _disk0Temperature },
            { OsdItem.Disk2Temperature, _disk1Temperature },

            // Motherboard
            { OsdItem.MotherboardTemperature, _motherboardTemperature },
            { OsdItem.SystemFans, _systemFansControl },
        };

        _measurementGroups = new()
        {
            // Game
            { _fpsGroup, ([OsdItem.Fps, OsdItem.LowFps, OsdItem.FrameTime], _separatorFps) },

            // CPU
            { _cpuGroup, ([OsdItem.CpuFrequency, OsdItem.CpuPCoreFrequency, OsdItem.CpuECoreFrequency, OsdItem.CpuUtilization, OsdItem.CpuTemperature, OsdItem.CpuPower, OsdItem.CpuVoltage, OsdItem.CpuFan], _separatorCpu) },

            // GPU
            { _gpuGroup, ([OsdItem.GpuFrequency, OsdItem.GpuUtilization, OsdItem.GpuTemperature, OsdItem.GpuVramUtilization, OsdItem.GpuVramTemperature, OsdItem.GpuPower, OsdItem.GpuFan], _separatorGpu) },

            // RAM
            { _memoryGroup, ([OsdItem.MemoryUtilization, OsdItem.MemoryTemperature], _separatorMemory) },

            // Storage / Motherboard
            { _systemGroup, ([OsdItem.Disk1Temperature, OsdItem.Disk2Temperature, OsdItem.MotherboardTemperature, OsdItem.SystemFans], null) }
        };

        if (_sensorsPanel.Resources["SensorLabelStyle"] is Style labelStyle)
            _originalLabelStyle = labelStyle;
        if (_sensorsPanel.Resources["SensorValueStyle"] is Style valueStyle)
            _originalValueStyle = valueStyle;

        InitOsd();
    }

    protected override OsdPosition? SavedPosition
    {
        get => _OsdSettings.Store.PanelPosition;
        set => _OsdSettings.Store.PanelPosition = value;
    }

    protected override void ApplyAppearanceSettings()
    {
        base.ApplyAppearanceSettings();

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(_OsdSettings.Store.BackgroundColor);
            var alpha = (byte)(_OsdSettings.Store.BackgroundOpacity * 255);
            color.A = alpha;
            _rootBorder.Background = new SolidColorBrush(color);
        }
        catch
        {
            _rootBorder.Background = new SolidColorBrush(Color.FromArgb(153, 30, 30, 30));
        }

        double fontSize = _OsdSettings.Store.FontSize;

        if (_originalLabelStyle != null)
        {
            var newStyle = new Style(typeof(TextBlock), _originalLabelStyle);
            newStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, fontSize - 1));
            newStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, _labelBrush));
            _sensorsPanel.Resources["SensorLabelStyle"] = newStyle;
        }

        if (_originalValueStyle != null)
        {
            var newStyle = new Style(typeof(TextBlock), _originalValueStyle);
            newStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, fontSize + 1));
            newStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, _valueBrush));
            _sensorsPanel.Resources["SensorValueStyle"] = newStyle;
        }

        _fpsHeader.Foreground = _categoryBrush;
        _cpuHeader.Foreground = _categoryBrush;
        _gpuHeader.Foreground = _categoryBrush;
        _memHeader.Foreground = _categoryBrush;
        _motherboardHeader.Foreground = _categoryBrush;

        _separatorFps.Background = _categoryBrush;
        _separatorCpu.Background = _categoryBrush;
        _separatorGpu.Background = _categoryBrush;
        _separatorMemory.Background = _categoryBrush;

        ApplyCornerRadius(_rootBorder);
    }

    protected override void SetDefaultWindowPosition()
    {
        if (double.IsNaN(ActualWidth) || ActualWidth <= 0) return;

        var workArea = CurrentWorkArea;

        Left = workArea.Left;
        Top = workArea.Top + (workArea.Height - ActualHeight) / 2;
        _positionSet = true;
    }

    protected override void OnItemVisibilityChanged(FrameworkElement element, bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (element is not TextBlock || element.Parent is not Panel panel) return;

        foreach (var child in System.Linq.Enumerable.OfType<TextBlock>(panel.Children))
        {
            if (child != element) child.Visibility = visibility;
        }
    }

    protected override void UpdateFpsDisplay(FpsDisplayData data)
    {
        if (data.FpsText != null)
        {
            SetTextIfChanged(_fps, data.FpsText);
            if (data.FpsBrush != null) SetForegroundIfChanged(_fps, data.FpsBrush);
        }

        if (data.LowFpsText != null)
        {
            SetTextIfChanged(_lowFps, data.LowFpsText);
            if (data.LowFpsBrush != null) SetForegroundIfChanged(_lowFps, data.LowFpsBrush);
        }

        if (data.FrameTimeText == null) return;

        SetTextIfChanged(_frameTime, data.FrameTimeText);
        if (data.FrameTimeBrush != null) SetForegroundIfChanged(_frameTime, data.FrameTimeBrush);
    }

    protected override void UpdateSensorData(SensorSnapshot data)
    {
        UpdateSystemFans(data.SystemFans);
        var store = _OsdSettings.Store;

        UpdateTextBlock(_cpuFrequency, data.CpuFrequency, $"{{0:F0}} {Resource.MHz}");
        UpdateTextBlock(_cpuPFrequency, data.CpuPClock, $"{{0:F0}} {Resource.MHz}");
        UpdateTextBlock(_cpuEFrequency, data.CpuEClock, $"{{0:F0}} {Resource.MHz}");
        UpdateTextBlock(_cpuUsage, data.CpuUsage, $"{{0:F0}}{Resource.Percent}", store.UsageThresholdWarning, store.UsageThresholdCritical);
        UpdateTemperatureTextBlock(_cpuTemperature, data.CpuTemp, store.TempThresholdWarning, store.TempThresholdCritical);
        UpdateTextBlock(_cpuPower, data.CpuPower, $"{{0:F1}} {Resource.Watt}");
        UpdateTextBlock(_cpuVoltage, data.CpuVoltage, $"{{0:F2}} V");
        UpdateTextBlock(_cpuFanSpeed, data.CpuFanSpeed);

        UpdateTextBlock(_gpuFrequency, data.GpuFrequency, $"{{0}} {Resource.MHz}");
        UpdateTextBlock(_gpuUsage, data.GpuUsage, $"{{0:F0}}{Resource.Percent}", store.UsageThresholdWarning, store.UsageThresholdCritical);
        UpdateTemperatureTextBlock(_gpuTemperature, data.GpuTemp, store.TempThresholdWarning, store.TempThresholdCritical);
        UpdateTextBlock(_gpuVramUsage, data.GpuVramUsage, GetGpuVramDisplayText(data), store.UsageThresholdWarning, store.UsageThresholdCritical);
        UpdateTemperatureTextBlock(_gpuVramTemperature, data.GpuVramTemp, store.TempThresholdWarning, store.TempThresholdCritical);
        UpdateTextBlock(_gpuPower, data.GpuPower, $"{{0:F1}} {Resource.Watt}");
        UpdateTextBlock(_gpuFanSpeed, data.GpuFanSpeed);

        UpdateTextBlock(_memUsage, data.MemUsage, GetMemoryDisplayText(data), store.UsageThresholdWarning, store.UsageThresholdCritical);
        UpdateTemperatureTextBlock(_memTemperature, data.MemTemp, store.TempThresholdWarning, store.TempThresholdCritical);

        UpdateTemperatureTextBlock(_motherboardTemperature, data.MotherboardTemp, store.TempThresholdWarning, store.TempThresholdCritical);

        UpdateTemperatureTextBlock(_disk0Temperature, data.Disk1Temp, store.TempThresholdWarning, store.TempThresholdCritical);
        UpdateTemperatureTextBlock(_disk1Temperature, data.Disk2Temp, store.TempThresholdWarning, store.TempThresholdCritical);
    }

    protected override void UpdateSystemFansCore(IReadOnlyList<FanData> fans)
    {
        var showNumber = fans.Count > 1;
        UpdateSystemFanRow(_systemFan1Row, _systemFan1Name, _systemFan1Value, fans.Count > 0 ? fans[0] : null, showNumber);
        UpdateSystemFanRow(_systemFan2Row, _systemFan2Name, _systemFan2Value, fans.Count > 1 ? fans[1] : null, showNumber);
    }

    private void UpdateSystemFanRow(Grid row, TextBlock name, TextBlock value, FanData? fan, bool showNumber)
    {
        if (fan is not { } data)
        {
            row.Visibility = Visibility.Collapsed;
            return;
        }

        row.Visibility = Visibility.Visible;
        SetTextIfChanged(name, showNumber ? $"{Resource.SensorsControl_Fan_Title} {data.DisplayNumber}" : Resource.SensorsControl_Fan_Title);
        SetTextIfChanged(value, data.Speed < 0 ? "-" : $"{data.Speed} {Resource.RPM}");
    }
}
