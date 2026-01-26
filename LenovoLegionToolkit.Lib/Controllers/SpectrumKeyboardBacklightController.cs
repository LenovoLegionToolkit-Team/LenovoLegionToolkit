using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace LenovoLegionToolkit.Lib.Controllers;

public class SpectrumKeyboardBacklightController
{
    public interface IScreenCapture
    {
        void CaptureScreen(ref RGBColor[,] buffer, int width, int height, CancellationToken token);
    }

    private readonly TimeSpan _auroraRefreshInterval = TimeSpan.FromMilliseconds(60);

    private readonly SpecialKeyListener _listener;
    private readonly VantageDisabler _vantageDisabler;
    private readonly IScreenCapture _screenCapture;
    private readonly SpectrumDeviceFactory _deviceFactory;

    private CancellationTokenSource? _auroraRefreshCancellationTokenSource;
    private Task? _auroraRefreshTask;

    private readonly JsonSerializerSettings _jsonSerializerSettings = new()
    {
        Formatting = Formatting.Indented,
        TypeNameHandling = TypeNameHandling.Auto,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Converters = [new StringEnumConverter()]
    };

    public bool ForceDisable
    {
        get => _deviceFactory.ForceDisable;
        set => _deviceFactory.ForceDisable = value;
    }

    public SpectrumKeyboardBacklightController(SpecialKeyListener listener, VantageDisabler vantageDisabler, IScreenCapture screenCapture)
    {
        _listener = listener;
        _vantageDisabler = vantageDisabler;
        _screenCapture = screenCapture;
        _deviceFactory = new SpectrumDeviceFactory();

        _listener.Changed += Listener_Changed;
    }

    private async void Listener_Changed(object? sender, SpecialKeyListener.ChangedEventArgs e)
    {
        if (!await IsSupportedAsync().ConfigureAwait(false))
            return;

        if (await _vantageDisabler.GetStatusAsync().ConfigureAwait(false) == SoftwareStatus.Enabled)
            return;

        switch (e.SpecialKey)
        {
            case SpecialKey.SpectrumPreset1
                or SpecialKey.SpectrumPreset2
                or SpecialKey.SpectrumPreset3
                or SpecialKey.SpectrumPreset4
                or SpecialKey.SpectrumPreset5
                or SpecialKey.SpectrumPreset6:
                {
                    await StartAuroraIfNeededAsync().ConfigureAwait(false);
                    break;
                }
        }
    }

    public async Task<bool> IsSupportedAsync() => await _deviceFactory.GetHandleAsync().ConfigureAwait(false) is not null;

    public async Task<(SpectrumLayout, KeyboardLayout, HashSet<ushort>)> GetKeyboardLayoutAsync()
    {
        Log.Instance.Trace($"Checking keyboard layout...");

        var (width, height, keys) = await ReadAllKeyCodesAsync().ConfigureAwait(false);
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Width: {width} Height: {height} Keys: {string.Join(", ", keys)}");

        if (mi.Properties.HasSpectrumProfileSwitchingBug)
        {
            return (SpectrumLayout.KeyboardOnly, KeyboardLayout.Keyboard24Zone, keys);
        }

        var spectrumLayout = (width, height) switch
        {
            (22, 9) when mi.Properties.HasAlternativeFullSpectrumLayout => SpectrumLayout.FullAlternative,
            (22, 9) => SpectrumLayout.Full,
            (20, 8) => SpectrumLayout.KeyboardAndFront,
            _ => SpectrumLayout.KeyboardOnly
        };

        KeyboardLayout keyboardLayout;
        if (keys.Contains(0xA9))
            keyboardLayout = KeyboardLayout.Jis;
        else if (keys.Contains(0xA8))
            keyboardLayout = KeyboardLayout.Iso;
        else
            keyboardLayout = KeyboardLayout.Ansi;

        Log.Instance.Trace($"Layout is {spectrumLayout}, {keyboardLayout}.");

        return (spectrumLayout, keyboardLayout, keys);
    }

    public async Task<int> GetBrightnessAsync()
    {
        var handle = await GetHandleOrThrow().ConfigureAwait(false);

        Log.Instance.Trace($"Getting keyboard brightness...");

        var input = new LENOVO_SPECTRUM_GET_BRIGHTNESS_REQUEST();
        var output = await SetAndGetFeatureAsync<LENOVO_SPECTRUM_GET_BRIGHTNESS_REQUEST, LENOVO_SPECTRUM_GET_BRIGHTNESS_RESPONSE>(handle, input).ConfigureAwait(false);
        var result = output.Brightness;

        Log.Instance.Trace($"Keyboard brightness is {result}.");

        return result;
    }

