using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class ChargeThresholdControl
{
    private readonly IFeature<ChargeThreshold> _feature = IoCContainer.Resolve<IFeature<ChargeThreshold>>();

    public ChargeThresholdControl()
    {
        InitializeComponent();
    }

    protected override async Task OnRefreshAsync()
    {
        if (!await _feature.IsSupportedAsync())
            throw new NotSupportedException();

        var state = await _feature.GetStateAsync();

        _toggle.IsChecked = state.Enabled;
        _rangeSlider.Start = state.Start;
        _rangeSlider.Stop = state.Stop;
        _rangeSlider.Visibility = Visibility.Visible;
        _toggle.Visibility = Visibility.Visible;

        UpdateLabels(state.Start, state.Stop);
        UpdateWarning(state);
    }

    private void UpdateWarning(ChargeThreshold state)
    {
        var warning = string.Empty;

        if (state.Enabled)
        {
            try
            {
                if (Battery.GetBatteryInformation().BatteryPercentage > state.Stop)
                    warning = Resource.ChargeThresholdControl_Warning;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to read battery information.", ex);
            }
        }

        _cardHeaderControl.Warning = warning;
    }

    protected override void OnFinishedLoading() { }

    private async void Toggle_Click(object sender, RoutedEventArgs e) => await OnStateChangeAsync(_toggle.IsChecked == true, null, null);

    private void RangeSlider_ValueChanged(object sender, EventArgs e) => UpdateLabels(_rangeSlider.Start, _rangeSlider.Stop);

    private async void RangeSlider_ValueCommitted(object sender, EventArgs e) => await OnStateChangeAsync(null, _rangeSlider.Start, _rangeSlider.Stop);

    private async Task OnStateChangeAsync(bool? enabled, int? start, int? stop)
    {
        var exceptionOccurred = false;

        try
        {
            if (IsRefreshing)
                return;

            _toggle.IsEnabled = false;
            _rangeSlider.IsEnabled = false;

            var currentState = await _feature.GetStateAsync();
            var state = new ChargeThreshold(enabled ?? currentState.Enabled, start ?? currentState.Start, stop ?? currentState.Stop);

            if (state.Equals(currentState))
                return;

            await _feature.SetStateAsync(state);

            UpdateWarning(state);
        }
        catch (Exception ex)
        {
            exceptionOccurred = true;

            Log.Instance.Trace($"Failed to change state. [feature={GetType().Name}]", ex);
        }
        finally
        {
            _toggle.IsEnabled = true;
            _rangeSlider.IsEnabled = true;
        }

        if (exceptionOccurred)
            await RefreshAsync();
    }

    private void UpdateLabels(int start, int stop)
    {
        _startTextBlock.Text = $"{Resource.ChargeThresholdControl_Start} {start}{Resource.Percent}";
        _stopTextBlock.Text = $"{Resource.ChargeThresholdControl_Stop} {stop}{Resource.Percent}";
    }
}
