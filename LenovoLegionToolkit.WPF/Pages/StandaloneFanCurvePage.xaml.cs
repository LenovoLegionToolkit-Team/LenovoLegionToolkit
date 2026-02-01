using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;

namespace LenovoLegionToolkit.WPF.Pages;

public partial class StandaloneFanCurvePage : UiPage
{
    private readonly FanCurveManager _fanCurveManager = IoCContainer.Resolve<FanCurveManager>();
    private readonly List<FanCurveControlV3> _fanCurveControls = new();

    public StandaloneFanCurvePage()
    {
        InitializeComponent();
        this.Loaded += StandaloneFanCurvePage_Loaded;
        this.Unloaded += StandaloneFanCurvePage_Unloaded;
    }

    private void StandaloneFanCurvePage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _fanCurveManager.Initialize();
            InitializeFanControls();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed init: {ex.Message}");
        }
    }

    private void StandaloneFanCurvePage_Unloaded(object sender, RoutedEventArgs e)
    {
        foreach (var ctrl in _fanCurveControls)
        {
            _fanCurveManager.UnregisterViewModel(ctrl.FanType, ctrl);
        }
    }

    private void InitializeFanControls()
    {
        _fanControlStackPanel.Children.Clear();
        _fanCurveControls.Clear();
        _fanSelector.Items.Clear();

        var fanTypes = new[] { FanTableType.CPU, FanTableType.GPU };

        foreach (var type in fanTypes)
        {
            var fanType = type switch
            {
                FanTableType.CPU => FanType.Cpu,
                FanTableType.GPU => FanType.Gpu,
                _ => FanType.System
            };

            var entry = _fanCurveManager.GetEntry(fanType);
            FanTableData data;

            if (entry != null)
            {
                data = new FanTableData(type, (byte)(type == FanTableType.CPU ? 1 : 2), (byte)(type == FanTableType.CPU ? 0 : 1), Array.Empty<ushort>(), Array.Empty<ushort>());
            }
            else
            {
                data = CreateRandomFanTableData(type);
            }

            var ctrl = CreateFanControl(data, fanType);
            _fanCurveControls.Add(ctrl);
            _fanControlStackPanel.Children.Add(ctrl);
            _fanSelector.Items.Add(ctrl.Tag);
        }

        if (_fanSelector.Items.Count > 0)
        {
            _fanSelector.SelectedIndex = 0;
        }
    }

    private FanTableData CreateRandomFanTableData(FanTableType type)
    {
        var speeds = new ushort[] { 0, 20, 35, 45, 55, 65, 75, 85, 95, 100 };
        var temps = new ushort[] { 30, 45, 50, 55, 60, 65, 70, 80, 90, 100 };

        return new FanTableData(
            type,
            (byte)(type == FanTableType.CPU ? 1 : 2),
            (byte)(type == FanTableType.CPU ? 0 : 1),
            speeds,
            temps
        );
    }

    private FanCurveControlV3 CreateFanControl(FanTableData data, FanType fanType)
    {
        var entry = _fanCurveManager.GetEntry(fanType);
        if (entry == null)
        {
            var info = new FanTableInfo(new[] { data }, default);
            entry = FanCurveEntry.FromFanTableInfo(info, (ushort)fanType);
            _fanCurveManager.AddEntry(entry);
        }

        var ctrl = new FanCurveControlV3
        {
            Margin = new Thickness(0, 40, 0, 20),
            Tag = fanType.GetDisplayName(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };

        ctrl.Initialize(entry, new[] { data }, fanType, data.FanId);

        ctrl.SettingsChanged += (s, e) =>
        {
            _fanCurveManager.UpdateConfig(fanType, entry);
            _fanCurveManager.UpdateGlobalSettings(entry);
        };

        _fanCurveManager.RegisterViewModel(fanType, ctrl);

        return ctrl;
    }

    private void FanSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_fanSelector.SelectedIndex < 0 || _fanSelector.SelectedIndex >= _fanCurveControls.Count)
        {
            return;
        }

        for (int i = 0; i < _fanCurveControls.Count; i++)
        {
            _fanCurveControls[i].Visibility = (i == _fanSelector.SelectedIndex) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public static async Task<bool> IsSupportedAsync()
    {
        try
        {
            var feature = IoCContainer.Resolve<FanCurveManager>();
            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            return feature.IsEnabled && mi.LegionSeries == LegionSeries.ThinkBook;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error checking keyboard support: {ex.Message}");
            return false;
        }
    }
}