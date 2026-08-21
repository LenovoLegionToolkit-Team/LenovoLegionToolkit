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
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum;

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
            _effectCard.IsEnabled = false;
            _colorModeCard.IsEnabled = false;

            _zone1ColorPicker.Visibility = Visibility.Hidden;

            _speedCard.IsEnabled = false;
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
            [1],
            colorMode: LENOVO_SPECTRUM_COLOR_MODE.ColorList);

        var effectType = effect.Type is SpectrumKeyboardBacklightEffectType.Always or SpectrumKeyboardBacklightEffectType.ColorPulse
            ? effect.Type
            : SpectrumKeyboardBacklightEffectType.Always;
        var speed = effect.Speed is SpectrumKeyboardBacklightSpeed.Speed1 or SpectrumKeyboardBacklightSpeed.Speed2 or SpectrumKeyboardBacklightSpeed.Speed3
            ? effect.Speed
            : SpectrumKeyboardBacklightSpeed.Speed2;
        var colorMode = effect.ColorMode is LENOVO_SPECTRUM_COLOR_MODE.RandomColor or LENOVO_SPECTRUM_COLOR_MODE.ColorList
            ? effect.ColorMode
            : effect.Colors.Length > 0
                ? LENOVO_SPECTRUM_COLOR_MODE.ColorList
                : LENOVO_SPECTRUM_COLOR_MODE.RandomColor;
        var color = effect.Colors.Length > 0 ? effect.Colors[0] : new RGBColor(255, 255, 255);

        _isUpdatingControls = true;
        try
        {
            _brightnessSlider.Value = Math.Clamp(brightness, 0, 9);
            _effectComboBox.SetItems(
                [SpectrumKeyboardBacklightEffectType.Always, SpectrumKeyboardBacklightEffectType.ColorPulse],
                effectType,
                v => v == SpectrumKeyboardBacklightEffectType.ColorPulse
                    ? Resource.ResourceManager.GetString("Breath_Breath") ?? "Breath"
                    : v.GetDisplayName());
            _speedComboBox.SetItems(
                [SpectrumKeyboardBacklightSpeed.Speed1, SpectrumKeyboardBacklightSpeed.Speed2, SpectrumKeyboardBacklightSpeed.Speed3],
                speed,
                v => v.GetDisplayName());
            _colorModeComboBox.SetItems(
                [LENOVO_SPECTRUM_COLOR_MODE.ColorList, LENOVO_SPECTRUM_COLOR_MODE.RandomColor],
                colorMode,
                v => v switch
                {
                    LENOVO_SPECTRUM_COLOR_MODE.RandomColor =>
                        Resource.ResourceManager.GetString("Random_Color") ?? "Random color",
                    LENOVO_SPECTRUM_COLOR_MODE.ColorList =>
                        Resource.ResourceManager.GetString("Setting_Customize") ?? "Customize",
                    _ => string.Empty
                });
            _zone1ColorPicker.SelectedColor = color.ToColor();
        }
        finally
        {
            _isUpdatingControls = false;
        }

        UpdateEffectEditorVisibility();

        _brightnessControl.IsEnabled = true;
        _effectCard.IsEnabled = true;
        _colorModeCard.IsEnabled = true;

        UpdateApplyButtonState();
    }

    protected override void OnFinishedLoading() { }

    private async Task SaveState()
    {
        var profile = await _controller.GetProfileAsync();
        var color = _zone1ColorPicker.SelectedColor.ToRGBColor();
        await _controller.SetBrightnessAsync((int)_brightnessSlider.Value);
        var effectType = _effectComboBox.TryGetSelectedItem(out SpectrumKeyboardBacklightEffectType selectedEffectType)
            ? selectedEffectType
            : SpectrumKeyboardBacklightEffectType.Always;
        var speed = effectType == SpectrumKeyboardBacklightEffectType.ColorPulse
            && _speedComboBox.TryGetSelectedItem(out SpectrumKeyboardBacklightSpeed selectedSpeed)
                ? selectedSpeed
                : effectType == SpectrumKeyboardBacklightEffectType.ColorPulse
                    ? SpectrumKeyboardBacklightSpeed.Speed2
                    : SpectrumKeyboardBacklightSpeed.None;
        var colorMode = _colorModeComboBox.TryGetSelectedItem(out LENOVO_SPECTRUM_COLOR_MODE selectedColorMode)
            ? selectedColorMode
            : LENOVO_SPECTRUM_COLOR_MODE.ColorList;
        var effect = new SpectrumKeyboardBacklightEffect(
            effectType,
            speed,
            SpectrumKeyboardBacklightDirection.None,
            SpectrumKeyboardBacklightClockwiseDirection.None,
            [color],
            [1],
            colorMode: colorMode);
        await _controller.SetProfileDescriptionAsync(profile, [effect]);
    }

    private void UpdateEffectEditorVisibility()
    {
        var effectType = _effectComboBox.TryGetSelectedItem(out SpectrumKeyboardBacklightEffectType selectedEffectType)
            ? selectedEffectType
            : SpectrumKeyboardBacklightEffectType.Always;
        var showColor = effectType is SpectrumKeyboardBacklightEffectType.Always or SpectrumKeyboardBacklightEffectType.ColorPulse;
        var showSpeed = effectType == SpectrumKeyboardBacklightEffectType.ColorPulse;
        var showColorPicker = showColor
            && _colorModeComboBox.TryGetSelectedItem(out LENOVO_SPECTRUM_COLOR_MODE colorMode)
            && colorMode == LENOVO_SPECTRUM_COLOR_MODE.ColorList;
        _colorModeCard.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
        _colorModeCard.IsEnabled = showColor;
        _zone1Control.Visibility = showColorPicker ? Visibility.Visible : Visibility.Collapsed;
        _zone1ColorPicker.Visibility = showColorPicker ? Visibility.Visible : Visibility.Collapsed;
        _zone1Control.IsEnabled = showColorPicker;
        _speedCard.Visibility = showSpeed ? Visibility.Visible : Visibility.Collapsed;
        _speedCard.IsEnabled = showSpeed;
    }

}
