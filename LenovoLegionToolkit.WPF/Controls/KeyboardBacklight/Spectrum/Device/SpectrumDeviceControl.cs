using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;

namespace LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum.Device;

public class SpectrumDeviceControl : UserControl
{
    private readonly SpectrumDeviceFullAlternativeControl _fullAlternative = new();
    private readonly SpectrumDeviceFullControl _full = new();
    private readonly SpectrumDeviceKeyboardAndFrontControl _keyboardAndFront = new();
    private readonly SpectrumDeviceKeyboardOnlyControl _keyboardOnly = new();

    private readonly StackPanel _stackPanel = new();

    public SpectrumDeviceControl()
    {
        Content = _stackPanel;
    }

    public void SetLayout(SpectrumLayout spectrumLayout, KeyboardLayout keyboardLayout, HashSet<ushort> keys)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetLayout(spectrumLayout, keyboardLayout, keys));
            return;
        }

        _stackPanel.Children.Remove(_fullAlternative);
        _stackPanel.Children.Remove(_full);
        _stackPanel.Children.Remove(_keyboardAndFront);
        _stackPanel.Children.Remove(_keyboardOnly);

        switch (spectrumLayout)
        {
            case SpectrumLayout.FullAlternative:
                _stackPanel.Children.Add(_fullAlternative);
                _fullAlternative.SetLayout(keyboardLayout);
                break;
            case SpectrumLayout.Full:
                _stackPanel.Children.Add(_full);
                _full.SetLayout(keyboardLayout);
                break;
            case SpectrumLayout.KeyboardAndFront:
                _stackPanel.Children.Add(_keyboardAndFront);
                _keyboardAndFront.SetLayout(keyboardLayout);
                break;
            case SpectrumLayout.KeyboardOnly:
                _stackPanel.Children.Add(_keyboardOnly);
                _keyboardOnly.SetLayout(keyboardLayout);
                break;
        }

        UpdateLayout();

        var is24Zone = keyboardLayout == KeyboardLayout.Keyboard24Zone;
        foreach (var button in GetButtons())
        {
            button.Visibility = is24Zone || keys.Contains(button.KeyCode) ? Visibility.Visible : Visibility.Hidden;
        }
    }

    public IEnumerable<SpectrumZoneControl> GetVisibleButtons() =>
        GetButtons().Where(b => b.Visibility == Visibility.Visible);

    public SpectrumAmbientZoneControl? GetAmbientButton() =>
        this.GetVisibleChildrenOfType<SpectrumAmbientZoneControl>().FirstOrDefault();

    private IEnumerable<SpectrumZoneControl> GetButtons() =>
        this.GetVisibleChildrenOfType<SpectrumZoneControl>()
            .Where(b => b.KeyCode > 0);

    public IEnumerable<SpectrumZoneControl> GetVisibleKeyboardButtons() =>
        GetKeyboardButtons().Where(b => b.Visibility == Visibility.Visible);

    private IEnumerable<SpectrumZoneControl> GetKeyboardButtons() =>
        this.GetVisibleChildrenOfType<SpectrumZoneControl>()
            .Where(b => b.KeyCode is > 0 and < 0x100);
}
