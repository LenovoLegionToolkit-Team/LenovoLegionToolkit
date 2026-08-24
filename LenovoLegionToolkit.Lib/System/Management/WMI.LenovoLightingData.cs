using System;
using System.Threading.Tasks;

// ReSharper disable InconsistentNaming
// ReSharper disable StringLiteralTypo

namespace LenovoLegionToolkit.Lib.System.Management;

public static partial class WMI
{
    public static class LenovoLightingData
    {
        public static async Task<int?> GetKeyboardTypeAsync()
        {
            var rows = await WMI.ReadAsync("root\\WMI", $"SELECT Lighting_Id, Lighting_Type FROM LENOVO_LIGHTING_DATA", properties =>
            {
                var lightingId = Convert.ToInt32(properties["Lighting_Id"].Value);
                var lightingType = Convert.ToInt32(properties["Lighting_Type"].Value);
                return (lightingId, lightingType);
            }).ConfigureAwait(false);

            foreach (var (lightingId, lightingType) in rows)
            {
                if ((lightingId & 7) != 0)
                {
                    return (lightingType >> 1) & 7;
                }
            }

            return null;
        }

        public static Task<bool> ExistsAsync(int lightingId, int controlInterface, int type) =>
            WMI.ExistsAsync("root\\WMI", $"SELECT * FROM LENOVO_LIGHTING_DATA WHERE Lighting_ID = {lightingId} AND Control_Interface = {controlInterface} AND Lighting_Type = {type}");
    }
}
