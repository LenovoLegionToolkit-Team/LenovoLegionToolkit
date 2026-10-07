using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using CardExpander = LenovoLegionToolkit.WPF.Controls.Custom.CardExpander;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public class RefreshRateControl : AbstractRefreshingControl
{
    private readonly RefreshRateFeature _feature = IoCContainer.Resolve<RefreshRateFeature>();
    private readonly DisplayConfigurationListener _listener = IoCContainer.Resolve<DisplayConfigurationListener>();

    private readonly CardExpander _cardExpander = new();
    private readonly CardHeaderControl _refreshRateHeaderControl = new();
    private readonly CardHeaderControl _dynamicRefreshRateHeaderControl = new();
    private readonly ComboBox _comboBox = new();
    private readonly ToggleSwitch _toggle = new();

    public RefreshRateControl()
    {
        _comboBox.SelectionChanged += ComboBox_SelectionChanged;
        _comboBox.MinWidth = 165;
        _comboBox.Margin = new(8, 0, 16, 0);
        _comboBox.Visibility = Visibility.Hidden;
        AutomationProperties.SetName(_comboBox, Resource.RefreshRateControl_Title);

        _toggle.Click += Toggle_Click;
        _toggle.Margin = new(8, 0, 0, 0);
        _toggle.Visibility = Visibility.Hidden;
        AutomationProperties.SetName(_toggle, Resource.RefreshRateControl_Dynamic_Title);

        _refreshRateHeaderControl.Title = Resource.RefreshRateControl_Title;
        _refreshRateHeaderControl.Subtitle = Resource.RefreshRateControl_Message;
        _refreshRateHeaderControl.Accessory = _comboBox;

        _dynamicRefreshRateHeaderControl.Title = Resource.RefreshRateControl_Dynamic_Title;
        _dynamicRefreshRateHeaderControl.Subtitle = Resource.RefreshRateControl_Dynamic_Message;
        _dynamicRefreshRateHeaderControl.Accessory = _toggle;
        _dynamicRefreshRateHeaderControl.Margin = new(0, 16, 0, 0);

        _cardExpander.Icon = SymbolRegular.DesktopPulse24;
        _cardExpander.Header = _refreshRateHeaderControl;
        _cardExpander.Content = _dynamicRefreshRateHeaderControl;
        _cardExpander.IsExpanded = false;
        _cardExpander.Margin = new(0, 0, 0, 8);

        Content = _cardExpander;

        _listener.Changed += Listener_Changed;
    }

    protected override async Task OnRefreshAsync()
    {
        if (!await _feature.IsSupportedAsync())
        {
            throw new NotSupportedException();
        }

        var states = await _feature.GetAllStatesAsync();
        var dynamicStates = await _feature.GetAllDynamicStatesAsync();
        var selectedState = await _feature.GetStateAsync();

        var rates = states
            .Where(state => !state.IsDynamic)
            .OrderBy(state => state.Frequency)
            .ToArray();

        var selectedRate = new RefreshRate(selectedState.Frequency);

        _comboBox.SetItems(rates, selectedRate, ComboBoxItemDisplayName);
        _comboBox.IsEnabled = rates.Length != 0;
        _comboBox.Visibility = rates.Length < 2 ? Visibility.Hidden : Visibility.Visible;

        var hasRange = selectedState.IsDynamic || dynamicStates.Any(state => state.Frequency == selectedState.Frequency);

        _toggle.IsChecked = selectedState.IsDynamic;
        _toggle.IsEnabled = hasRange;
        _toggle.Visibility = rates.Length < 2 ? Visibility.Hidden : Visibility.Visible;

        Visibility = rates.Length < 2 ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override void OnFinishedLoading()
    {
        MessagingCenter.Subscribe<FeatureStateMessage<RefreshRate>>(this, () => Dispatcher.InvokeTask(async () =>
        {
            if (!IsVisible)
            {
                return;
            }

            await RefreshAsync();
        }));
    }

    private async void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsRefreshing)
        {
            return;
        }

        var newValue = e.GetNewValue<RefreshRate>();
        if (newValue is null)
        {
            return;
        }

        var applied = false;
        var exceptionOccurred = false;

        try
        {
            _comboBox.IsEnabled = false;

            var selectedState = await _feature.GetStateAsync();
            var dynamicStates = await _feature.GetAllDynamicStatesAsync();

            var lowFrequency = dynamicStates.FirstOrDefault(state => state.Frequency == newValue.Value.Frequency).BaseFrequency;

            var targetState = selectedState.IsDynamic && lowFrequency > 0
                ? new RefreshRate(newValue.Value.Frequency, isDynamic: true, baseFrequency: lowFrequency)
                : newValue.Value;

            if (targetState.Equals(selectedState))
            {
                return;
            }

            await _feature.SetStateAsync(targetState);
            applied = true;
        }
        catch (Exception ex)
        {
            exceptionOccurred = true;

            Log.Instance.Trace($"Failed to change refresh rate. [feature={GetType().Name}]", ex);
        }
        finally
        {
            _comboBox.IsEnabled = true;
        }

        if (applied || exceptionOccurred)
        {
            await RefreshAsync();
        }
    }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (IsRefreshing)
        {
            return;
        }

        var applied = false;
        var exceptionOccurred = false;

        try
        {
            _toggle.IsEnabled = false;

            var selectedState = await _feature.GetStateAsync();
            var dynamicStates = await _feature.GetAllDynamicStatesAsync();

            var lowFrequency = dynamicStates.FirstOrDefault(state => state.Frequency == selectedState.Frequency).BaseFrequency;

            if (lowFrequency <= 0)
            {
                return;
            }

            var targetState = _toggle.IsChecked ?? false
                ? new RefreshRate(selectedState.Frequency, isDynamic: true, baseFrequency: lowFrequency)
                : new RefreshRate(selectedState.Frequency);

            if (targetState.Equals(selectedState))
            {
                return;
            }

            await _feature.SetStateAsync(targetState);
            applied = true;
        }
        catch (Exception ex)
        {
            exceptionOccurred = true;

            Log.Instance.Trace($"Failed to change dynamic refresh rate. [feature={GetType().Name}]", ex);
        }
        finally
        {
            _toggle.IsEnabled = true;
        }

        if (applied || exceptionOccurred)
        {
            await RefreshAsync();
        }
    }

    private static string ComboBoxItemDisplayName(RefreshRate value) => LocalizationHelper.ForceLeftToRight(value.DisplayName);

    private void Listener_Changed(object? sender, EventArgs e) => Dispatcher.Invoke(async () =>
    {
        if (IsLoaded)
        {
            await RefreshAsync();
        }
    });
}
