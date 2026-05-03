using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.Lib.Utils.LampEffects;
using LenovoLegionToolkit.WPF.Controls.LampArray;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using WinUIColor = Windows.UI.Color;
using WpfColor = System.Windows.Media.Color;
using LibDirection = LenovoLegionToolkit.Lib.Utils.LampEffects.GradientDirection;
using WpfDirection = LenovoLegionToolkit.WPF.GradientDirection;

namespace LenovoLegionToolkit.WPF.Pages;

public partial class LampArrayRGBKeyboardPage : UiPage
{
    private readonly LampArrayController _controller;
    private readonly LampArraySettings _settings;
    private WinUIColor _selectedColor = WinUIColor.FromArgb(255, 255, 0, 128);

    private readonly HashSet<int> _selectedIndices = new();
    private readonly Dictionary<int, ILampEffect> _lampEffectMap = new();
    private readonly Dictionary<int, LampArrayZoneControl> _controlMap = new();
    private readonly CustomPatternEffect _globalCustomEffect = new();
    private readonly AuroraSyncEffect _globalAuroraEffect = new();
    private readonly SpectrumScreenCapture _screenCapture = new();
    private RGBColor[,] _screenBuffer = new RGBColor[32, 18];
    private CancellationTokenSource? _screenCaptureCts;

    public IEnumerable<LampEffectType> EffectTypes => Enum.GetValues<LampEffectType>();
    public IEnumerable<WpfDirection> Directions => Enum.GetValues<WpfDirection>();

    private readonly ILampEffect _defaultEffect = new RainbowEffect(4.0, true);

    private CancellationTokenSource? _probeCts;
    private bool _isUpdatingUi = false;
    private bool _isProbing = false;
    private bool _isDragging = false;
    private Point _startPoint;

    public LampArrayRGBKeyboardPage()
    {
        InitializeComponent();

        _controller = IoCContainer.Resolve<LampArrayController>();
        _settings = IoCContainer.Resolve<LampArraySettings>();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        if (_colorPicker != null)
        {
            _colorPicker.ColorChangedContinuous += OnColorChanged;
        }
    }

    public static async Task<bool> IsSupportedAsync()
    {
        try
        {
            if (AppFlags.Instance.EnableLampArray)
                return true;

            var devices = await DeviceInformation.FindAllAsync(LampArray.GetDeviceSelector());
            return devices.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error checking LampArray support: {ex.Message}");
            return false;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var mi = await Compatibility.GetMachineInformationAsync();
            bool showAmbients = mi is { LegionSeries: LegionSeries.Legion_Pro_7, Generation: >= 10 };

            if (!showAmbients)
            {
                _rearAmbientLight.Visibility = Visibility.Collapsed;
                _aftAmbientLight.Visibility = Visibility.Collapsed;
                if (_cbiRear != null) _cbiRear.Visibility = Visibility.Collapsed;
                if (_cbiAft != null) _cbiAft.Visibility = Visibility.Collapsed;
                _zoneSelect.SelectedIndex = 0;
            }

            await _controller.StartAsync();
        }
        catch { }

        await Dispatcher.InvokeAsync(() =>
        {
            if (_visualKeyboard != null)
            {
                _visualKeyboard.Visibility = Visibility.Visible;
                _visualKeyboard.ApplyTemplate();
            }
            if (_rearAmbientLight != null)
            {
                _rearAmbientLight.Visibility = Visibility.Visible;
                _rearAmbientLight.ApplyTemplate();
            }
            if (_aftAmbientLight != null)
            {
                _aftAmbientLight.Visibility = Visibility.Visible;
                _aftAmbientLight.ApplyTemplate();
            }

            UpdateLayout();

            var allRoots = new DependencyObject[] { _visualKeyboard!, _rearAmbientLight!, _aftAmbientLight! };
            foreach (var root in allRoots)
            {
                InitializeKeyboardEvents(root);

                foreach (var key in EnumerateKeys(root))
                {
                    var indices = key.GetIndices();
                    foreach (var idx in indices) _controlMap[idx] = key;
                }
            }

            if (_zoneSelect.SelectedIndex == -1) _zoneSelect.SelectedIndex = 0;

            var store = _settings.Store;
            var hasPerLampConfig = store.PerLampEffects.Count > 0;

            if (hasPerLampConfig)
            {
                RestoreEffectsFromSettings();
            }
            else
            {
                var allIndices = _controlMap.Keys.ToList();
                foreach (var idx in allIndices) _lampEffectMap[idx] = _defaultEffect;
                _controller.SetEffectForIndices(allIndices, _defaultEffect);
            }

            _controller.Brightness = store.Brightness;
            _controller.Speed = store.Speed;
            _controller.SmoothTransition = store.SmoothTransition;

            if (_brightnessSlider != null) _brightnessSlider.Value = store.Brightness * 100.0;
            if (_brightnessValue != null) _brightnessValue.Text = $"{store.Brightness * 100:F0}%";

            if (_speedSlider != null) _speedSlider.Value = store.Speed * 100.0;
            if (_speedValue != null) _speedValue.Text = $"{store.Speed * 100:F0}%";

            if (_smoothTransitionCheckBox != null) _smoothTransitionCheckBox.IsChecked = store.SmoothTransition;

            UpdateZoneVisibility();
            ClearSelection();
            UpdateEffectSelectionUI();

        }, DispatcherPriority.Loaded);

        CompositionTarget.Rendering += OnEffectTick;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnEffectTick;

        _controller.SaveSettings(_settings);
        StopScreenCapture();
    }

