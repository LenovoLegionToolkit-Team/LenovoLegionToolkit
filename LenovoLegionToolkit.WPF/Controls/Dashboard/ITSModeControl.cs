using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Dashboard;
using Wpf.Ui.Common;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public class ITSModeControl : AbstractComboBoxFeatureCardControl<ITSMode>
{
    private readonly ITSModeFeature _itsModeFeature = IoCContainer.Resolve<ITSModeFeature>();
    private readonly ITSModeListener _iTSModeListener = IoCContainer.Resolve<ITSModeListener>();

    private readonly Button _configButton = new()
    {
        Icon = SymbolRegular.Settings24,
        FontSize = 20,
        Margin = new(8, 0, 0, 0),
        Visibility = Visibility.Visible,
    };

    public ITSModeControl()
    {
        Icon = SymbolRegular.Gauge24;
        Title = Resource.ITSModeControl_Title;
        Subtitle = Resource.ITSModeControl_Message;

        AutomationProperties.SetName(_configButton, Resource.ITSModeControl_Title);
        _configButton.Click += ConfigButton_Click;

        IsVisibleChanged += ITSModeControl_IsVisibleChanged;
        _iTSModeListener.Changed += ITSModeListener_Changed;
    }

    protected override FrameworkElement GetAccessory(ComboBox comboBox)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(_configButton);
        panel.Children.Add(comboBox);
        return panel;
    }

    private void ConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new GodModeSettingsWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private async void ITSModeListener_Changed(object? sender, ITSModeListener.ChangedEventArgs e)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            if (IsLoaded && IsVisible)
                await RefreshAsync();
        });
    }

    private async void ITSModeControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!await _itsModeFeature.IsSupportedAsync().ConfigureAwait(false))
        {
            return;
        }

        ITSMode mode;
        if (_itsModeFeature.LastItsMode == ITSMode.None)
        {
            mode = await _itsModeFeature.GetStateAsync();
            _itsModeFeature.LastItsMode = mode;
            Log.Instance.Trace($"Read ITSMode from GetStateAsync(): {mode}");
        }
        else
        {
            mode = _itsModeFeature.LastItsMode;
            Log.Instance.Trace($"Read ITSMode from LastItsMode: {mode}");
        }

        Log.Instance.Trace($"Visible changed. Set ITSMode to {mode}");
        _comboBox.SelectedItem = mode;
    }

    protected override string ComboBoxItemDisplayName(ITSMode value) => _itsModeFeature.GetITSModeDisplayName(value);

    protected override async Task OnStateChangeAsync(ComboBox comboBox, IFeature<ITSMode> feature, ITSMode? newValue, ITSMode? oldValue)
    {
        if (newValue == null || oldValue == null)
            return;

        if (newValue.Value != oldValue.Value)
        {
            await _itsModeFeature.SetStateAsync(newValue.Value);
            await _iTSModeListener.NotifyAsync(newValue.Value);
        }

        await base.OnStateChangeAsync(comboBox, feature, newValue, oldValue);
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
}
