using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LenovoLegionToolkit.WPF.Controls.Custom;

public class RangeSlider : Control
{
    private const double ThumbSize = 16;
    private const double TrackHeight = 4;
    private const int SmallChange = 1;
    private const int LargeChange = 5;

    private const int DefaultMinimum = 2;
    private const int DefaultMaximum = 99;
    private const int DefaultMinimumDelta = 4;

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(int), typeof(RangeSlider), new PropertyMetadata(DefaultMinimum, OnLayoutPropertyChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(RangeSlider), new PropertyMetadata(DefaultMaximum, OnLayoutPropertyChanged));

    public static readonly DependencyProperty MinimumDeltaProperty = DependencyProperty.Register(
        nameof(MinimumDelta), typeof(int), typeof(RangeSlider), new PropertyMetadata(DefaultMinimumDelta, OnLayoutPropertyChanged));

    public static readonly DependencyProperty StartProperty = DependencyProperty.Register(
        nameof(Start), typeof(int), typeof(RangeSlider), new FrameworkPropertyMetadata(DefaultMinimum, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValuePropertyChanged, CoerceStart));

    public static readonly DependencyProperty StopProperty = DependencyProperty.Register(
        nameof(Stop), typeof(int), typeof(RangeSlider), new FrameworkPropertyMetadata(DefaultMinimum + DefaultMinimumDelta, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValuePropertyChanged, CoerceStop));

    public static readonly DependencyProperty StartNameProperty = DependencyProperty.Register(
        nameof(StartName), typeof(string), typeof(RangeSlider), new PropertyMetadata(string.Empty, OnNamePropertyChanged));

    public static readonly DependencyProperty StopNameProperty = DependencyProperty.Register(
        nameof(StopName), typeof(string), typeof(RangeSlider), new PropertyMetadata(string.Empty, OnNamePropertyChanged));

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int MinimumDelta
    {
        get => (int)GetValue(MinimumDeltaProperty);
        set => SetValue(MinimumDeltaProperty, value);
    }

    public int Start
    {
        get => (int)GetValue(StartProperty);
        set => SetValue(StartProperty, value);
    }

    public int Stop
    {
        get => (int)GetValue(StopProperty);
        set => SetValue(StopProperty, value);
    }

    public string StartName
    {
        get => (string)GetValue(StartNameProperty);
        set => SetValue(StartNameProperty, value);
    }

    public string StopName
    {
        get => (string)GetValue(StopNameProperty);
        set => SetValue(StopNameProperty, value);
    }

    public event EventHandler? ValueChanged;

    public event EventHandler? ValueCommitted;

    private Canvas? _canvas;
    private Border? _track;
    private Border? _selection;
    private Thumb? _startThumb;
    private Thumb? _stopThumb;

    private bool _startThumbActive;

    public RangeSlider()
    {
        Focusable = true;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_canvas is not null)
            _canvas.SizeChanged -= Canvas_SizeChanged;

        if (_startThumb is not null)
        {
            _startThumb.DragDelta -= StartThumb_DragDelta;
            _startThumb.DragCompleted -= Thumb_DragCompleted;
            _startThumb.DragStarted -= StartThumb_DragStarted;
        }

        if (_stopThumb is not null)
        {
            _stopThumb.DragDelta -= StopThumb_DragDelta;
            _stopThumb.DragCompleted -= Thumb_DragCompleted;
            _stopThumb.DragStarted -= StopThumb_DragStarted;
        }

        _canvas = GetTemplateChild("PART_Canvas") as Canvas;
        _track = GetTemplateChild("PART_Track") as Border;
        _selection = GetTemplateChild("PART_Selection") as Border;
        _startThumb = GetTemplateChild("PART_StartThumb") as Thumb;
        _stopThumb = GetTemplateChild("PART_StopThumb") as Thumb;

        if (_canvas is not null)
            _canvas.SizeChanged += Canvas_SizeChanged;

        if (_startThumb is not null)
        {
            _startThumb.DragStarted += StartThumb_DragStarted;
            _startThumb.DragDelta += StartThumb_DragDelta;
            _startThumb.DragCompleted += Thumb_DragCompleted;
        }

        if (_stopThumb is not null)
        {
            _stopThumb.DragStarted += StopThumb_DragStarted;
            _stopThumb.DragDelta += StopThumb_DragDelta;
            _stopThumb.DragCompleted += Thumb_DragCompleted;
        }

