using System;
using LenovoLegionToolkit.Lib.Settings;

namespace LenovoLegionToolkit.Lib;

public static class SpecialKeyCodes
{
    public static bool IsDriverKey(int code)
    {
        var offset = SpecialKeySettings.SpecialKeySettingsStore.DriverKeyCodeOffset;
        return code >= offset && Enum.IsDefined(typeof(DriverKey), (DriverKey)(code - offset));
    }

    public static bool IsConsumerControlKey(int code)
    {
        var offset = SpecialKeySettings.SpecialKeySettingsStore.ConsumerControlKeyCodeOffset;
        return code >= offset && Enum.IsDefined(typeof(ConsumerControlKey), (ConsumerControlKey)(code - offset));
    }

    public static bool IsKeyboardKey(int code)
    {
        var offset = SpecialKeySettings.SpecialKeySettingsStore.KeyboardKeyCodeOffset;
        return code >= offset && Enum.IsDefined(typeof(KeyboardKey), (KeyboardKey)(code - offset));
    }

    public static DriverKey ToDriverKey(int code) => (DriverKey)(code - SpecialKeySettings.SpecialKeySettingsStore.DriverKeyCodeOffset);

    public static ConsumerControlKey ToConsumerControlKey(int code) => (ConsumerControlKey)(code - SpecialKeySettings.SpecialKeySettingsStore.ConsumerControlKeyCodeOffset);

    public static KeyboardKey ToKeyboardKey(int code) => (KeyboardKey)(code - SpecialKeySettings.SpecialKeySettingsStore.KeyboardKeyCodeOffset);
}