    public async Task SetBrightnessAsync(int brightness)
    {
        var handle = await GetHandleOrThrow().ConfigureAwait(false);

        if (brightness is < 0 or > 9)
            throw new InvalidOperationException("Brightness must be between 0 and 9");

        Log.Instance.Trace($"Setting keyboard brightness to: {brightness}.");

        var input = new LENOVO_SPECTRUM_SET_BRIGHTNESS_REQUEST((byte)brightness);
        await SetFeatureAsync(handle, input).ConfigureAwait(false);

        Log.Instance.Trace($"Keyboard brightness set.");
    }

    public async Task<bool> GetLogoStatusAsync()
    {
        var handle = await GetHandleOrThrow().ConfigureAwait(false);

        Log.Instance.Trace($"Getting logo status...");

        var input = new LENOVO_SPECTRUM_GET_LOGO_STATUS();
        var output = await SetAndGetFeatureAsync<LENOVO_SPECTRUM_GET_LOGO_STATUS, LENOVO_SPECTRUM_GET_LOGO_STATUS_RESPONSE>(handle, input).ConfigureAwait(false);
        var result = output.IsOn;

        Log.Instance.Trace($"Logo status is {result}.");

        return result;
    }

    public async Task SetLogoStatusAsync(bool isOn)
    {
        var handle = await GetHandleOrThrow().ConfigureAwait(false);

        Log.Instance.Trace($"Setting logo status to: {isOn}.");

        var input = new LENOVO_SPECTRUM_SET_LOGO_STATUS_REQUEST(isOn);
        await SetFeatureAsync(handle, input).ConfigureAwait(false);

        Log.Instance.Trace($"Logo status set.");
    }

    public async Task<int> GetProfileAsync()
    {
        var handle = await GetHandleOrThrow().ConfigureAwait(false);

        Log.Instance.Trace($"Getting keyboard profile...");

        var input = new LENOVO_SPECTRUM_GET_PROFILE_REQUEST();
        var output = await SetAndGetFeatureAsync<LENOVO_SPECTRUM_GET_PROFILE_REQUEST, LENOVO_SPECTRUM_GET_PROFILE_RESPONSE>(handle, input).ConfigureAwait(false);
        var result = output.Profile;

        Log.Instance.Trace($"Keyboard profile is {result}.");

        return result;
    }

    public async Task SetProfileAsync(int profile)
    {
        var handle = await GetHandleOrThrow().ConfigureAwait(false);

        await StopAuroraIfNeededAsync().ConfigureAwait(false);

        if (profile is < 0 or > 6)
            throw new InvalidOperationException("Profile must be between 0 and 6");

        Log.Instance.Trace($"Setting keyboard profile to {profile}...");

        var input = new LENOVO_SPECTRUM_SET_PROFILE_REQUEST((byte)profile);
        await SetFeatureAsync(handle, input).ConfigureAwait(false);

        await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

        Log.Instance.Trace($"Keyboard profile set to {profile}.");

        await StartAuroraIfNeededAsync(profile).ConfigureAwait(false);
    }

    public async Task SetProfileDefaultAsync(int profile)
    {
        var handle = await GetHandleOrThrow().ConfigureAwait(false);

        Log.Instance.Trace($"Setting keyboard profile {profile} to default...");

        Log.Instance.Trace($"Keyboard profile {profile} set to default.");

        var input = new LENOVO_SPECTRUM_SET_PROFILE_DEFAULT_REQUEST((byte)profile);
        await SetFeatureAsync(handle, input).ConfigureAwait(false);
    }

    public async Task SetProfileDescriptionAsync(int profile, SpectrumKeyboardBacklightEffect[] effects)
    {
        var handle = await GetHandleOrThrow().ConfigureAwait(false);

        Log.Instance.Trace($"Setting {effects.Length} effect to keyboard profile {profile}...");

        effects = Compress(effects);
        var bytes = Convert(profile, effects).ToBytes();
        await SetFeatureAsync(handle, bytes).ConfigureAwait(false);

        Log.Instance.Trace($"Set {effects.Length} effect to keyboard profile {profile}.");

        await StartAuroraIfNeededAsync(profile).ConfigureAwait(false);
    }

    public async Task<(int Profile, SpectrumKeyboardBacklightEffect[] Effects)> GetProfileDescriptionAsync(int profile)
    {
        var handle = await GetHandleOrThrow().ConfigureAwait(false);

        Log.Instance.Trace($"Getting effects for keyboard profile {profile}...");

        var input = new LENOVO_SPECTRUM_GET_EFFECT_REQUEST((byte)profile);
        var buffer = await SetAndGetFeatureBytesAsync(handle, input, 960).ConfigureAwait(false);

        var description = LENOVO_SPECTRUM_EFFECT_DESCRIPTION.FromBytes(buffer);
        var result = Convert(description);

        Log.Instance.Trace($"Retrieved {result.Effects.Length} effects for keyboard profile {profile}...");

        return result;
    }

