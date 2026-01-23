using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls;
using System;
using System.Windows;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class TestWindow : Window
{
    public TestWindow()
    {
        InitializeComponent();
        Loaded += TestWindow_Loaded;
    }

    private void TestWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var mockInfo = CreateMockFanTableInfo();

            foreach (var data in mockInfo.Data)
            {
                var ctrl = CreateFanControlWithoutSaving(data, mockInfo);
                _fanControlStackPanel.Children.Add(ctrl);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed init: {ex.Message}");
        }
    }

    private FanTableInfo CreateMockFanTableInfo()
    {
        var cpuSpeeds = new ushort[] { 0, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        var cpuTemps = new ushort[] { 30, 40, 50, 55, 60, 65, 70, 75, 80, 90 };

        var cpuData = new FanTableData(
            FanTableType.CPU,
            1,
            0,
            cpuSpeeds,
            cpuTemps
        );

        var gpuSpeeds = new ushort[] { 0, 15, 25, 35, 45, 55, 65, 75, 85, 100 };
        var gpuTemps = new ushort[] { 35, 45, 55, 60, 65, 70, 75, 82, 87, 95 };

        var gpuData = new FanTableData(
            FanTableType.GPU,
            2,
            1,
            gpuSpeeds,
            gpuTemps
        );

        var dataArray = new FanTableData[] { cpuData, gpuData };

        return new FanTableInfo(dataArray, default);
    }

    private FanCurveControlV3 CreateFanControlWithoutSaving(FanTableData data, FanTableInfo info)
    {
        var fanType = data.Type switch
        {
            FanTableType.CPU => FanType.Cpu,
            FanTableType.GPU => FanType.Gpu,
            _ => FanType.System
        };

        var entry = FanCurveEntry.FromFanTableInfo(info, (ushort)fanType);

        var ctrl = new FanCurveControlV3
        {
            Margin = new Thickness(0, 0, 0, 20),
            Tag = fanType.GetDisplayName(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        ctrl.Initialize(entry, info.Data, fanType, data.FanId);

        ctrl.SettingsChanged += (s, e) =>
        {
            Log.Instance.Trace($"[TestWindow] Mock {fanType} Curve adjusted locally.");
        };

        return ctrl;
    }
}
