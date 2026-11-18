using System;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class SensorSettingsWindow
{
    private readonly HardwareSensorSettings _sensorsSettings = IoCContainer.Resolve<HardwareSensorSettings>();
    private readonly ApplicationSettings _appSettings = IoCContainer.Resolve<ApplicationSettings>();

    public SensorSettingsWindow()
    {
        InitializeComponent();

        var settings = _sensorsSettings.Store;
        _cpuFrequencySelector.SelectedIndex = settings.ShowCpuAverageFrequency ? 1 : 0;
        _memoryDisplayModeSelector.SelectedIndex = settings.DisplayMemoryInGigabytes ? 1 : 0;

        int coreCount = IoCContainer.Resolve<SensorsGroupController>().AvailableVoltageCoreCount;
        if (coreCount == 0) coreCount = 1;
        for (int i = 0; i < coreCount; i++)
        {
            _voltageCoreSelector.Items.Add(new ComboBoxItem { Content = $"{Resource.Core} {i + 1}" });
        }

        _voltageCoreSelector.SelectedIndex = Math.Clamp(settings.CpuVoltageCoreIndex, 0, coreCount - 1);

        _voltageSelector.SelectedIndex = (int)settings.CpuVoltageMode;
        UpdateCoreSelectorVisibility();

        _cpuTemperatureSourceSelector.SelectedIndex = (int)settings.CpuTemperatureSource;

        if (Displays.HasMultipleGpus())
        {
            _gpuSelectionCard.Visibility = Visibility.Visible;
            _gpuSelector.SelectedIndex = settings.SelectedGpuIsIgpu ? 1 : 0;
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
    }

    private void UpdateCoreSelectorVisibility()
    {
        _voltageCoreSelector.Visibility = _voltageSelector.SelectedIndex == (int)CpuVoltageMode.Core
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DefaultButton_Click(object sender, RoutedEventArgs e)
    {
        SaveAndClose(new(), TemperatureUnit.C);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var current = _sensorsSettings.Store;
        var settings = current with
        {
            ShowCpuAverageFrequency = _cpuFrequencySelector.SelectedIndex == 1,
            CpuVoltageMode = (CpuVoltageMode)_voltageSelector.SelectedIndex,
            CpuVoltageCoreIndex = _voltageCoreSelector.SelectedIndex,
            CpuTemperatureSource = (CpuTemperatureSource)_cpuTemperatureSourceSelector.SelectedIndex,
            SelectedGpuIsIgpu = Displays.HasMultipleGpus() ? _gpuSelector.SelectedIndex == 1 : current.SelectedGpuIsIgpu,
            DisplayMemoryInGigabytes = _memoryDisplayModeSelector.SelectedIndex == 1,
        };
        var temperatureUnit = _temperatureUnitSelector.SelectedIndex == 1 ? TemperatureUnit.F : TemperatureUnit.C;

        SaveAndClose(settings, temperatureUnit);
    }

    private void SaveAndClose(HardwareSensorSettings.HardwareSensorSettingsStore settings, TemperatureUnit temperatureUnit)
    {
        _sensorsSettings.Save(settings);
        _appSettings.Store.TemperatureUnit = temperatureUnit;
        _appSettings.SynchronizeStore();
        Close();
    }
}
