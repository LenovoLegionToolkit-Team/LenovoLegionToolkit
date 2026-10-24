using System;

namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

internal static class FanTableVerification
{
    // Some EC firmware raises requested levels to its minimum. Accept only two
    // identical, valid readbacks that never reduce any requested fan level.
    internal static bool IsStableMatchOrUpwardAdjustment(
        ReadOnlySpan<ushort> requested, ReadOnlySpan<uint> first, ReadOnlySpan<uint> second)
    {
        if (requested.Length != 10 || first.Length != 10 || second.Length != 10)
            return false;

        for (var i = 0; i < 10; i++)
        {
            if (requested[i] is < 1 or > 10 || first[i] != second[i]
                || second[i] < requested[i] || second[i] > 10)
                return false;
        }

        return true;
    }
}