    public async Task ImportProfileDescription(int profile, string jsonPath)
    {
        var json = await File.ReadAllTextAsync(jsonPath).ConfigureAwait(false);
        var effects = JsonConvert.DeserializeObject<SpectrumKeyboardBacklightEffect[]>(json)
                      ?? throw new InvalidOperationException("Couldn't deserialize effects");

        await SetProfileDescriptionAsync(profile, effects).ConfigureAwait(false);
    }

    public async Task ExportProfileDescriptionAsync(int profile, string jsonPath)
    {
        var (_, effects) = await GetProfileDescriptionAsync(profile).ConfigureAwait(false);
        var json = JsonConvert.SerializeObject(effects, _jsonSerializerSettings);
        await File.WriteAllTextAsync(jsonPath, json).ConfigureAwait(false);
    }

    public async Task<bool> StartAuroraIfNeededAsync(int? profile = null)
    {
        await ThrowIfVantageEnabled().ConfigureAwait(false);

        await StopAuroraIfNeededAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Starting Aurora... [profile={profile}]");

        profile ??= await GetProfileAsync().ConfigureAwait(false);
        var (_, effects) = await GetProfileDescriptionAsync(profile.Value).ConfigureAwait(false);

        if (!effects.Any(e => e.Type == SpectrumKeyboardBacklightEffectType.AuroraSync))
        {
            Log.Instance.Trace($"Aurora not needed. [profile={profile}]");

            return false;
        }

        _auroraRefreshCancellationTokenSource = new();
        var token = _auroraRefreshCancellationTokenSource.Token;
        _auroraRefreshTask = Task.Run(() => AuroraRefreshAsync(profile.Value, token), token);

        Log.Instance.Trace($"Aurora started. [profile={profile}]");

        return true;
    }

    public async Task StopAuroraIfNeededAsync()
    {
        await ThrowIfVantageEnabled().ConfigureAwait(false);

        Log.Instance.Trace($"Stopping Aurora...");

        if (_auroraRefreshCancellationTokenSource is not null)
            await _auroraRefreshCancellationTokenSource.CancelAsync().ConfigureAwait(false);

        if (_auroraRefreshTask is not null)
            await _auroraRefreshTask.ConfigureAwait(false);

        _auroraRefreshTask = null;

        Log.Instance.Trace($"Aurora stopped.");
    }

    public async Task<Dictionary<ushort, RGBColor>> GetStateAsync(bool skipVantageCheck = false)
    {
        if (!skipVantageCheck)
            await ThrowIfVantageEnabled().ConfigureAwait(false);

        var handle = await _deviceFactory.GetHandleAsync().ConfigureAwait(false);
        if (handle is null)
            throw new InvalidOperationException(nameof(handle));

        var state = await GetFeatureAsync<LENOVO_SPECTRUM_STATE_RESPONSE>(handle).ConfigureAwait(false);

        var dict = new Dictionary<ushort, RGBColor>();

        foreach (var key in state.Data.Where(k => k.KeyCode > 0))
        {
            var rgb = new RGBColor(key.Color.R, key.Color.G, key.Color.B);
            dict.TryAdd(key.KeyCode, rgb);
        }

        return dict;
    }

    private async Task ThrowIfVantageEnabled()
    {
        var vantageStatus = await _vantageDisabler.GetStatusAsync().ConfigureAwait(false);
        if (vantageStatus == SoftwareStatus.Enabled)
            throw new InvalidOperationException("Can't manage Spectrum keyboard with Vantage enabled");
    }

    private async Task<(int Width, int Height, HashSet<ushort> Keys)> ReadAllKeyCodesAsync()
    {
        var keyMap = await GetKeyMapAsync().ConfigureAwait(false);
        var keyCodes = new HashSet<ushort>(keyMap.Width * keyMap.Height);

        foreach (var keyCode in keyMap.KeyCodes)
            if (keyCode > 0)
                keyCodes.Add(keyCode);

        foreach (var keyCode in keyMap.AdditionalKeyCodes)
            if (keyCode > 0)
                keyCodes.Add(keyCode);

        return (keyMap.Width, keyMap.Height, keyCodes);
    }

