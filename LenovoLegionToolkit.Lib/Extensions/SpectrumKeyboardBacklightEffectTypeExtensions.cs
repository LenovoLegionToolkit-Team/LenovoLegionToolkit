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
        SpectrumKeyboardBacklightEffectType.Type => true,
        SpectrumKeyboardBacklightEffectType.Ripple => true,
        _ => false
    };

    public static bool SupportsColorMode(this SpectrumKeyboardBacklightEffectType type) => type switch
    {
        SpectrumKeyboardBacklightEffectType.Always => true,
        SpectrumKeyboardBacklightEffectType.ColorChange => true,
        SpectrumKeyboardBacklightEffectType.ColorPulse => true,
        SpectrumKeyboardBacklightEffectType.ColorWave => true,
        SpectrumKeyboardBacklightEffectType.Smooth => true,
        SpectrumKeyboardBacklightEffectType.Rain => true,
        SpectrumKeyboardBacklightEffectType.Ripple => true,
        SpectrumKeyboardBacklightEffectType.Type => true,
        _ => false
    };
}
