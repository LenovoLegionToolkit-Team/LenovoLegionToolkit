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
        if (_isInitialized)
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

        int coresPerCcd = 8;
        int totalCores = (int)cpu.info.topology.physicalCores;
        int ccdCount = (int)Math.Ceiling((double)totalCores / coresPerCcd);

        for (int ccdIndex = 0; ccdIndex < ccdCount; ccdIndex++)
        {
            var group = new AmdCcdGroup
            {
                HeaderTitle = $"CCD {ccdIndex}",
                IsExpanded = true
            };

            int startCore = ccdIndex * coresPerCcd;
            int endCore = Math.Min(startCore + coresPerCcd, totalCores);

            for (int i = startCore; i < endCore; i++)
            {
                group.Cores.Add(new AmdCoreItem
                {
                    Index = i,
                    DisplayName = $"{Resource.AmdOverclocking_Core_Title} {i}",
                    OffsetValue = 0,
                    IsActive = true
                });
            }
            CcdList.Add(group);
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
                var allCores = CcdList.SelectMany(x => x.Cores).ToList();

                foreach (var core in allCores)
                {
                    if (_controller.IsCoreActive(core.Index))
                    {
                        activeCores.Add(core.Index);
                        uint? margin = cpu.GetPsmMarginSingleCore(_controller.EncodeCoreMarginBitmask(core.Index));
                        if (margin.HasValue)
                        {
                            coreReadings[core.Index] = (double)(int)margin.Value;
                        }
                    }
                }

                bool isX3dModeActive = false;
                if (cpu.info.topology.physicalCores == 16)
                {
                    bool hasDataForCcd1 = false;
                    for (int i = 8; i < 16; i++)
                    {
                        if (coreReadings.ContainsKey(i))
                        {
                            hasDataForCcd1 = true;
                            break;
                        }
                    }

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
                    core.IsActive = result.ActiveCores.Contains(core.Index);
                    if (result.Readings.TryGetValue(core.Index, out var reading))
                    {
                        core.OffsetValue = reading;
                    }
                    else
                    {
                        core.OffsetValue = 0;
                    }
                }
                ccd.IsExpanded = true;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Hardware Read Failed: {ex.Message}");
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

        foreach (var ccd in CcdList)
        {
            ccd.IsExpanded = true;
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
                core.OffsetValue = 0;
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
                core.OffsetValue -= 1;
            }
        }
    }

    private void OnGlobalIncrementClick(object sender, RoutedEventArgs e)
    {
        foreach (var ccd in CcdList)
        {
            foreach (var core in ccd.Cores)
            {
                core.OffsetValue += 1;
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