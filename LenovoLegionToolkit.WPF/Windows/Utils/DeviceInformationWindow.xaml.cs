using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.Lib.Utils.Warranty;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Overclocking.Amd;
using Wpf.Ui.Controls;
using SymbolRegular = Wpf.Ui.Common.SymbolRegular;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class DeviceInformationWindow
{
    private readonly WarrantyChecker _warrantyChecker = IoCContainer.Resolve<WarrantyChecker>();

    private int _count = 0;
    private AmdOverclocking? _amdOverclockingWindow;
    private string _actualSerialNumber = string.Empty;
    private bool _isSerialNumberRevealed = false;

    public DeviceInformationWindow()
    {
        InitializeComponent();
    }

    private async void DeviceInformationWindow_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync(bool forceRefresh = false)
    {
        var mi = await Compatibility.GetMachineInformationAsync();

        var vendor = mi.Vendor;
        var model = mi.Model;
        var machineType = mi.MachineType;
        var serialNumber = mi.SerialNumber;
        var biosVersion = mi.BiosVersionRaw;

        if (Compatibility.FakeMachineInformationMode)
        {
            var fakeMi = await Compatibility.GetFakeMachineInformationAsync();
            if (fakeMi.HasValue)
            {
                var fake = fakeMi.Value;
                vendor = fake.Manufacturer ?? vendor;
                model = fake.Model ?? model;
                machineType = fake.MachineType ?? machineType;
                serialNumber = fake.SerialNumber ?? serialNumber;
                biosVersion = fake.BiosVersion ?? biosVersion;
            }
        }

        _modelLargeLabel.Text = $"{vendor} {model}".ToUpperInvariant();
        _osVersionLabel.Text = GetOSFriendlyName();
        _modelLabel.Text = model;
        _mtmLabel.Text = machineType;

        _actualSerialNumber = serialNumber;
        _isSerialNumberRevealed = false;
        _serialNumberLabel.Text = new string('*', serialNumber.Length);

        _biosLabel.Text = biosVersion;

        if (mi.LegionSeries != LegionSeries.Unknown)
        {
            _seriesLabel.Text = mi.LegionSeries.ToString().Replace("_", " ");
            _seriesRow.Visibility = Visibility.Visible;
        }
        else
        {
            _seriesRow.Visibility = Visibility.Collapsed;
        }

        if (mi.Generation > 0)
        {
            _generationLabel.Text = $"{Resource.Generation} {mi.Generation}";
            _generationRow.Visibility = Visibility.Visible;
        }
        else
        {
            _generationRow.Visibility = Visibility.Collapsed;
        }

        if (mi.SupportedPowerModes != null && mi.SupportedPowerModes.Length > 0)
        {
            _powerModesTitle.Text = Resource.PowerModes;
            _powerModesLabel.Text = string.Join(", ", mi.SupportedPowerModes.Select(m => m.GetDisplayName()));
            _modesRow.Visibility = Visibility.Visible;
        }
        else if (mi.Properties.SupportsITSMode)
        {
            _powerModesTitle.Text = Resource.PowerModes;
            var itsModes = mi.LegionSeries == LegionSeries.ThinkBook
                ? new[] { ITSMode.MmcCool, ITSMode.ItsAuto, ITSMode.MmcPerformance, ITSMode.MmcGeek }
                : new[] { ITSMode.MmcCool, ITSMode.ItsAuto, ITSMode.MmcPerformance };
            _powerModesLabel.Text = string.Join(", ", itsModes.Select(m => m.GetDisplayName()));
            _modesRow.Visibility = Visibility.Visible;
        }
        else
        {
            _modesRow.Visibility = Visibility.Collapsed;
        }

        var caps = new List<string>();
        if (mi.Properties.SupportsGSync) caps.Add("G-Sync");
        if (mi.Properties.SupportsIGPUMode) caps.Add("iGPU Mode");
        if (mi.Properties.SupportsAIMode) caps.Add("AI Mode");
        if (mi.Properties.SupportsAlwaysOnAc.status) caps.Add("Always On AC");
        if (mi.Properties.SupportsExtremeMode) caps.Add("Extreme Mode");
        if (mi.Properties.SupportsGodMode) caps.Add("Custom Mode (God Mode)");
        if (mi.Properties.SupportsBootLogoChange) caps.Add("Boot Logo Customization");
        if (mi.LegionZoneVersion > 0) caps.Add($"Legion Zone v{mi.LegionZoneVersion}");

        if (mi.Features.Source != MachineInformation.FeatureData.SourceType.Unknown)
        {
            var friendlyNames = new Dictionary<CapabilityID, string>
            {
                { CapabilityID.IGPUMode, "iGPU Mode" },
                { CapabilityID.FlipToStart, "Flip to Start" },
                { CapabilityID.NvidiaGPUDynamicDisplaySwitching, "NVIDIA Advanced Optimus" },
                { CapabilityID.AMDSmartShiftMode, "AMD SmartShift" },
                { CapabilityID.AMDSkinTemperatureTracking, "AMD Skin Temperature Tracking" },
                { CapabilityID.AutoSwitchRefreshRate, "Auto Refresh Rate Switching" },
                { CapabilityID.GodModeFnQSwitchable, "Fn+Q Custom Mode Switching" },
                { CapabilityID.OverDrive, "Screen Overdrive" },
                { CapabilityID.AIChip, "Lenovo LA AI Chip" },
                { CapabilityID.InstantBootAc, "Flip to Boot (AC)" },
                { CapabilityID.InstantBootUsbPowerDelivery, "Flip to Boot (USB-PD)" },
                { CapabilityID.FanFullSpeed, "Full Fan Speed Control" },
                { CapabilityID.CpuCurrentFanSpeed, "CPU Fan Speed Monitoring" },
                { CapabilityID.GpuCurrentFanSpeed, "GPU Fan Speed Monitoring" },
                { CapabilityID.PchCurrentFanSpeed, "PCH Fan Speed Monitoring" },
                { CapabilityID.CpuCurrentTemperature, "CPU Temp Monitoring" },
                { CapabilityID.GpuCurrentTemperature, "GPU Temp Monitoring" },
                { CapabilityID.PchCurrentTemperature, "PCH Temp Monitoring" },
                { CapabilityID.CPUShortTermPowerLimit, "CPU Short Term Power Limit Control" },
                { CapabilityID.CPULongTermPowerLimit, "CPU Long Term Power Limit Control" },
                { CapabilityID.CPUPeakPowerLimit, "CPU Peak Power Limit Control" },
                { CapabilityID.CPUTemperatureLimit, "CPU Thermal Limit Control" },
                { CapabilityID.APUsPPTPowerLimit, "APU PPT Power Limit Control" },
                { CapabilityID.CPUCrossLoadingPowerLimit, "CPU Cross Loading Power Limit Control" },
                { CapabilityID.CPUPL2Tau, "CPU PL2 Tau Control" },
                { CapabilityID.CPUOverclockingEnable, "CPU Overclocking Support" },
                { CapabilityID.GPUPowerBoost, "GPU Dynamic Boost Control" },
                { CapabilityID.GPUConfigurableTGP, "GPU Configurable TGP Control" },
                { CapabilityID.GPUTemperatureLimit, "GPU Thermal Limit Control" },
                { CapabilityID.GPUTotalProcessingPowerTargetOnAcOffsetFromBaseline, "GPU Total Power Target Offset" },
                { CapabilityID.GPUToCPUDynamicBoost, "GPU-to-CPU Dynamic Boost" },
                { CapabilityID.GPUStatus, "GPU Status Monitoring" },
                { CapabilityID.GPUDidVid, "GPU Voltage Control" }
            };

            foreach (var cap in mi.Features.All)
            {
                if (friendlyNames.TryGetValue(cap, out var name))
                {
                    caps.Add(name);
                }
            }
        }

        var uniqueCaps = caps.Distinct().ToList();
        if (uniqueCaps.Count > 0)
        {
            _capabilitiesLabel.Text = string.Join(", ", uniqueCaps);
            _capabilitiesRow.Visibility = Visibility.Visible;
        }
        else
        {
            _capabilitiesRow.Visibility = Visibility.Collapsed;
        }

        _ = Task.Run(() =>
        {
            var cpuInfo = LenovoLegionToolkit.Lib.System.DeviceInformation.GetCpuInfo();
            var gpuInfos = LenovoLegionToolkit.Lib.System.DeviceInformation.GetGpuInfos();
            var ramInfos = LenovoLegionToolkit.Lib.System.DeviceInformation.GetMemoryInfos();
            var diskInfos = LenovoLegionToolkit.Lib.System.DeviceInformation.GetDiskInfos();
            var displayInfos = LenovoLegionToolkit.Lib.System.DeviceInformation.GetDisplayInfos();
            var mbInfo = LenovoLegionToolkit.Lib.System.DeviceInformation.GetMotherboardInfo();
            var batteryInfo = LenovoLegionToolkit.Lib.System.Battery.GetBatteryInformation();
            var logicalDrives = LenovoLegionToolkit.Lib.System.DeviceInformation.GetLogicalDrives();
            var networkAdapters = LenovoLegionToolkit.Lib.System.DeviceInformation.GetNetworkAdapters();

            Dispatcher.Invoke(() =>
            {
                if (cpuInfo != null)
                {
                    _cpuNameLabel.Text = cpuInfo.Name;
                    _cpuDetailsLabel.Text = $"{cpuInfo.CoreCount} {Resource.Cores} / {cpuInfo.ThreadCount} {Resource.Threads}";
                }
                else
                {
                    _cpuNameLabel.Text = Resource.Unknown;
                    _cpuDetailsLabel.Text = "";
                }
                
                if (cpuInfo?.Name != null)
                {
                    if (cpuInfo.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                        _cpuIcon.Foreground = (System.Windows.Media.Brush)FindResource("PaletteBlueBrush");
                    else if (cpuInfo.Name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                        _cpuIcon.Foreground = (System.Windows.Media.Brush)FindResource("PaletteRedBrush");
                }

                _gpuList.ItemsSource = gpuInfos;
                if (gpuInfos.Count > 0 && gpuInfos[0].Name?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _gpuIcon.Foreground = (System.Windows.Media.Brush)FindResource("PaletteGreenBrush");
                }
                else if (gpuInfos.Count > 0 && gpuInfos[0].Name?.Contains("AMD", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _gpuIcon.Foreground = (System.Windows.Media.Brush)FindResource("PaletteRedBrush");
                }

                _ramList.ItemsSource = ramInfos;
                _diskList.ItemsSource = logicalDrives;
                _displayList.ItemsSource = displayInfos;
                _networkList.ItemsSource = networkAdapters;

                if (mbInfo != null)
                {
                    _motherboardNameLabel.Text = mbInfo.Product;
                    _motherboardDetailsLabel.Text = mbInfo.Manufacturer;
                }
                else
                {
                    _motherboardNameLabel.Text = Resource.Unknown;
                    _motherboardDetailsLabel.Text = "";
                }

                if (batteryInfo.DesignCapacity > 0)
                {
                    _batteryDesignCapacityLabel.Text = $"{batteryInfo.DesignCapacity / 1000.0:F1} Wh";
                    _batteryFullCapacityLabel.Text = $"{batteryInfo.FullChargeCapacity / 1000.0:F1} Wh";
                    float healthPercent = (float)batteryInfo.BatteryHealth;
                    _batteryHealthLabel.Text = $"{healthPercent / 100.0f:P1}";
                    _batteryHealthProgressBar.Value = healthPercent;
                    
                    var gradient = new System.Windows.Media.LinearGradientBrush
                    {
                        StartPoint = new System.Windows.Point(0, 0),
                        EndPoint = new System.Windows.Point(1, 0)
                    };

                    if (healthPercent > 80)
                    {
                        gradient.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)FindResource("PaletteGreenColor"), 0));
                        gradient.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)FindResource("PaletteTealColor"), 1));
                    }
                    else if (healthPercent > 50)
                    {
                        gradient.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)FindResource("PaletteYellowColor"), 0));
                        gradient.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)FindResource("PaletteOrangeColor"), 1));
                    }
                    else
                    {
                        gradient.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)FindResource("PaletteRedColor"), 0));
                        gradient.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)FindResource("PaletteRedColor"), 1));
                    }
                    _batteryHealthProgressBar.Foreground = gradient;

                    float liveCharge = System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent * 100;
                    _batteryLiveChargeLabel.Text = $"{liveCharge:F1}%";

                    _batteryCycleCountLabel.Text = batteryInfo.CycleCount.ToString();
                    _batteryManufactureDateLabel.Text = batteryInfo.ManufactureDate?.ToShortDateString() ?? Resource.Unknown;
                    _batteryInfo.Visibility = Visibility.Visible;
                }
                else
                {
                    _batteryInfo.Visibility = Visibility.Collapsed;
                }
            });
        });

        try
        {
            _refreshWarrantyButton.IsEnabled = false;
            ResetWarrantyUi();

            var language = await LocalizationHelper.GetLanguageAsync();

            var warrantyInfo = await _warrantyChecker.GetWarrantyInfo(mi, language, forceRefresh);

            if (warrantyInfo.HasValue)
            {
                var info = warrantyInfo.Value;

                _warrantyStartLabel.Text = info.Start?.ToString(LocalizationHelper.ShortDateFormat) ?? "-";
                _warrantyEndLabel.Text = info.End?.ToString(LocalizationHelper.ShortDateFormat) ?? "-";

                if (info.End.HasValue)
                {
                    var daysRemaining = (info.End.Value - DateTime.Today).Days;
                    if (daysRemaining > 0)
                    {
                        _warrantyDaysRemainingLabel.Text = daysRemaining.ToString();
                        _warrantyStatusLabel.Text = Resource.Valid;
                        _warrantyStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("PaletteGreenBrush");
                        _warrantyStatusIcon.Foreground = (System.Windows.Media.Brush)FindResource("PaletteGreenBrush");

                        if (info.Start.HasValue)
                        {
                            var totalDays = (info.End.Value - info.Start.Value).TotalDays;
                            var elapsedDays = (DateTime.Today - info.Start.Value).TotalDays;
                            if (totalDays > 0 && elapsedDays >= 0)
                            {
                                var pct = Math.Min(100, Math.Max(0, (1.0 - (elapsedDays / totalDays)) * 100));
                                _warrantyProgressBar.Value = pct;
                            }
                            else
                            {
                                _warrantyProgressBar.Value = 100;
                            }
                        }
                        else
                        {
                            _warrantyProgressBar.Value = 100;
                        }
                    }
                    else
                    {
                        _warrantyDaysRemainingLabel.Text = Resource.Expired;
                        _warrantyStatusLabel.Text = Resource.Expired;
                        _warrantyStatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("PaletteRedBrush");
                        _warrantyStatusIcon.Foreground = (System.Windows.Media.Brush)FindResource("PaletteRedBrush");
                        _warrantyProgressBar.Value = 0;
                    }
                }
                else
                {
                    _warrantyDaysRemainingLabel.Text = "-";
                    _warrantyProgressBar.Value = 0;
                }

                _warrantyLinkCardAction.Tag = info.Link;
                _warrantyLinkCardAction.IsEnabled = true;
                _warrantyInfo.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't load warranty info.", ex);
        }
        finally
        {
            _refreshWarrantyButton.IsEnabled = true;
        }
    }

    private void ResetWarrantyUi()
    {
        _warrantyStartLabel.Text = "-";
        _warrantyEndLabel.Text = "-";
        _warrantyDaysRemainingLabel.Text = "-";
        _warrantyStatusLabel.Text = "-";
        _warrantyProgressBar.Value = 0;
        _warrantyLinkCardAction.Tag = null;
        _warrantyLinkCardAction.IsEnabled = false;
    }

    private string GetOSFriendlyName()
    {

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                var productName = key.GetValue("ProductName")?.ToString();
                var displayVersion = key.GetValue("DisplayVersion")?.ToString() ?? key.GetValue("ReleaseId")?.ToString();
                var currentBuildStr = key.GetValue("CurrentBuildNumber")?.ToString() ?? key.GetValue("CurrentBuild")?.ToString();

                if (int.TryParse(currentBuildStr, out int build) && build >= 22000 && productName != null)
                {
                    productName = productName.Replace("Windows 10", "Windows 11");
                }

                if (!string.IsNullOrEmpty(productName))
                {
                    if (!string.IsNullOrEmpty(displayVersion))
                    {
                        return $"{productName} {displayVersion}";
                    }
                    return productName;
                }
            }
        }
        catch { /* fallback */ }
        return System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    }

    private async void RefreshWarrantyButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private async void BiosCard_OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _count++;

        if (_count == 5)
        {
            _count = 0;

            if (!PawnIOHelper.IsPawnIOInstalled())
            {
                PawnIOHelper.ShowPawnIONotify();
            }

            if (_amdOverclockingWindow is not { IsLoaded: true })
            {
                var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
                if (!mi.Properties.IsAmdDevice && !AppFlags.Instance.Debug)
                {
                    return;
                }

                _amdOverclockingWindow = new AmdOverclocking();
                _amdOverclockingWindow.Show();
            }
            else
            {
                _amdOverclockingWindow.Activate();
                if (_amdOverclockingWindow.WindowState == WindowState.Minimized)
                {
                    _amdOverclockingWindow.BringToForeground();
                }
            }
        }
        else if (_count > 1)
        {
            _ = _snackBar.ShowAsync(Resource.GodModeSettingsWindow_Toggle_OC_Title, string.Format(Resource.Overclocking_UnlockSteps, 5 - _count));
        }
    }

    private void WarrantyLinkCardAction_OnClick(object sender, RoutedEventArgs e)
    {
        var link = _warrantyLinkCardAction.Tag as Uri;
        link?.Open();
    }

    private void ToggleSerialNumberButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isSerialNumberRevealed = !_isSerialNumberRevealed;
        _serialNumberLabel.Text = _isSerialNumberRevealed ? _actualSerialNumber : new string('*', _actualSerialNumber.Length);
        _toggleSerialNumberButton.Icon = _isSerialNumberRevealed ? SymbolRegular.EyeOff24 : SymbolRegular.Eye24;
    }

    private void CopyButton_OnClick(object sender, RoutedEventArgs e)
    {
        _count = 0;

        if (sender is not Wpf.Ui.Controls.Button button)
            return;

        var textToCopy = button == _copySerialNumberButton ? _actualSerialNumber : button.Tag as string;
        if (textToCopy is null)
            return;

        try
        {
            Clipboard.SetText(textToCopy);
            _ = _snackBar.ShowAsync(Resource.CopiedToClipboard_Title, string.Format(Resource.CopiedToClipboard_Message_WithParam, textToCopy));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't copy to clipboard", ex);
        }
    }

    private void ExpandCapabilities_OnClick(object sender, RoutedEventArgs e)
    {
        if (_expandCapabilitiesButton.IsChecked == true)
        {
            _capabilitiesLabel.MaxHeight = double.PositiveInfinity;
            _capabilitiesLabel.TextTrimming = TextTrimming.None;
            _expandCapabilitiesButton.Content = Resource.ShowLess;
        }
        else
        {
            _capabilitiesLabel.MaxHeight = 35;
            _capabilitiesLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            _expandCapabilitiesButton.Content = Resource.ShowMore;
        }
    }
}
