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
    private Button[] PresetButtons => [_offPresetButton, _preset1Button, _preset2Button, _preset3Button, _preset4Button];

    private readonly SpectrumKeyboardBacklightController _controller = IoCContainer.Resolve<SpectrumKeyboardBacklightController>();
    private readonly VantageDisabler _vantageDisabler = IoCContainer.Resolve<VantageDisabler>();

    private RGBKeyboardBacklightEffect? _pendingEffect;
    private RGBKeyboardBacklightSpeed? _pendingSpeed;
    private RGBKeyboardBacklightBrightness? _pendingBrightness;
    private bool _hasPendingChanges;

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

        foreach (var presetButton in PresetButtons)
        {
            var buttonPreset = Convert.ToInt32(presetButton.Tag);
            var selected = profile == buttonPreset;
            presetButton.Appearance = selected ? ControlAppearance.Primary : ControlAppearance.Secondary;
        }

        _vantageWarningInfoBar.IsOpen = false;

        foreach (var presetButton in PresetButtons)
            presetButton.IsEnabled = true;

        if (profile == 0)
        {
            _effectControl.IsEnabled = false;
            _speedControl.IsEnabled = false;
            _brightnessControl.IsEnabled = false;

            _zone1ColorPicker.Visibility = Visibility.Hidden;

            _zone1Control.IsEnabled = false;

            _applyButton.IsEnabled = false;

            return;
        }

        var (_, effects) = await _controller.GetProfileDescriptionAsync(profile);
        var effect = effects.Length > 0 ? effects[0] : new SpectrumKeyboardBacklightEffect(
            SpectrumKeyboardBacklightEffectType.Always,
            SpectrumKeyboardBacklightSpeed.None,
            SpectrumKeyboardBacklightDirection.None,
            SpectrumKeyboardBacklightClockwiseDirection.None,
            [new RGBColor(255, 255, 255)],
            [1]);

        var brightness = await _controller.GetBrightnessAsync();
        var preset = ToRgbPreset(effect, brightness);

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
        var profile = await _controller.GetProfileAsync();
        if (profile == 0)
            return;

        var color = _zone1ColorPicker.SelectedColor.ToRGBColor();
        await _controller.SetBrightnessAsync(_brightnessControl.SelectedItem == RGBKeyboardBacklightBrightness.High ? 9 : 3);
        await _controller.SetProfileDescriptionAsync(profile, [ToSpectrumEffect(
            _effectControl.SelectedItem,
            _speedControl.SelectedItem,
            color)]);
    }

    private static RGBKeyboardBacklightEffectDescription ToRgbPreset(SpectrumKeyboardBacklightEffect effect, int brightness)
    {
        var type = effect.Type switch
        {
            SpectrumKeyboardBacklightEffectType.Always => RGBKeyboardBacklightEffect.Static,
            SpectrumKeyboardBacklightEffectType.ColorPulse => RGBKeyboardBacklightEffect.Breath,
            SpectrumKeyboardBacklightEffectType.Smooth => RGBKeyboardBacklightEffect.Smooth,
            SpectrumKeyboardBacklightEffectType.ColorWave when effect.Direction == SpectrumKeyboardBacklightDirection.LeftToRight => RGBKeyboardBacklightEffect.WaveLTR,
            _ => RGBKeyboardBacklightEffect.WaveRTL
        };

        var speed = effect.Speed switch
        {
            SpectrumKeyboardBacklightSpeed.Speed1 => RGBKeyboardBacklightSpeed.Slowest,
            SpectrumKeyboardBacklightSpeed.Speed2 => RGBKeyboardBacklightSpeed.Slow,
            _ => RGBKeyboardBacklightSpeed.Fastest
        };

        var color = effect.Colors.Length > 0 ? effect.Colors[0] : new RGBColor(255, 255, 255);
        var rgbBrightness = brightness < 5
            ? RGBKeyboardBacklightBrightness.Low
            : RGBKeyboardBacklightBrightness.High;

        return new(type, speed, rgbBrightness, color);
    }

    private static SpectrumKeyboardBacklightEffect ToSpectrumEffect(
        RGBKeyboardBacklightEffect? effect,
        RGBKeyboardBacklightSpeed? speed,
        RGBColor color)
    {
        var effectType = effect switch
        {
            RGBKeyboardBacklightEffect.Breath => SpectrumKeyboardBacklightEffectType.ColorPulse,
            RGBKeyboardBacklightEffect.Smooth => SpectrumKeyboardBacklightEffectType.Smooth,
            RGBKeyboardBacklightEffect.WaveLTR or RGBKeyboardBacklightEffect.WaveRTL => SpectrumKeyboardBacklightEffectType.ColorWave,
            _ => SpectrumKeyboardBacklightEffectType.Always
        };

        var spectrumSpeed = effect switch
        {
            RGBKeyboardBacklightEffect.Static => SpectrumKeyboardBacklightSpeed.None,
            _ when speed == RGBKeyboardBacklightSpeed.Slowest => SpectrumKeyboardBacklightSpeed.Speed1,
            _ when speed == RGBKeyboardBacklightSpeed.Slow => SpectrumKeyboardBacklightSpeed.Speed2,
            _ => SpectrumKeyboardBacklightSpeed.Speed3
        };

        var direction = effect == RGBKeyboardBacklightEffect.WaveLTR
            ? SpectrumKeyboardBacklightDirection.LeftToRight
            : effect == RGBKeyboardBacklightEffect.WaveRTL
                ? SpectrumKeyboardBacklightDirection.RightToLeft
                : SpectrumKeyboardBacklightDirection.None;

        return new(effectType, spectrumSpeed, direction, SpectrumKeyboardBacklightClockwiseDirection.None, [color], [1]);
    }

    private readonly record struct RGBKeyboardBacklightEffectDescription(
        RGBKeyboardBacklightEffect Effect,
        RGBKeyboardBacklightSpeed Speed,
        RGBKeyboardBacklightBrightness Brightness,
        RGBColor Zone1);
}