    private async Task<KeyMap> GetKeyMapAsync()
    {
        try
        {
            var handle = await _deviceFactory.GetHandleAsync().ConfigureAwait(false);
            if (handle is null)
                return KeyMap.Empty;

            var keyCountResponse = await SetAndGetFeatureAsync<LENOVO_SPECTRUM_GET_KEY_COUNT_REQUEST, LENOVO_SPECTRUM_GET_KEY_COUNT_RESPONSE>(handle, new LENOVO_SPECTRUM_GET_KEY_COUNT_REQUEST()).ConfigureAwait(false);

            var width = keyCountResponse.KeysPerIndex;
            var height = keyCountResponse.Indexes;

            var keyCodes = new ushort[width, height];
            var additionalKeyCodes = new ushort[width];

            for (var y = 0; y < height; y++)
            {
                var keyPageResponse = await SetAndGetFeatureAsync<LENOVO_SPECTRUM_GET_KEY_PAGE_REQUEST, LENOVO_SPECTRUM_GET_KEY_PAGE_RESPONSE>(handle, new LENOVO_SPECTRUM_GET_KEY_PAGE_REQUEST((byte)y)).ConfigureAwait(false);

                for (var x = 0; x < width; x++)
                    keyCodes[x, y] = keyPageResponse.Items[x].KeyCode;
            }

            var secondaryKeyPageResponse = await SetAndGetFeatureAsync<LENOVO_SPECTRUM_GET_KEY_PAGE_REQUEST, LENOVO_SPECTRUM_GET_KEY_PAGE_RESPONSE>(handle, new LENOVO_SPECTRUM_GET_KEY_PAGE_REQUEST(0, true)).ConfigureAwait(false);

            for (var x = 0; x < width; x++)
                additionalKeyCodes[x] = secondaryKeyPageResponse.Items[x].KeyCode;

            return new(width, height, keyCodes, additionalKeyCodes);
        }
        catch
        {
            return KeyMap.Empty;
        }
    }

