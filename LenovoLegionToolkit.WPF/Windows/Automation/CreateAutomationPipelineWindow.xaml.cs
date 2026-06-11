using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Station.Services;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using CardControl = LenovoLegionToolkit.WPF.Controls.Custom.CardControl;

namespace LenovoLegionToolkit.WPF.Windows.Automation;

public partial class CreateAutomationPipelineWindow
{
    private readonly MachineInformation machineInformation = Compatibility.GetMachineInformationAsync().Result;

    private List<IAutomationPipelineTrigger> _triggers =
    [
        new ACAdapterConnectedAutomationPipelineTrigger(),
        new LowWattageACAdapterConnectedAutomationPipelineTrigger(),
        new ACAdapterDisconnectedAutomationPipelineTrigger(),
        new BatteryPercentageAutomationPipelineTrigger(),
        new PowerModeAutomationPipelineTrigger(PowerModeState.Balance),
        new GodModePresetChangedAutomationPipelineTrigger(Guid.Empty),
        new GamesAreRunningAutomationPipelineTrigger(),
        new GamesStopAutomationPipelineTrigger(),
        new ProcessesAreRunningAutomationPipelineTrigger([]),
        new ProcessesStopRunningAutomationPipelineTrigger([]),
        new UserInactivityAutomationPipelineTrigger(TimeSpan.Zero),
        new UserInactivityAutomationPipelineTrigger(TimeSpan.FromMinutes(1)),
        new SessionLockAutomationPipelineTrigger(),
        new SessionUnlockAutomationPipelineTrigger(),
        new LidOpenedAutomationPipelineTrigger(),
        new LidClosedAutomationPipelineTrigger(),
        new DisplayOnAutomationPipelineTrigger(),
        new DisplayOffAutomationPipelineTrigger(),
        new HDROnAutomationPipelineTrigger(),
        new HDROffAutomationPipelineTrigger(),
        new DeviceConnectedAutomationPipelineTrigger([]),
        new DeviceDisconnectedAutomationPipelineTrigger([]),
        new ExternalDisplayConnectedAutomationPipelineTrigger(),
        new ExternalDisplayDisconnectedAutomationPipelineTrigger(),
        new WiFiConnectedAutomationPipelineTrigger([]),
        new WiFiDisconnectedAutomationPipelineTrigger(),
        new TimeAutomationPipelineTrigger(false, false, TimeExtensions.UtcNow, Enum.GetValues<DayOfWeek>()),
        new PeriodicAutomationPipelineTrigger(TimeSpan.FromMinutes(1)),
        new OnStartupAutomationPipelineTrigger(),
        new OnResumeAutomationPipelineTrigger()
    ];

    private readonly HashSet<Type> _existingTriggerTypes;
    private readonly Action<IAutomationPipelineTrigger> _createPipeline;

    private readonly HashSet<IAutomationPipelineTrigger> _selectedTriggers = new();

    private bool _multiSelect;

    public CreateAutomationPipelineWindow(HashSet<Type> existingTriggerTypes, Action<IAutomationPipelineTrigger> createPipeline)
    {
        _existingTriggerTypes = existingTriggerTypes;
        _createPipeline = createPipeline;

        InitializeComponent();

        if (machineInformation.Properties.SupportsITSMode)
        {
            _triggers.Insert(1, new ITSModeAutomationPipelineTrigger(ITSMode.ItsAuto));
            _triggers.Remove(new PowerModeAutomationPipelineTrigger(PowerModeState.Balance));
            _triggers.Remove(new GodModePresetChangedAutomationPipelineTrigger(Guid.Empty));
        }

        var triggerRegistry = IoCContainer.Resolve<IAutomationTriggerRegistry>();
        foreach (var extInfo in triggerRegistry.Triggers)
        {
            if (extInfo.Factory() is IAutomationPipelineTrigger trigger)
                _triggers.Add(trigger);
        }

        IsVisibleChanged += CreateAutomationPipelineWindow_IsVisibleChanged;
        _logicComboBox.SelectionChanged += (_, _) => _ = RefreshAsync();
    }

