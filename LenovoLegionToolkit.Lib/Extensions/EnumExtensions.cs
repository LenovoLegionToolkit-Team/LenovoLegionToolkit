using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class EnumExtensions
{
    public static string GetDisplayName(this Enum enumValue)
    {
        var displayAttribute = enumValue.GetType()
            .GetMember(enumValue.ToString())
            .FirstOrDefault()?
            .GetCustomAttribute<DisplayAttribute>();
        if (displayAttribute == null)
        {
            return enumValue.ToString();
        }

        return displayAttribute.GetName() ?? enumValue.ToString();
    }

    public static string GetFlagsDisplayName(this Enum enumValue, Enum? excluding = null)
    {
        var values = Enum.GetValues(enumValue.GetType()).Cast<Enum>();
        if (excluding is not null)
            values = values.Where(v => !v.Equals(excluding));
        var names = values.Where(enumValue.HasFlag).Select(GetDisplayName);
        return string.Join(", ", names);
    }
}
