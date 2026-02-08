using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using Windows.System;
using Windows.UI;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.Lib.Utils.LampEffects;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.Controllers;

public class LampArrayPreviewController : IDisposable
{
    private class LampArrayDevice
    {
        public LampArray Device { get; }
        public Dictionary<VirtualKey, List<int>> VirtualKeyToIndex { get; } = new();

        public LampArrayDevice(LampArray device)
        {
            Device = device;
            Log.Instance.Trace($"Initializing device: {device.DeviceId}, Lamp count: {device.LampCount}");

            Log.Instance.Trace($"--- Exporting hardware VirtualKey mapping table ---");
            var mappedKeys = 0;
            for (var vk = 1; vk <= 255; vk++)
            {
                var key = (VirtualKey)vk;
                try
                {
                    var indices = device.GetIndicesForKey(key);
                    if (indices != null && indices.Length > 0)
                    {
                        VirtualKeyToIndex[key] = indices.ToList();
                        mappedKeys++;
                        Log.Instance.Trace(
                            $"Hardware Export: {key}(0x{vk:X2}) -> Lamp Indices [{string.Join(", ", indices)}]");
                    }
                }
                catch
                {
                }
            }

            Log.Instance.Trace($"--- Export complete: Found {mappedKeys} valid key bindings ---");
        }

        public void SetLayout(int width, int height, IEnumerable<(ushort Code, int X, int Y)> keys)
        {
        }
    }

    public IDictionary<string, IDictionary<VirtualKey, List<int>>> GetHardwareKeyMap()
    {
        var result = new Dictionary<string, IDictionary<VirtualKey, List<int>>>();
        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
            {
                var deviceMap = new Dictionary<VirtualKey, List<int>>();
                foreach (var mapKvp in kvp.Value.VirtualKeyToIndex) deviceMap[mapKvp.Key] = mapKvp.Value.ToList();
                result[kvp.Key] = deviceMap;
            }
        }

