using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using LenovoLegionToolkit.WPF.Windows.Utils;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Windows.Overclocking.Amd;

public partial class AmdOverclocking : UiWindow
{
    private readonly AmdOverclockingController _controller = IoCContainer.Resolve<AmdOverclockingController>();
    private NumberBox[] _coreBoxes = null!;
    private bool _isInitialized;
    private CancellationTokenSource? _statusCts;

    public AmdOverclocking()
    {
        InitializeComponent();

        IsVisibleChanged += AmdOverclocking_IsVisibleChanged;
        Loaded += AmdOverclocking_Loaded;
    }

    private async void AmdOverclocking_Loaded(object sender, RoutedEventArgs e)
    {
        await InitAndRefreshAsync();
    }

    private async void AmdOverclocking_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _initCoreArray();

        if (_isInitialized)
        {
            await RefreshAsync();
        }
    }

    private void _initCoreArray() => _coreBoxes = [_core0, _core1, _core2, _core3, _core4, _core5, _core6, _core7, _core8, _core9, _core10, _core11, _core12, _core13, _core14, _core15];

    private async Task InitAndRefreshAsync()
    {
        if (!_isInitialized)
        {
            try
            {
                await _controller.InitializeAsync();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Init Failed: {ex.Message}"); return;
            }
        }

        UpdateUi();

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

    public async Task ApplyInternalProfileAsync()
    {
        await _controller.ApplyInternalProfileAsync();
    }

    public void UpdateUi()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateUiFromProfile(_controller.LoadProfile());
            _ = RefreshAsync();
        });
    }

    private async Task RefreshAsync()
    {
        try
        {
            var data = await Task.Run(() =>
            {
                var cpu = _controller.GetCpu();
                var activeCores = cpu.info.topology.physicalCores;
                var coreStates = Enumerable.Range(0, _coreBoxes.Length).Select(i =>
                {
                    bool active = i < activeCores && _controller.IsCoreActive(i);
                    uint? margin = active ? cpu.GetPsmMarginSingleCore(_controller.EncodeCoreMarginBitmask(i)) : null;

                    double? val = margin.HasValue ? (double)(int)margin.Value : null;
                    return (active, val);
                }).ToList();

                return new
                {
                    FMax = cpu.GetFMax(),
                    Prochot = cpu.IsProchotEnabled(),
                    States = coreStates,
                    MarginSupported = cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin != 0
                };
            });

            _fMaxNumberBox.Value = data.FMax;
            _prochotCheckBox.IsChecked = data.Prochot;

            for (int i = 0; i < _coreBoxes.Length; i++)
            {
                var state = data.States[i];
                _coreBoxes[i].Visibility = state.active ? Visibility.Visible : Visibility.Collapsed;
                _coreBoxes[i].IsEnabled = state.active && data.MarginSupported;
                _coreBoxes[i].Value = state.active ? state.val : null;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Refresh Failed: {ex.Message}");
        }
    }

    private void UpdateUiFromProfile(OverclockingProfile? profile)
    {
        if (profile == null) return;
        _fMaxNumberBox.Value = profile.Value.FMax;
        _prochotCheckBox.IsChecked = profile.Value.ProchotEnabled;

        for (int i = 0; i < _coreBoxes.Length && i < profile.Value.CoreValues.Count; i++)
        {
            _coreBoxes[i].Value = _controller.IsCoreActive(i) ? profile.Value.CoreValues[i] : null;
        }
    }

    private OverclockingProfile GetProfileFromUi()
    {
        var coreValues = _coreBoxes.Select((t, i) => _controller.IsCoreActive(i) ? t.Value : null).ToList();

        return new OverclockingProfile
        {
            FMax = (uint?)(_fMaxNumberBox.Value),
            ProchotEnabled = _prochotCheckBox.IsChecked ?? false,
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
            await RefreshAsync();
        }
        catch (Exception ex) { ShowStatus($"{Resource.Error}", ex.Message, InfoBarSeverity.Error); }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var sfd = new SaveFileDialog { Filter = "JSON Profile (*.json)|*.json", FileName = "AmdOverclocking.json" };
        if (sfd.ShowDialog() == true) { _controller.SaveProfile(GetProfileFromUi(), sfd.FileName); ShowStatus("Saved", "Success", InfoBarSeverity.Success); }
    }

    private void OnLoadClick(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog { Filter = "JSON Profile (*.json)|*.json" };
        if (ofd.ShowDialog() == true) { UpdateUiFromProfile(_controller.LoadProfile(ofd.FileName)); ShowStatus("Loaded", "Success", InfoBarSeverity.Informational); }
    }

    private void OnResetClick(object sender, RoutedEventArgs e) { foreach (var box in _coreBoxes) box.Value = 0; }

    private void ShowStatus(string title, string message, InfoBarSeverity severity, bool showForever = false)
    {
        _statusCts?.Cancel(); _statusCts = new CancellationTokenSource();
        _statusInfoBar.Title = title; _statusInfoBar.Message = message; _statusInfoBar.Severity = severity; _statusInfoBar.IsOpen = true;
        if (showForever)
        {
            _statusInfoBar.IsOpen = true;
        }
        else
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
        _controller.SwitchProfile(CpuProfileMode.X3DGaming);

        MessagingCenter.Publish(new NotificationMessage(NotificationType.AutomationNotification, Resource.SettingsPage_UseNewDashboard_Restart_Message));
    }

    private void X3DGamingModeDisabled_OnClick(object sender, RoutedEventArgs e)
    {
        _controller.SwitchProfile(CpuProfileMode.Productivity);

        MessagingCenter.Publish(new NotificationMessage(NotificationType.AutomationNotification, Resource.SettingsPage_UseNewDashboard_Restart_Message));
    }
}