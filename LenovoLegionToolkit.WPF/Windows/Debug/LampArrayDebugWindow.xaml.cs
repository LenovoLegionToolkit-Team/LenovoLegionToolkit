using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Utils.LampEffects;
using LenovoLegionToolkit.WPF.Controls.LampArray;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.Lights;
using Windows.System;
using Color = Windows.UI.Color;
using WpfColor = System.Windows.Media.Color;

namespace LenovoLegionToolkit.WPF.Windows.Debug
{
    public partial class LampArrayDebugWindow : Window
    {
        private readonly LampArrayPreviewController _controller;
        private readonly DispatcherTimer _effectTimer;
        private CancellationTokenSource? _probeCts;
        private Color _selectedColor = Color.FromArgb(255, 255, 0, 128);
        private readonly Dictionary<LampArrayZoneControl, int[]> _keyToIndexMap = new();
        private bool _isCustomMode = false;

        public LampArrayDebugWindow()
        {
            InitializeComponent();
            _controller = new LampArrayPreviewController();

            _effectTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _effectTimer.Tick += OnEffectTick;

            this.Loaded += OnLoaded;
            this.Closed += OnClosed;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Log("Initializing LampArray Preview Controller...");
            await _controller.StartAsync();

            _controller.AvailabilityChanged += (s, args) =>
            {
                Log($"LampArray Available: {_controller.IsAvailable} (Lamp Count: {args.LampCount})");
                if (_controller.IsAvailable)
                {
                    Dispatcher.InvokeAsync(async () => {
                        await Task.Delay(500);
                        InitializeKeyMapping();
                    });
                }
            };

            InitializeKeyboardEvents();
            if (_controller.IsAvailable) InitializeKeyMapping();

            EffectSelect.SelectedIndex = 3;
            _effectTimer.Start();
        }

        private async void ProbeLamps_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;

            if (_probeCts != null)
            {
                _probeCts.Cancel();
                _probeCts = null;
                btn.Content = "Probe Indices (One by One)";
                Log("Stopped.");
                _controller.SetAllLampsColor(Color.FromArgb(255, 0, 0, 0));
                return;
            }

            _probeCts = new CancellationTokenSource();
            var token = _probeCts.Token;
            btn.Content = "STOP PROBE";

            var lamps = _controller.GetLamps().OrderBy(l => l.Info.Index).ToList();
            var allIndices = lamps.Select(l => l.Info.Index).ToList();

            Log($"Starting probe for {allIndices.Count} indices...");

            _controller.SetAllLampsColor(Color.FromArgb(255, 0, 0, 0));

            try
            {
                foreach (var index in allIndices)
                {
                    if (token.IsCancellationRequested) break;

                    Log($">>> LIGHTING UP INDEX: {index}");

                    _controller.SetAllLampsColor(Color.FromArgb(255, 0, 0, 0));

                    var dict = new Dictionary<int, Color>
                    {
                        { index, Color.FromArgb(255, 255, 0, 0) } // Red
                    };
                    _controller.SetLampColors(dict);

                    await Task.Delay(500, token);
                }
            }
            catch (TaskCanceledException) { /* Ignore */ }
            finally
            {
                if (_probeCts != null)
                {
                    _probeCts = null;
                    btn.Content = "Probe Indices (One by One)";
                    Log("Finished.");
                    _controller.SetAllLampsColor(Color.FromArgb(255, 0, 0, 0));
                }
            }
        }

        private void InitializeKeyboardEvents()
        {
            var buttons = EnumerateKeys(VisualKeyboard);
            foreach (var button in buttons)
            {
                button.Click += KeyButton_Click;
            }
            Log("UI Keyboard events hooked.");
        }

        private void InitializeKeyMapping()
        {
            _keyToIndexMap.Clear();

            foreach (var button in EnumerateKeys(VisualKeyboard))
            {
                int targetIndex = button.KeyCode;
                _keyToIndexMap[button] = new[] { targetIndex };
            }

            Log($"DIRECT MODE ENABLED. KeyCode is treated as Index directly.");
            Log($"Mapped {_keyToIndexMap.Count} keys.");
        }

