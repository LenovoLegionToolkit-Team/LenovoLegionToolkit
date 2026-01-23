using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.Lib.View;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Controls;

public partial class FanCurveControlV3 : UserControl, INotifyPropertyChanged, IFanControlView
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SettingsChanged;

    private FanCurveEntry? _curveEntry;
    private FanTableData[]? _tableData;
    private bool _drawRequested;
    private double _maxPwm = 255;

    public FanType FanType { get; private set; }
    public int FanId { get; private set; }

    public ObservableCollection<CurveNode>? CurveNodes => _curveEntry?.CurveNodes;

    #region Configuration Properties
    public int CriticalTemp
    {
        get => _curveEntry?.CriticalTemp ?? 0;
        set
        {
            if (_curveEntry != null && _curveEntry.CriticalTemp != value)
            {
                _curveEntry.CriticalTemp = value;
                OnPropertyChanged();
                NotifySettingsChanged();
            }
        }
    }

    public bool IsLegion
    {
        get => _curveEntry?.IsLegion ?? false;
        set
        {
            if (_curveEntry != null && _curveEntry.IsLegion != value)
            {
                _curveEntry.IsLegion = value;
                OnPropertyChanged();
                NotifySettingsChanged();
            }
        }
    }

    public float LegionLowTempThreshold
    {
        get => _curveEntry?.LegionLowTempThreshold ?? 0f;
        set
        {
            if (_curveEntry != null && Math.Abs(_curveEntry.LegionLowTempThreshold - value) > 0.01f)
            {
                _curveEntry.LegionLowTempThreshold = value;
                OnPropertyChanged();
                NotifySettingsChanged();
            }
        }
    }

    public int AccelerationDcrReduction
    {
        get => _curveEntry?.AccelerationDcrReduction ?? 0;
        set
        {
            if (_curveEntry != null && _curveEntry.AccelerationDcrReduction != value)
            {
                _curveEntry.AccelerationDcrReduction = value;
                OnPropertyChanged();
                NotifySettingsChanged();
            }
        }
    }

    public int DecelerationDcrReduction
    {
        get => _curveEntry?.DecelerationDcrReduction ?? 0;
        set
        {
            if (_curveEntry != null && _curveEntry.DecelerationDcrReduction != value)
            {
                _curveEntry.DecelerationDcrReduction = value;
                OnPropertyChanged();
                NotifySettingsChanged();
            }
        }
    }

    public double MaxPwm
    {
        get => _maxPwm;
        set
        {
            if (value != _maxPwm)
            {
                _maxPwm = value;
                OnPropertyChanged();
                if (_curveEntry != null)
                {
                    _curveEntry.MaxPwm = value;
                    NotifySettingsChanged();
                }
            }
        }
    }
    #endregion

    #region Monitoring Properties
    private string _displayTemp = "-- °C";
    public string DisplayTemp
    {
        get => _displayTemp;
        set { _displayTemp = value; OnPropertyChanged(); }
    }

    private string _actualRpmDisplay = "0 RPM";
    public string ActualRpmDisplay
    {
        get => _actualRpmDisplay;
        set { _actualRpmDisplay = value; OnPropertyChanged(); }
    }

    private string _currentPwmDisplay = "0";
    public string CurrentPwmDisplay
    {
        get => _currentPwmDisplay;
        set { _currentPwmDisplay = value; OnPropertyChanged(); }
    }

    private byte _currentPwmByte;
    public byte CurrentPwmByte
    {
        get => _currentPwmByte;
        set { _currentPwmByte = value; OnPropertyChanged(); }
    }
    #endregion

    public ICommand AddPointCommand { get; }
    public ICommand RemovePointCommand { get; }

    public FanCurveControlV3()
    {
        InitializeComponent();

        AddPointCommand = new RelayCommand(() => AddPoint());
        RemovePointCommand = new RelayCommand<object>(RemovePoint, _ => _curveEntry?.CurveNodes?.Count > 2);

        SizeChanged += (s, e) => RequestDraw();
        Loaded += (s, e) => RequestDraw();
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        RequestDraw();
    }

    public void Initialize(FanCurveEntry entry, FanTableData[] tableData, LenovoLegionToolkit.Lib.FanType fanType, int fanId = 0)
    {
        _curveEntry = entry;
        _tableData = tableData;
        FanType = fanType;
        FanId = fanId;

        _maxPwm = _curveEntry.MaxPwm;
        OnPropertyChanged(nameof(MaxPwm));

        DataContext = this;

        if (_curveEntry.CurveNodes != null)
        {
            _curveEntry.CurveNodes.CollectionChanged += (s, e) => 
            {
                RequestDraw();
                NotifySettingsChanged();
                SubscribeToNodeChanges();
            };
            SubscribeToNodeChanges();
        }
        
        NotifyAllPropertiesChanged();
        Dispatcher.InvokeAsync(DrawGraph, DispatcherPriority.Render);
    }
    
    private void SubscribeToNodeChanges()
    {
        if (_curveEntry?.CurveNodes == null) return;
        foreach (var node in _curveEntry.CurveNodes)
        {
            node.PropertyChanged -= OnNodePropertyChanged;
            node.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CurveNode node) return;

        if (e.PropertyName == nameof(CurveNode.Temperature))
        {
            ValidateNodeTemperature(node);
        }
        else if (e.PropertyName == nameof(CurveNode.TargetPercent))
        {
            ValidateNodeSpeed(node);
        }

        RequestDraw();
        NotifySettingsChanged();
    }
    
    private void ValidateNodeTemperature(CurveNode node)
    {
        if (_curveEntry?.CurveNodes == null) return;
        int index = _curveEntry.CurveNodes.IndexOf(node);
        if (index < 0) return;

        float min = (index > 0) ? _curveEntry.CurveNodes[index - 1].Temperature : 0;
        float max = (index < _curveEntry.CurveNodes.Count - 1) ? _curveEntry.CurveNodes[index + 1].Temperature : 120;

        if (node.Temperature < min) node.Temperature = min;
        if (node.Temperature > max) node.Temperature = max;
    }

    private void ValidateNodeSpeed(CurveNode node)
    {
        if (_curveEntry?.CurveNodes == null) return;
        int index = _curveEntry.CurveNodes.IndexOf(node);
        if (index < 0) return;

        int min = (index > 0) ? _curveEntry.CurveNodes[index - 1].TargetPercent : 0;
        int max = (index < _curveEntry.CurveNodes.Count - 1) ? _curveEntry.CurveNodes[index + 1].TargetPercent : 100;

        if (node.TargetPercent < min) node.TargetPercent = min;
        if (node.TargetPercent > max) node.TargetPercent = max;
    }

    private void AddPoint()
    {
        if (_curveEntry?.CurveNodes == null) return;
        var lastNode = _curveEntry.CurveNodes.LastOrDefault();
        float newTemp = lastNode != null ? Math.Min(lastNode.Temperature + 5, 100) : 50;
        int newPercent = lastNode?.TargetPercent ?? 50;
        _curveEntry.CurveNodes.Add(new CurveNode { Temperature = newTemp, TargetPercent = newPercent });
    }

    private void RemovePoint(object? parameter)
    {
        if (_curveEntry?.CurveNodes == null) return;
        if (parameter is CurveNode node && _curveEntry.CurveNodes.Count > 2)
        {
            _curveEntry.CurveNodes.Remove(node);
        }
    }

    private void NotifySettingsChanged()
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    
    public void NotifyGlobalSettingsChanged()
    {
        NotifyAllPropertiesChanged();
    }

    private void NotifyAllPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsLegion));
        OnPropertyChanged(nameof(CriticalTemp));
        OnPropertyChanged(nameof(LegionLowTempThreshold));
        OnPropertyChanged(nameof(AccelerationDcrReduction));
        OnPropertyChanged(nameof(DecelerationDcrReduction));
        OnPropertyChanged(nameof(MaxPwm));
    }

    public void UpdateMonitoring(float temperature, int rpm, byte pwmByte)
    {
        DisplayTemp = $"{temperature:F1} °C";
        ActualRpmDisplay = $"{rpm} RPM";
        CurrentPwmDisplay = $"{pwmByte}";
        CurrentPwmByte = pwmByte;
    }
    
    public FanTableInfo? GetFanTableInfo()
    {
        if (_tableData is null || _curveEntry is null)
            return null;

        try
        {
            _curveEntry.Type = FanType;
            var fanTable = _curveEntry.ToFanTable(_tableData);
            return new FanTableInfo(_tableData, fanTable);
        }
        catch
        {
            return null;
        }
    }
    
    public void Reset(FanCurveEntry defaultEntry)
    {
        if (_curveEntry == null) return;
        
        IsLegion = defaultEntry.IsLegion;
        CriticalTemp = defaultEntry.CriticalTemp;
        LegionLowTempThreshold = defaultEntry.LegionLowTempThreshold;
        AccelerationDcrReduction = defaultEntry.AccelerationDcrReduction;
        DecelerationDcrReduction = defaultEntry.DecelerationDcrReduction;
        MaxPwm = defaultEntry.MaxPwm;

        _curveEntry.CurveNodes.Clear();
        foreach (var node in defaultEntry.CurveNodes)
        {
            _curveEntry.CurveNodes.Add(node);
        }
    }

    public FanCurveEntry? GetCurveEntry() => _curveEntry;

    private void RequestDraw()
    {
        if (!_drawRequested)
        {
            _drawRequested = true;
            Dispatcher.InvokeAsync(() => {
                _drawRequested = false;
                DrawGraph();
            }, DispatcherPriority.ApplicationIdle);
        }
    }

    private void DrawGraph()
    {
        if (_curveEntry is null || _canvas.ActualWidth <= 0 || _canvas.ActualHeight <= 0)
        {
            return;
        }

        var color = Application.Current.Resources["ControlFillColorDefaultBrush"] as SolidColorBrush
                    ?? new SolidColorBrush(Colors.CornflowerBlue);

        _canvas.Children.Clear();

        var sliders = FindVisualChildren<Slider>(_itemsControl).ToList();

        if (sliders.Count == 0 || sliders.Count != _curveEntry.CurveNodes.Count)
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

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj != null)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child != null && child is T)
                {
                    yield return (T)child;
                }

                foreach (T childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        T? child = default;
        int numVisuals = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < numVisuals; i++)
        {
            DependencyObject v = VisualTreeHelper.GetChild(parent, i);
            child = v as T;
            if (child == null)
            {
                child = FindVisualChild<T>(v);
            }
            if (child != null)
            {
                break;
            }
        }
        return child;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
