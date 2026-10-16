using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// ReSharper disable InconsistentNaming
// ReSharper disable StringLiteralTypo

namespace LenovoLegionToolkit.Lib.System.Management;

public static partial class WMI
{
    public static class LenovoCapabilityData00
    {
        public static async Task<int?> GetCapabilityAsync(int id)
        {
            var values = await WMI.ReadAsync("root\\WMI",
                $"SELECT Capability FROM LENOVO_CAPABILITY_DATA_00 WHERE IDs = {id}",
                pdc => Convert.ToInt32(pdc["Capability"].Value)).ConfigureAwait(false);
            return values.Select(value => (int?)value).SingleOrDefault();
        }

        public static Task<IEnumerable<CapabilityID>> ReadAsync() => WMI.ReadAsync("root\\WMI",
            $"SELECT * FROM LENOVO_CAPABILITY_DATA_00",
            pdc => (CapabilityID)Convert.ToInt32(pdc["IDs"].Value));
    }
}
