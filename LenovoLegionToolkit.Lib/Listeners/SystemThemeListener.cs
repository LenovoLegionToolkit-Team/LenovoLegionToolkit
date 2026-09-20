using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

public class SystemThemeListener : IListener<EventArgs>
{
    public event EventHandler<EventArgs>? Changed;

    public event EventHandler<DarkModeChangedEventArgs>? DarkModeChanged;

    private IDisposable? _darkModeListener;
    private IDisposable? _colorizationColorListener;

    private RGBColor? _currentRegColor;

    private bool? _isDarkMode;

    private bool _started;

    public Task StartAsync()
    {
        if (_started)
            return Task.CompletedTask;
        try
        {
            _isDarkMode = SystemTheme.IsDarkMode();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't determine the current app mode.", ex);
        }

        _darkModeListener = SystemTheme.GetDarkModeListener(OnDarkModeChanged);
        _colorizationColorListener = SystemTheme.GetColorizationColorListener(OnColorizationColorChanged);

        _started = true;

        return Task.CompletedTask;
    }

    private void OnDarkModeChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            var isDarkMode = SystemTheme.IsDarkMode();
            if (_isDarkMode == isDarkMode)
                return;

            _isDarkMode = isDarkMode;

            DarkModeChanged?.Invoke(this, new DarkModeChangedEventArgs(isDarkMode));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't determine the current app mode.", ex);
        }
    }

    private void OnColorizationColorChanged()
    {
        try
        {
            var color = SystemTheme.GetColorizationColor();

            // Ignore alpha channel transition events
            if (color.Equals(_currentRegColor))
                return;

            _currentRegColor = color;

            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to notify on accent color change.", ex);
        }
    }

    public Task StopAsync()
    {
        _darkModeListener?.Dispose();
        _colorizationColorListener?.Dispose();

        _started = false;

        return Task.CompletedTask;
    }

    public class DarkModeChangedEventArgs(bool isDarkMode) : EventArgs
    {
        public bool IsDarkMode { get; } = isDarkMode;
    }
}
