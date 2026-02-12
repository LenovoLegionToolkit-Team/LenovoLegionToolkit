using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Overclocking.Amd;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using Microsoft.Win32;
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

            var profile = _controller.LoadProfile();
            if (profile != null)
            {
                UpdateUiFromProfile(profile);
            }
        }
    }

    private async Task InitAndRefreshAsync()
    {
        if (!_isInitialized)
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
        }

        await LoadFromHardwareAsync();

        var profile = _controller.LoadProfile();
        if (profile != null)
        {
            UpdateUiFromProfile(profile);
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
            _fMaxToggle.IsChecked = true;

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

        if (profile.Value.FMax.HasValue)
        {
            _fMaxNumberBox.Value = profile.Value.FMax.Value;
            _fMaxToggle.IsChecked = true;
        }

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
            CoreValues = coreValues
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
            ShowStatus("Saved", "Success", InfoBarSeverity.Success);
        }
    }

    private void OnLoadClick(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog { Filter = "JSON Profile (*.json)|*.json" };
        if (ofd.ShowDialog() == true)
        {
            var loadedProfile = _controller.LoadProfile(ofd.FileName);
            UpdateUiFromProfile(loadedProfile);
            ShowStatus("Loaded", "Success", InfoBarSeverity.Informational);
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
        _fMaxToggle.IsChecked = true;
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

    private void X3DGamingModeEnabled_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        _controller.SwitchProfile(CpuProfileMode.X3DGaming);
        MessagingCenter.Publish(new NotificationMessage(NotificationType.AutomationNotification, Resource.SettingsPage_UseNewDashboard_Restart_Message));
    }

    private void X3DGamingModeDisabled_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingUi)
        {
            return;
        }

        _controller.SwitchProfile(CpuProfileMode.Productivity);
        MessagingCenter.Publish(new NotificationMessage(NotificationType.AutomationNotification, Resource.SettingsPage_UseNewDashboard_Restart_Message));
    }
}