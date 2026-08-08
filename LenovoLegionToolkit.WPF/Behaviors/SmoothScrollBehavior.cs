using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LenovoLegionToolkit.WPF.Behaviors;

public static class SmoothScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new UIPropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static readonly DependencyProperty ScrollDataProperty =
        DependencyProperty.RegisterAttached(
            "ScrollData",
            typeof(ScrollViewerScrollData),
            typeof(SmoothScrollBehavior),
            new UIPropertyMetadata(null));

    private static ScrollViewerScrollData GetScrollData(DependencyObject obj) => (ScrollViewerScrollData)obj.GetValue(ScrollDataProperty);
    private static void SetScrollData(DependencyObject obj, ScrollViewerScrollData value) => obj.SetValue(ScrollDataProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer scrollViewer)
            return;

        if ((bool)e.NewValue)
        {
            var data = new ScrollViewerScrollData(scrollViewer);
            SetScrollData(scrollViewer, data);
            scrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
        }
        else
        {
            scrollViewer.PreviewMouseWheel -= ScrollViewer_PreviewMouseWheel;
            var data = GetScrollData(scrollViewer);
            if (data != null)
            {
                data.Stop();
                SetScrollData(scrollViewer, null!);
            }
        }
    }

    private static void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        if (scrollViewer.ScrollableHeight <= 0)
            return;

        if (e.OriginalSource is DependencyObject source && IsInsideOpenComboBox(source))
            return;

        var data = GetScrollData(scrollViewer);
        if (data == null)
            return;

        data.Scroll(e.Delta);
        e.Handled = true;
    }

    private static bool IsInsideOpenComboBox(DependencyObject source)
    {
        var current = source;
        while (current != null)
        {
            if (current is ComboBox { IsDropDownOpen: true })
                return true;
            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current!);
        }
        return false;
    }

    private class ScrollViewerScrollData
    {
        private readonly ScrollViewer _scrollViewer;
        private double _targetOffset;
        private bool _isHooked;
        private readonly Stopwatch _stopwatch = new();
        private double _lastTickSeconds;

        public ScrollViewerScrollData(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
        }

        public void Scroll(double delta)
        {
            if (!_isHooked)
            {
                _targetOffset = _scrollViewer.VerticalOffset;
                _stopwatch.Restart();
                _lastTickSeconds = 0;
            }

            _targetOffset -= delta;

            if (_targetOffset < 0) _targetOffset = 0;
            double maxScroll = _scrollViewer.ScrollableHeight;
            if (_targetOffset > maxScroll) _targetOffset = maxScroll;

            Start();
        }

        public void Stop()
        {
            if (_isHooked)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isHooked = false;
                _stopwatch.Stop();
            }
        }

        private void Start()
        {
            if (!_isHooked)
            {
                CompositionTarget.Rendering += OnRendering;
                _isHooked = true;
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            double currentTick = _stopwatch.Elapsed.TotalSeconds;
            double dt = currentTick - _lastTickSeconds;
            _lastTickSeconds = currentTick;

            if (dt > 0.1) dt = 0.1;
            if (dt <= 0) return;

            double currentOffset = _scrollViewer.VerticalOffset;
            double diff = _targetOffset - currentOffset;

            if (Math.Abs(diff) < 0.2)
            {
                _scrollViewer.ScrollToVerticalOffset(_targetOffset);
                Stop();
                return;
            }

            double speed = 15.0;
            double lerpFactor = 1.0 - Math.Exp(-speed * dt);

            double newOffset = currentOffset + diff * lerpFactor;
            _scrollViewer.ScrollToVerticalOffset(newOffset);
        }
    }
}
