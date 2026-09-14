using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32.SafeHandles;
using NeoSmart.AsyncLock;

namespace LenovoLegionToolkit.Lib.System;

public class SpectrumDeviceFactory : IDisposable
{
    private readonly AsyncLock _lock = new();
    private SafeFileHandle? _cachedHandle;

    public bool ForceDisable { get; set; }

    public void Dispose()
    {
        _cachedHandle?.Dispose();
        _cachedHandle = null;
    }

    public async Task<SafeFileHandle?> GetHandleAsync()
    {
        if (ForceDisable)
        {
            Log.Instance.Trace($"Skipping Spectrum keyboard.");

            return null;
        }

        if (_cachedHandle is not null && !_cachedHandle.IsInvalid && !_cachedHandle.IsClosed)
        {
            if (await IsDeviceResponsiveAsync(_cachedHandle).ConfigureAwait(false))
            {
                return _cachedHandle;
            }
        }

        using (await _lock.LockAsync().ConfigureAwait(false))
        {
            if (_cachedHandle is not null && !_cachedHandle.IsInvalid && !_cachedHandle.IsClosed)
            {
                if (await IsDeviceResponsiveAsync(_cachedHandle).ConfigureAwait(false))
                {
                    return _cachedHandle;
                }

                Log.Instance.Trace($"Cached Spectrum keyboard is not responsive, reopening.");
            }

            _cachedHandle?.Dispose();
            _cachedHandle = null;

            _cachedHandle = await Task.Run(async () =>
            {
                var candidates = await Devices.GetSpectrumRGBKeyboardsAsync(true).ConfigureAwait(false);

                Log.Instance.Trace($"Found Spectrum keyboard candidates. [count={candidates.Count}]");

                foreach (var candidate in candidates.Where(candidate => candidate is { IsInvalid: false, IsClosed: false }))
                {
                    if (await TryInitializeDeviceAsync(candidate).ConfigureAwait(false))
                    {
                        foreach (var other in candidates.Where(other => other != candidate))
                        {
                            other.Dispose();
                        }

                        return candidate;
                    }
                    candidate.Dispose();
                }

                Log.Instance.Trace($"No usable Spectrum keyboard found.");

                return null;
            }).ConfigureAwait(false);

            return _cachedHandle;
        }
    }

    private static async Task<bool> IsDeviceResponsiveAsync(SafeFileHandle handle)
    {
        return await Task.Run(() =>
        {
            try
            {
                var buffer = new byte[960];
                buffer[0] = 7;
                return HidUtils.SetFeature(handle, buffer);
            }
            catch
            {
                return false;
            }
        }).ConfigureAwait(false);
    }

    private static async Task<bool> TryInitializeDeviceAsync(SafeFileHandle handle)
    {
        return await Task.Run(() =>
        {
            try
            {
                var input = new LENOVO_SPECTRUM_GET_COMPATIBILITY_REQUEST();

                if (!HidUtils.SetFeature(handle, input))
                {
                    Log.Instance.Trace($"Spectrum keyboard initialization failed. [stage=SetFeature]");

                    return false;
                }

                if (!HidUtils.GetFeature(handle, out LENOVO_SPECTRUM_GET_COMPATIBILITY_RESPONSE output))
                {
                    Log.Instance.Trace($"Spectrum keyboard initialization failed. [stage=GetFeature]");

                    return false;
                }

                if (!output.IsCompatible)
                {
                    Log.Instance.Trace($"Spectrum keyboard is not compatible.");

                    return false;
                }

                Log.Instance.Trace($"Spectrum keyboard initialized.");

                return true;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to initialize Spectrum keyboard.", ex);

                return false;
            }
        }).ConfigureAwait(false);
    }
}