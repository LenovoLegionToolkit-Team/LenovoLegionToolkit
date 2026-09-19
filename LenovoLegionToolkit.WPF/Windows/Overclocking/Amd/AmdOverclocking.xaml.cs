using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Overclocking.Amd;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Windows.Overclocking.Amd;

public partial class AmdOverclocking : UiWindow
{
    private readonly AmdOverclockingController _controller = IoCContainer.Resolve<AmdOverclockingController>();
    private bool _isInitialized;
    private bool _isUpdatingUi = false;
    private CancellationTokenSource? _statusCts;

    public ObservableCollection<AmdCcdGroup> CcdList { get; set; } = new();

    public AmdOverclocking()
    {
        InitializeComponent();
        _ccdItemsControl.ItemsSource = CcdList;

        IsVisibleChanged += AmdOverclocking_IsVisibleChanged;
        Loaded += AmdOverclocking_Loaded;
    }

    private async void AmdOverclocking_Loaded(object sender, RoutedEventArgs e)
    {
        await InitAndRefreshAsync();
    }

    private async void AmdOverclocking_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_isInitialized && IsVisible)
        {
            await LoadFromHardwareAsync();

            try
            {
                _isUpdatingUi = true;
                _allowOnBatteryToggle.IsChecked = _controller.AllowOnBattery;
                _allowInAllPowerModesToggle.IsChecked = _controller.AllowInAllPowerModes;
            }
            finally
            {
                _isUpdatingUi = false;
            }

            var profile = _controller.LoadProfile();
            if (profile != null)
            {
                UpdateUiFromProfile(profile);
            }
        }
    }

    private async Task InitAndRefreshAsync()
    {
        if (_isInitialized)
        {
            await LoadFromHardwareAsync();

            var profile = _controller.LoadProfile();
            if (profile != null)
                UpdateUiFromProfile(profile);

            return;
        }

        var isEnabled = _controller.Enabled;

        try
        {
            _isUpdatingUi = true;
            _enableToggle.IsChecked = isEnabled;
            _allowOnBatteryToggle.IsChecked = _controller.AllowOnBattery;
            _allowInAllPowerModesToggle.IsChecked = _controller.AllowInAllPowerModes;
        }
        finally
        {
            _isUpdatingUi = false;
        }

        if (!isEnabled)
        {
            SetControlsEnabled(false);
            return;
        }

        await InitializeAndLoadAsync(_controller.LoadProfile());
    }

    private async Task InitializeAndLoadAsync(OverclockingProfile? existingProfile)
    {
        try
        {
            await _controller.InitializeAsync();
            InitializeDynamicUi();
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Init Failed: {ex.Message}");
            return;
        }

        SetControlsEnabled(true);

        await LoadFromHardwareAsync();

        _controller.SaveDefaultProfile(GetProfileFromUi());

        if (existingProfile != null)
        {
            UpdateUiFromProfile(existingProfile.Value);
        }

        if (!_controller.DoNotApply)
        {
            await ApplyInternalProfileAsync();
        }
        else
        {
            ShowStatus($"{Resource.Error}", $"{Resource.AmdOverclocking_Do_Not_Apply_Message}", InfoBarSeverity.Error, true);
            _controller.DoNotApply = false;
        }
    }

    private void InitializeDynamicUi()
    {
        CcdList.Clear();
        var cpu = _controller.GetCpu();

        int totalCores = (int)cpu.info.topology.physicalCores;

        // Assume CCX = CCD = 1
        int coresPerCcd = (int)cpu.info.topology.coresPerCcx;

        for (int coreIndex = 0; coreIndex < totalCores; coreIndex++)
        {
            int currentCcdIndex = coreIndex / coresPerCcd;

            if (CcdList.Count <= currentCcdIndex)
            {
                var newGroup = new AmdCcdGroup
                {
                    HeaderTitle = $"CCD {currentCcdIndex}",
                    IsExpanded = true
                };
                CcdList.Add(newGroup);
            }

            var currentGroup = CcdList[currentCcdIndex];

            currentGroup.Cores.Add(new AmdCoreItem
            {
                Index = coreIndex,
                DisplayName = $"{Resource.AmdOverclocking_Core_Title} {coreIndex}",
                OffsetValue = 0,
                IsActive = false,
                IsEnabled = false
            });
        }
    }

    private async Task LoadFromHardwareAsync()
    {
        try
        {
            var result = await Task.Run(() =>
            {
                var cpu = _controller.GetCpu();
                var fmax = cpu.GetFMax();

                var coreReadings = new Dictionary<int, double>();
                var activeCores = new HashSet<int>();

                int totalCores = (int)cpu.info.topology.physicalCores;

                for (int i = 0; i < totalCores; i++)
                {
                    if (_controller.IsCoreActive(i))
                    {
                        try
                        {
                            uint? margin = cpu.GetPsmMarginSingleCore(_controller.EncodeCoreMarginBitmask(i));
                            if (margin.HasValue)
                            {
                                coreReadings[i] = (double)(int)margin.Value;
                                activeCores.Add(i);
                            }
                        }
                        catch { /* Ignore */ }
                    }
                }

                bool isX3dModeActive = false;
                if (totalCores == 16)
                {
                    bool hasDataForCcd1 = activeCores.Any(id => id >= 8);
                    if (!hasDataForCcd1)
                    {
                        isX3dModeActive = true;
                    }
                }

                return new { FMax = fmax, Readings = coreReadings, IsX3dMode = isX3dModeActive, ActiveCores = activeCores };
            });

            _fMaxNumberBox.Value = result.FMax;

            if (_x3dGamingToggle != null)
            {
                _isUpdatingUi = true;
                _x3dGamingToggle.IsChecked = result.IsX3dMode;
            }

            foreach (var ccd in CcdList)
            {
                foreach (var core in ccd.Cores)
                {
                    bool isActive = result.ActiveCores.Contains(core.Index);

                    core.IsActive = isActive;
                    core.IsEnabled = isActive;

                    if (isActive && result.Readings.TryGetValue(core.Index, out var reading))
                    {
                        core.OffsetValue = reading;
                    }
                }

                ccd.IsExpanded = ccd.Cores.Any(x => x.IsActive);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Hardware Read Failed: {ex.Message}");
        }
        finally
        {
            _isUpdatingUi = false;
        }
    }

    private void UpdateUiFromProfile(OverclockingProfile? profile)
    {
        if (profile == null) return;

        _fMaxToggle.IsChecked = profile.Value.FMax.HasValue;

        if (profile.Value.FMax.HasValue)
        {
            _fMaxNumberBox.Value = profile.Value.FMax.Value;
        }

        if (profile.Value.PowerLimit1.HasValue) _powerLimit1Box.Value = profile.Value.PowerLimit1.Value;
        if (profile.Value.PowerLimit2.HasValue) _powerLimit2Box.Value = profile.Value.PowerLimit2.Value;
        if (profile.Value.PowerLimit3.HasValue) _powerLimit3Box.Value = profile.Value.PowerLimit3.Value;
        if (profile.Value.TDCSoc.HasValue) _tdcSocBox.Value = profile.Value.TDCSoc.Value;
        if (profile.Value.TDCVdd.HasValue) _tdcVddBox.Value = profile.Value.TDCVdd.Value;
        if (profile.Value.EDCSoc.HasValue) _edcSocBox.Value = profile.Value.EDCSoc.Value;
        if (profile.Value.EDCVdd.HasValue) _edcVddBox.Value = profile.Value.EDCVdd.Value;

        var allCores = CcdList.SelectMany(ccd => ccd.Cores).ToList();

        for (int i = 0; i < allCores.Count; i++)
        {
            var coreItem = allCores[i];
            if (i < profile.Value.CoreValues.Count)
            {
                var savedVal = profile.Value.CoreValues[i];
                if (savedVal.HasValue)
                {
                    coreItem.OffsetValue = savedVal.Value;
                }
            }
        }

        if (profile.Value.CurveShapeValues != null)
        {
            ApplyCurveShapeToUi(profile.Value.CurveShapeValues);
        }
    }

    public async Task ApplyInternalProfileAsync()
    {
        await _controller.ApplyInternalProfileAsync();
    }

    private OverclockingProfile GetProfileFromUi()
    {
        var allCores = CcdList.SelectMany(ccd => ccd.Cores).ToList();

        var coreValues = allCores.Select(c => (double?)c.OffsetValue).ToList();

        uint? fmaxVal = _fMaxToggle.IsChecked == true ? (uint?)(_fMaxNumberBox.Value) : null;

        return new OverclockingProfile
        {
            FMax = fmaxVal,
            CoreValues = coreValues,
            PowerLimit1 = (short?)_powerLimit1Box.Value,
            PowerLimit2 = (short?)_powerLimit2Box.Value,
            PowerLimit3 = (short?)_powerLimit3Box.Value,
            TDCSoc = (short?)_tdcSocBox.Value,
            TDCVdd = (short?)_tdcVddBox.Value,
            EDCSoc = (short?)_edcSocBox.Value,
            EDCVdd = (short?)_edcVddBox.Value,
            CurveShapeValues = GetCurveShapeValuesFromUi(),
        };
    }

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = GetProfileFromUi();
            await _controller.ApplyProfileAsync(profile);
            _controller.SaveProfile(profile);
            ShowStatus($"{Resource.AmdOverclocking_Success_Title}", $"{Resource.AmdOverclocking_Success_Message}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus($"{Resource.Error}", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog { Filter = "JSON Profile (*.json)|*.json", FileName = "AmdOverclocking.json" };
        if (sfd.ShowDialog() == true)
        {
            _controller.SaveProfile(GetProfileFromUi(), sfd.FileName);
            ShowStatus(Resource.Export, Resource.AmdOverclocking_Success_Title, InfoBarSeverity.Success);
        }
    }

    private void OnLoadClick(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog { Filter = "JSON Profile (*.json)|*.json" };
        if (ofd.ShowDialog() == true)
        {
            var loadedProfile = _controller.LoadProfile(ofd.FileName);
            UpdateUiFromProfile(loadedProfile);
            ShowStatus(Resource.Import, Resource.AmdOverclocking_Success_Title, InfoBarSeverity.Informational);
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        foreach (var ccd in CcdList)
        {
            foreach (var core in ccd.Cores)
            {
                if (core.IsEnabled)
                {
                    core.OffsetValue = 0;
                }
            }
        }
        _fMaxNumberBox.Value = 0;
        _fMaxToggle.IsChecked = false;
    }

    private void OnGlobalDecrementClick(object sender, RoutedEventArgs e)
    {
        foreach (var ccd in CcdList)
        {
            foreach (var core in ccd.Cores)
            {
                if (core.IsEnabled)
                {
                    core.OffsetValue -= 1;
                }
            }
        }
    }

    private void OnGlobalIncrementClick(object sender, RoutedEventArgs e)
    {
        foreach (var ccd in CcdList)
        {
            foreach (var core in ccd.Cores)
            {
                if (core.IsEnabled)
                {
                    core.OffsetValue += 1;
                }
            }
        }
    }

    private void CurveShaperGlobalDecrement_Click(object sender, RoutedEventArgs e)
    {
        AdjustAllCurveShaperValues(-1);
    }

    private void CurveShaperGlobalIncrement_Click(object sender, RoutedEventArgs e)
    {
        AdjustAllCurveShaperValues(+1);
    }

    private void AdjustAllCurveShaperValues(int delta)
    {
        var seen = new HashSet<string>();

        foreach (var btn in FindVisualChildren<System.Windows.Controls.Button>(_curveShaperPanel))
        {
            if (btn.Tag is not string tag || string.IsNullOrEmpty(tag))
                continue;

            if (!seen.Add(tag))
                continue;

            if (btn.Parent is not System.Windows.Controls.StackPanel panel)
                continue;

            var textBlock = panel.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
            if (textBlock is null || !int.TryParse(textBlock.Text, out var value))
                continue;

            value += delta;
            value = Math.Clamp(value, -200, 200);
            textBlock.Text = value.ToString();
        }
    }

    private async void EnableToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;

        if (_isInitialized)
        {
            SaveEnabledState(true);
            SetControlsEnabled(true);
            return;
        }

        SaveEnabledState(true);
        await InitializeAndLoadAsync(_controller.LoadProfile());
    }

    private void EnableToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;

        SaveEnabledState(false);
        SetControlsEnabled(false);
    }

    private void AllowOnBatteryToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;

        _controller.AllowOnBattery = _allowOnBatteryToggle.IsChecked == true;
    }

    private void AllowInAllPowerModesToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi) return;

        _controller.AllowInAllPowerModes = _allowInAllPowerModesToggle.IsChecked == true;
    }

    private void SaveEnabledState(bool enabled)
    {
        _controller.Enabled = enabled;
    }

    private void SetControlsEnabled(bool enabled)
    {
        _allowOnBatteryToggle.IsEnabled = enabled;
        _allowInAllPowerModesToggle.IsEnabled = enabled;
        _fMaxToggle.IsEnabled = enabled;
        _fMaxNumberBox.IsEnabled = enabled;
        _powerLimit1Box.IsEnabled = enabled;
        _powerLimit2Box.IsEnabled = enabled;
        _powerLimit3Box.IsEnabled = enabled;
        _tdcSocBox.IsEnabled = enabled;
        _tdcVddBox.IsEnabled = enabled;
        _edcSocBox.IsEnabled = enabled;
        _edcVddBox.IsEnabled = enabled;
        _x3dGamingToggle.IsEnabled = enabled;
        _ccdItemsControl.IsEnabled = enabled;
        _curveShaperPanel.IsEnabled = enabled;
        _applyButton.IsEnabled = enabled;
        _loadButton.IsEnabled = enabled;
        _saveButton.IsEnabled = enabled;
        _refreshButton.IsEnabled = enabled;
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity, bool showForever = false)
    {
        _statusCts?.Cancel();
        _statusCts = new CancellationTokenSource();
        _statusInfoBar.Title = title;
        _statusInfoBar.Message = message;
        _statusInfoBar.Severity = severity;
        _statusInfoBar.IsOpen = true;

        if (!showForever)
        {
            Task.Delay(5000, _statusCts.Token).ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    Dispatcher.Invoke(() => _statusInfoBar.IsOpen = false);
                }
            }, TaskScheduler.Default);
        }
    }

    private async void X3DGamingModeEnabled_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        var result = await MessageBoxHelper.ShowAsync(
            this,
            Resource.RestartRequired_Title,
            Resource.RestartRequired_Message,
            Resource.RestartNow,
            Resource.RestartLater).ConfigureAwait(false);

        if (!result)
        {
            return;
        }

        _controller.SwitchProfile(CpuProfileMode.X3DGaming);
        await Power.RestartAsync().ConfigureAwait(false);
    }

    private async void X3DGamingModeDisabled_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        var result = await MessageBoxHelper.ShowAsync(
            this,
            Resource.RestartRequired_Title,
            Resource.RestartRequired_Message,
            Resource.RestartNow,
            Resource.RestartLater).ConfigureAwait(false);

        if (!result)
        {
            return;
        }

        _controller.SwitchProfile(CpuProfileMode.Productivity);
        await Power.RestartAsync().ConfigureAwait(false);
    }

    #region Curve Shape

    private void CurveShaperDecrement_Click(object sender, RoutedEventArgs e)
    {
        AdjustCurveShaperValue(sender, -1);
    }

    private void CurveShaperIncrement_Click(object sender, RoutedEventArgs e)
    {
        AdjustCurveShaperValue(sender, +1);
    }

    private static void AdjustCurveShaperValue(object sender, int delta)
    {
        if (sender is not System.Windows.Controls.Button btn)
            return;

        var tag = btn.Tag as string;
        if (string.IsNullOrEmpty(tag))
            return;

        if (btn.Parent is not System.Windows.Controls.StackPanel panel)
            return;

        var textBlock = panel.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
        if (textBlock is null)
            return;

        if (!int.TryParse(textBlock.Text, out var value))
            return;

        value += delta;
        value = Math.Clamp(value, -200, 200);
        textBlock.Text = value.ToString();
    }

    private async void CurveShaperApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = GetProfileFromUi();
            await _controller.ApplyProfileAsync(profile);
            _controller.SaveProfile(profile);
            ShowStatus($"{Resource.AmdOverclocking_Success_Title}", $"{Resource.AmdOverclocking_Success_Message}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus($"{Resource.Error}", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void CurveShaperReset_Click(object sender, RoutedEventArgs e)
    {
        ApplyCurveShapeToUi(null);
    }

    private Dictionary<CurveShapeLevel, List<int>>? GetCurveShapeValuesFromUi()
    {
        var result = new Dictionary<CurveShapeLevel, List<int>>();
        var seen = new HashSet<string>();

        foreach (var btn in FindVisualChildren<System.Windows.Controls.Button>(_curveShaperPanel))
        {
            if (btn.Tag is not string tag || string.IsNullOrEmpty(tag))
                continue;

            if (!seen.Add(tag))
                continue;

            var parts = tag.Split(',');
            if (parts.Length != 2) continue;
            if (!Enum.TryParse<CurveShapeLevel>(parts[0], out var level)) continue;
            if (!int.TryParse(parts[1], out var tempIndex)) continue;

            if (btn.Parent is not System.Windows.Controls.StackPanel panel)
                continue;

            var textBlock = panel.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
            if (textBlock is null || !int.TryParse(textBlock.Text, out var value))
                continue;

            if (!result.ContainsKey(level))
                result[level] = [0, 0, 0];

            if (tempIndex < result[level].Count)
                result[level][tempIndex] = value;
        }

        return result.Any(kvp => kvp.Value.Any(v => v != 0)) ? result : null;
    }

    private void ApplyCurveShapeToUi(Dictionary<CurveShapeLevel, List<int>>? values)
    {
        var seen = new HashSet<string>();

        foreach (var btn in FindVisualChildren<System.Windows.Controls.Button>(_curveShaperPanel))
        {
            if (btn.Tag is not string tag || string.IsNullOrEmpty(tag))
                continue;

            if (!seen.Add(tag))
                continue;

            var parts = tag.Split(',');
            if (parts.Length != 2) continue;
            if (!Enum.TryParse<CurveShapeLevel>(parts[0], out var level)) continue;
            if (!int.TryParse(parts[1], out var tempIndex)) continue;

            if (btn.Parent is not System.Windows.Controls.StackPanel panel)
                continue;

            var textBlock = panel.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
            if (textBlock is null)
                continue;

            var newValue = 0;
            if (values != null &&
                values.TryGetValue(level, out var list) &&
                tempIndex < list.Count)
            {
                newValue = list[tempIndex];
            }

            textBlock.Text = newValue.ToString();
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                yield return typed;

            foreach (var grandChild in FindVisualChildren<T>(child))
                yield return grandChild;
        }
    }

    #endregion
}
