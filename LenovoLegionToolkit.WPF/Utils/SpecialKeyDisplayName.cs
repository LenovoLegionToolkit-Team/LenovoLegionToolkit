using System;

namespace LenovoLegionToolkit.WPF.Utils;

internal static class SpecialKeyDisplayName
{
    public static string GetKey<TEnum>(TEnum key, string? suffix = null) where TEnum : Enum
    {
        var str = key.ToString();

        if (str.StartsWith("Fn", StringComparison.OrdinalIgnoreCase) && str.Length > 2)
            str = $"Fn + {str[2..]}";

        return string.IsNullOrEmpty(suffix) ? str : $"{str} {suffix}";
    }

    public static string GetUnknownKey(int code, int hexDigits, string? suffix = null)
    {
        var str = $"Fn + 0x{code.ToString("X" + hexDigits)}";

        return string.IsNullOrEmpty(suffix) ? str : $"{str} {suffix}";
    }
}
