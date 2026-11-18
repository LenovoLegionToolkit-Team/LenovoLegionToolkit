using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Controls;

public partial class FanCurveControlV2
{
    private readonly List<Slider> _sliders = [];
    private readonly InfoTooltip _customToolTip = new();

    private FanTableData[]? _tableData;
    private FanTable? _minimumFanTable;

    public FanCurveControlV2()
    {
        InitializeComponent();

        MouseLeave += FanCurveControl_MouseLeave;
    }

    private void FanCurveControl_MouseLeave(object sender, MouseEventArgs e)
    {
        _customToolTip.IsOpen = false;
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        var size = base.ArrangeOverride(arrangeBounds);
        DrawGraph();
        return size;
    }

    public void SetFanTableInfo(FanTableInfo fanTableInfo, FanTable minimumFanTable)
    {
        _sliders.Clear();
        _slidersGrid.Children.Clear();

        var tableValues = fanTableInfo.Table.GetTable();

        for (var i = 0; i < tableValues.Length; i++)
        {
            var slider = GenerateSlider(i, 0, 10);
            slider.Value = tableValues[i];
            _sliders.Add(slider);
            _slidersGrid.Children.Add(slider);
        }

        _tableData = fanTableInfo.Data;
        _minimumFanTable = minimumFanTable;

        Dispatcher.InvokeAsync(DrawGraph, DispatcherPriority.Render);
    }

    public FanTableInfo? GetFanTableInfo()
    {
        if (_tableData is null)
            return null;

        var fanTable = _sliders.Select(s => (ushort)s.Value).ToArray();
        return new(_tableData, new FanTable(fanTable));
    }

    private Slider GenerateSlider(int index, int minimum, int maximum)
    {
        var slider = new Slider
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Orientation = Orientation.Vertical,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            Maximum = maximum,
            Minimum = minimum,
            Tag = index,
        };

        slider.MouseMove += Slider_MouseMove;
        slider.ValueChanged += Slider_OnValueChanged;

        Grid.SetColumn(slider, index + 1);