        return result;
    }

    private readonly AsyncLock _lock = new();
    private readonly Dictionary<string, LampArrayDevice> _lampArrays = [];
    private DeviceWatcher? _watcher;
    private bool _isDisposed;

    private double _brightness = 1.0;
    private double _speed = 1.0;
    private bool _smoothTransition = true;

    private ILampEffect? _currentEffect;
    private ILampEffect? _targetEffect;
    private double _transitionStartTime;
    private double _transitionDuration;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public event EventHandler<LampArrayAvailabilityChangedEventArgs>? AvailabilityChanged;

    public double Brightness
    {
        get => _brightness;
        set => _brightness = Math.Clamp(value, 0.0, 1.0);
    }

    public double Speed
    {
        get => _speed;
        set => _speed = Math.Clamp(value, 0.1, 5.0);
    }

    public bool SmoothTransition
    {
        get => _smoothTransition;
        set => _smoothTransition = value;
    }

    public ILampEffect? CurrentEffect => _currentEffect;

    public bool IsAvailable
    {
        get
        {
            lock (_lampArrays)
            {
                foreach (var kvp in _lampArrays)
                    if (kvp.Value.Device.IsAvailable)
                        return true;
                return false;
            }
        }
    }

    public int LampCount
    {
        get
        {
            lock (_lampArrays)
            {
                foreach (var kvp in _lampArrays)
                    if (kvp.Value.Device.IsAvailable)
                        return kvp.Value.Device.LampCount;
                return 0;
            }
        }
    }

    public async Task StartAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            if (_watcher is not null)
                return;

            Log.Instance.Trace($"Starting LampArray device watcher...");

            var selector = LampArray.GetDeviceSelector();
            _watcher = DeviceInformation.CreateWatcher(selector);

            _watcher.Added += Watcher_Added;
            _watcher.Removed += Watcher_Removed;
            _watcher.EnumerationCompleted += Watcher_EnumerationCompleted;
            _watcher.Start();

            Log.Instance.Trace($"LampArray device watcher started.");
        }
    }

    private void Watcher_EnumerationCompleted(DeviceWatcher sender, object args)
    {
        lock (_lampArrays)
        {
            Log.Instance.Trace($"LampArray device enumeration completed. Devices found: {_lampArrays.Count}");
        }
    }

    public async Task StopAsync()
    {
        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            if (_watcher is null)
                return;

            Log.Instance.Trace($"Stopping LampArray device watcher...");

            _watcher.Added -= Watcher_Added;
            _watcher.Removed -= Watcher_Removed;
            _watcher.EnumerationCompleted -= Watcher_EnumerationCompleted;
            _watcher.Stop();
            _watcher = null;

            lock (_lampArrays)
            {
                foreach (var device in _lampArrays.Values)
                    device.Device.AvailabilityChanged -= LampArray_AvailabilityChanged;
                _lampArrays.Clear();
            }

            Log.Instance.Trace($"LampArray device watcher stopped.");
        }
    }

    public void SetLayout(int width, int height, IEnumerable<(ushort Code, int X, int Y)> keys)
    {
        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays) kvp.Value.SetLayout(width, height, keys);
        }
    }

    public IDictionary<VirtualKey, List<int>> GetVirtualKeyMapping()
    {
        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
                if (kvp.Value.Device.IsAvailable)
                    return new Dictionary<VirtualKey, List<int>>(kvp.Value.VirtualKeyToIndex);
        }

        return new Dictionary<VirtualKey, List<int>>();
    }

    [Obsolete("Use GetVirtualKeyMapping instead.")]
    public Dictionary<ushort, List<int>> GetScanCodeToLampMapping()
    {
        return new Dictionary<ushort, List<int>>();
    }

    public void SetPreviewColor(RGBColor color)
    {
        if (!IsAvailable)
            return;

        var winColor = ToWindowsColor(color);

        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
                if (kvp.Value.Device.IsAvailable)
                    try
                    {
                        kvp.Value.Device.SetColor(winColor);
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Failed to set preview color: {ex.Message}");
                    }
        }
    }

    public void SetPreviewColorsForIndices(int[] indices, RGBColor color)
    {
        if (!IsAvailable)
            return;

        var winColor = ToWindowsColor(color);

        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
                if (kvp.Value.Device.IsAvailable)
                    try
                    {
                        kvp.Value.Device.SetColorsForIndices(CreateColorArray(winColor, indices.Length), indices);
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Failed to set preview colors for indices: {ex.Message}");
                    }
        }
    }

    public void SetPreviewColorsForScanCodes(IEnumerable<ushort> scanCodes, RGBColor color)
    {
        if (!IsAvailable)
            return;

        var winColor = ToWindowsColor(color);

        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
            {
                if (!kvp.Value.Device.IsAvailable)
                    continue;

                try
                {
                    var virtualKeys = new List<VirtualKey>();
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to set preview colors for scan codes: {ex.Message}");
                }
            }
        }
    }

    public void SetPreviewZoneColors(RGBColor[] zoneColors, ILampArrayZoneMapper mapper)
    {
        if (!IsAvailable) return;

        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
            {
                if (!kvp.Value.Device.IsAvailable)
                    continue;

                try
                {
                    for (var zone = 0; zone < zoneColors.Length; zone++)
                    {
                        var indices = mapper.GetLampIndicesForZone(zone, kvp.Value.Device.LampCount);
                        if (indices.Length > 0)
                        {
                            var winColor = ToWindowsColor(zoneColors[zone]);
                            kvp.Value.Device.SetColorsForIndices(CreateColorArray(winColor, indices.Length), indices);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to set preview zone colors: {ex.Message}");
                }
            }
        }
    }

    public IEnumerable<(string DeviceId, LampInfo Info)> GetLamps()
    {
        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
            {
                var device = kvp.Value.Device;
                if (!device.IsAvailable) continue;

                for (var i = 0; i < device.LampCount; i++) yield return (device.DeviceId, device.GetLampInfo(i));
            }
        }
    }

    public List<string> GetDeviceDetails()
    {
        var details = new List<string>();
        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
            {
                var d = kvp.Value.Device;
                var sb = new StringBuilder();
                sb.AppendLine($"Device ID: {d.DeviceId}");
                sb.AppendLine($"  IsAvailable: {d.IsAvailable}");
                sb.AppendLine($"  LampCount: {d.LampCount}");
                sb.AppendLine($"  HardwareVendorId: {d.HardwareVendorId:X4}");
                sb.AppendLine($"  HardwareProductId: {d.HardwareProductId:X4}");
                sb.AppendLine($"  HardwareVersion: {d.HardwareVersion:X4}");
                sb.AppendLine($"  LampArrayKind: {d.LampArrayKind}");
                sb.AppendLine($"  MinUpdateInterval: {d.MinUpdateInterval}");
                sb.AppendLine($"  SupportsVirtualKeys: {d.SupportsVirtualKeys}");

                details.Add(sb.ToString());
            }
        }

        return details;
    }

    public void SetColorForScanCodes(IDictionary<ushort, Color> scanCodeColors)
    {
        if (!IsAvailable)
        {
            Log.Instance.Trace($"SetColorForScanCodes failed: Controller not available.");
            return;
        }

        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
                if (!kvp.Value.Device.IsAvailable)
                {
                    Log.Instance.Trace($"Device {kvp.Key} not available for scan codes.");
                    continue;
                }
        }
    }

    public void SetColorsForKeys(IList<int> scanCodes, IList<Color> colors)
    {
        if (!IsAvailable || scanCodes.Count != colors.Count) return;

        var dict = new Dictionary<ushort, Color>();
        for (var i = 0; i < scanCodes.Count; i++) dict[(ushort)scanCodes[i]] = colors[i];
        SetColorForScanCodes(dict);
    }

    public void SetAllLampsColor(Color color)
    {
        if (!IsAvailable)
        {
            Log.Instance.Trace($"SetAllLampsColor failed: Controller not available.");
            return;
        }

        lock (_lampArrays)
        {
            Log.Instance.Trace($"SetAllLampsColor: RGB({color.R},{color.G},{color.B}) on {_lampArrays.Count} devices.");
            foreach (var kvp in _lampArrays)
            {
                if (!kvp.Value.Device.IsAvailable)
                {
                    Log.Instance.Trace($"Device {kvp.Key} is not available.");
                    continue;
                }

                try
                {
                    Log.Instance.Trace($"Setting all {kvp.Value.Device.LampCount} lamps on {kvp.Key} to color.");
                    kvp.Value.Device.SetColor(color);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to set all lamps color on {kvp.Key}: {ex.Message}");
                }
            }
        }
    }

    public void SetLampColors(Dictionary<int, Color> lampColors)
    {
        if (!IsAvailable)
        {
            Log.Instance.Trace($"SetLampColors failed: Controller not available.");
            return;
        }

        if (lampColors.Count == 0) return;

        lock (_lampArrays)
        {
            Log.Instance.Trace($"SetLampColors: Setting {lampColors.Count} lamp colors.");
            foreach (var kvp in _lampArrays)
            {
                if (!kvp.Value.Device.IsAvailable) continue;

                try
                {
                    var indices = lampColors.Keys.ToArray();
                    var colors = lampColors.Values.ToArray();
                    Log.Instance.Trace($"Applying {indices.Length} colors to {kvp.Key}.");
                    kvp.Value.Device.SetColorsForIndices(colors, indices);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Failed to set lamp colors on {kvp.Key}: {ex.Message}");
                }
            }
        }
    }

    public void SetColorsForVirtualKeys(IDictionary<VirtualKey, Color> keyColors)
    {
        if (!IsAvailable || keyColors == null || keyColors.Count == 0) return;

        var vks = keyColors.Keys.ToArray();
        var colors = keyColors.Values.ToArray();

        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
            {
                if (!kvp.Value.Device.IsAvailable) continue;
                try
                {
                    kvp.Value.Device.SetColorsForKeys(colors, vks);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"SetColorsForKeys failed: {ex.Message}");
                }
            }
        }
    }

    public void SetColorsForAllLamps(IDictionary<ushort, Color> scanCodeColors)
    {
        if (!IsAvailable)
        {
            Log.Instance.Trace($"SetColorsForAllLamps Stopped: Controller not initialized.");
            return;
        }

        if (scanCodeColors == null)
            return;

        Log.Instance.Trace($"Update for all lamps: Dictionary size {scanCodeColors?.Count ?? 0}");

        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
            {
                if (!kvp.Value.Device.IsAvailable)
                {
                    Log.Instance.Trace($"Device {kvp.Key} Unavailable, skipping");
                    continue;
                }

                var device = kvp.Value;
                var lampCount = device.Device.LampCount;
                var fullColors = new Color[lampCount];
                var indices = new int[lampCount];

                Log.Instance.Trace($"Preparing buffer for {kvp.Key} with size {lampCount}");

                for (var i = 0; i < lampCount; i++)
                {
                    fullColors[i] = Color.FromArgb(255, 0, 0, 0);
                    indices[i] = i;
                }

                var mappedCount = 0;
                var keyMatchCount = 0;
                var keyFailCount = 0;

                foreach (var kvp2 in scanCodeColors)
                {
                    var vk = (VirtualKey)kvp2.Key;
                    if (device.VirtualKeyToIndex.TryGetValue(vk, out var indexList))
                    {
                        keyMatchCount++;
                        foreach (var idx in indexList)
                            if (idx >= 0 && idx < lampCount)
                            {
                                fullColors[idx] = kvp2.Value;
                                mappedCount++;
                            }
                            else
                            {
                                Log.Instance.Trace($"Warning: Index out of bounds {idx} Max {lampCount}");
                            }
                    }
                    else
                    {
                        keyFailCount++;
                        if (keyFailCount < 5)
                            Log.Instance.Trace(
                                $"Compatibility skip: Key {vk}(0x{kvp2.Key:X2}) no response from hardware");
                    }
                }

                Log.Instance.Trace(
                    $"Sync Result: Hardware hit {keyMatchCount} physical keys, skipped {keyFailCount} unknown keys. Total lit {mappedCount}/{lampCount} lamps.");
                Log.Instance.Trace($"Sending SetColorsForIndices command...");

                try
                {
                    device.Device.SetColorsForIndices(fullColors, indices);
                    Log.Instance.Trace($"Hardware command sent successfully: {kvp.Key}");
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"!!! Hardware command failed ({kvp.Key}): {ex.Message}");
                    Log.Instance.Trace($"StackTrace: {ex.StackTrace}");
                }
            }
        }
    }

    public void ApplyProfile(IEnumerable<SpectrumKeyboardBacklightEffect> effects)
    {
        if (!IsAvailable)
            return;
    }

    public void ApplyEffect(ILampEffect effect, bool immediate = false)
    {
        if (!IsAvailable) return;

        if (immediate || !_smoothTransition)
        {
            _currentEffect = effect;
            _targetEffect = null;
            effect.Reset();
        }
        else
        {
            _targetEffect = effect;
            _transitionStartTime = _stopwatch.Elapsed.TotalSeconds;
            _transitionDuration = 0.5;
        }
    }

    public void UpdateEffect()
    {
        if (!IsAvailable) return;

        var currentTime = _stopwatch.Elapsed.TotalSeconds * _speed;

        if (_targetEffect != null)
        {
            var elapsed = _stopwatch.Elapsed.TotalSeconds - _transitionStartTime;
            if (elapsed >= _transitionDuration)
            {
                _currentEffect = _targetEffect;
                _targetEffect = null;
                _currentEffect.Reset();
                Log.Instance.Trace($"Transition complete to {_currentEffect.Name}.");
            }
        }

        if (_currentEffect == null) return;

        lock (_lampArrays)
        {
            foreach (var kvp in _lampArrays)
            {
                if (!kvp.Value.Device.IsAvailable) continue;

                try
                {
                    var device = kvp.Value.Device;
                    var lampCount = device.LampCount;
                    var colors = new Color[lampCount];

                    for (var i = 0; i < lampCount; i++)
                    {
                        var lampInfo = device.GetLampInfo(i);
                        var color = _currentEffect.GetColorForLamp(i, currentTime, lampInfo, lampCount);

                        if (_targetEffect != null)
                        {
                            var targetColor = _targetEffect.GetColorForLamp(i, currentTime, lampInfo, lampCount);
                            var elapsed = _stopwatch.Elapsed.TotalSeconds - _transitionStartTime;
                            var t = Math.Clamp(elapsed / _transitionDuration, 0, 1);
                            color = LerpColor(color, targetColor, t);
                        }

                        colors[i] = ApplyBrightness(color, _brightness);
                    }

                    device.SetColorsForIndices(colors, Enumerable.Range(0, lampCount).ToArray());
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Error updating lights on {kvp.Key}: {ex.Message}");
                }
            }
        }
    }

    private static Color ApplyBrightness(Color color, double brightness)
    {
        return Color.FromArgb(
            color.A,
            (byte)(color.R * brightness),
            (byte)(color.G * brightness),
            (byte)(color.B * brightness)
        );
    }

    private static Color LerpColor(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(from.A + (to.A - from.A) * t),
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t)
        );
    }

    private async void Watcher_Added(DeviceWatcher sender, DeviceInformation args)
    {
        try
        {
            Log.Instance.Trace($"LampArray device added: {args.Id}");
            var lampArray = await LampArray.FromIdAsync(args.Id);

            if (lampArray is null)
            {
                Log.Instance.Trace($"Failed to create LampArray instance from {args.Id}");
                return;
            }

            var deviceWrapper = new LampArrayDevice(lampArray);

            lock (_lampArrays)
            {
                if (_lampArrays.TryGetValue(args.Id, out var oldWrapper))
                {
                    Log.Instance.Trace($"Refreshing stale LampArray instance for {args.Id}");
                    oldWrapper.Device.AvailabilityChanged -= LampArray_AvailabilityChanged;
                }

                _lampArrays[args.Id] = deviceWrapper;
                lampArray.AvailabilityChanged += LampArray_AvailabilityChanged;
            }

            Log.Instance.Trace(
                $"LampArray device registered: DeviceId={args.Id}, LampCount={lampArray.LampCount}, IsAvailable={lampArray.IsAvailable}");

            if (lampArray.IsAvailable)
                _ = Task.Run(() =>
                    AvailabilityChanged?.Invoke(this,
                        new LampArrayAvailabilityChangedEventArgs(true, lampArray.LampCount)));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to add LampArray device: {ex.Message}");
        }
    }

    private void Watcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        try
        {
            Log.Instance.Trace($"LampArray device removed: {args.Id}");

            lock (_lampArrays)
            {
                if (_lampArrays.TryGetValue(args.Id, out var device))
                {
                    device.Device.AvailabilityChanged -= LampArray_AvailabilityChanged;
                    _lampArrays.Remove(args.Id);
                }
            }

            AvailabilityChanged?.Invoke(this, new LampArrayAvailabilityChangedEventArgs(IsAvailable, LampCount));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to remove LampArray device: {ex.Message}");
        }
    }

    private void LampArray_AvailabilityChanged(LampArray sender, object args)
    {
        Log.Instance.Trace($"LampArray availability changed: IsAvailable={sender.IsAvailable}");

        _ = Task.Run(() =>
        {
            AvailabilityChanged?.Invoke(this,
                new LampArrayAvailabilityChangedEventArgs(sender.IsAvailable, sender.LampCount));
        });
    }

    private static Color ToWindowsColor(RGBColor color)
    {
        return Color.FromArgb(255, color.R, color.G, color.B);
    }

    private static Color[] CreateColorArray(Color color, int count)
    {
        var colors = new Color[count];
        Array.Fill(colors, color);
        return colors;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _watcher?.Stop();
        _watcher = null;

        lock (_lampArrays)
        {
            foreach (var dev in _lampArrays.Values) dev.Device.AvailabilityChanged -= LampArray_AvailabilityChanged;
            _lampArrays.Clear();
        }

        GC.SuppressFinalize(this);
    }
}

public interface ILampArrayZoneMapper
{
    int ZoneCount { get; }
    int[] GetLampIndicesForZone(int zone, int totalLampCount);
}
