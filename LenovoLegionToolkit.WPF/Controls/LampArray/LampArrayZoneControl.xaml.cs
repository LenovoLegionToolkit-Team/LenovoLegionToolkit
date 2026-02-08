using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LenovoLegionToolkit.WPF.Controls.LampArray;

public partial class LampArrayZoneControl : UserControl
{
    public static readonly DependencyProperty KeyCodeProperty = DependencyProperty.Register(
        nameof(KeyCode), typeof(ushort), typeof(LampArrayZoneControl), new PropertyMetadata(default(ushort)));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(LampArrayZoneControl), new PropertyMetadata(default(string)));

    public ushort KeyCode
    {
        get => (ushort)GetValue(KeyCodeProperty);
        set => SetValue(KeyCodeProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Color? Color
    {
        get => (_background.Background as SolidColorBrush)?.Color;
        set
        {
            if (!value.HasValue)
                _background.Background = null;
            else if (_background.Background is SolidColorBrush brush)
                brush.Color = value.Value;
            else
                _background.Background = new SolidColorBrush(value.Value);
        }
    }

    public bool? IsChecked
    {
        get => _button.IsChecked;
        set => _button.IsChecked = value;
    }

    private RoutedEventHandler? _clickHandlers;
    public event RoutedEventHandler Click
    {
        add => _clickHandlers += value;
        remove => _clickHandlers -= value;
    }

    public LampArrayZoneControl()
    {
        InitializeComponent();
        _button.Click += (s, e) => _clickHandlers?.Invoke(this, e);
    }
}
