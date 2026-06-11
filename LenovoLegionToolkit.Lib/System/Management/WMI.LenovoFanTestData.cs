using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;

// ReSharper disable InconsistentNaming
// ReSharper disable StringLiteralTypo

namespace LenovoLegionToolkit.Lib.System.Management;

public static partial class WMI
{
    public static class LenovoFanTestData
    {
        public static Task<bool> ExistsAsync(int fanId) =>
            WMI.ExistsAsync("root\\WMI", $"SELECT * FROM LENOVO_FAN_TEST_DATA WHERE FanId = {fanId}");

        public static async Task<int> GetFanMinSpeedAsync(int fanId)
        {
            var results = await WMI.ReadAsync("root\\WMI",
                $"SELECT * FROM LENOVO_FAN_TEST_DATA",
                pdc =>
                {
                    var fanIds = (uint[]?)pdc["FanId"].Value ?? [];
                    var fanMaxSpeeds = (uint[]?)pdc["FanMin"].Value ?? [];

                    var index = Array.IndexOf(fanIds, (uint)fanId);

                    if (index >= 0 && index < fanMaxSpeeds.Length)
                    {
                        return (int)fanMaxSpeeds[index];
                    }

                    return -1;
                }).ConfigureAwait(false);

            return results.FirstOrDefault(speed => speed > -1);
        }

        public static async Task<int> GetFanMaxSpeedAsync(int fanId)
        {
            var results = await WMI.ReadAsync("root\\WMI",
                $"SELECT * FROM LENOVO_FAN_TEST_DATA",
                pdc =>
                {
                    var fanIds = (uint[]?)pdc["FanId"].Value ?? [];
                    var fanMaxSpeeds = (uint[]?)pdc["FanMaxSpeed"].Value ?? [];

                    var index = Array.IndexOf(fanIds, (uint)fanId);

                    if (index >= 0 && index < fanMaxSpeeds.Length)
                    {
                        return (int)fanMaxSpeeds[index];
                    }

                    return -1;
                }).ConfigureAwait(false);

            return results.FirstOrDefault(speed => speed > -1);
        }
    }
}