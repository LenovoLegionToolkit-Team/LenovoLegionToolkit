using System;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class SensorSettingsWindow
{
    private readonly HardwareSensorSettings _sensorsSettings = IoCContainer.Resolve<HardwareSensorSettings>();
    private readonly ApplicationSettings _appSettings = IoCContainer.Resolve<ApplicationSettings>();

    public SensorSettingsWindow()
    {
        InitializeComponent();

        _cpuFrequencySelector.SelectedIndex = _sensorsSettings.Store.ShowCpuAverageFrequency ? 1 : 0;
        _memoryDisplayModeSelector.SelectedIndex = _sensorsSettings.Store.DisplayMemoryInGigabytes ? 1 : 0;

        var controller = IoCContainer.Resolve<SensorsGroupController>();
        int coreCount = controller.AvailableVoltageCoreCount;
        if (coreCount == 0) coreCount = 1;
        for (int i = 0; i < coreCount; i++)
        {
            _voltageCoreSelector.Items.Add(new ComboBoxItem { Content = $"{Resource.Core} {i + 1}" });
        }

        _voltageCoreSelector.SelectedIndex = Math.Min(_sensorsSettings.Store.CpuVoltageCoreIndex, coreCount - 1);

        _voltageSelector.SelectedIndex = (int)_sensorsSettings.Store.CpuVoltageMode;
        UpdateCoreSelectorVisibility();

        _cpuTemperatureSourceSelector.SelectedIndex = (int)_sensorsSettings.Store.CpuTemperatureSource;

        if (Displays.HasMultipleGpus())
        {
            _gpuSelectionCard.Visibility = Visibility.Visible;
            _gpuSelector.SelectedIndex = _sensorsSettings.Store.SelectedGpuIsIgpu ? 1 : 0;
        }
        else
        {
            _gpuSelectionCard.Visibility = Visibility.Collapsed;
        }

        _temperatureUnitSelector.SelectedIndex = _appSettings.Store.TemperatureUnit == TemperatureUnit.F ? 1 : 0;
    }

    private void _voltageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateCoreSelectorVisibility();
        _sensorsSettings.Store.CpuVoltageCoreIndex = _voltageCoreSelector.SelectedIndex;
    }

    private void UpdateCoreSelectorVisibility()
    {
        _voltageCoreSelector.Visibility = _voltageSelector.SelectedIndex == (int)CpuVoltageMode.Core
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DefaultButton_Click(object sender, RoutedEventArgs e)
    {
        _sensorsSettings.Store.ShowCpuAverageFrequency = false;
        _sensorsSettings.Store.SelectedGpuIsIgpu = false;
        _sensorsSettings.Store.DisplayMemoryInGigabytes = false;
        _sensorsSettings.Store.CpuVoltageMode = CpuVoltageMode.Average;
        _sensorsSettings.Store.CpuVoltageCoreIndex = 0;
        _sensorsSettings.Store.CpuTemperatureSource = CpuTemperatureSource.Auto;
        _appSettings.Store.TemperatureUnit = TemperatureUnit.C;
        _cpuFrequencySelector.SelectedIndex = 0;
        _voltageSelector.SelectedIndex = 0;
        _voltageCoreSelector.SelectedIndex = 0;
        UpdateCoreSelectorVisibility();
        _cpuTemperatureSourceSelector.SelectedIndex = 0;
        _gpuSelector.SelectedIndex = 0;
        _memoryDisplayModeSelector.SelectedIndex = 0;
        _temperatureUnitSelector.SelectedIndex = 0;
        _sensorsSettings.SynchronizeStore();
        _appSettings.SynchronizeStore();

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _sensorsSettings.Store.ShowCpuAverageFrequency = _cpuFrequencySelector.SelectedIndex == 1;
        _sensorsSettings.Store.CpuVoltageMode = (CpuVoltageMode)_voltageSelector.SelectedIndex;
        _sensorsSettings.Store.CpuVoltageCoreIndex = _voltageCoreSelector.SelectedIndex;
        _sensorsSettings.Store.CpuTemperatureSource = (CpuTemperatureSource)_cpuTemperatureSourceSelector.SelectedIndex;
        if (Displays.HasMultipleGpus())
        {
            _sensorsSettings.Store.SelectedGpuIsIgpu = _gpuSelector.SelectedIndex == 1;
        }

        _sensorsSettings.Store.DisplayMemoryInGigabytes = _memoryDisplayModeSelector.SelectedIndex == 1;
        _appSettings.Store.TemperatureUnit = _temperatureUnitSelector.SelectedIndex == 1 ? TemperatureUnit.F : TemperatureUnit.C;

        _sensorsSettings.SynchronizeStore();
        _appSettings.SynchronizeStore();
        Close();
    }
}
