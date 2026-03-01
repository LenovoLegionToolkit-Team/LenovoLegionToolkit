using System;
using System.Runtime.InteropServices;
using System.Timers;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features.WhiteKeyboardBacklight;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Services;

/// <summary>
/// Monitors user inactivity and automatically turns off keyboard backlight
/// after the configured timeout. Restores the previous brightness when user interacts again.
/// </summary>
public class KeyboardBacklightTimeoutService : IDisposable
{
    private readonly ApplicationSettings _settings;
    private readonly WhiteKeyboardBacklightFeature _backlightFeature;
    
    private readonly Timer _checkTimer;
    private WhiteKeyboardBacklightState? _savedState;
    private bool _isBacklightOff;
    private uint _lastInputTime;
    

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public KeyboardBacklightTimeoutService(
        ApplicationSettings settings,
        WhiteKeyboardBacklightFeature backlightFeature)
    {
        _settings = settings;
        _backlightFeature = backlightFeature;
        
        _checkTimer = new Timer(1000); 
        _checkTimer.Elapsed += CheckTimer_Elapsed;
    }

    public void Start()
    {
        if (_settings.Store.KeyboardBacklightTimeout <= 0)
            return;
            
        _lastInputTime = GetIdleTime();
        _checkTimer.Start();
        Log.Instance.Trace($"[BacklightTimeout] Started with {_settings.Store.KeyboardBacklightTimeout}s timeout");
    }

    public void Stop()
    {
        _checkTimer.Stop();
        RestoreBacklight();
        Log.Instance.Trace($"[BacklightTimeout] Stopped");
    }

    private async void CheckTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            int timeoutSeconds = _settings.Store.KeyboardBacklightTimeout;
            if (timeoutSeconds <= 0)
            {
                RestoreBacklight();
                return;
            }

            uint idleMs = GetIdleTime();
            uint idleSeconds = idleMs / 1000;

            if (_isBacklightOff)
            {
                if (idleSeconds < 2)
                {
                    await RestoreBacklightAsync();
                }
            }
            else
            {
                if (idleSeconds >= timeoutSeconds)
                {
                    await TurnOffBacklightAsync();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[BacklightTimeout] Error: {ex.Message}");
        }
    }

    private async Task TurnOffBacklightAsync()
    {
        if (_isBacklightOff) return;

        try
        {
            if (!await _backlightFeature.IsSupportedAsync())
                return;

            _savedState = await _backlightFeature.GetStateAsync();
            
            await _backlightFeature.SetStateAsync(WhiteKeyboardBacklightState.Off);
            _isBacklightOff = true;
            
            Log.Instance.Trace($"[BacklightTimeout] Backlight turned off (was: {_savedState})");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[BacklightTimeout] Failed to turn off: {ex.Message}");
        }
    }

    private async Task RestoreBacklightAsync()
    {
        if (!_isBacklightOff || _savedState == null) return;

        try
        {
            if (!await _backlightFeature.IsSupportedAsync())
                return;

            await _backlightFeature.SetStateAsync(_savedState.Value);
            _isBacklightOff = false;
            
            Log.Instance.Trace($"[BacklightTimeout] Backlight restored to {_savedState}");
            _savedState = null;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[BacklightTimeout] Failed to restore: {ex.Message}");
        }
    }
    
    private void RestoreBacklight()
    {
        if (_isBacklightOff && _savedState != null)
        {
            Task.Run(RestoreBacklightAsync);
        }
    }

    private static uint GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref info))
        {
            return (uint)Environment.TickCount - info.dwTime;
        }
        return 0;
    }

    public void Dispose()
    {
        Stop();
        _checkTimer.Dispose();
    }
}
