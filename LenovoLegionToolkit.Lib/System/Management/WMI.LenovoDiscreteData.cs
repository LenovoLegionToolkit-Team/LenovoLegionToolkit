using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// ReSharper disable InconsistentNaming
// ReSharper disable StringLiteralTypo

namespace LenovoLegionToolkit.Lib.System.Management;

public static partial class WMI
{
    public static class LenovoDiscreteData
    {
        /// <summary>
        /// Returns static discrete capabilities. Cached because hardware capabilities never change.
        /// </summary>
        public static Task<IEnumerable<DiscreteCapability>> ReadAsync() => WMI.ReadCachedAsync("root\\WMI",
            $"SELECT * FROM LENOVO_DISCRETE_DATA",
            pdc =>
            {
                var id = (CapabilityID)Convert.ToInt32(pdc["IDs"].Value);
                var value = Convert.ToInt32(pdc["Value"].Value);
                return new DiscreteCapability(id, value);
            });
    }
}
