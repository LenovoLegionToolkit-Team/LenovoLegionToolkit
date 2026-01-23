using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls;

namespace LenovoLegionToolkit.WPF.Windows.Utils
{
    public partial class TestWindow : Window
    {
        public TestWindow()
        {
            InitializeComponent();
            this.Loaded += TestWindow_Loaded;
        }

        private void TestWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. 创建模拟数据
                var mockInfo = CreateMockFanTableInfo();

                // 2. 遍历并创建控件
                foreach (var data in mockInfo.Data)
                {
                    var ctrl = CreateFanControlWithoutSaving(data, mockInfo);
                    _fanControlStackPanel.Children.Add(ctrl);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"模拟数据初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 构造假的 BIOS 风扇数据 (适配 ushort[] 和 SensorId)
        /// </summary>
        private FanTableInfo CreateMockFanTableInfo()
        {
            // --- 模拟 CPU 数据 ---
            ushort[] cpuSpeeds = new ushort[] { 0, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
            ushort[] cpuTemps = new ushort[] { 30, 40, 50, 55, 60, 65, 70, 75, 80, 90 };

            // 使用主构造函数传参
            var cpuData = new FanTableData(
                FanTableType.CPU,
                fanId: 1,
                sensorId: 0,
                fanSpeeds: cpuSpeeds,
                temps: cpuTemps
            );

            // --- 模拟 GPU 数据 ---
            ushort[] gpuSpeeds = new ushort[] { 0, 15, 25, 35, 45, 55, 65, 75, 85, 100 };
            ushort[] gpuTemps = new ushort[] { 35, 45, 55, 60, 65, 70, 75, 82, 87, 95 };

            var gpuData = new FanTableData(
                FanTableType.GPU,
                fanId: 2,
                sensorId: 1,
                fanSpeeds: gpuSpeeds,
                temps: gpuTemps
            );

            // 【修复 1】: 直接使用数组，或者用 List.ToArray()
            var dataArray = new FanTableData[] { cpuData, gpuData };

            // 【修复 2】: 第二个参数传递 default(FanTable) 而不是 new byte[]
            // 因为 UI 控件绘图主要依靠上面的 dataArray，底层的 FanTable 在测试中用不到，置空即可
            return new FanTableInfo(dataArray, default(FanTable));
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
}