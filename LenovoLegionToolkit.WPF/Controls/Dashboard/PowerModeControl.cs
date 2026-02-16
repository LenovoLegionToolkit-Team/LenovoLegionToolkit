using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Dashboard;
using Wpf.Ui.Common;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public class PowerModeControl : AbstractComboBoxFeatureCardControl<PowerModeState>
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly ThermalModeListener _thermalModeListener = IoCContainer.Resolve<ThermalModeListener>();
    private readonly PowerModeListener _powerModeListener = IoCContainer.Resolve<PowerModeListener>();

    private readonly ThrottleLastDispatcher _throttleDispatcher = new(TimeSpan.FromMilliseconds(500), nameof(PowerModeControl));

    private readonly Button _configButton = new()
    {
        Icon = SymbolRegular.Settings24,
        FontSize = 20,
        Margin = new(8, 0, 0, 0),
        Visibility = Visibility.Collapsed,
    };

    public PowerModeControl()
    {
        Icon = SymbolRegular.Gauge24;
        Title = Resource.PowerModeControl_Title;
        Subtitle = Resource.PowerModeControl_Message;

        AutomationProperties.SetName(_configButton, Resource.PowerModeControl_Title);

        _thermalModeListener.Changed += ThermalModeListener_Changed;
        _powerModeListener.Changed += PowerModeListener_Changed;
    }

    private async void ThermalModeListener_Changed(object? sender, ThermalModeListener.ChangedEventArgs e) => await _throttleDispatcher.DispatchAsync(async () =>
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (IsLoaded && IsVisible)
                await RefreshAsync();
        });
    });

    private async void PowerModeListener_Changed(object? sender, PowerModeListener.ChangedEventArgs e) => await _throttleDispatcher.DispatchAsync(async () =>
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (IsLoaded && IsVisible)
                await RefreshAsync();
        });
    });

    protected override async Task OnRefreshAsync()
    {
        await base.OnRefreshAsync();

        if (await Power.IsPowerAdapterConnectedAsync() != PowerAdapterStatus.Connected
            && TryGetSelectedItem(out var state)
            && state is PowerModeState.Performance or PowerModeState.GodMode or PowerModeState.Extreme)
            Warning = Resource.PowerModeControl_Warning;
        else
            Warning = string.Empty;
    }

    protected override async Task OnStateChangeAsync(ComboBox comboBox, IFeature<PowerModeState> feature, PowerModeState? newValue, PowerModeState? oldValue)
    {
        await base.OnStateChangeAsync(comboBox, feature, newValue, oldValue);

        if (newValue is null)
        {
            return;
        }

        var mi = await Compatibility.GetMachineInformationAsync();
        var adapterStatus = await Power.IsPowerAdapterConnectedAsync();

        bool isAdapterConnected = adapterStatus != PowerAdapterStatus.Disconnected;

        bool shouldShowButton = newValue switch
        {
            PowerModeState.Balance when mi.Properties.SupportsAIMode => true,
            PowerModeState.GodMode when mi.Properties.SupportsGodMode => true,
            _ => false
        } && isAdapterConnected;

        _configButton.ToolTip = shouldShowButton ? Resource.PowerModeControl_Settings : null;
        _configButton.Visibility = shouldShowButton ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnStateChangeException(Exception exception)
    {
        if (exception is PowerModeUnavailableWithoutACException ex1)
        {
            SnackbarHelper.Show(Resource.PowerModeUnavailableWithoutACException_Title,
                string.Format(Resource.PowerModeUnavailableWithoutACException_Message, ex1.PowerMode.GetDisplayName()),
                SnackbarType.Warning);
        }
    }

    protected override FrameworkElement GetAccessory(ComboBox comboBox)
    {
        _configButton.Click += ConfigButton_Click;

        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
        };
        stackPanel.Children.Add(_configButton);
        stackPanel.Children.Add(comboBox);

        return stackPanel;
    }

    private async void ConfigButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedItem(out var state))
        {
            return;
        }

        var result = await CheckCustomModeWarningAsync();

        if (!result)
        {
            return;
        }

        switch (state)
        {
            case PowerModeState.Balance:
            {
                var window = new BalanceModeSettingsWindow { Owner = Window.GetWindow(this) };
                window.ShowDialog();
                break;
            }
            case PowerModeState.GodMode:
            {
                var window = new GodModeSettingsWindow { Owner = Window.GetWindow(this) };
                window.ShowDialog();
                break;
            }
            default:
                throw new Exception($"Access to Custom Mode in {state} is denied.");
        }
    }

    private async Task<bool> CheckCustomModeWarningAsync()
    {
        if (_settings.Store.CustomModeWarningDontShowAgain)
        {
            return true;
        }

        try
        {
            var result = await ShowDialogAsync();

            if (result is { DontShowAgain: true, Yes: true })
            {
                _settings.Store.CustomModeWarningDontShowAgain = true;
                _settings.SynchronizeStore();
                return true;
            }

            if (result.Yes)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to load power mode control", ex);
        }

        return false;
    }

    private async Task<(bool Yes, bool DontShowAgain)> ShowDialogAsync()
    {
        return await await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var result = await MessageBoxHelper.ShowAsync(
                this,
                Resource.Warning,
                Resource.GodModeSettingsWindow_CustomMode_Warning,
                true,
                Resource.Yes,
                Resource.No);

            return (result.Result, result.DontShowAgain);
        });
    }
}