        return slider;
    }

    private void Slider_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        if (slider.Template.FindName("PART_Track", slider) is not Track track)
            return;

        if (!track.Thumb.IsMouseOver || _tableData is null)
        {
            _customToolTip.IsOpen = false;
            return;
        }

        _customToolTip.Update(_tableData, (int)slider.Tag, (int)slider.Value - 1);

        _customToolTip.Placement = PlacementMode.Custom;
        _customToolTip.PlacementTarget = track.Thumb;
        _customToolTip.CustomPopupPlacementCallback = ToolTipCustomPopupPlacementCallback;

        _customToolTip.HorizontalOffset += -0.1;
        _customToolTip.HorizontalOffset += +0.1;

        _customToolTip.IsOpen = true;
    }

    private void Slider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_sliders.Count < 10)
            return;

        if (sender is not Slider currentSlider)
            return;

        if (currentSlider is { IsKeyboardFocusWithin: false, IsMouseCaptureWithin: false })
            return;

        if (_minimumFanTable.HasValue)
        {
            var index = (int)currentSlider.Tag;
            var minimum = _minimumFanTable.Value.GetTable();

            if (currentSlider.Value < minimum[index])
            {
                currentSlider.Value = minimum[index];
                return;
            }
        }

        VerifyValues(currentSlider);
        DrawGraph();
    }

    private static CustomPopupPlacement[] ToolTipCustomPopupPlacementCallback(Size size, Size targetSize, Point _)
    {
        return
        [
            new(new((targetSize.Width - size.Width) * 0.5, -targetSize.Height -size.Height + 8), PopupPrimaryAxis.Vertical)
        ];
    }

    private void VerifyValues(Slider currentSlider)
    {
        var currentIndex = _sliders.IndexOf(currentSlider);
        if (currentIndex < 0)
            return;

        var currentValue = currentSlider.Value;
        var slidersBefore = _sliders.Take(currentIndex);
        var slidersAfter = _sliders.Skip(currentIndex + 1);

        foreach (var slider in slidersBefore)
        {
            if (slider.Value > currentValue)
                slider.Value = currentValue;
        }

        foreach (var slider in slidersAfter)
        {
            if (slider.Value < currentValue)
                slider.Value = currentValue;
        }
    }

    private void DrawGraph()
    {
        var color = Application.Current.Resources["ControlFillColorDefaultBrush"] as SolidColorBrush;

        _canvas.Children.Clear();

        var points = _sliders
            .Select(GetThumbLocation)
            .Select(p => new Point(p.X, p.Y))
            .ToArray();

        if (points.IsEmpty())
            return;

        // Line

        var pathSegmentCollection = new PathSegmentCollection();
        foreach (var point in points.Skip(1))
            pathSegmentCollection.Add(new LineSegment { Point = point });
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

        // Fill

        var pointCollection = new PointCollection { new(points[0].X, _canvas.ActualHeight - 1) };
        foreach (var point in points)
            pointCollection.Add(point);
        pointCollection.Add(new(points[^1].X, _canvas.ActualHeight - 1));

        var polygon = new Polygon
        {
            Fill = color,
            Points = pointCollection
        };
        _canvas.Children.Add(polygon);
    }

    private Point GetThumbLocation(Slider slider)
    {
        var ratio = slider.Value / (slider.Maximum - slider.Minimum);
        var y = slider.ActualHeight - (slider.ActualHeight * ratio);
        var x = slider.ActualWidth * 0.5;
        var point = slider.TranslatePoint(new(x, y), _canvas);
        return point;
    }

    private class InfoTooltip : ToolTip
    {
        private readonly Grid _grid = new()
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            ColumnDefinitions =
            {
                new() { Width = GridLength.Auto},
                new() { Width = GridLength.Auto},
                new() { Width = GridLength.Auto},
                new() { Width = GridLength.Auto}
            }
        };

        private readonly List<(TextBlock Temperature, TextBlock FanSpeed, ushort[] Temps, ushort[] FanSpeeds)> _rows = [];

        private FanTableData[]? _builtFor;

        public InfoTooltip()
        {
            SetResourceReference(StyleProperty, typeof(ToolTip));

            Content = _grid;
        }

        public void Update(FanTableData[] tableData, int index, int value)
        {
            if (!ReferenceEquals(_builtFor, tableData))
            {
                Build(tableData);
            }

            foreach (var (temperatureTextBlock, fanSpeedTextBlock, temps, fanSpeeds) in _rows)
            {
                temperatureTextBlock.Text = FormatTemperature(temps, index);
                fanSpeedTextBlock.Text = FormatFanSpeed(fanSpeeds, value);
            }
        }

        private void Build(FanTableData[] tableData)
        {
            _builtFor = tableData;

            _rows.Clear();
            _grid.Children.Clear();
            _grid.RowDefinitions.Clear();

            var rows = tableData
                .GroupBy(td => td.Type)
                .Select(g => g.First())
                .OrderBy(td => GetFanOrder(td.FanId))
                .ToArray();

            foreach (var td in rows)
            {
                AddRow(GetLabel(td), GetTemps(tableData, td), td.FanSpeeds);
            }
        }

        private static ushort[] GetTemps(FanTableData[] tableData, FanTableData fan)
        {
            var sensorId = fan.Type switch
            {
                FanTableType.CPU => 4,
                FanTableType.CPUSensor => 1,
                FanTableType.SystemFan => 1,
                _ => 5,
            };

            return tableData
                .Where(td => td.SensorId == sensorId)
                .Select(td => td.Temps)
                .FirstOrDefault() ?? fan.Temps;
        }

        private void AddRow(string label, ushort[] temps, ushort[] fanSpeeds)
        {
            var row = _rows.Count;

            var labelTextBlock = new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(0, 0, 16, 0),
            };
            var temperatureTextBlock = new TextBlock { TextAlignment = TextAlignment.Right };
            var separatorTextBlock = new TextBlock { Text = "@", Margin = new Thickness(6, 0, 6, 0) };
            var fanSpeedTextBlock = new TextBlock { TextAlignment = TextAlignment.Right };

            Grid.SetColumn(labelTextBlock, 0);
            Grid.SetColumn(temperatureTextBlock, 1);
            Grid.SetColumn(separatorTextBlock, 2);
            Grid.SetColumn(fanSpeedTextBlock, 3);
            Grid.SetRow(labelTextBlock, row);
            Grid.SetRow(temperatureTextBlock, row);
            Grid.SetRow(separatorTextBlock, row);
            Grid.SetRow(fanSpeedTextBlock, row);

            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _grid.Children.Add(labelTextBlock);
            _grid.Children.Add(temperatureTextBlock);
            _grid.Children.Add(separatorTextBlock);
            _grid.Children.Add(fanSpeedTextBlock);

            _rows.Add((temperatureTextBlock, fanSpeedTextBlock, temps, fanSpeeds));
        }

        private static int GetFanOrder(byte fanId) => fanId switch
        {
            1 => 0,
            2 => 1,
            3 => 2,
            4 => 3,
            _ => 4,
        };

        private static string GetLabel(FanTableData tableData) => tableData.Type switch
        {
            FanTableType.CPU => Resource.FanCurveControl_CPU,
            FanTableType.CPUSensor => Resource.FanCurveControl_CPUSensor,
            FanTableType.GPU => Resource.FanCurveControl_GPU,
            FanTableType.GPU2 => Resource.FanCurveControl_GPU2,
            FanTableType.SystemFan => Resource.FanCurveControl_SystemFan,
            _ => "-",
        };

        private static string FormatTemperature(ushort[] temps, int index)
        {
            if (temps is null || index < 0 || index >= temps.Length)
            {
                return "-";
            }

            var temp = temps[index];

            return temp >= 127 ? "-" : $"{temp}{Resource.Celsius}";
        }

        private static string FormatFanSpeed(ushort[] fanSpeeds, int value)
        {
            if (fanSpeeds is null || value < 0 || value >= fanSpeeds.Length)
            {
                return "-";
            }

            return $"{fanSpeeds[value]} {Resource.RPM}";
        }
    }
}
