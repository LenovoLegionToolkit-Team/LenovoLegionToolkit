using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public sealed class SoftwareDisablerReconciler(
    VantageDisabler vantageDisabler,
    LegionSpaceDisabler legionSpaceDisabler,
    LegionZoneDisabler legionZoneDisabler,
    FnKeysDisabler fnKeysDisabler)
{
    private readonly AbstractSoftwareDisabler[] _disablers =
    [
        vantageDisabler,
        legionSpaceDisabler,
        legionZoneDisabler,
        fnKeysDisabler
    ];

    public async Task ReconcileAsync()
    {
        foreach (var disabler in _disablers)
        {
            try
            {
                await disabler.ReconcileDisableIntentAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to reconcile software disable intent. [type={disabler.GetType().Name}]", ex);
            }
        }
    }
}