        UpdateAutomationNames();
        UpdatePositions();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!TryAdjust(e.Key))
            return;

        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (!IsAdjustKey(e.Key))
            return;

        e.Handled = true;

        ValueCommitted?.Invoke(this, EventArgs.Empty);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var rangeSlider = (RangeSlider)d;

        rangeSlider.CoerceValue(StartProperty);
        rangeSlider.CoerceValue(StopProperty);
        rangeSlider.UpdatePositions();
    }

    private static void OnValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var rangeSlider = (RangeSlider)d;

        rangeSlider.UpdatePositions();
        rangeSlider.ValueChanged?.Invoke(rangeSlider, EventArgs.Empty);
    }

    private static void OnNamePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((RangeSlider)d).UpdateAutomationNames();
    }

    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePositions();

    private void StartThumb_DragStarted(object sender, DragStartedEventArgs e) => _startThumbActive = true;

    private void StopThumb_DragStarted(object sender, DragStartedEventArgs e) => _startThumbActive = false;

    private void StartThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        _startThumbActive = true;

        if (TryMove(ref e))
            e.Handled = true;
    }

    private void StopThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        _startThumbActive = false;

        if (TryMove(ref e))
            e.Handled = true;
    }

    private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e) => ValueCommitted?.Invoke(this, EventArgs.Empty);

    private bool TryMove(ref DragDeltaEventArgs e)
    {
        if (_canvas is null)
            return false;

        var usable = _canvas.ActualWidth - ThumbSize;
        var range = Maximum - Minimum;

        if (usable <= 0 || range <= 0)
            return false;

        var delta = (int)Math.Round(e.HorizontalChange / usable * range);

        if (delta == 0)
            return false;

        if (_startThumbActive)
        {
            var value = ClampStart(Start + delta);

            if (value == Start)
                return false;

            Start = value;
        }
        else
        {
            var value = ClampStop(Stop + delta);

            if (value == Stop)
                return false;

            Stop = value;
        }

        return true;
    }

    private static bool IsAdjustKey(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown;

    private bool TryAdjust(Key key)
    {
        var delta = key switch
        {
            Key.Left or Key.Down => -SmallChange,
            Key.Right or Key.Up => SmallChange,
            Key.PageDown => -LargeChange,
            Key.PageUp => LargeChange,
            _ => 0
        };

        if (delta == 0)
            return false;

        if (_startThumbActive)
        {
            var value = ClampStart(Start + delta);

            if (value == Start)
                return true;

            Start = value;
        }
        else
        {
            var value = ClampStop(Stop + delta);

            if (value == Stop)
                return true;

            Stop = value;
        }

        return true;
    }

    private int ClampStart(int value)
    {
        var max = Math.Max(Minimum, Math.Min(Maximum, Stop - MinimumDelta));

        return Math.Clamp(value, Minimum, max);
    }

    private int ClampStop(int value)
    {
        var min = Math.Min(Maximum, Math.Max(Minimum, Start + MinimumDelta));

        return Math.Clamp(value, min, Maximum);
    }

    private static object CoerceStart(DependencyObject d, object baseValue) => ((RangeSlider)d).ClampStart((int)baseValue);

    private static object CoerceStop(DependencyObject d, object baseValue) => ((RangeSlider)d).ClampStop((int)baseValue);

    private void UpdateAutomationNames()
    {
        if (_startThumb is not null && !string.IsNullOrWhiteSpace(StartName))
            AutomationProperties.SetName(_startThumb, StartName);

        if (_stopThumb is not null && !string.IsNullOrWhiteSpace(StopName))
            AutomationProperties.SetName(_stopThumb, StopName);
    }

    private void UpdatePositions()
    {
        if (_canvas is null || _track is null || _selection is null || _startThumb is null || _stopThumb is null)
            return;

        var width = _canvas.ActualWidth;
        var height = _canvas.ActualHeight;
        var usable = width - ThumbSize;
        var range = Maximum - Minimum;

        if (usable <= 0 || height <= 0 || range <= 0)
            return;

        double Offset(int value) => Math.Clamp((value - Minimum) / (double)range * usable, 0, usable);

        var startOffset = Offset(Start);
        var stopOffset = Offset(Stop);

        _track.Width = usable;
        _track.Height = TrackHeight;
        Canvas.SetLeft(_track, ThumbSize / 2);
        Canvas.SetTop(_track, (height - TrackHeight) / 2);

        _selection.Width = Math.Max(0, stopOffset - startOffset);
        _selection.Height = TrackHeight;
        Canvas.SetLeft(_selection, ThumbSize / 2 + startOffset);
        Canvas.SetTop(_selection, (height - TrackHeight) / 2);

        _startThumb.Width = ThumbSize;
        _startThumb.Height = ThumbSize;
        Canvas.SetLeft(_startThumb, startOffset);
        Canvas.SetTop(_startThumb, (height - ThumbSize) / 2);

        _stopThumb.Width = ThumbSize;
        _stopThumb.Height = ThumbSize;
        Canvas.SetLeft(_stopThumb, stopOffset);
        Canvas.SetTop(_stopThumb, (height - ThumbSize) / 2);
    }
}