    private async Task AuroraRefreshAsync(int profile, CancellationToken token)
    {
        try
        {
            var settings = IoCContainer.Resolve<SpectrumKeyboardSettings>();

            await ThrowIfVantageEnabled().ConfigureAwait(false);

            var handle = await GetHandleOrThrow().ConfigureAwait(false);

            Log.Instance.Trace($"Aurora refresh starting...");

            var keyMap = await GetKeyMapAsync().ConfigureAwait(false);
            var width = keyMap.Width;
            var height = keyMap.Height;
            var colorBuffer = new RGBColor[width, height];

            await SetFeatureAsync(handle, new LENOVO_SPECTRUM_AURORA_START_STOP_REQUEST(true, (byte)profile)).ConfigureAwait(false);

            var useBoost = settings.Store.AuroraVantageColorBoost;
            
            while (!token.IsCancellationRequested)
            {
                var delay = Task.Delay(_auroraRefreshInterval, token);

                try
                {
                    _screenCapture.CaptureScreen(ref colorBuffer, width, height, token);
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Screen capture failed. Delaying before next refresh...", ex);

                    await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                }

                token.ThrowIfCancellationRequested();

                var items = new List<LENOVO_SPECTRUM_AURORA_ITEM>(width * height);

                var avgR = 0;
                var avgG = 0;
                var avgB = 0;

                for (var x = 0; x < width; x++)
                {
                    for (var y = 0; y < height; y++)
                    {
                        var keyCode = keyMap.KeyCodes[x, y];
                        if (keyCode < 1)
                            continue;

                        var color = colorBuffer[x, y];
                        
                        if (useBoost)
                            color = ApplyVantageColorBoost(color, settings);
                        
                        avgR += color.R;
                        avgG += color.G;
                        avgB += color.B;
                        items.Add(new(keyCode, new(color.R, color.G, color.B)));
                    }
                }

                if (items.Count > 0)
                {
                    avgR /= items.Count;
                    avgG /= items.Count;
                    avgB /= items.Count;
                }

                for (var x = 0; x < width; x++)
                {
                    var keyCode = keyMap.AdditionalKeyCodes[x];
                    if (keyCode < 1)
                        continue;

                    items.Add(new(keyCode, new((byte)avgR, (byte)avgB, (byte)avgG)));
                }

                token.ThrowIfCancellationRequested();

                await SetFeatureAsync(handle, new LENOVO_SPECTRUM_AURORA_SEND_BITMAP_REQUEST([.. items]).ToBytes()).ConfigureAwait(false);

                await delay.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unexpected exception while refreshing Aurora.", ex);
        }
        finally
        {
            var handle = await _deviceFactory.GetHandleAsync().ConfigureAwait(false);
            if (handle is not null)
            {
                var currentProfile = await GetProfileAsync().ConfigureAwait(false);
                await SetFeatureAsync(handle, new LENOVO_SPECTRUM_AURORA_START_STOP_REQUEST(false, (byte)currentProfile)).ConfigureAwait(false);
            }

            Log.Instance.Trace($"Aurora refresh stopped.");
        }
    }

    private async Task<SafeFileHandle> GetHandleOrThrow()
    {
        await ThrowIfVantageEnabled().ConfigureAwait(false);
        var handle = await _deviceFactory.GetHandleAsync().ConfigureAwait(false);
        return handle ?? throw new InvalidOperationException("Spectrum Device not found");
    }

    private static async Task<TOut> SetAndGetFeatureAsync<TIn, TOut>(SafeHandle handle, TIn input) where TIn : notnull where TOut : struct
    {
        return await Task.Run(() =>
        {
            if (!HidUtils.SetFeature(handle, input))
                throw new InvalidOperationException($"Failed to set feature {typeof(TIn).Name}");
            if (!HidUtils.GetFeature(handle, out TOut output))
                throw new InvalidOperationException($"Failed to get feature {typeof(TOut).Name}");
            return output;
        }).ConfigureAwait(false);
    }

    private static async Task<byte[]> SetAndGetFeatureBytesAsync<TIn>(SafeHandle handle, TIn input, int size) where TIn : notnull
    {
        return await Task.Run(() =>
        {
            if (!HidUtils.SetFeature(handle, input))
                throw new InvalidOperationException($"Failed to set feature {typeof(TIn).Name}");
            if (!HidUtils.GetFeature(handle, out byte[] output, size))
                throw new InvalidOperationException($"Failed to get feature bytes");
            return output;
        }).ConfigureAwait(false);
    }

    private static async Task SetFeatureAsync<T>(SafeHandle handle, T data) where T : notnull
    {
        var success = await Task.Run(() => HidUtils.SetFeature(handle, data)).ConfigureAwait(false);
        if (!success)
            throw new InvalidOperationException($"Failed to set feature {typeof(T).Name}");
    }

    private static async Task<T> GetFeatureAsync<T>(SafeHandle handle) where T : struct
    {
        return await Task.Run(() =>
        {
            if (!HidUtils.GetFeature(handle, out T result))
                throw new InvalidOperationException($"Failed to get feature {typeof(T).Name}");
            return result;
        }).ConfigureAwait(false);
    }

    private static SpectrumKeyboardBacklightEffect[] Compress(SpectrumKeyboardBacklightEffect[] effects)
    {
        if (effects.Any(e => e.Type.IsAllLightsEffect()))
            return [effects.Last(e => e.Type.IsAllLightsEffect())];

        var usedKeyCodes = new HashSet<ushort>();
        var newEffects = new List<SpectrumKeyboardBacklightEffect>();

        foreach (var effect in effects.Reverse())
        {
            if (effect.Type.IsWholeKeyboardEffect() && usedKeyCodes.Intersect(effect.Keys).Any())
                continue;

            var newKeyCodes = effect.Keys.Except(usedKeyCodes).ToArray();

            foreach (var keyCode in newKeyCodes)
                usedKeyCodes.Add(keyCode);

            if (newKeyCodes.IsEmpty())
                continue;

            var newEffect = new SpectrumKeyboardBacklightEffect(effect.Type,
                effect.Speed,
                effect.Direction,
                effect.ClockwiseDirection,
                effect.Colors,
                newKeyCodes);

            newEffects.Add(newEffect);
        }

        newEffects.Reverse();
        return [.. newEffects];
    }

    private static (int Profile, SpectrumKeyboardBacklightEffect[] Effects) Convert(LENOVO_SPECTRUM_EFFECT_DESCRIPTION description)
    {
        var profile = description.Profile;
        var successfulEffects = new List<SpectrumKeyboardBacklightEffect>();

        foreach (var effect in description.Effects)
        {
            try
            {
                var convertedEffect = Convert(effect);
                successfulEffects.Add(convertedEffect);
            }
            catch (ArgumentException ex)
            {
                Log.Instance.Trace($"Failed to convert effect: {ex.Message}");
            }
        }
        return (profile, successfulEffects.ToArray());
    }

    private static SpectrumKeyboardBacklightEffect Convert(LENOVO_SPECTRUM_EFFECT effect)
    {
        var effectType = effect.EffectHeader.EffectType switch
        {
            LENOVO_SPECTRUM_EFFECT_TYPE.Always => SpectrumKeyboardBacklightEffectType.Always,
            LENOVO_SPECTRUM_EFFECT_TYPE.LegionAuraSync => SpectrumKeyboardBacklightEffectType.AuroraSync,
            LENOVO_SPECTRUM_EFFECT_TYPE.AudioBounceLighting => SpectrumKeyboardBacklightEffectType.AudioBounce,
            LENOVO_SPECTRUM_EFFECT_TYPE.AudioRippleLighting => SpectrumKeyboardBacklightEffectType.AudioRipple,
            LENOVO_SPECTRUM_EFFECT_TYPE.ColorChange => SpectrumKeyboardBacklightEffectType.ColorChange,
            LENOVO_SPECTRUM_EFFECT_TYPE.ColorPulse => SpectrumKeyboardBacklightEffectType.ColorPulse,
            LENOVO_SPECTRUM_EFFECT_TYPE.ColorWave => SpectrumKeyboardBacklightEffectType.ColorWave,
            LENOVO_SPECTRUM_EFFECT_TYPE.Rain => SpectrumKeyboardBacklightEffectType.Rain,
            LENOVO_SPECTRUM_EFFECT_TYPE.ScrewRainbow => SpectrumKeyboardBacklightEffectType.RainbowScrew,
            LENOVO_SPECTRUM_EFFECT_TYPE.RainbowWave => SpectrumKeyboardBacklightEffectType.RainbowWave,
            LENOVO_SPECTRUM_EFFECT_TYPE.Ripple => SpectrumKeyboardBacklightEffectType.Ripple,
            LENOVO_SPECTRUM_EFFECT_TYPE.Smooth => SpectrumKeyboardBacklightEffectType.Smooth,
            LENOVO_SPECTRUM_EFFECT_TYPE.TypeLighting => SpectrumKeyboardBacklightEffectType.Type,
            _ => throw new ArgumentException("Unsupported Lenovo Spectrum Effect Preset.")
        };
        
        var useVantageColorBoost = effectType == SpectrumKeyboardBacklightEffectType.AuroraSync 
                                   && effect.EffectHeader.Speed == LENOVO_SPECTRUM_SPEED.Speed3;

        var speed = effect.EffectHeader.Speed switch
        {
            LENOVO_SPECTRUM_SPEED.Speed1 => SpectrumKeyboardBacklightSpeed.Speed1,
            LENOVO_SPECTRUM_SPEED.Speed2 => SpectrumKeyboardBacklightSpeed.Speed2,
            LENOVO_SPECTRUM_SPEED.Speed3 => SpectrumKeyboardBacklightSpeed.Speed3,
            _ => SpectrumKeyboardBacklightSpeed.None
        };

        var direction = effect.EffectHeader.Direction switch
        {
            LENOVO_SPECTRUM_DIRECTION.LeftToRight => SpectrumKeyboardBacklightDirection.LeftToRight,
            LENOVO_SPECTRUM_DIRECTION.RightToLeft => SpectrumKeyboardBacklightDirection.RightToLeft,
            LENOVO_SPECTRUM_DIRECTION.BottomToTop => SpectrumKeyboardBacklightDirection.BottomToTop,
            LENOVO_SPECTRUM_DIRECTION.TopToBottom => SpectrumKeyboardBacklightDirection.TopToBottom,
            _ => SpectrumKeyboardBacklightDirection.None
        };

        var clockwiseDirection = effect.EffectHeader.ClockwiseDirection switch
        {
            LENOVO_SPECTRUM_CLOCKWISE_DIRECTION.Clockwise => SpectrumKeyboardBacklightClockwiseDirection.Clockwise,
            LENOVO_SPECTRUM_CLOCKWISE_DIRECTION.CounterClockwise => SpectrumKeyboardBacklightClockwiseDirection.CounterClockwise,
            _ => SpectrumKeyboardBacklightClockwiseDirection.None
        };

        var colors = effect.Colors.Select(c => new RGBColor(c.R, c.G, c.B)).ToArray();

        var keys = effect.KeyCodes;
        if (effect.KeyCodes is [0x65])
            keys = [];

        return new(effectType, speed, direction, clockwiseDirection, colors, keys, useVantageColorBoost);
    }

    private static LENOVO_SPECTRUM_EFFECT_DESCRIPTION Convert(int profile, SpectrumKeyboardBacklightEffect[] effects)
    {
        var header = new LENOVO_SPECTRUM_HEADER(LENOVO_SPECTRUM_OPERATION_TYPE.EffectChange, 0);
        var convertedEffects = new List<LENOVO_SPECTRUM_EFFECT>();

        if (effects != null)
        {
            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                try
                {
                    var convertedEffect = Convert(i, effect);
                    convertedEffects.Add(convertedEffect);
                }
                catch (ArgumentException ex)
                {
                    Log.Instance.Trace($"Failed to convert effect: {ex.Message}");
                }
            }
        }

        var result = new LENOVO_SPECTRUM_EFFECT_DESCRIPTION(header, (byte)profile, convertedEffects.ToArray());
        return result;
    }

