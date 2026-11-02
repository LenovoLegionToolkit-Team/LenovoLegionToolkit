namespace LenovoLegionToolkit.Lib.Extensions;

public static class SpectrumKeyboardBacklightEffectTypeExtensions
{

    public static bool IsAllLightsEffect(this SpectrumKeyboardBacklightEffectType type) => type switch
    {
        SpectrumKeyboardBacklightEffectType.AudioBounce => true,
        SpectrumKeyboardBacklightEffectType.AudioRipple => true,
        SpectrumKeyboardBacklightEffectType.AuroraSync => true,
        _ => false
    };

    public static bool IsWholeKeyboardEffect(this SpectrumKeyboardBacklightEffectType type) => type switch
    {
        SpectrumKeyboardBacklightEffectType.Lighting => true,
        SpectrumKeyboardBacklightEffectType.Ripples => true,
        _ => false
    };

    public static bool SupportsColorMode(this SpectrumKeyboardBacklightEffectType type) => type switch
    {
        SpectrumKeyboardBacklightEffectType.Always => true,
        SpectrumKeyboardBacklightEffectType.Breath => true,
        SpectrumKeyboardBacklightEffectType.ColorChange => true,
        SpectrumKeyboardBacklightEffectType.ColorPulse => true,
        SpectrumKeyboardBacklightEffectType.ColorWave => true,
        SpectrumKeyboardBacklightEffectType.HorseRaceLamp => true,
        SpectrumKeyboardBacklightEffectType.Smooth => true,
        SpectrumKeyboardBacklightEffectType.Raining => true,
        SpectrumKeyboardBacklightEffectType.Ripples => true,
        SpectrumKeyboardBacklightEffectType.Lighting => true,
        _ => false
    };
}
