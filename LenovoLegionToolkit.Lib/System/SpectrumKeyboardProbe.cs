using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

public static class SpectrumKeyboardProbe
{
    public static async Task<KeyMap?> GetKeyMapAsync()
    {
        using var factory = new SpectrumDeviceFactory();
        var handle = await factory.GetHandleAsync().ConfigureAwait(false);
        if (handle is null)
        {
            return null;
        }

        try
        {
            var keyCountResponse = await SetAndGetFeatureAsync<LENOVO_SPECTRUM_GET_KEY_COUNT_REQUEST, LENOVO_SPECTRUM_GET_KEY_COUNT_RESPONSE>(
                handle,
                new LENOVO_SPECTRUM_GET_KEY_COUNT_REQUEST()).ConfigureAwait(false);

            var width = keyCountResponse.KeysPerIndex;
            var height = keyCountResponse.Indexes;

            var keyCodes = new ushort[width, height];
            var additionalKeyCodes = new ushort[width];

            for (var y = 0; y < height; y++)
            {
                var keyPageResponse = await SetAndGetFeatureAsync<LENOVO_SPECTRUM_GET_KEY_PAGE_REQUEST, LENOVO_SPECTRUM_GET_KEY_PAGE_RESPONSE>(
                    handle,
                    new LENOVO_SPECTRUM_GET_KEY_PAGE_REQUEST((byte)y)).ConfigureAwait(false);

                for (var x = 0; x < width; x++)
                {
                    keyCodes[x, y] = keyPageResponse.Items[x].KeyCode;
                }
            }

            var secondaryKeyPageResponse = await SetAndGetFeatureAsync<LENOVO_SPECTRUM_GET_KEY_PAGE_REQUEST, LENOVO_SPECTRUM_GET_KEY_PAGE_RESPONSE>(
                handle,
                new LENOVO_SPECTRUM_GET_KEY_PAGE_REQUEST(0, true)).ConfigureAwait(false);

            for (var x = 0; x < width; x++)
            {
                additionalKeyCodes[x] = secondaryKeyPageResponse.Items[x].KeyCode;
            }

            return new KeyMap(width, height, keyCodes, additionalKeyCodes);
        }
        catch
        {
            return KeyMap.Empty;
        }
    }

    private static async Task<TOut> SetAndGetFeatureAsync<TIn, TOut>(SafeHandle handle, TIn input)
        where TIn : notnull
        where TOut : struct
    {
        return await Task.Run(() =>
        {
            if (!HidUtils.SetFeature(handle, input))
            {
                throw new InvalidOperationException($"Failed to set feature {typeof(TIn).Name}");
            }

            if (!HidUtils.GetFeature(handle, out TOut output))
            {
                throw new InvalidOperationException($"Failed to get feature {typeof(TOut).Name}");
            }

            return output;
        }).ConfigureAwait(false);
    }
}