    private void CalculateAuroraBounds()
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool found = false;

        var lamps = _controller.GetLamps().ToList();
        foreach (var lamp in lamps)
        {
            if (lamp.Info.Position.X < minX) minX = lamp.Info.Position.X;
            if (lamp.Info.Position.Y < minY) minY = lamp.Info.Position.Y;
            if (lamp.Info.Position.X > maxX) maxX = lamp.Info.Position.X;
            if (lamp.Info.Position.Y > maxY) maxY = lamp.Info.Position.Y;
            found = true;
        }

        if (found)
        {
            double width = maxX - minX;
            double height = maxY - minY;
            double centerX = minX + width / 2.0;
            double centerY = minY + height / 2.0;

            if (height < 0.05) height = 0.15;
            if (width < 0.2) width = 0.45;

            centerY -= 0.04;

            _globalAuroraEffect.SetBounds(centerX, centerY, width, height);
        }
    }

    private void StartScreenCapture()
    {
        if (_screenCaptureCts != null) return;

        CalculateAuroraBounds();

        _screenCaptureCts = new CancellationTokenSource();
        var token = _screenCaptureCts.Token;

        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    _screenCapture.CaptureScreen(ref _screenBuffer, 32, 18, token);
                    _globalAuroraEffect.UpdateScreenData(_screenBuffer, 32, 18);
                    await Task.Delay(33, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Screen capture failed: {ex.Message}");
            }
        }, token);
    }

    private void StopScreenCapture()
    {
        _screenCaptureCts?.Cancel();
        _screenCaptureCts = null;
    }

    private void ZoneSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateZoneVisibility();
        ClearSelection();
        UpdateEffectSelectionUI();
    }

    private void UpdateZoneVisibility()
    {
        if (_visualKeyboard == null || _rearAmbientLight == null || _aftAmbientLight == null) return;

        int index = _zoneSelect.SelectedIndex;

        _visualKeyboard.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        _rearAmbientLight.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        _aftAmbientLight.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is LampArrayZoneControl key)
        {
            var indices = key.GetIndices();
            if (indices == null || indices.Length == 0) return;

            bool newState = !(key.IsChecked ?? false);
            key.IsChecked = newState;

            if (newState)
            {
                foreach (var idx in indices) _selectedIndices.Add(idx);
            }
            else
            {
                foreach (var idx in indices) _selectedIndices.Remove(idx);
            }

            UpdateEffectSelectionUI();
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var key in _controlMap.Values)
        {
            if (key.Visibility == Visibility.Visible && key.IsVisible)
            {
                key.IsChecked = true;
                var indices = key.GetIndices();
                if (indices != null)
                {
                    foreach (var idx in indices) _selectedIndices.Add(idx);
                }
            }
        }
        UpdateEffectSelectionUI();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        ClearSelection();
        UpdateEffectSelectionUI();
    }

    private void PreviewGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Grid grid) return;
        _isDragging = true;
        _startPoint = e.GetPosition(grid);

        if (_selectionRect != null)
        {
            _selectionRect.Visibility = Visibility.Visible;
            _selectionRect.Width = 0;
            _selectionRect.Height = 0;
            Canvas.SetLeft(_selectionRect, _startPoint.X);
            Canvas.SetTop(_selectionRect, _startPoint.Y);
        }

        grid.CaptureMouse();

        bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (!isCtrl)
        {
            ClearSelection();
            UpdateEffectSelectionUI();
        }
    }

    private void PreviewGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || sender is not Grid grid || _selectionRect == null) return;

        var currentPoint = e.GetPosition(grid);

        var x = Math.Min(_startPoint.X, currentPoint.X);
        var y = Math.Min(_startPoint.Y, currentPoint.Y);
        var w = Math.Abs(_startPoint.X - currentPoint.X);
        var h = Math.Abs(_startPoint.Y - currentPoint.Y);

        Canvas.SetLeft(_selectionRect, x);
        Canvas.SetTop(_selectionRect, y);
        _selectionRect.Width = w;
        _selectionRect.Height = h;
    }

    private void PreviewGrid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging || sender is not Grid grid) return;

        _isDragging = false;
        grid.ReleaseMouseCapture();
        if (_selectionRect != null) _selectionRect.Visibility = Visibility.Collapsed;

        var rect = _selectionRect != null
            ? new Rect(Canvas.GetLeft(_selectionRect), Canvas.GetTop(_selectionRect), _selectionRect.Width, _selectionRect.Height)
            : default;

        if (rect.Width < 2 && rect.Height < 2) return;

        foreach (var kvp in _controlMap)
        {
            var idx = kvp.Key;
            var control = kvp.Value;

            if (control.Visibility != Visibility.Visible || !control.IsVisible) continue;

            try
            {
                var transform = control.TransformToAncestor(grid);
                var bounds = transform.TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));

                if (rect.IntersectsWith(bounds))
                {
                    if (!_selectedIndices.Contains(idx))
                    {
                        _selectedIndices.Add(idx);
                        control.IsChecked = true;
                    }
                }
            }
            catch { }
        }

        UpdateEffectSelectionUI();
    }

    private void ClearSelection()
    {
        _selectedIndices.Clear();
        foreach (var key in _controlMap.Values)
        {
            key.IsChecked = false;
        }
    }

    private void UpdateEffectSelectionUI()
    {
        if (_effectSelect == null) return;

        if (_selectedIndices.Count == 0)
        {
            _effectSelect.IsEnabled = false;
            if (_selectionSummary != null) _selectionSummary.Text = $"{Resource.LampArrayRGBKeyboardPage_No_Selection}";
            return;
        }

        _effectSelect.IsEnabled = true;

        ILampEffect? firstEffect = null;
        bool mixed = false;

        foreach (var idx in _selectedIndices)
        {
            var effect = _lampEffectMap.GetValueOrDefault(idx, _defaultEffect);

            if (firstEffect == null) firstEffect = effect;
            else if (effect != firstEffect && effect?.Name != firstEffect?.Name)
            {
                mixed = true;
                break;
            }
        }

        _isUpdatingUi = true;
        string effectName = mixed ? "Mixed" : string.Empty;

        if (mixed)
        {
            _effectSelect.SelectedIndex = -1;
            if (_directionPanel != null) _directionPanel.Visibility = Visibility.Collapsed;
        }
        else if (firstEffect != null)
        {
            LampEffectType? selectedType = firstEffect switch
            {
                StaticEffect => LampEffectType.Static,
                BreatheEffect => LampEffectType.Breathe,
                WaveEffect => LampEffectType.Wave,
                RainbowEffect => LampEffectType.Rainbow,
                MeteorEffect => LampEffectType.Meteor,
                RippleEffect => LampEffectType.Ripple,
                SparkleEffect => LampEffectType.Sparkle,
                GradientEffect => LampEffectType.Gradient,
                CustomPatternEffect => LampEffectType.Custom,
                RainbowWaveEffect => LampEffectType.RainbowWave,
                SpiralRainbowEffect => LampEffectType.SpiralRainbow,
                AuroraSyncEffect => LampEffectType.AuroraSync,
                _ => null
            };

            if (firstEffect is RainbowWaveEffect rwe && rwe.Parameters.TryGetValue("Direction", out var d) && d is LibDirection dir)
            {
                _directionSelect.SelectedItem = (WpfDirection)(int)dir;
            }

            if (_directionPanel != null)
            {
                _directionPanel.Visibility = selectedType is LampEffectType.Gradient or LampEffectType.RainbowWave
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (selectedType.HasValue)
            {
                _effectSelect.SelectedItem = selectedType.Value;
                effectName = selectedType.Value.GetDisplayName();
            }
        }
        else
        {
            if (_directionPanel != null) _directionPanel.Visibility = Visibility.Collapsed;
        }

        _isUpdatingUi = false;

        if (_selectionSummary != null)
        {
            _selectionSummary.Text = string.Format(Resource.LampArrayRGBKeyboardPage_Selected_Key, _selectedIndices.Count, effectName);
        }
    }

    private void EffectSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingUi || _selectedIndices.Count == 0 || _effectSelect.SelectedItem == null) return;

        var selectedType = (LampEffectType)_effectSelect.SelectedItem;
        ILampEffect effectToApply = selectedType switch
        {
            LampEffectType.Static => new StaticEffect(_selectedColor),
            LampEffectType.Breathe => new BreatheEffect(_selectedColor, 2.0),
            LampEffectType.Wave => new WaveEffect(_selectedColor, WinUIColor.FromArgb(0, 0, 0, 0), 2.0),
            LampEffectType.Rainbow => new RainbowEffect(4.0, true),
            LampEffectType.Meteor => new MeteorEffect(_selectedColor, 3, 2.0),
            LampEffectType.Ripple => new RippleEffect(_selectedColor, 2.0),
            LampEffectType.Sparkle => new SparkleEffect(_selectedColor, 0.5),
            LampEffectType.Gradient => new GradientEffect([_selectedColor, WinUIColor.FromArgb(255, 0, 0, 0)], GetSelectedDirection()),
            LampEffectType.Custom => _globalCustomEffect,
            LampEffectType.RainbowWave => new RainbowWaveEffect(1.0, 2.0, GetSelectedDirection()),
            LampEffectType.SpiralRainbow => new SpiralRainbowEffect(),
            LampEffectType.AuroraSync => _globalAuroraEffect,
            _ => new RainbowEffect(4.0, true)
        };

        if (_directionPanel != null)
        {
            _directionPanel.Visibility = selectedType is LampEffectType.Gradient or LampEffectType.RainbowWave
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (selectedType == LampEffectType.AuroraSync)
        {
            StartScreenCapture();
        }
        else
        {
            StopScreenCapture();
        }

        foreach (var idx in _selectedIndices)
        {
            _lampEffectMap[idx] = effectToApply;
        }
        _controller.SetEffectForIndices(_selectedIndices.ToList(), effectToApply);

        SyncVisualsForIndices(_selectedIndices);

        if (_selectionSummary != null)
        {
            _selectionSummary.Text = string.Format(Resource.LampArrayRGBKeyboardPage_Selected_Key, _selectedIndices.Count, selectedType.GetDisplayName());
        }
    }

    private LibDirection GetSelectedDirection()
    {
        if (_directionSelect?.SelectedItem is WpfDirection uiDir)
        {
            return (LibDirection)(int)uiDir;
        }
        return LibDirection.LeftToRight;
    }

    private void SyncVisualsForIndices(IEnumerable<int> indices)
    {
        foreach (var idx in indices)
        {
            if (_controlMap.TryGetValue(idx, out var control))
            {
                if (_lampEffectMap.TryGetValue(idx, out var effect) && effect is CustomPatternEffect custom)
                {
                    var c = custom.GetColorForLamp(idx, 0, null!, 0);
                    control.Color = (c.A > 0) ? WpfColor.FromArgb(c.A, c.R, c.G, c.B) : null;
                }
                else
                {
                    control.Color = null;
                }
            }
        }
    }

    private void OnColorChanged(object? sender, EventArgs e)
    {
        if (_colorPicker == null) return;
        var mediaColor = _colorPicker.SelectedColor;
        var drawingColor = WinUIColor.FromArgb(mediaColor.A, mediaColor.R, mediaColor.G, mediaColor.B);
        ApplyColorToSelection(drawingColor);
    }

    private void ApplyColorToSelection(WinUIColor color)
    {
        _selectedColor = color;

        bool needSwitch = false;
        foreach (var idx in _selectedIndices)
        {
            if (!_lampEffectMap.TryGetValue(idx, out var eff) || !(eff is CustomPatternEffect))
            {
                needSwitch = true;
                break;
            }
        }

        if (needSwitch)
        {
            _isUpdatingUi = true;
            _effectSelect.SelectedItem = LampEffectType.Custom;
            _isUpdatingUi = false;

            foreach (var idx in _selectedIndices) _lampEffectMap[idx] = _globalCustomEffect;
            _controller.SetEffectForIndices(_selectedIndices.ToList(), _globalCustomEffect);

            if (_selectionSummary != null)
                _selectionSummary.Text = string.Format(Resource.LampArrayRGBKeyboardPage_Selected_Key, _selectedIndices.Count, LampEffectType.Custom.GetDisplayName());
        }

        foreach (var idx in _selectedIndices)
        {
            _globalCustomEffect.SetColor(idx, color);
        }
        SyncVisualsForIndices(_selectedIndices);
    }

    private void ClearCustomColors_Click(object sender, RoutedEventArgs e)
    {
        ApplyColorToSelection(WinUIColor.FromArgb(0, 0, 0, 0));
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider && _controller != null)
        {
            _controller.Brightness = slider.Value / 100.0;
            if (_brightnessValue != null) _brightnessValue.Text = $"{slider.Value:F0}%";
        }
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider && _controller != null)
        {
            _controller.Speed = slider.Value / 100.0;
            if (_speedValue != null) _speedValue.Text = $"{slider.Value:F0}%";
        }
    }

    private void SmoothTransition_Changed(object sender, RoutedEventArgs e)
    {
        if (_controller != null && _smoothTransitionCheckBox != null)
            _controller.SmoothTransition = _smoothTransitionCheckBox.IsChecked ?? true;
    }

    private void LogZoneSummaries()
    {
        try
        {
            var lamps = _controller.GetLamps().OrderBy(l => l.Info.Index).ToList();
            if (lamps.Count == 0)
            {
                Log.Instance.Trace($"[Probe Zones] No lamps found from HID device.");
                System.Diagnostics.Debug.WriteLine($"[Probe Zones] No lamps found from HID device.");
                return;
            }

            var summaries = new List<string>();
            var groupedLamps = lamps.GroupBy(l => l.Info.Purposes);

            foreach (var group in groupedLamps)
            {
                string purposeName = group.Key.ToString();
                var indices = group.Select(l => l.Info.Index).Distinct().OrderBy(i => i).ToList();
                var ranges = new List<string>();

                if (indices.Count > 0)
                {
                    int start = indices[0];
                    int end = indices[0];

                    for (int i = 1; i < indices.Count; i++)
                    {
                        if (indices[i] == end + 1)
                        {
                            end = indices[i];
                        }
                        else
                        {
                            ranges.Add(start == end ? start.ToString() : $"{start}-{end}");
                            start = end = indices[i];
                        }
                    }
                    ranges.Add(start == end ? start.ToString() : $"{start}-{end}");
                }

                summaries.Add($"{purposeName} {string.Join(", ", ranges)}");
            }

            string summaryText = string.Join(" | ", summaries);
            Log.Instance.Trace($"[Probe Zones] Total Lamps: {lamps.Count} ({summaryText})");
            System.Diagnostics.Debug.WriteLine($"[Probe Zones] Total Lamps: {lamps.Count} ({summaryText})");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[Probe Zones] Failed to retrieve lamp info: {ex.Message}");
        }
    }

    private async void ProbeLamps_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        if (btn == null) return;

        if (_probeCts != null)
        {
            CancelProbe(btn);
            return;
        }

        _isProbing = true;
        _probeCts = new CancellationTokenSource();
        var token = _probeCts.Token;
        btn.Content = "STOP PROBE";

        LogZoneSummaries();

        var allIndices = _controlMap.Keys.ToList();
        foreach (var idx in allIndices)
        {
            _lampEffectMap[idx] = _globalCustomEffect;
            _globalCustomEffect.SetColor(idx, WinUIColor.FromArgb(0, 0, 0, 0));
        }
        _controller.SetEffectForIndices(allIndices, _globalCustomEffect);
        SyncVisualsForIndices(allIndices);

        try
        {
            int maxIndex = allIndices.Count > 0 ? allIndices.Max() : 0;

            for (int i = 0; i <= maxIndex; i++)
            {
                if (token.IsCancellationRequested) break;

                if (i > 0)
                {
                    _globalCustomEffect.SetColor(i - 1, WinUIColor.FromArgb(0, 0, 0, 0));
                    SyncVisualsForIndices(new[] { i - 1 });
                }

                _globalCustomEffect.SetColor(i, WinUIColor.FromArgb(255, 255, 0, 0));
                SyncVisualsForIndices(new[] { i });

                await Task.Delay(500, token);
            }

            _globalCustomEffect.SetColor(maxIndex, WinUIColor.FromArgb(0, 0, 0, 0));
            SyncVisualsForIndices(new[] { maxIndex });
        }
        catch (TaskCanceledException) { }
        finally
        {
            if (_probeCts != null) CancelProbe(btn);
            _isProbing = false;

            RestoreEffectsFromSettings();
            UpdateEffectSelectionUI();
        }
    }

    private void CancelProbe(Button btn)
    {
        _probeCts?.Cancel();
        _probeCts = null;
        btn.Content = Resource.LampArrayRGBKeyboardPage_Probe_Indices;

        var allIndices = _controlMap.Keys.ToList();
        foreach (var idx in allIndices)
        {
            _globalCustomEffect.SetColor(idx, WinUIColor.FromArgb(0, 0, 0, 0));
        }
        SyncVisualsForIndices(allIndices);
    }

    private void OnEffectTick(object? sender, EventArgs e)
    {
        if (_isProbing) return;

        if (_controlMap == null) return;

        foreach (var kvp in _controlMap)
        {
            var idx = kvp.Key;
            var control = kvp.Value;
            var color = _controller.GetCurrentColor(idx);

            if (color.HasValue && color.Value.A > 0)
            {
                control.Color = WpfColor.FromArgb(color.Value.A, color.Value.R, color.Value.G, color.Value.B);
            }
            else
            {
                control.Color = null;
            }
        }
    }

    private void InitializeKeyboardEvents(DependencyObject root)
    {
        foreach (var key in EnumerateKeys(root))
        {
            key.Click -= Key_Click;
            key.Click += Key_Click;
        }
    }

    private IEnumerable<LampArrayZoneControl> EnumerateKeys(DependencyObject? root)
    {
        if (root == null) yield break;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is LampArrayZoneControl zone) yield return zone;
            foreach (var sub in EnumerateKeys(child)) yield return sub;
        }
    }

    private void RestoreEffectsFromSettings()
    {
        var store = _settings.Store;
        bool hasAurora = false;

        var defEffect = store.DefaultEffect is { } defCfg
            ? LampArrayController.EffectFromConfig(defCfg) ?? _defaultEffect
            : _defaultEffect;

        if (defEffect is AuroraSyncEffect)
        {
            defEffect = _globalAuroraEffect;
            hasAurora = true;
        }

        var allIndices = _controlMap.Keys.ToList();
        foreach (var idx in allIndices)
        {
            _lampEffectMap[idx] = defEffect;
        }
        _controller.SetEffectForIndices(allIndices, defEffect);

        foreach (var kvp in store.PerLampEffects)
        {
            var effect = LampArrayController.EffectFromConfig(kvp.Value);
            if (effect != null)
            {
                if (effect is AuroraSyncEffect)
                {
                    effect = _globalAuroraEffect;
                    hasAurora = true;
                }
                _lampEffectMap[kvp.Key] = effect;
                _controller.SetEffectForIndices([kvp.Key], effect);
            }
        }

        if (hasAurora)
        {
            StartScreenCapture();
        }
    }

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json",
            DefaultExt = "json",
            FileName = "lamp_array_profile.json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            _controller.SaveSettings(_settings);
            _settings.ExportToFile(dialog.FileName);
            SnackbarHelper.Show("Export", "Profile exported successfully.", SnackbarType.Success);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Export profile failed.", ex);
            SnackbarHelper.Show("Export", $"Export failed: {ex.Message}", SnackbarType.Error);
        }
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json",
            DefaultExt = "json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            _settings.ImportFromFile(dialog.FileName);

            var store = _settings.Store;
            _controller.Brightness = store.Brightness;
            _controller.Speed = store.Speed;
            _controller.SmoothTransition = store.SmoothTransition;

            RestoreEffectsFromSettings();
            UpdateEffectSelectionUI();

            SnackbarHelper.Show("Import", "Profile imported successfully.", SnackbarType.Success);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Import profile failed.", ex);
            SnackbarHelper.Show("Import", $"Import failed: {ex.Message}", SnackbarType.Error);
        }
    }
}