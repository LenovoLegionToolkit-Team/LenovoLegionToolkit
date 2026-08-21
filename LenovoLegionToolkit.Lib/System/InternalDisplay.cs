using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using WindowsDisplayAPI;

namespace LenovoLegionToolkit.Lib.System;

public static class InternalDisplay
{
    private readonly struct DisplayHolder
    {
        public static readonly DisplayHolder Empty = new();
        private readonly Display? _display;
        private DisplayHolder(Display? display) => _display = display;
        public static implicit operator DisplayHolder(Display? s) => new(s);
        public static implicit operator Display?(DisplayHolder s) => s._display;
    }

    private static readonly SemaphoreSlim Semaphore = new(1, 1);
    private static DisplayHolder? _displayHolder;

    public static void SetNeedsRefresh()
    {
        _displayHolder = null;
        Log.Instance.Trace($"Resetting holder...");
    }

    public static async Task<Display?> GetAsync()
    {
        if (_displayHolder is not null)
            return _displayHolder;

        await Semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_displayHolder is not null)
                return _displayHolder;

            var result = await FindInternalDisplayLogicAsync().ConfigureAwait(false);

            _displayHolder = result;
            return result;
        }
        finally
        {
            Semaphore.Release();
        }
    }

    private static async Task<DisplayHolder> FindInternalDisplayLogicAsync()
    {
        var displays = await Task.Run(() => Display.GetDisplays().ToArray()).ConfigureAwait(false);

        var internalDisplay = FindInternalDisplay(displays);
        if (internalDisplay is not null)
        {
            Log.Instance.Trace($"Found internal display: {internalDisplay.DevicePath}");
            return internalDisplay;
        }

        var aoDisplay = await FindInternalAdvancedOptimusDisplayAsync(displays).ConfigureAwait(false);
        if (aoDisplay is not null)
        {
            Log.Instance.Trace($"Found internal AO display: {aoDisplay.DevicePath}");
            return aoDisplay;
        }

        var primaryDisplay = displays.FirstOrDefault(d => d.DisplayScreen?.IsPrimary == true && !d.IsIndirect);
        if (primaryDisplay is not null)
        {
            Log.Instance.Trace($"Found primary fallback display: {primaryDisplay.DevicePath}");
            return primaryDisplay;
        }

        Log.Instance.Trace($"No internal displays found.");
        return DisplayHolder.Empty;
    }

    public static Display? Get()
    {
        if (_displayHolder is not null) return _displayHolder;
        return Task.Run(async () => await GetAsync().ConfigureAwait(false)).Result;
    }

    private static Display? FindInternalDisplay(IEnumerable<Display> displays)
    {
        return displays.FirstOrDefault(d => d.IsInternal);
    }

    private static async Task<Display?> FindInternalAdvancedOptimusDisplayAsync(IEnumerable<Display> displays)
    {
        var exDpDisplays = displays.Where(di => di.IsExternalDisplayPort).ToArray();

        if (exDpDisplays.Length < 1)
            return null;

        var exDpDisplay = exDpDisplays[0];
        var exDpPathDisplayTarget = exDpDisplay.ToPathDisplayTarget();
        if (exDpPathDisplayTarget is null)
            return null;

        var exDpPortDisplayEdid = exDpPathDisplayTarget.EDIDManufactureId;

        var otherAdapters = DisplayAdapter.GetDisplayAdapters()
            .Where(da => da.DevicePath != exDpDisplay.Adapter.DevicePath)
            .ToArray();

        var queryTasks = otherAdapters.Select(adapter => Task.Run(() =>
        {
            try
            {
                return adapter.GetDisplayDevices();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to query adapter {adapter.DevicePath}", ex);
                return [];
            }
        }));

        var allDevicesResults = await Task.WhenAll(queryTasks).ConfigureAwait(false);

        var sameDeviceIsOnAnotherAdapter = allDevicesResults
            .SelectMany(devices => devices)
            .Select(dd => dd.ToPathDisplayTarget())
            .Any(pdt => pdt is not null && pdt.EDIDManufactureId == exDpPortDisplayEdid && pdt.IsInternal);

        return sameDeviceIsOnAnotherAdapter ? exDpDisplay : null;
    }
}