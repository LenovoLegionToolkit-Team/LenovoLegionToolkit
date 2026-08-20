using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.WPF.Extensions;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.RGB;

public partial class SpectrumRGBKeyboard1ZoneControl
{
    private Button[] PresetButtons => [_preset1Button, _preset2Button, _preset3Button];

    private readonly SpectrumKeyboardBacklightController _controller = IoCContainer.Resolve<SpectrumKeyboardBacklightController>();
    private readonly VantageDisabler _vantageDisabler = IoCContainer.Resolve<VantageDisabler>();

    private bool _hasPendingChanges;
    private bool _isUpdatingControls;

    protected override bool DisablesWhileRefreshing => false;

    public SpectrumRGBKeyboard1ZoneControl()
    {
        InitializeComponent();

        MessagingCenter.Subscribe<SpectrumBacklightChangedMessage>(this, () => Dispatcher.InvokeTask(async () =>
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

    private void ClearPendingChanges()
    {
        _hasPendingChanges = false;
        UpdateApplyButtonState();
    }

    private async void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button presetButton || presetButton.Appearance == ControlAppearance.Primary)
            return;

        ClearPendingChanges();

        var selectedPreset = Convert.ToInt32(presetButton.Tag);
        await _controller.SetProfileAsync(selectedPreset);

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
        if (_isUpdatingControls || !IsInitialized)
            return;

        UpdateEffectEditorVisibility();
        UpdatePendingState();
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isUpdatingControls || !IsInitialized)
            return;

        UpdatePendingState();
    }

    private void UpdatePendingState()
    {
        UpdatePendingZoneColors();

        _hasPendingChanges = true;

        UpdateApplyButtonState();
    }

    private void UpdatePendingZoneColors()
    {
        _hasPendingChanges = true;
        UpdateApplyButtonState();
    }

    protected override async Task OnRefreshAsync()
    {
        if (!await _controller.IsSupportedAsync())
            throw new InvalidOperationException("Spectrum Keyboard does not seem to be supported");

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

        var profile = await _controller.GetProfileAsync();
        var brightness = await _controller.GetBrightnessAsync();

        foreach (var presetButton in PresetButtons)
        {
            var buttonPreset = Convert.ToInt32(presetButton.Tag);
            var selected = profile == buttonPreset;
            presetButton.Appearance = selected ? ControlAppearance.Primary : ControlAppearance.Secondary;
        }

        _vantageWarningInfoBar.IsOpen = false;

        foreach (var presetButton in PresetButtons)
            presetButton.IsEnabled = true;

        var (_, effects) = await _controller.GetProfileDescriptionAsync(profile);
        var effect = effects.Length > 0 ? effects[0] : new SpectrumKeyboardBacklightEffect(
            SpectrumKeyboardBacklightEffectType.Always,
            SpectrumKeyboardBacklightSpeed.None,
            SpectrumKeyboardBacklightDirection.None,
            SpectrumKeyboardBacklightClockwiseDirection.None,
            [new RGBColor(255, 255, 255)],
            [1]);

        var preset = ToRgbPreset(effect);

        _isUpdatingControls = true;
        try
        {
            _brightnessSlider.Value = Math.Clamp(brightness, 0, 9);
            _effectControl.SetItems(
                [RGBKeyboardBacklightEffect.Static, RGBKeyboardBacklightEffect.Breath],
                preset.Effect,
                v => v.GetDisplayName());
            _speedControl.SetItems(
                [RGBKeyboardBacklightSpeed.Slowest, RGBKeyboardBacklightSpeed.Slow, RGBKeyboardBacklightSpeed.Fast],
                preset.Speed,
                v => v.GetDisplayName());
            _zone1ColorPicker.SelectedColor = preset.Zone1.ToColor();
        }
        finally
        {
            _isUpdatingControls = false;
        }

        UpdateEffectEditorVisibility();

        _brightnessControl.IsEnabled = true;
        _effectControl.IsEnabled = true;

        UpdateApplyButtonState();
    }

    protected override void OnFinishedLoading() { }

    private async Task SaveState()
    {
        var profile = await _controller.GetProfileAsync();
        var color = _zone1ColorPicker.SelectedColor.ToRGBColor();
        await _controller.SetBrightnessAsync((int)_brightnessSlider.Value);
        await _controller.SetProfileDescriptionAsync(profile, [ToSpectrumEffect(
            _effectControl.SelectedItem,
            _speedControl.SelectedItem,
            color)]);
    }

    private void UpdateEffectEditorVisibility()
    {
        var showColor = _effectControl.SelectedItem == RGBKeyboardBacklightEffect.Static;
        var showSpeed = _effectControl.SelectedItem == RGBKeyboardBacklightEffect.Breath;
        _zone1Control.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
        _zone1ColorPicker.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
        _zone1Control.IsEnabled = showColor;
        _speedControl.Visibility = showSpeed ? Visibility.Visible : Visibility.Collapsed;
        _speedControl.IsEnabled = showSpeed;
    }

    private static RGBKeyboardBacklightEffectDescription ToRgbPreset(SpectrumKeyboardBacklightEffect effect)
    {
        var type = effect.Type switch
        {
            SpectrumKeyboardBacklightEffectType.Always => RGBKeyboardBacklightEffect.Static,
            SpectrumKeyboardBacklightEffectType.ColorPulse => RGBKeyboardBacklightEffect.Breath,
            _ => RGBKeyboardBacklightEffect.Static
        };

        var color = effect.Colors.Length > 0 ? effect.Colors[0] : new RGBColor(255, 255, 255);
        var speed = effect.Speed switch
        {
            SpectrumKeyboardBacklightSpeed.Speed1 => RGBKeyboardBacklightSpeed.Slowest,
            SpectrumKeyboardBacklightSpeed.Speed2 => RGBKeyboardBacklightSpeed.Slow,
            _ => RGBKeyboardBacklightSpeed.Fast
        };
        return new(type, speed, color);
    }

    private static SpectrumKeyboardBacklightEffect ToSpectrumEffect(
        RGBKeyboardBacklightEffect? effect,
        RGBKeyboardBacklightSpeed? speed,
        RGBColor color)
    {
        var effectType = effect switch
        {
            RGBKeyboardBacklightEffect.Breath => SpectrumKeyboardBacklightEffectType.ColorPulse,
            _ => SpectrumKeyboardBacklightEffectType.Always
        };

        RGBColor[] colors = [color];
        var spectrumSpeed = speed switch
        {
            RGBKeyboardBacklightSpeed.Slowest => SpectrumKeyboardBacklightSpeed.Speed1,
            RGBKeyboardBacklightSpeed.Slow => SpectrumKeyboardBacklightSpeed.Speed2,
            _ => SpectrumKeyboardBacklightSpeed.Speed3
        };
        return new(
            effectType,
            effect == RGBKeyboardBacklightEffect.Breath ? spectrumSpeed : SpectrumKeyboardBacklightSpeed.None,
            SpectrumKeyboardBacklightDirection.None,
            SpectrumKeyboardBacklightClockwiseDirection.None,
            colors,
            [1]);
    }

    private readonly record struct RGBKeyboardBacklightEffectDescription(
        RGBKeyboardBacklightEffect Effect,
        RGBKeyboardBacklightSpeed Speed,
        RGBColor Zone1);
}