    private async void CreateAutomationPipelineWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var triggers = _selectedTriggers.ToArray();

        if (triggers.IsEmpty())
            return;

        if (triggers.Length == 1)
        {
            _createPipeline(triggers[0]);
        }
        else if (_logicComboBox.SelectedIndex == 0)
        {
            foreach (var t in triggers)
                _createPipeline(t);
        }
        else
        {
            IAutomationPipelineTrigger composite = _logicComboBox.SelectedIndex == 2
                ? new OrAutomationPipelineTrigger(triggers)
                : new AndAutomationPipelineTrigger(triggers);
            _createPipeline(composite);
        }

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _multiSelect = false;
        _selectedTriggers.Clear();
        await RefreshAsync();
    }

    private void _searchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = RefreshAsync();
    }

    private Task RefreshAsync()
    {
        _content.Children.Clear();

        if (!_multiSelect)
            _content.Children.Add(CreateMultipleSelectCardControl());

        var filter = _searchBox.Text?.Trim() ?? string.Empty;

        foreach (var trigger in _triggers)
        {
            if (string.IsNullOrWhiteSpace(filter) ||
                trigger.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                _content.Children.Add(CreateCardControl(trigger));
            }
        }

        _backButton.Visibility = _multiSelect ? Visibility.Visible : Visibility.Collapsed;
        _createButton.Visibility = _multiSelect ? Visibility.Visible : Visibility.Collapsed;
        _logicComboBox.Visibility = _multiSelect ? Visibility.Visible : Visibility.Collapsed;
        RefreshCreateButton();

        return Task.CompletedTask;
    }

    private CardControl CreateMultipleSelectCardControl()
    {
        var control = new CardControl
        {
            Icon = SymbolRegular.SquareMultiple24,
            Header = new CardHeaderControl
            {
                Title = Resource.MultipleTriggersAutomationPipelineTrigger_DisplayName,
                Accessory = new SymbolIcon { Symbol = SymbolRegular.ChevronRight24 }
            },
            Margin = new(0, 8, 0, 0),
        };

        control.Click += async (_, _) =>
        {
            _multiSelect = true;
            await RefreshAsync();
        };

        return control;
    }

    private CardControl CreateCardControl(IAutomationPipelineTrigger trigger)
    {
        UIElement accessory;

        if (_multiSelect)
        {
            var checkbox = new CheckBox
            {
                Tag = trigger,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsChecked = _selectedTriggers.Contains(trigger)
            };
            checkbox.Click += (_, e) =>
            {
                if (checkbox.IsChecked == true)
                    _selectedTriggers.Add(trigger);
                else
                    _selectedTriggers.Remove(trigger);

                RefreshCreateButton();
                e.Handled = true;
            };
            accessory = checkbox;
        }
        else
        {
            accessory = new SymbolIcon { Symbol = SymbolRegular.ChevronRight24 };
        }

        var control = new CardControl
        {
            Icon = trigger.Icon(),
            Header = new CardHeaderControl
            {
                Title = trigger.DisplayName,
                Accessory = accessory
            },
            Margin = new(0, 8, 0, 0),
        };

        if (trigger is IDisallowDuplicatesAutomationPipelineTrigger && (!_multiSelect || _logicComboBox.SelectedIndex == 0))
            control.IsEnabled = !_existingTriggerTypes.Contains(trigger.GetType());

        control.Click += (_, _) =>
        {
            if (_multiSelect)
            {
                if (accessory is not CheckBox checkbox)
                    return;

                var isChecked = !checkbox.IsChecked ?? false;
                checkbox.IsChecked = isChecked;

                if (isChecked)
                    _selectedTriggers.Add(trigger);
                else
                    _selectedTriggers.Remove(trigger);

                RefreshCreateButton();
            }
            else
            {
                _createPipeline(trigger);
                Close();
            }
        };

        return control;
    }

    private void RefreshCreateButton()
    {
        if (!_multiSelect)
        {
            _createButton.IsEnabled = false;
            return;
        }

        _createButton.IsEnabled = _selectedTriggers.Count > 0;
    }
}
