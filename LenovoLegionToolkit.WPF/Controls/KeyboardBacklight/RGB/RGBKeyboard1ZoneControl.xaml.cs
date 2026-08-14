using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.WPF.Extensions;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.RGB;

public partial class RGBKeyboard1ZoneControl
{
    private Button[] PresetButtons => [_offPresetButton, _preset1Button, _preset2Button, _preset3Button, _preset4Button];

    private ColorPickerControl[] Zones => [_zone1ColorPicker];

    private readonly RGBKeyboardBacklightController _controller = IoCContainer.Resolve<RGBKeyboardBacklightController>();
    private readonly RGBKeyboardBacklightListener _listener = IoCContainer.Resolve<RGBKeyboardBacklightListener>();
    private readonly VantageDisabler _vantageDisabler = IoCContainer.Resolve<VantageDisabler>();

    private RGBColor[]? _pendingZoneColors;
    private RGBKeyboardBacklightEffect? _pendingEffect;
    private RGBKeyboardBacklightSpeed? _pendingSpeed;
    private RGBKeyboardBacklightBrightness? _pendingBrightness;
    private bool _hasPendingChanges;

    protected override bool DisablesWhileRefreshing => false;

    public RGBKeyboard1ZoneControl()
    {
        InitializeComponent();

        _listener.Changed += Listener_Changed;

        MessagingCenter.Subscribe<RGBKeyboardBacklightChangedMessage>(this, () => Dispatcher.InvokeTask(async () =>
        {
            if (!IsVisible)
                return;

            await RefreshAsync();
        }));
    }

    private void UpdateApplyButtonState()
    {
        _applyButton.IsEnabled = _hasPendingChanges;
    }

    private void Listener_Changed(object? sender, EventArgs e) => Dispatcher.Invoke(async () =>
    {
        if (!IsLoaded || !IsVisible)
            return;

        ClearPendingChanges();
        await RefreshAsync();
    });

    private void ClearPendingChanges()
    {
        _pendingZoneColors = null;
        _pendingEffect = null;
        _pendingSpeed = null;
        _pendingBrightness = null;
        _hasPendingChanges = false;
        UpdateApplyButtonState();
    }

    private async void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button presetButton || presetButton.Appearance == ControlAppearance.Primary)
            return;

        ClearPendingChanges();

        var selectedPreset = (RGBKeyboardBacklightPreset)presetButton.Tag;
        var state = await _controller.GetStateAsync();
        await _controller.SetStateAsync(new(selectedPreset, state.Presets));

        await RefreshAsync();
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        await CommitChangesAsync();
    }

    private async Task CommitChangesAsync()
    {
        if (!_hasPendingChanges)
            return;

        await SaveState();
        await RefreshAsync();
        ClearPendingChanges();
    }

    private void CardControl_Changed(object? sender, EventArgs e)
    {
        UpdatePendingState();
    }

    private void UpdatePendingState()
    {
        UpdatePendingZoneColors();

        _pendingEffect = _effectControl.SelectedItem;
        _pendingSpeed = _speedControl.SelectedItem;
        _pendingBrightness = _brightnessControl.SelectedItem;
        _hasPendingChanges = true;

        UpdateApplyButtonState();
    }

    private void UpdatePendingZoneColors()
    {
        _pendingZoneColors = [_zone1ColorPicker.SelectedColor.ToRGBColor()];
        _hasPendingChanges = true;
        UpdateApplyButtonState();
    }

    protected override async Task OnRefreshAsync()
    {
        if (!await _controller.IsSupportedAsync())
            throw new InvalidOperationException("RGB Keyboard does not seem to be supported");

        var vantageStatus = await _vantageDisabler.GetStatusAsync();
        if (vantageStatus == SoftwareStatus.Enabled)
        {
            _vantageWarningInfoBar.IsOpen = true;

            foreach (var presetButton in PresetButtons)
                presetButton.IsEnabled = false;

            _brightnessControl.IsEnabled = false;
            _effectControl.IsEnabled = false;

            _zone1ColorPicker.Visibility = Visibility.Hidden;

            _speedControl.IsEnabled = false;
            _zone1Control.IsEnabled = false;

            _applyButton.IsEnabled = false;

            Visibility = Visibility.Visible;

            return;
        }

        var state = await _controller.GetStateAsync();

        foreach (var presetButton in PresetButtons)
        {
            var buttonPreset = (RGBKeyboardBacklightPreset)presetButton.Tag;
            var selected = state.SelectedPreset == buttonPreset;
            presetButton.Appearance = selected ? ControlAppearance.Primary : ControlAppearance.Secondary;
        }

        _vantageWarningInfoBar.IsOpen = false;

        foreach (var presetButton in PresetButtons)
            presetButton.IsEnabled = true;

        if (state.SelectedPreset == RGBKeyboardBacklightPreset.Off)
        {
            _effectControl.IsEnabled = false;
            _speedControl.IsEnabled = false;
            _brightnessControl.IsEnabled = false;

            _zone1ColorPicker.Visibility = Visibility.Hidden;

            _zone1Control.IsEnabled = false;

            _applyButton.IsEnabled = false;

            return;
        }

        var preset = state.Presets.GetValueOrDefault(state.SelectedPreset, RGBKeyboardBacklightBacklightPresetDescription.Default);

        var speedEnabled = preset.Effect is not RGBKeyboardBacklightEffect.Static;
        var zonesEnabled = preset.Effect is RGBKeyboardBacklightEffect.Static or RGBKeyboardBacklightEffect.Breath;

        _brightnessControl.SetItems(Enum.GetValues<RGBKeyboardBacklightBrightness>(), preset.Brightness, v => v.GetDisplayName());
        _effectControl.SetItems(Enum.GetValues<RGBKeyboardBacklightEffect>(), preset.Effect, v => v.GetDisplayName());
        if (speedEnabled)
            _speedControl.SetItems(Enum.GetValues<RGBKeyboardBacklightSpeed>(), preset.Speed, v => v.GetDisplayName());

        if (zonesEnabled)
        {
            _zone1ColorPicker.SelectedColor = preset.Zone1.ToColor();

            _zone1ColorPicker.Visibility = Visibility.Visible;
        }
        else
        {
            _zone1ColorPicker.Visibility = Visibility.Hidden;
        }

        _brightnessControl.IsEnabled = true;
        _effectControl.IsEnabled = true;
        _speedControl.IsEnabled = speedEnabled;

        _zone1Control.IsEnabled = zonesEnabled;

        UpdateApplyButtonState();
    }

    protected override void OnFinishedLoading() { }

    private async Task SaveState()
    {
        var state = await _controller.GetStateAsync();

        var selectedPreset = state.SelectedPreset;
        var presets = state.Presets;

        if (selectedPreset == RGBKeyboardBacklightPreset.Off)
            return;

        var color = _zone1ColorPicker.SelectedColor.ToRGBColor();
        presets[selectedPreset] = new(_effectControl.SelectedItem,
            _speedControl.SelectedItem,
            _brightnessControl.SelectedItem,
            color,
            color,
            color,
            color);

        await _controller.SetStateAsync(new(selectedPreset, presets));
    }
}