        private void KeyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is LampArrayZoneControl button && _isCustomMode)
            {
                if (_keyToIndexMap.TryGetValue(button, out var indices) && indices.Length > 0)
                {
                    Log($"Setting {indices.Length} Indices [{string.Join(",", indices)}] for Key {button.KeyCode} to selected color.");

                    var colorDict = new Dictionary<int, Color>();
                    foreach (var idx in indices)
                    {
                        colorDict[idx] = _selectedColor;
                    }

                    _controller.SetLampColors(colorDict);
                    button.Color = WpfColor.FromArgb(_selectedColor.A, _selectedColor.R, _selectedColor.G, _selectedColor.B);
                }
                else
                {
                    Log($"Warning: No hardware index found for KeyCode {button.KeyCode}");
                }
            }
        }

        private void OnEffectTick(object sender, EventArgs e)
        {
            if (!_isCustomMode) _controller.UpdateEffect();
        }

        private void EffectSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_controller == null) return;
            _isCustomMode = EffectSelect.SelectedIndex == 8;
            CustomModePanel.Visibility = _isCustomMode ? Visibility.Visible : Visibility.Collapsed;

            if (!_isCustomMode)
            {
                ILampEffect effect = EffectSelect.SelectedIndex switch
                {
                    0 => new StaticEffect(_selectedColor),
                    1 => new BreatheEffect(_selectedColor, 2.0),
                    3 => new RainbowEffect(4.0, true),
                    _ => new RainbowEffect(4.0, true)
                };
                _controller.ApplyEffect(effect);
                Log($"Effect Applied: {effect.Name}");
            }
        }

        private void BrightnessSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            if (sender is Slider slider)
            {
                if (_controller != null) _controller.Brightness = slider.Value / 100.0;
                if (BrightnessValue != null) BrightnessValue.Text = $"Brightness: {slider.Value:F0}%";
            }
        }

        private void SpeedSlider_ValueChanged(object sender, RoutedEventArgs e)
        {
            if (sender is Slider slider)
            {
                if (_controller != null) _controller.Speed = slider.Value / 100.0;
                if (SpeedValue != null) SpeedValue.Text = $"Speed: {slider.Value:F0}%";
            }
        }

        private void SmoothTransition_Changed(object sender, RoutedEventArgs e)
        {
            if (_controller != null) _controller.SmoothTransition = SmoothTransitionCheckBox.IsChecked ?? true;
        }

        private void SetColor(byte r, byte g, byte b) => _selectedColor = Color.FromArgb(255, r, g, b);
        private void ColorRed_Click(object sender, RoutedEventArgs e) => SetColor(255, 0, 0);
        private void ColorWhite_Click(object sender, RoutedEventArgs e) => SetColor(255, 255, 255);

        private void ClearCustomColors_Click(object sender, RoutedEventArgs e)
        {
            _controller.SetAllLampsColor(Color.FromArgb(255, 0, 0, 0));
            foreach (var key in EnumerateKeys(VisualKeyboard)) { key.Color = null; key.IsChecked = false; }
            Log("Custom colors cleared.");
        }

        private void DumpLamps_Click(object sender, RoutedEventArgs e)
        {
            Log("=== FULL DIAGNOSTIC DUMP ===");

            var details = _controller.GetDeviceDetails();
            foreach (var detail in details)
            {
                Log(detail);
            }

            var lamps = _controller.GetLamps().ToList();
            Log($"Total Lamps Found: {lamps.Count}");

            foreach (var item in lamps)
            {
                var i = item.Info;
                Log($"[LAMP] Idx:{i.Index,-3} | Pos:({i.Position.X:F3},{i.Position.Y:F3},{i.Position.Z:F3}) | Purpose:{i.Purposes} | UpdLatency:{i.UpdateLatency}");
            }

            var map = _controller.GetHardwareKeyMap();
            Log("\n=== VIRTUAL KEY MAPPING (Key -> [Indices]) ===");
            foreach (var dev in map)
            {
                Log($"Device: {dev.Key}");
                foreach (var key in dev.Value)
                {
                    Log($"  Key: {key.Key,-15} ({(int)key.Key}) -> Indices: [{string.Join(", ", key.Value)}]");
                }
            }

            Log("\n=== EXPORT ALL INDICES (FOR CONTROL) ===");

            var allIndices = lamps.Select(l => l.Info.Index).OrderBy(x => x).ToList();
            Log($"All ({allIndices.Count}): {string.Join(",", allIndices)}");

            var controlIndices = lamps.Where(l => l.Info.Purposes.HasFlag(LampPurposes.Control)).Select(l => l.Info.Index).ToList();
            Log($"Control ({controlIndices.Count}): {string.Join(",", controlIndices)}");

            var accentIndices = lamps.Where(l => l.Info.Purposes.HasFlag(LampPurposes.Accent)).Select(l => l.Info.Index).ToList();
            Log($"Accent ({accentIndices.Count}): {string.Join(",", accentIndices)}");

            var brandingIndices = lamps.Where(l => l.Info.Purposes.HasFlag(LampPurposes.Branding)).Select(l => l.Info.Index).ToList();
            Log($"Branding ({brandingIndices.Count}): {string.Join(",", brandingIndices)}");

            Log("=== DUMP COMPLETE ===");
        }

        private void Log(string msg) => Dispatcher.InvokeAsync(() => {
            LogOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            LogOutput.ScrollToEnd();
        });

        private IEnumerable<LampArrayZoneControl> EnumerateKeys(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is LampArrayZoneControl zone) yield return zone;
                foreach (var sub in EnumerateKeys(child)) yield return sub;
            }
        }

        private void OnClosed(object sender, EventArgs e) { _effectTimer.Stop(); _controller.Dispose(); }
    }
}