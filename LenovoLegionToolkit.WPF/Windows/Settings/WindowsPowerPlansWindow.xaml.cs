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
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using Wpf.Ui.Common;
using static LenovoLegionToolkit.Lib.Settings.GodModeSettings;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class WindowsPowerPlansWindow
{
    private static readonly WindowsPowerPlan DefaultValue = new(Guid.Empty, Resource.WindowsPowerPlansWindow_DefaultPowerPlan, false);

    private readonly WindowsPowerPlanController _windowsPowerPlanController = IoCContainer.Resolve<WindowsPowerPlanController>();
    private readonly PowerModeFeature _powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
    private readonly ITSModeFeature _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly GodModeController _godModeController = IoCContainer.Resolve<GodModeController>();
    private readonly GodModeSettings _godModeSettings = IoCContainer.Resolve<GodModeSettings>();

    private bool IsRefreshing => _loader.IsLoading;
    private bool _isNonGaming;
    private ITSMode _godModeItsMode;

    public WindowsPowerPlansWindow()
    {
        InitializeComponent();
        IsVisibleChanged += PowerPlansWindow_IsVisibleChanged;
        MessagingCenter.Subscribe<NotificationMessage>(this, OnNotificationReceived);
        Closed += (sender, e) => MessagingCenter.Unsubscribe(this);
    }

    private async void PowerPlansWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
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

        var compatibility = await Compatibility.GetMachineInformationAsync();
        _aoAcWarningCard.Visibility = compatibility.Properties.SupportsAlwaysOnAc.status
            ? Visibility.Visible
            : Visibility.Collapsed;

        _tabControl.Items.Clear();

        _isNonGaming = compatibility.Properties.GodModePlatform == GodModePlatform.NonGaming;
        _godModeItsMode = _isNonGaming ? await _itsModeFeature.GetStateAsync() : ITSMode.None;

        var isPowerModeSupported = await _powerModeFeature.IsSupportedAsync();

        if (isPowerModeSupported)
        {
            var powerPlans = _windowsPowerPlanController.GetPowerPlans().OrderBy(x => x.Name).Prepend(DefaultValue).ToArray();
            var powerModes = Enum.GetValues<WindowsPowerMode>();
            var allStates = await _powerModeFeature.GetAllStatesAsync();

            foreach (var state in allStates)
            {
                if (state == PowerModeState.GodMode)
                {
                    await BuildGodModeTabsAsync(powerPlans, powerModes);
                }
                else
                {
                    BuildPowerPlanTab(powerPlans, powerModes, state, state.GetDisplayName());
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
                var powerPlans = _windowsPowerPlanController.GetPowerPlans().OrderBy(x => x.Name).Prepend(DefaultValue).ToArray();
                var powerModes = Enum.GetValues<WindowsPowerMode>();
                var itsModes = await _itsModeFeature.GetAllStatesAsync();

                foreach (var itsMode in itsModes.Where(m => m != ITSMode.None))
                {
                    BuildITSPowerPlanTab(powerPlans, powerModes, itsMode);
                }

                if (_isNonGaming)
                {
                    await BuildGodModeTabsAsync(powerPlans, powerModes);
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

    private void BuildPowerPlanTab(WindowsPowerPlan[] powerPlans, WindowsPowerMode[] powerModes,
        PowerModeState state, string title,
        Guid? savedPlan = null, WindowsPowerMode? savedAc = null, WindowsPowerMode? savedDc = null,
        Func<WindowsPowerPlan, Task>? onPlanChanged = null,
        Func<WindowsPowerMode, bool, Task>? onOverlayChanged = null)
    {
        Guid settingsPowerPlanGuid;
        if (savedPlan.HasValue)
        {
            settingsPowerPlanGuid = savedPlan.Value;
        }
        else if (!_settings.Store.PowerPlans.TryGetValue(state, out settingsPowerPlanGuid))
        {
            settingsPowerPlanGuid = Guid.Empty;
        }
        var selectedPlan = powerPlans.FirstOrDefault(pp => pp.Guid == settingsPowerPlanGuid);
        var effectivePlan = (selectedPlan == default) ? DefaultValue : selectedPlan;

        var tabItem = new TabItem
        {
            Header = title,
            Tag = state
        };

        var container = new StackPanel();

        var comboBox = new ComboBox
        {
            MinWidth = 200,
            MaxDropDownHeight = 300,
            VerticalAlignment = VerticalAlignment.Center
        };
        comboBox.SetItems(powerPlans, effectivePlan, pp => pp.Name);

        var planCard = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = SymbolRegular.FlashSettings24,
            Header = new CardHeaderControl { Title = Resource.WindowsPowerPlan_Title },
            Content = comboBox
        };
        container.Children.Add(planCard);

        var acCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        var dcCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };

        var ac = savedAc ?? (_settings.Store.Overrides.GetPowerPlanBalanceOnAc(state) ?? WindowsPowerMode.Balanced);
        var dc = savedDc ?? (_settings.Store.Overrides.GetPowerPlanBalanceOnDc(state) ?? WindowsPowerMode.Balanced);

        acCombo.SetItems(powerModes, ac, pm => pm.GetDisplayName());
        dcCombo.SetItems(powerModes, dc, pm => pm.GetDisplayName());

        var acCard = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = SymbolRegular.PlugConnected24,
            Header = new CardHeaderControl { Title = $"{Resource.WindowsPowerPlansWindow_PowerMode_Title} - {Resource.WindowsPowerPlansWindow_PowerMode_AC}" },
            Content = acCombo,
            Visibility = IsBalancedPlan(effectivePlan.Guid) ? Visibility.Visible : Visibility.Collapsed
        };

        var dcCard = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = SymbolRegular.BatterySaver24,
            Header = new CardHeaderControl { Title = $"{Resource.WindowsPowerPlansWindow_PowerMode_Title} - {Resource.WindowsPowerPlansWindow_PowerMode_DC}" },
            Content = dcCombo,
            Visibility = IsBalancedPlan(effectivePlan.Guid) ? Visibility.Visible : Visibility.Collapsed
        };

        acCombo.SelectionChanged += async (_, _) =>
        {
            if (acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                if (onOverlayChanged != null)
                {
                    await onOverlayChanged(mode, true);
                }
                else
                {
                    await BalanceOverlayChangedAsync(mode, state, isAc: true);
                }
            }
        };
        dcCombo.SelectionChanged += async (_, _) =>
        {
            if (dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                if (onOverlayChanged != null)
                {
                    await onOverlayChanged(mode, false);
                }
                else
                {
                    await BalanceOverlayChangedAsync(mode, state, isAc: false);
                }
            }
        };

        comboBox.SelectionChanged += async (_, _) =>
        {
            if (comboBox.TryGetSelectedItem(out WindowsPowerPlan plan))
            {
                var visibility = IsBalancedPlan(plan.Guid) ? Visibility.Visible : Visibility.Collapsed;
                acCard.Visibility = visibility;
                dcCard.Visibility = visibility;


                if (onPlanChanged != null)
                {
                    await onPlanChanged(plan);
                }
                else
                {
                    await WindowsPowerPlanChangedAsync(plan, state);
                }
            }
        };

        container.Children.Add(acCard);
        container.Children.Add(dcCard);

        tabItem.Content = container;
        _tabControl.Items.Add(tabItem);
    }

    private void BuildITSPowerPlanTab(WindowsPowerPlan[] powerPlans, WindowsPowerMode[] powerModes, ITSMode itsMode)
    {
        var settingsPowerPlanGuid = _settings.Store.ITSPowerPlans.GetValueOrDefault(itsMode);
        var selectedPlan = powerPlans.FirstOrDefault(pp => pp.Guid == settingsPowerPlanGuid);
        var effectivePlan = (selectedPlan == default) ? DefaultValue : selectedPlan;

        var tabItem = new TabItem
        {
            Header = itsMode.GetDisplayName(),
            Tag = itsMode
        };

        var container = new StackPanel();

        var comboBox = new ComboBox
        {
            MinWidth = 200,
            MaxDropDownHeight = 300,
            VerticalAlignment = VerticalAlignment.Center
        };
        comboBox.SetItems(powerPlans, effectivePlan, pp => pp.Name);

        var planCard = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = SymbolRegular.FlashSettings24,
            Header = new CardHeaderControl { Title = Resource.WindowsPowerPlan_Title },
            Content = comboBox
        };
        container.Children.Add(planCard);

        var acCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
        var dcCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };

        var ac = _settings.Store.ITSOverrides.GetPowerPlanBalanceOnAc(itsMode) ?? WindowsPowerMode.Balanced;
        var dc = _settings.Store.ITSOverrides.GetPowerPlanBalanceOnDc(itsMode) ?? WindowsPowerMode.Balanced;

        acCombo.SetItems(powerModes, ac, pm => pm.GetDisplayName());
        dcCombo.SetItems(powerModes, dc, pm => pm.GetDisplayName());

        var acCard = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = SymbolRegular.PlugConnected24,
            Header = new CardHeaderControl { Title = $"{Resource.WindowsPowerPlansWindow_PowerMode_Title} - {Resource.WindowsPowerPlansWindow_PowerMode_AC}" },
            Content = acCombo,
            Visibility = IsBalancedPlan(effectivePlan.Guid) ? Visibility.Visible : Visibility.Collapsed
        };

        var dcCard = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Icon = SymbolRegular.BatterySaver24,
            Header = new CardHeaderControl { Title = $"{Resource.WindowsPowerPlansWindow_PowerMode_Title} - {Resource.WindowsPowerPlansWindow_PowerMode_DC}" },
            Content = dcCombo,
            Visibility = IsBalancedPlan(effectivePlan.Guid) ? Visibility.Visible : Visibility.Collapsed
        };

        acCombo.SelectionChanged += async (_, _) =>
        {
            if (acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await ITSBalanceOverlayChangedAsync(itsMode, mode, isAc: true);
            }
        };
        dcCombo.SelectionChanged += async (_, _) =>
        {
            if (dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
            {
                await ITSBalanceOverlayChangedAsync(itsMode, mode, isAc: false);
            }
        };

        comboBox.SelectionChanged += async (_, _) =>
        {
            if (comboBox.TryGetSelectedItem(out WindowsPowerPlan plan))
            {
                var visibility = IsBalancedPlan(plan.Guid) ? Visibility.Visible : Visibility.Collapsed;
                acCard.Visibility = visibility;
                dcCard.Visibility = visibility;
                await ITSPlanChangedAsync(itsMode, plan);
            }
        };

        container.Children.Add(acCard);
        container.Children.Add(dcCard);

        tabItem.Content = container;
        _tabControl.Items.Add(tabItem);
    }

    private async Task BuildGodModeTabsAsync(WindowsPowerPlan[] powerPlans, WindowsPowerMode[] powerModes)
    {
        var presets = await _godModeController.GetGodModePresetsAsync().ConfigureAwait(false);

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

            var presetCard = new CardControl
            {
                Margin = new Thickness(0, 0, 0, 8),
                Icon = SymbolRegular.WrenchScrewdriver24,
                Header = new CardHeaderControl { Title = Resource.GodModeSettingsWindow_ActivePreset_Title },
                Content = presetCombo
            };
            container.Children.Add(presetCard);

            var planCombo = new ComboBox { MinWidth = 200, MaxDropDownHeight = 300, VerticalAlignment = VerticalAlignment.Center };
            planCombo.SetItems(powerPlans, DefaultValue, pp => pp.Name);
            var planCard = new CardControl
            {
                Margin = new Thickness(0, 0, 0, 8),
                Icon = SymbolRegular.FlashSettings24,
                Header = new CardHeaderControl { Title = Resource.WindowsPowerPlan_Title },
                Content = planCombo
            };
            container.Children.Add(planCard);

            var acCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
            acCombo.SetItems(powerModes, WindowsPowerMode.Balanced, pm => pm.GetDisplayName());
            var dcCombo = new ComboBox { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
            dcCombo.SetItems(powerModes, WindowsPowerMode.Balanced, pm => pm.GetDisplayName());

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

            var isSwappingPreset = false;

            void UpdateFieldsForPreset(KeyValuePair<Guid, GodModeSettingsStore.Preset> kvp)
            {
                isSwappingPreset = true;
                try
                {
                    var livePreset = _godModeSettings.Store.Presets.GetValueOrDefault(kvp.Key, kvp.Value);
                    var currentPowerPlanGuid = GetGodModePresetPowerPlan(kvp.Key.ToString()) ?? Guid.Empty;
                    var selectedPlan = powerPlans.FirstOrDefault(pp => pp.Guid == currentPowerPlanGuid);
                    var effectivePlan = (selectedPlan == default) ? DefaultValue : selectedPlan;
                    planCombo.SelectedIndex = Array.IndexOf(powerPlans, effectivePlan);

                    var presetBalanceAc = livePreset.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnAc);
                    var presetBalanceDc = livePreset.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnDc);
                    var isPreset = presetBalanceAc.HasValue || presetBalanceDc.HasValue;
                    var savedAc = (isPreset ? presetBalanceAc : GetGodModeFallbackBalanceOnAc()) ?? WindowsPowerMode.Balanced;
                    var savedDc = (isPreset ? presetBalanceDc : GetGodModeFallbackBalanceOnDc()) ?? WindowsPowerMode.Balanced;
                    acCombo.SelectItem(savedAc);
                    dcCombo.SelectItem(savedDc);

                    var visibility = IsBalancedPlan(effectivePlan.Guid) ? Visibility.Visible : Visibility.Collapsed;
                    acCard.Visibility = visibility;
                    dcCard.Visibility = visibility;
                }
                finally
                {
                    isSwappingPreset = false;
                }
            }

            presetCombo.SelectionChanged += (s, e) =>
            {
                if (presetCombo.TryGetSelectedItem(out KeyValuePair<Guid, GodModeSettingsStore.Preset> selectedKvp))
                {
                    UpdateFieldsForPreset(selectedKvp);
                }
            };

            planCombo.SelectionChanged += async (s, e) =>
            {
                if (isSwappingPreset || IsRefreshing) return;
                if (presetCombo.TryGetSelectedItem(out KeyValuePair<Guid, GodModeSettingsStore.Preset> selectedKvp) && planCombo.TryGetSelectedItem(out WindowsPowerPlan plan))
                {
                    var visibility = IsBalancedPlan(plan.Guid) ? Visibility.Visible : Visibility.Collapsed;
                    acCard.Visibility = visibility;
                    dcCard.Visibility = visibility;


                    await GodModePresetPowerPlanChangedAsync(selectedKvp.Key.ToString(), plan);
                }
            };

            acCombo.SelectionChanged += async (s, e) =>
            {
                if (isSwappingPreset || IsRefreshing) return;
                if (presetCombo.TryGetSelectedItem(out KeyValuePair<Guid, GodModeSettingsStore.Preset> selectedKvp) && acCombo.TryGetSelectedItem(out WindowsPowerMode mode))
                {
                    await GodModePresetBalanceOverlayChangedAsync(selectedKvp.Key.ToString(), mode, isAc: true);
                }
            };

            dcCombo.SelectionChanged += async (s, e) =>
            {
                if (isSwappingPreset || IsRefreshing) return;
                if (presetCombo.TryGetSelectedItem(out KeyValuePair<Guid, GodModeSettingsStore.Preset> selectedKvp) && dcCombo.TryGetSelectedItem(out WindowsPowerMode mode))
                {
                    await GodModePresetBalanceOverlayChangedAsync(selectedKvp.Key.ToString(), mode, isAc: false);
                }
            };

            if (presetCombo.TryGetSelectedItem(out KeyValuePair<Guid, GodModeSettingsStore.Preset> initialKvp))
            {
                UpdateFieldsForPreset(initialKvp);
            }

            tabItem.Content = container;
            _tabControl.Items.Add(tabItem);
        }
        else
        {
            var singlePreset = presets.FirstOrDefault();
            var presetBalanceAc = singlePreset.Value?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnAc);
            var presetBalanceDc = singlePreset.Value?.Overrides.TryGetEnum<WindowsPowerMode>(PowerOverrideKey.PowerPlanBalanceOnDc);
            var isPreset = presetBalanceAc.HasValue || presetBalanceDc.HasValue;
            var savedAc = (isPreset ? presetBalanceAc : GetGodModeFallbackBalanceOnAc()) ?? WindowsPowerMode.Balanced;
            var savedDc = (isPreset ? presetBalanceDc : GetGodModeFallbackBalanceOnDc()) ?? WindowsPowerMode.Balanced;

            BuildPowerPlanTab(powerPlans, powerModes, PowerModeState.GodMode,
                PowerModeState.GodMode.GetDisplayName(),
                savedPlan: GetGodModePresetPowerPlan(singlePreset.Key.ToString()),
                savedAc: savedAc,
                savedDc: savedDc,
                onPlanChanged: async (plan) => await GodModePresetPowerPlanChangedAsync(singlePreset.Key.ToString(), plan),
                onOverlayChanged: async (mode, isAc) => await GodModePresetBalanceOverlayChangedAsync(singlePreset.Key.ToString(), mode, isAc));
        }
    }

    private static bool IsBalancedPlan(Guid guid) =>
        PowerPlanExtensions.IsPlanBasedOnBalanced(guid);

    private Guid? GetGodModePresetPowerPlan(string presetKey)
    {
        if (Guid.TryParse(presetKey, out var presetGuid) &&
            _godModeSettings.Store.Presets.TryGetValue(presetGuid, out var preset))
        {
            var powerPlanGuid = preset.Overrides.TryGetGuid(PowerOverrideKey.PowerPlan);
            if (powerPlanGuid != null)
            {
                return powerPlanGuid;
            }
        }
        if (_isNonGaming)
        {
            return _settings.Store.ITSPowerPlans.GetValueOrDefault(_godModeItsMode);
        }
        if (!_settings.Store.PowerPlans.TryGetValue(PowerModeState.GodMode, out var globalGuid))
        {
            return Guid.Empty;
        }
        return globalGuid;
    }

    private async Task WindowsPowerPlanChangedAsync(WindowsPowerPlan windowsPowerPlan, PowerModeState powerModeState, GodModeSettingsStore.Preset? preset = null)
    {
        if (IsRefreshing)
        {
            return;
        }

        if (preset == null)
        {
            _settings.Store.PowerPlans[powerModeState] = windowsPowerPlan.Guid;

        }
        else
        {
            var powerPlan = preset.Overrides.TryGetGuid(PowerOverrideKey.PowerPlan);
            if (powerPlan == null)
            {
                return;
            }
        }

        _settings.SynchronizeStore();

        var currentState = await _powerModeFeature.GetStateAsync();
        if (currentState == powerModeState)
        {
            if (powerModeState == PowerModeState.GodMode && preset != null)
            {
                if (_godModeSettings.Store.Presets.TryGetValue(_godModeSettings.Store.ActivePresetId, out var activePreset) &&
                    !activePreset.Equals(preset))
                {
                    return;
                }
            }
            await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync(preset);
        }
    }

    private async Task BalanceOverlayChangedAsync(WindowsPowerMode selectedMode, PowerModeState powerModeState, bool isAc)
    {
        if (IsRefreshing)
        {
            return;
        }

        if (isAc)
        {
            _settings.Store.Overrides.SetPowerPlanBalanceOnAc(powerModeState, selectedMode);
        }
        else
        {
            _settings.Store.Overrides.SetPowerPlanBalanceOnDc(powerModeState, selectedMode);
        }

        _settings.SynchronizeStore();

        var currentState = await _powerModeFeature.GetStateAsync();
        if (currentState == powerModeState)
        {
            await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync();
        }
    }

    private async Task ITSPlanChangedAsync(ITSMode itsMode, WindowsPowerPlan windowsPowerPlan)
    {
        if (IsRefreshing)
        {
            return;
        }

        _settings.Store.ITSPowerPlans[itsMode] = windowsPowerPlan.Guid;

        _settings.SynchronizeStore();

        var currentState = await _itsModeFeature.GetStateAsync();
        if (currentState == itsMode)
        {
            await _windowsPowerPlanController.SetPowerPlanAsync(itsMode, true);
        }
    }

    private async Task ITSBalanceOverlayChangedAsync(ITSMode itsMode, WindowsPowerMode selectedMode, bool isAc)
    {
        if (IsRefreshing)
        {
            return;
        }

        if (isAc)
        {
            _settings.Store.ITSOverrides.SetPowerPlanBalanceOnAc(itsMode, selectedMode);
        }
        else
        {
            _settings.Store.ITSOverrides.SetPowerPlanBalanceOnDc(itsMode, selectedMode);
        }

        _settings.SynchronizeStore();

        var currentState = await _itsModeFeature.GetStateAsync();
        if (currentState == itsMode)
        {
            await _windowsPowerPlanController.SetPowerPlanAsync(itsMode, true);
        }
    }

    private async Task GodModePresetPowerPlanChangedAsync(string presetKey, WindowsPowerPlan windowsPowerPlan)
    {
        if (IsRefreshing)
        {
            return;
        }

        var presetKvp = _godModeSettings.Store.Presets.FirstOrDefault(profile => profile.Key.ToString() == presetKey);

        if (!presetKvp.Equals(default(KeyValuePair<Guid, GodModeSettingsStore.Preset>)) && presetKvp.Value != null)
        {
            var newOv = new Dictionary<PowerOverrideKey, string>(presetKvp.Value.Overrides ?? []);
            newOv.SetGuid(PowerOverrideKey.PowerPlan, windowsPowerPlan.Guid);

            var updated = presetKvp.Value with { Overrides = newOv };
            _godModeSettings.Store.Presets[presetKvp.Key] = updated;
            _godModeSettings.SynchronizeStore();

            if (_isNonGaming && _godModeSettings.Store.ActivePresetId.ToString() == presetKey)
            {
                await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync(updated);
            }
            else if (await _powerModeFeature.GetStateAsync() == PowerModeState.GodMode && _godModeSettings.Store.ActivePresetId.ToString() == presetKey)
            {
                await WindowsPowerPlanChangedAsync(windowsPowerPlan, PowerModeState.GodMode, updated);
            }
        }
    }

    private async Task GodModePresetBalanceOverlayChangedAsync(string presetKey, WindowsPowerMode selectedMode, bool isAc)
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
                newOv.SetEnum(PowerOverrideKey.PowerPlanBalanceOnAc, (WindowsPowerMode?)selectedMode);
            }
            else
            {
                newOv.SetEnum(PowerOverrideKey.PowerPlanBalanceOnDc, (WindowsPowerMode?)selectedMode);
            }
            var updated = presetKvp.Value with { Overrides = newOv };
            _godModeSettings.Store.Presets[presetKvp.Key] = updated;
            _godModeSettings.SynchronizeStore();

            if (_isNonGaming && _godModeSettings.Store.ActivePresetId.ToString() == presetKey)
            {
                await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync(updated);
            }
            else if (await _powerModeFeature.GetStateAsync() == PowerModeState.GodMode && _godModeSettings.Store.ActivePresetId.ToString() == presetKey)
            {
                await _powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync(updated);
            }
        }
    }

    private WindowsPowerMode? GetGodModeFallbackBalanceOnAc() => _isNonGaming
        ? _settings.Store.ITSOverrides.GetPowerPlanBalanceOnAc(_godModeItsMode)
        : _settings.Store.Overrides.GetPowerPlanBalanceOnAc(PowerModeState.GodMode);

    private WindowsPowerMode? GetGodModeFallbackBalanceOnDc() => _isNonGaming
        ? _settings.Store.ITSOverrides.GetPowerPlanBalanceOnDc(_godModeItsMode)
        : _settings.Store.Overrides.GetPowerPlanBalanceOnDc(PowerModeState.GodMode);

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