    private static LENOVO_SPECTRUM_EFFECT Convert(int index, SpectrumKeyboardBacklightEffect effect)
    {
        var effectType = effect.Type switch
        {
            SpectrumKeyboardBacklightEffectType.Always => LENOVO_SPECTRUM_EFFECT_TYPE.Always,
            SpectrumKeyboardBacklightEffectType.AuroraSync => LENOVO_SPECTRUM_EFFECT_TYPE.LegionAuraSync,
            SpectrumKeyboardBacklightEffectType.AudioBounce => LENOVO_SPECTRUM_EFFECT_TYPE.AudioBounceLighting,
            SpectrumKeyboardBacklightEffectType.AudioRipple => LENOVO_SPECTRUM_EFFECT_TYPE.AudioRippleLighting,
            SpectrumKeyboardBacklightEffectType.ColorChange => LENOVO_SPECTRUM_EFFECT_TYPE.ColorChange,
            SpectrumKeyboardBacklightEffectType.ColorPulse => LENOVO_SPECTRUM_EFFECT_TYPE.ColorPulse,
            SpectrumKeyboardBacklightEffectType.ColorWave => LENOVO_SPECTRUM_EFFECT_TYPE.ColorWave,
            SpectrumKeyboardBacklightEffectType.Rain => LENOVO_SPECTRUM_EFFECT_TYPE.Rain,
            SpectrumKeyboardBacklightEffectType.RainbowScrew => LENOVO_SPECTRUM_EFFECT_TYPE.ScrewRainbow,
            SpectrumKeyboardBacklightEffectType.RainbowWave => LENOVO_SPECTRUM_EFFECT_TYPE.RainbowWave,
            SpectrumKeyboardBacklightEffectType.Ripple => LENOVO_SPECTRUM_EFFECT_TYPE.Ripple,
            SpectrumKeyboardBacklightEffectType.Smooth => LENOVO_SPECTRUM_EFFECT_TYPE.Smooth,
            SpectrumKeyboardBacklightEffectType.Type => LENOVO_SPECTRUM_EFFECT_TYPE.TypeLighting,
            _ => throw new ArgumentException("Unsupported Spectrum Keyboard Effect Preset.")
        };

        var speed = effect.Speed switch
        {
            SpectrumKeyboardBacklightSpeed.Speed1 => LENOVO_SPECTRUM_SPEED.Speed1,
            SpectrumKeyboardBacklightSpeed.Speed2 => LENOVO_SPECTRUM_SPEED.Speed2,
            SpectrumKeyboardBacklightSpeed.Speed3 => LENOVO_SPECTRUM_SPEED.Speed3,
            _ => LENOVO_SPECTRUM_SPEED.None
        };
        
        if (effect is { Type: SpectrumKeyboardBacklightEffectType.AuroraSync, UseVantageColorBoost: true })
            speed = LENOVO_SPECTRUM_SPEED.Speed3;

        var direction = effect.Direction switch
        {
            SpectrumKeyboardBacklightDirection.LeftToRight => LENOVO_SPECTRUM_DIRECTION.LeftToRight,
            SpectrumKeyboardBacklightDirection.RightToLeft => LENOVO_SPECTRUM_DIRECTION.RightToLeft,
            SpectrumKeyboardBacklightDirection.BottomToTop => LENOVO_SPECTRUM_DIRECTION.BottomToTop,
            SpectrumKeyboardBacklightDirection.TopToBottom => LENOVO_SPECTRUM_DIRECTION.TopToBottom,
            _ => LENOVO_SPECTRUM_DIRECTION.None
        };

        var clockwiseDirection = effect.ClockwiseDirection switch
        {
            SpectrumKeyboardBacklightClockwiseDirection.Clockwise => LENOVO_SPECTRUM_CLOCKWISE_DIRECTION.Clockwise,
            SpectrumKeyboardBacklightClockwiseDirection.CounterClockwise => LENOVO_SPECTRUM_CLOCKWISE_DIRECTION.CounterClockwise,
            _ => LENOVO_SPECTRUM_CLOCKWISE_DIRECTION.None
        };

        var colorMode = effect.Type switch
        {
            SpectrumKeyboardBacklightEffectType.Always => LENOVO_SPECTRUM_COLOR_MODE.ColorList,
            SpectrumKeyboardBacklightEffectType.ColorChange when effect.Colors.Length != 0 => LENOVO_SPECTRUM_COLOR_MODE.ColorList,
            SpectrumKeyboardBacklightEffectType.ColorPulse when effect.Colors.Length != 0 => LENOVO_SPECTRUM_COLOR_MODE.ColorList,
            SpectrumKeyboardBacklightEffectType.ColorWave when effect.Colors.Length != 0 => LENOVO_SPECTRUM_COLOR_MODE.ColorList,
            SpectrumKeyboardBacklightEffectType.Rain when effect.Colors.Length != 0 => LENOVO_SPECTRUM_COLOR_MODE.ColorList,
            SpectrumKeyboardBacklightEffectType.Smooth when effect.Colors.Length != 0 => LENOVO_SPECTRUM_COLOR_MODE.ColorList,
            SpectrumKeyboardBacklightEffectType.Ripple when effect.Colors.Length != 0 => LENOVO_SPECTRUM_COLOR_MODE.ColorList,
            SpectrumKeyboardBacklightEffectType.Type when effect.Colors.Length != 0 => LENOVO_SPECTRUM_COLOR_MODE.ColorList,
            SpectrumKeyboardBacklightEffectType.ColorChange => LENOVO_SPECTRUM_COLOR_MODE.RandomColor,
            SpectrumKeyboardBacklightEffectType.ColorPulse => LENOVO_SPECTRUM_COLOR_MODE.RandomColor,
            SpectrumKeyboardBacklightEffectType.ColorWave => LENOVO_SPECTRUM_COLOR_MODE.RandomColor,
            SpectrumKeyboardBacklightEffectType.Rain => LENOVO_SPECTRUM_COLOR_MODE.RandomColor,
            SpectrumKeyboardBacklightEffectType.Smooth => LENOVO_SPECTRUM_COLOR_MODE.RandomColor,
            SpectrumKeyboardBacklightEffectType.Ripple => LENOVO_SPECTRUM_COLOR_MODE.RandomColor,
            SpectrumKeyboardBacklightEffectType.Type => LENOVO_SPECTRUM_COLOR_MODE.RandomColor,
            _ => LENOVO_SPECTRUM_COLOR_MODE.None
        };

        var header = new LENOVO_SPECTRUM_EFFECT_HEADER(effectType, speed, direction, clockwiseDirection, colorMode);
        var colors = effect.Colors.Select(c => new LENOVO_SPECTRUM_COLOR(c.R, c.G, c.B)).ToArray();
        var keys = effect.Type.IsAllLightsEffect() ? [0x65] : effect.Keys;
        var result = new LENOVO_SPECTRUM_EFFECT(header, index + 1, colors, keys);
        return result;
    }
    
