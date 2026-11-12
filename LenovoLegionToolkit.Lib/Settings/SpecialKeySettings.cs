using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Settings;

public class SpecialKeySettings : AbstractSettings<SpecialKeySettings.SpecialKeySettingsStore>
{
    public class SpecialKeySettingsStore
    {
        public const int DriverKeyCodeOffset = 0x10000;
        public const int ConsumerControlKeyCodeOffset = 0x20000;
        public const int KeyboardKeyCodeOffset = 0x30000;

        public Dictionary<int, CustomSpecialKey> KeyModes { get; set; } = [];
        public Dictionary<int, List<Guid>> KeySinglePressActions { get; set; } = [];
        public Dictionary<int, List<Guid>> KeyDoublePressActions { get; set; } = [];
        public Dictionary<int, string> KeyDescriptions { get; set; } = [];

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<int> HiddenKeys { get; set; } =
        [
            (int)SpecialKey.FnLockOn,
            (int)SpecialKey.FnLockOff,
            (int)SpecialKey.CameraOn,
            (int)SpecialKey.CameraOff,
            (int)SpecialKey.SpectrumBacklightOff,
            (int)SpecialKey.SpectrumBacklight1,
            (int)SpecialKey.SpectrumBacklight2,
            (int)SpecialKey.SpectrumBacklight3,
            (int)SpecialKey.SpectrumPreset1,
            (int)SpecialKey.SpectrumPreset2,
            (int)SpecialKey.SpectrumPreset3,
            (int)SpecialKey.SpectrumPreset4,
            (int)SpecialKey.SpectrumPreset5,
            (int)SpecialKey.SpectrumPreset6,
            (int)SpecialKey.WhiteBacklightOff,
            (int)SpecialKey.WhiteBacklight1,
            (int)SpecialKey.WhiteBacklight2,
        ];
    }

    public SpecialKeySettings() : base("special_key.json") { }

    public override SpecialKeySettingsStore? LoadStore()
    {
        return base.LoadStore() ?? new SpecialKeySettingsStore();
    }
}
