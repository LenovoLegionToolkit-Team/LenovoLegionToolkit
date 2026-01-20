using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Autofac;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.Lib.View;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Controls;

public partial class FanCurveControlV3
{
    private FanControlViewModel? _viewModel;
    private FanTableData[]? _tableData;
    private FanType _fanType = FanType.Cpu;
    private int _fanId = 0;

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    public static IEnumerable<T> FindVisualChildren<T>(DependencyObject? depObj) where T : DependencyObject
    {
        if (depObj == null) yield break;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);
            if (child is T t) yield return t;
            foreach (var childOfChild in FindVisualChildren<T>(child)) yield return childOfChild;
        }
    }

    private readonly SensorsGroupController _sensorsController;
    private FanTableInfo? _lastFanTableInfo;

    private bool _drawRequested;

    public FanCurveControlV3()
    {
        InitializeComponent();

        _sensorsController = IoCContainer.Resolve<SensorsGroupController>();

        SizeChanged += (s, e) => RequestDraw();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void RequestDraw()
    {
        if (_drawRequested) return;
        _drawRequested = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _drawRequested = false;
            DrawGraph();
        }), DispatcherPriority.ApplicationIdle);
    }

    public int FanId => _fanId;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var state = await _sensorsController.IsSupportedAsync();
            if (state != LibreHardwareMonitorInitialState.Success && state != LibreHardwareMonitorInitialState.Initialized)
            {
                Log.Instance.Trace($"Sensors initialization state: {state}");
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Sensors initialization failed: {ex.Message}");
        }
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0)
        {
            RequestDraw();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {

    }

    public void SetFanTableInfo(FanTableInfo fanTableInfo, FanTable minimumFanTable, FanType fanType = FanType.Cpu, int fanId = 0, FanCurveEntry? savedEntry = null)
    {
        var curveEntry = savedEntry ?? FanCurveEntry.FromFanTableInfo(fanTableInfo, (ushort)fanType);

        _viewModel = IoCContainer.Resolve<FanControlViewModel>(
            new NamedParameter("fanType", fanType),
            new TypedParameter(typeof(FanCurveEntry), curveEntry)
        );

        DataContext = _viewModel;

        _viewModel.CurveNodes.CollectionChanged += (s, e) => RequestDraw();
        foreach (var node in _viewModel.CurveNodes)
        {
            node.PropertyChanged += (s, e) => RequestDraw();
        }

        Dispatcher.InvokeAsync(DrawGraph, DispatcherPriority.Render);
    }


    public event EventHandler<string>? OnFanSettingsSyncRequired;

    public void SyncFanSettings(FanControlViewModel source)
    {
        if (_viewModel == null || source == null || _viewModel == source) return;

        if (_viewModel.AccelerationDcrReduction != source.AccelerationDcrReduction)
            _viewModel.AccelerationDcrReduction = source.AccelerationDcrReduction;

        if (_viewModel.DecelerationDcrReduction != source.DecelerationDcrReduction)
            _viewModel.DecelerationDcrReduction = source.DecelerationDcrReduction;

        if (_viewModel.IsLegion != source.IsLegion)
            _viewModel.IsLegion = source.IsLegion;

        if (_viewModel.CriticalTemp != source.CriticalTemp)
            _viewModel.CriticalTemp = source.CriticalTemp;

        if (Math.Abs(_viewModel.LegionLowTempThreshold - source.LegionLowTempThreshold) > 0.01f)
            _viewModel.LegionLowTempThreshold = source.LegionLowTempThreshold;
    }

    public FanTableInfo? GetFanTableInfo()
    {
        if (_tableData is null || _viewModel is null)
            return null;

        try
        {
            var fanTable = _viewModel.GetCurveEntry().ToFanTable(_tableData);
            return new FanTableInfo(_tableData, fanTable);
        }
        catch
        {
            return null;
        }
    }

    public FanControlViewModel? GetViewModel() => _viewModel;

    private void DrawGraph()
    {
        if (_viewModel is null || _canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0)
        {
            return;
        }

        var color = Application.Current.Resources["ControlFillColorDefaultBrush"] as SolidColorBrush
                    ?? new SolidColorBrush(Colors.CornflowerBlue);

        _canvas.Children.Clear();

        var sliders = FindVisualChildren<Slider>(_itemsControl).ToList();

        if (sliders.Count == 0 || sliders.Count != _viewModel.CurveNodes.Count)
        {
            return;
        }

        var points = new List<Point>();
        foreach (var slider in sliders)
        {
            var thumb = FindVisualChild<Thumb>(slider);
            if (thumb is { IsLoaded: true, ActualHeight: > 0 })
            {
                var center = new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2);
                points.Add(thumb.TranslatePoint(center, _canvas));
            }
            else
            {
                var ratio = (slider.Value - slider.Minimum) / (slider.Maximum - slider.Minimum);
                var sy = slider.ActualHeight * (1 - ratio);
                var sx = slider.ActualWidth / 2;
                points.Add(slider.TranslatePoint(new Point(sx, sy), _canvas));
            }
        }

        if (points.Count < 2)
        {
            return;
        }

        var gridBrush = new SolidColorBrush(Color.FromArgb(30, color.Color.R, color.Color.G, color.Color.B));
        for (int i = 0; i <= 100; i += 20)
        {
            double gy = _canvas.ActualHeight * (1 - (i / 100.0));
            var gridLine = new Line
            {
                X1 = 0,
                Y1 = gy,
                X2 = _canvas.ActualWidth,
                Y2 = gy,
                Stroke = gridBrush,
                StrokeThickness = 1,
                StrokeDashArray = [2, 2]
            };
            _canvas.Children.Add(gridLine);
        }

        var pathSegmentCollection = new PathSegmentCollection();

        foreach (var point in points.Skip(1))
        {
            pathSegmentCollection.Add(new LineSegment { Point = point });
        }

        var pathFigure = new PathFigure { StartPoint = points[0], Segments = pathSegmentCollection };

        var path = new Path
        {
            StrokeThickness = 2,
            Stroke = color,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = new PathGeometry { Figures = [pathFigure] },
        };
        _canvas.Children.Add(path);

        var pointCollection = new PointCollection { new(points[0].X, _canvas.ActualHeight) };
        foreach (var point in points)
            pointCollection.Add(point);
        pointCollection.Add(new(points[^1].X, _canvas.ActualHeight));

        var polygon = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb(50, color.Color.R, color.Color.G, color.Color.B)),
            Points = pointCollection
        };
        _canvas.Children.Add(polygon);

        foreach (var point in points)
        {
            var ellipse = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = color,
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 2
            };
            Canvas.SetLeft(ellipse, point.X - 5);
            Canvas.SetTop(ellipse, point.Y - 5);
            _canvas.Children.Add(ellipse);
        }
    }
}