    private static RGBColor ApplyVantageColorBoost(RGBColor color, SpectrumKeyboardSettings settings)
    {
        var vWhite = settings.Store.AuroraVantageColorBoostWhite;
        var boostFloor = settings.Store.AuroraVantageColorBoostFloor;
        var boostTarget = settings.Store.AuroraVantageColorBoostTarget;
        var brightnessBoostFactor = settings.Store.AuroraVantageColorBoostBrightnessFactor;

        int r = color.R;
        int g = color.G;
        int b = color.B;

        var maxC = Math.Max(r, Math.Max(g, b));
        var minC = Math.Min(r, Math.Min(g, b));

        if (maxC < boostFloor)
        {
            return new RGBColor(0, 0, 0);
        }
        
        var originalMaxC = maxC;

        if (maxC < boostTarget)
        {
            var scale = (double)boostTarget / maxC;
            r = Math.Min(255, (int)Math.Round(r * scale));
            g = Math.Min(255, (int)Math.Round(g * scale));
            b = Math.Min(255, (int)Math.Round(b * scale));

            maxC = Math.Max(r, Math.Max(g, b));
            minC = Math.Min(r, Math.Min(g, b));
        }

        var delta = maxC - minC;

        if (delta * 8 < maxC)
        {
            var grayValue = Math.Max(originalMaxC, boostFloor);
            return grayValue >= vWhite 
                ? new RGBColor(255, 255, 255) 
                : new RGBColor((byte)grayValue, (byte)grayValue, (byte)grayValue);
        }

        double h;
        if (maxC == r)
        {
            h = (g - b) * 60.0 / delta;
            if (h < 0) h += 360;
        }
        else if (maxC == g)
        {
            h = (b - r) * 60.0 / delta + 120;
        }
        else
        {
            h = (r - g) * 60.0 / delta + 240;
        }

        var hIdx = (int)Math.Clamp(Math.Round(h), 0, 360);
        var saturatedColor = AuroraColorUtils.HueToRGBLut[hIdx];
        
        double outputBrightness;
        if (originalMaxC >= boostTarget)
        {
            outputBrightness = 255.0;
        }
        else
        {
            var minOutput = boostFloor + (255 - boostFloor) * brightnessBoostFactor;
            var t = (double)(originalMaxC - boostFloor) / (boostTarget - boostFloor);
            outputBrightness = minOutput + t * (255 - minOutput);
        }
        
        var scaleFactor = outputBrightness / 255.0;
        return new RGBColor(
            (byte)Math.Round(saturatedColor.R * scaleFactor),
            (byte)Math.Round(saturatedColor.G * scaleFactor),
            (byte)Math.Round(saturatedColor.B * scaleFactor)
        );
    }
}