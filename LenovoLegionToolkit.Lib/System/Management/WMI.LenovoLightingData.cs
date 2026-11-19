using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System.Management;

public static partial class WMI
{
    public static class LenovoLightingData
    {
        public static async Task<int?> GetKeyboardTypeAsync()
        {
            try
            {
                var rows = await WMI.ReadAsync("root\\WMI", $"SELECT Lighting_Type FROM LENOVO_LIGHTING_DATA", properties =>
                {
                    var lightingType = Convert.ToInt32(properties["Lighting_Type"].Value);
                    return lightingType;
                }).ConfigureAwait(false);

                var hasRows = false;

                foreach (var lightingType in rows)
                {
                    hasRows = true;

                    var keyboardType = (lightingType >> 1) & 7;
                    if (keyboardType != 0)
                    {
                        return keyboardType;
                    }
                }

                if (hasRows)
                {
                    Log.Instance.Trace($"No keyboard type reported in lighting data. [lightingTypes={string.Join(", ", rows)}]");

                    return 0;
                }

                Log.Instance.Trace($"No lighting data found. [query=LENOVO_LIGHTING_DATA]");

                return null;
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to get keyboard type. [query=LENOVO_LIGHTING_DATA]", ex);

                return null;
            }
        }

        public static Task<bool> ExistsAsync(int lightingId, int controlInterface, int type) =>
            WMI.ExistsAsync("root\\WMI", $"SELECT * FROM LENOVO_LIGHTING_DATA WHERE Lighting_ID = {lightingId} AND Control_Interface = {controlInterface} AND Lighting_Type = {type}");
    }
}
