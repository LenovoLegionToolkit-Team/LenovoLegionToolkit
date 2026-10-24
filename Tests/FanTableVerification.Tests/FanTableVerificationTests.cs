using System.Collections.Generic;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using Xunit;

namespace LenovoLegionToolkit.Tests;

public class FanTableVerificationTests
{
    public static IEnumerable<object[]> Cases()
    {
        ushort[] requested = [1, 1, 1, 1, 2, 3, 4, 5, 7, 10];
        uint[] exact = [1, 1, 1, 1, 2, 3, 4, 5, 7, 10];
        uint[] raised = [1, 2, 3, 4, 5, 6, 7, 7, 7, 10];
        uint[] changed = [1, 2, 3, 4, 5, 6, 7, 8, 7, 10];
        uint[] slower = [1, 2, 3, 4, 5, 6, 7, 7, 7, 9];
        uint[] zero = [0, 2, 3, 4, 5, 6, 7, 7, 7, 10];
        uint[] rpm = [1700, 1900, 2100, 2300, 2500, 2900, 3400, 3700, 4400, 5400];
        uint[] overflow = [65537, 2, 3, 4, 5, 6, 7, 7, 7, 10];

        yield return ["Exact readback", requested, exact, exact, true];
        yield return ["LPCN65WW stable upward adjustment", requested, raised, raised, true];
        yield return ["LPCN65WW default upward adjustment",
            new ushort[] { 1, 2, 3, 4, 4, 6, 7, 8, 9, 10 },
            new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            new uint[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, true];
        yield return ["Unstable readback", requested, raised, changed, false];
        yield return ["Reduced hottest point", requested, slower, slower, false];
        yield return ["Zeroed EC point", requested, zero, zero, false];
        yield return ["RPM instead of levels", requested, rpm, rpm, false];
        yield return ["Value that would truncate to a valid ushort", requested, overflow, overflow, false];
        yield return ["Truncated first read", requested, raised[..9], raised, false];
        yield return ["Truncated second read", requested, raised, raised[..9], false];
        yield return ["Oversized reply", requested, new uint[] { 1, 2, 3, 4, 5, 6, 7, 7, 7, 10, 10 }, raised, false];
        yield return ["Empty replies", requested, new uint[0], new uint[0], false];
        yield return ["Invalid request length", requested[..9], raised, raised, false];
        yield return ["Invalid requested zero", new ushort[] { 0, 1, 1, 1, 2, 3, 4, 5, 7, 10 }, raised, raised, false];
        yield return ["Invalid requested level", new ushort[] { 11, 1, 1, 1, 2, 3, 4, 5, 7, 10 }, raised, raised, false];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RequiresStableValidReadbacksWithoutReducingRequestedLevels(
        string scenario, ushort[] requested, uint[] first, uint[] second, bool expected)
    {
        var actual = FanTableVerification.IsStableMatchOrUpwardAdjustment(requested, first, second);
        Assert.True(actual == expected, scenario);
    }
}