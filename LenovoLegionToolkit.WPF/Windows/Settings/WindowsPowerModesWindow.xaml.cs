using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using Wpf.Ui.Common;
using static LenovoLegionToolkit.Lib.Settings.GodModeSettings;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class WindowsPowerModesWindow
{
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly ITSModeFeature _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();
    private readonly GodModeSettings _godModeSettings = IoCContainer.Resolve<GodModeSettings>();

    private bool IsRefreshing => _loader.IsLoading;

    public WindowsPowerModesWindow()
    {
        InitializeComponent();
        IsVisibleChanged += PowerModesWindow_IsVisibleChanged;
        MessagingCenter.Subscribe<NotificationMessage>(this, OnNotificationReceived);
        Closed += (sender, e) => MessagingCenter.Unsubscribe(this);
    }

    private async void PowerModesWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        _loader.IsLoading = true;

        var loadingTask = Task.Delay(500);

        _tabControl.Items.Clear();

        var powerModes = Enum.GetValues<WindowsPowerMode>();

        var isPowerModeSupported = await _powerModeFeature.IsSupportedAsync();
        if (isPowerModeSupported)
        {
            var allStates = await _powerModeFeature.GetAllStatesAsync();

            foreach (var state in allStates)
            {
                if (state == PowerModeState.GodMode)
                {
                    await BuildGodModeTabAsync(powerModes);
                }
                else
                {
                    BuildModeTab(powerModes, state, state.GetDisplayName(), (mode, isAc) => WindowsPowerModeAcDcChangedAsync(mode, state, isAc));
                }
            }

            var currentState = await _powerModeFeature.GetStateAsync();
            var activeTab = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is PowerModeState state && state == currentState);
            if (activeTab != null)
            {
                _tabControl.SelectedItem = activeTab;
            }
            else if (_tabControl.Items.Count > 0)
            {
                _tabControl.SelectedIndex = 0;
            }
        }
        else
        {
            var isITSModeSupported = await _itsModeFeature.IsSupportedAsync();
            if (isITSModeSupported)
            {
                var itsModes = await _itsModeFeature.GetAllStatesAsync();
                foreach (var itsMode in itsModes.Where(m => m != ITSMode.None))
                {
                    BuildITSModeTab(powerModes, itsMode);
                }
            }

            var currentState = await _itsModeFeature.GetStateAsync();
            var activeTab = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is ITSMode mode && mode == currentState);
            if (activeTab != null)
            {
                _tabControl.SelectedItem = activeTab;
            }
            else if (_tabControl.Items.Count > 0)
            {
                _tabControl.SelectedIndex = 0;
            }
        }

        await loadingTask;
        _loader.IsLoading = false;
    }

    private void BuildModeTab(WindowsPowerMode[] powerModes, PowerModeState powerModeState, string title,
        Func<WindowsPowerMode, bool, Task> onChanged,
        WindowsPowerMode? savedAc = null, WindowsPowerMode? savedDc = null)
    {
        var defaultMode = _settings.Store.PowerModes.GetValueOrDefault(powerModeState, WindowsPowerMode.Balanced);
        savedAc ??= _settings.Store.Overrides.GetPowerModeOnAc(powerModeState);
        savedDc ??= _settings.Store.Overrides.GetPowerModeOnDc(powerModeState);

        var tabItem = new TabItem
        {
            Header = title,
            Tag = powerModeState
        };

        var container = new StackPanel();

        var acCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        acCombo.SetItems(powerModes, savedAc ?? defaultMode, pm => pm.GetDisplayName());
        acCombo.SelectionChanged += async (_, _) =>
        {
            if (acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await onChanged(mode, true);
            }
        };

        var dcCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        dcCombo.SetItems(powerModes, savedDc ?? defaultMode, pm => pm.GetDisplayName());
        dcCombo.SelectionChanged += async (_, _) =>
        {
            if (dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await onChanged(mode, false);
            }
        };

        var acCard = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = SymbolRegular.PlugConnected24,
            Header = new CardHeaderControl { Title = $"{Resource.WindowsPowerPlansWindow_PowerMode_Title} - {Resource.WindowsPowerPlansWindow_PowerMode_AC}" },
            Content = acCombo
        };

        var dcCard = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = SymbolRegular.BatterySaver24,
            Header = new CardHeaderControl { Title = $"{Resource.WindowsPowerPlansWindow_PowerMode_Title} - {Resource.WindowsPowerPlansWindow_PowerMode_DC}" },
            Content = dcCombo
        };

        container.Children.Add(acCard);
        container.Children.Add(dcCard);

        tabItem.Content = container;
        _tabControl.Items.Add(tabItem);
    }

    private void BuildITSModeTab(WindowsPowerMode[] powerModes, ITSMode itsMode)
    {
        var defaultMode = _settings.Store.ITSPowerModes.GetValueOrDefault(itsMode, WindowsPowerMode.Balanced);
        var savedAc = _settings.Store.ITSOverrides.GetPowerModeOnAc(itsMode);
        var savedDc = _settings.Store.ITSOverrides.GetPowerModeOnDc(itsMode);

        var tabItem = new TabItem
        {
            Header = _itsModeFeature.GetITSModeDisplayName(itsMode),
            Tag = itsMode
        };

        var container = new StackPanel();

        var acCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        acCombo.SetItems(powerModes, savedAc ?? defaultMode, pm => pm.GetDisplayName());
        acCombo.SelectionChanged += async (_, _) =>
        {
            if (acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await ITSModeAcDcChangedAsync(itsMode, mode, isAc: true);
            }
        };

        var dcCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        dcCombo.SetItems(powerModes, savedDc ?? defaultMode, pm => pm.GetDisplayName());
        dcCombo.SelectionChanged += async (_, _) =>
        {
            if (dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await ITSModeAcDcChangedAsync(itsMode, mode, isAc: false);
            }
        };

        var acCard = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = SymbolRegular.PlugConnected24,
            Header = new CardHeaderControl { Title = $"{Resource.WindowsPowerPlansWindow_PowerMode_Title} - {Resource.WindowsPowerPlansWindow_PowerMode_AC}" },
            Content = acCombo
        };

        var dcCard = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = SymbolRegular.BatterySaver24,
            Header = new CardHeaderControl { Title = $"{Resource.WindowsPowerPlansWindow_PowerMode_Title} - {Resource.WindowsPowerPlansWindow_PowerMode_DC}" },
            Content = dcCombo
        };

        container.Children.Add(acCard);
        container.Children.Add(dcCard);

        tabItem.Content = container;
        _tabControl.Items.Add(tabItem);
    }

    private async Task BuildGodModeTabAsync(WindowsPowerMode[] powerModes)
    {
        var controller = await _godModeController.GetControllerAsync().ConfigureAwait(false);
        var presets = await controller.GetGodModePresetsAsync().ConfigureAwait(false);

        if (presets.Count > 1)
        {
            var tabItem = new TabItem
            {
                Header = PowerModeState.GodMode.GetDisplayName(),
                Tag = PowerModeState.GodMode
            };

            var container = new StackPanel();

            var presetCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
            var presetList = presets.ToList();
            var activePresetId = _godModeSettings.Store.ActivePresetId;
            var activePreset = presetList.FirstOrDefault(kvp => kvp.Key == activePresetId);
            presetCombo.SetItems(presetList, activePreset.Value is not null ? activePreset : presetList.FirstOrDefault(), kvp => kvp.Value.Name);

            var defaultMode = _settings.Store.PowerModes.GetValueOrDefault(PowerModeState.GodMode, WindowsPowerMode.Balanced);

            var acCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
            acCombo.SetItems(powerModes, defaultMode, pm => pm.GetDisplayName());

            var dcCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
            dcCombo.SetItems(powerModes, defaultMode, pm => pm.GetDisplayName());

            var presetCard = new CardControl
            {
                Margin = new Thickness(0, 0, 0, 8),
                Icon = SymbolRegular.WrenchScrewdriver24,
                Header = new CardHeaderControl { Title = Resource.GodModeSettingsWindow_ActivePreset_Title },
                Content = presetCombo
            };

            var acCard = new CardControl
            {
                Margin = new Thickness(0, 0, 0, 8),
                Icon = SymbolRegular.PlugConnected24,
                Header = new CardHeaderControl { Title = $"{Resource.WindowsPowerPlansWindow_PowerMode_Title} - {Resource.WindowsPowerPlansWindow_PowerMode_AC}" },
                Content = acCombo
            };

            var dcCard = new CardControl
            {
                Margin = new Thickness(0, 0, 0, 8),
                Icon = SymbolRegular.BatterySaver24,
                Header = new CardHeaderControl { Title = $"{Resource.WindowsPowerPlansWindow_PowerMode_Title} - {Resource.WindowsPowerPlansWindow_PowerMode_DC}" },
                Content = dcCombo
            };

            container.Children.Add(presetCard);
            container.Children.Add(acCard);
            container.Children.Add(dcCard);

            var isSwappingPreset = false;

            presetCombo.SelectionChanged += (s, e) =>
            {
                if (presetCombo.TryGetSelectedItem(out KeyValuePair<Guid, GodModeSettingsStore.Preset> selectedKvp))
                {
                    isSwappingPreset = true;
                    try
                    {
                        var livePreset = _godModeSettings.Store.Presets.GetValueOrDefault(selectedKvp.Key, selectedKvp.Value);
                        var savedAc = livePreset.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnAc) ?? _settings.Store.Overrides.GetPowerModeOnAc(PowerModeState.GodMode) ?? defaultMode;
                        var savedDc = livePreset.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnDc) ?? _settings.Store.Overrides.GetPowerModeOnDc(PowerModeState.GodMode) ?? defaultMode;
                        acCombo.SelectItem(savedAc);
                        dcCombo.SelectItem(savedDc);
                    }
                    finally
                    {
                        isSwappingPreset = false;
                    }
                }
            };

            acCombo.SelectionChanged += async (s, e) =>
            {
                if (isSwappingPreset || IsRefreshing) return;
                if (presetCombo.TryGetSelectedItem(out KeyValuePair<Guid, GodModeSettingsStore.Preset> selectedKvp) && acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
                {
                    await GodModePresetPowerModeChangedAsync(selectedKvp.Key.ToString(), mode, isAc: true);
                }
            };

            dcCombo.SelectionChanged += async (s, e) =>
            {
                if (isSwappingPreset || IsRefreshing) return;
                if (presetCombo.TryGetSelectedItem(out KeyValuePair<Guid, GodModeSettingsStore.Preset> selectedKvp) && dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
                {
                    await GodModePresetPowerModeChangedAsync(selectedKvp.Key.ToString(), mode, isAc: false);
                }
            };

            if (presetCombo.TryGetSelectedItem(out KeyValuePair<Guid, GodModeSettingsStore.Preset> initialKvp))
            {
                var liveInitialPreset = _godModeSettings.Store.Presets.GetValueOrDefault(initialKvp.Key, initialKvp.Value);
                var initialAc = liveInitialPreset.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnAc) ?? _settings.Store.Overrides.GetPowerModeOnAc(PowerModeState.GodMode) ?? defaultMode;
                var initialDc = liveInitialPreset.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnDc) ?? _settings.Store.Overrides.GetPowerModeOnDc(PowerModeState.GodMode) ?? defaultMode;
                acCombo.SelectItem(initialAc);
                dcCombo.SelectItem(initialDc);
            }

            tabItem.Content = container;
            _tabControl.Items.Add(tabItem);
        }
        else
        {
            var singlePreset = presets.FirstOrDefault();
            var defaultMode = _settings.Store.PowerModes.GetValueOrDefault(PowerModeState.GodMode, WindowsPowerMode.Balanced);
            var savedAc = singlePreset.Value?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnAc) ?? _settings.Store.Overrides.GetPowerModeOnAc(PowerModeState.GodMode) ?? defaultMode;
            var savedDc = singlePreset.Value?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerModeOnDc) ?? _settings.Store.Overrides.GetPowerModeOnDc(PowerModeState.GodMode) ?? defaultMode;

            var presetKey = singlePreset.Key.ToString();
            BuildModeTab(powerModes, PowerModeState.GodMode, PowerModeState.GodMode.GetDisplayName(),
                async (mode, isAc) => await GodModePresetPowerModeChangedAsync(presetKey, mode, isAc),
                savedAc: savedAc, savedDc: savedDc);
        }
    }

    private async Task WindowsPowerModeAcDcChangedAsync(WindowsPowerMode windowsPowerMode, PowerModeState powerModeState, bool isAc)
    {
        if (IsRefreshing)
        {
            return;
        }

        if (isAc)
        {
            _settings.Store.Overrides.SetPowerModeOnAc(powerModeState, windowsPowerMode);
        }
        else
        {
            _settings.Store.Overrides.SetPowerModeOnDc(powerModeState, windowsPowerMode);
        }

        _settings.SynchronizeStore();

        var currentState = await _powerModeFeature.GetStateAsync();
        if (currentState == powerModeState)
        {
            await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync();
        }
    }

    private async Task ITSModeAcDcChangedAsync(ITSMode itsMode, WindowsPowerMode windowsPowerMode, bool isAc)
    {
        if (IsRefreshing)
        {
            return;
        }

        if (isAc)
        {
            _settings.Store.ITSOverrides.SetPowerModeOnAc(itsMode, windowsPowerMode);
        }
        else
        {
            _settings.Store.ITSOverrides.SetPowerModeOnDc(itsMode, windowsPowerMode);
        }

        _settings.SynchronizeStore();

        var currentState = await _itsModeFeature.GetStateAsync();
        if (currentState == itsMode)
        {
            var controller = IoCContainer.Resolve<WindowsPowerModeController>();
            await controller.SetPowerModeAsync(itsMode);
        }
    }

    private async Task GodModePresetPowerModeChangedAsync(string presetKey, WindowsPowerMode windowsPowerMode, bool isAc)
    {
        if (IsRefreshing)
        {
            return;
        }

        var presetKvp = _godModeSettings.Store.Presets.FirstOrDefault(profile => profile.Key.ToString() == presetKey);

        if (!presetKvp.Equals(default(KeyValuePair<Guid, GodModeSettingsStore.Preset>)) && presetKvp.Value != null)
        {
            var newOv = new Dictionary<PowerOverrideKey, string>(presetKvp.Value.Overrides ?? []);
            if (isAc)
            {
                newOv.SetEnum(PowerOverrideKey.PowerModeOnAc, (WindowsPowerMode?)windowsPowerMode);
            }
            else
            {
                newOv.SetEnum(PowerOverrideKey.PowerModeOnDc, (WindowsPowerMode?)windowsPowerMode);
            }
            var updated = presetKvp.Value with { Overrides = newOv };
            _godModeSettings.Store.Presets[presetKvp.Key] = updated;
            _godModeSettings.SynchronizeStore();

            var currentState = await _powerModeFeature.GetStateAsync();
            if (currentState == PowerModeState.GodMode && _godModeSettings.Store.ActivePresetId.ToString() == presetKey)
            {
                await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync(updated);
            }
        }
    }

    private void OnNotificationReceived(NotificationMessage message)
    {
        Dispatcher.Invoke(() =>
        {
            if (IsRefreshing)
            {
                return;
            }

            TabItem? targetTab = null;

            switch (message.Type)
            {
                case NotificationType.PowerModeQuiet:
                    targetTab = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is PowerModeState state && state == PowerModeState.Quiet);
                    break;
                case NotificationType.PowerModeBalance:
                    targetTab = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is PowerModeState state && state == PowerModeState.Balance);
                    break;
                case NotificationType.PowerModePerformance:
                    targetTab = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is PowerModeState state && state == PowerModeState.Performance);
                    break;
                case NotificationType.PowerModeExtreme:
                    targetTab = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is PowerModeState state && state == PowerModeState.Extreme);
                    break;
                case NotificationType.PowerModeGodMode:
                    targetTab = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is PowerModeState state && state == PowerModeState.GodMode);
                    break;

                case NotificationType.ITSModeAuto:
                    targetTab = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is ITSMode mode && mode == ITSMode.ItsAuto);
                    break;
                case NotificationType.ITSModeCool:
                    targetTab = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is ITSMode mode && mode == ITSMode.MmcCool);
                    break;
                case NotificationType.ITSModePerformance:
                    targetTab = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is ITSMode mode && mode == ITSMode.MmcPerformance);
                    break;
                case NotificationType.ITSModeGeek:
                    targetTab = _tabControl.Items.Cast<TabItem>().FirstOrDefault(t => t.Tag is ITSMode mode && mode == ITSMode.MmcGeek);
                    break;
            }

            if (targetTab != null && _tabControl.SelectedItem != targetTab)
            {
                _tabControl.SelectedItem = targetTab;
            }
        });
    }
}
