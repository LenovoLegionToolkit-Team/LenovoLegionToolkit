using System;
using System.Collections.Generic;
using System.IO;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Settings;

public class LampArraySettings : AbstractSettings<LampArraySettings.LampArraySettingsStore>
{
    public class LampEffectConfig
    {
        public LampEffectType EffectType { get; set; } = LampEffectType.Rainbow;
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class LampArraySettingsStore
    {
        public double Brightness { get; set; } = 1.0;
        public double Speed { get; set; } = 1.0;
        public bool SmoothTransition { get; set; } = true;
        public LampEffectConfig DefaultEffect { get; set; } = new();
        public Dictionary<int, LampEffectConfig> PerLampEffects { get; set; } = new();
    }

    public LampArraySettings() : base("lamp_array.json") { }

    public void ExportToFile(string path)
    {
        var json = JsonConvert.SerializeObject(Store, JsonSerializerSettings);
        File.WriteAllText(path, json);
    }

    public void ImportFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Profile file not found.", path);

        var json = File.ReadAllText(path);
        var imported = JsonConvert.DeserializeObject<LampArraySettingsStore>(json, JsonSerializerSettings);

        if (imported == null)
            throw new InvalidOperationException("Failed to deserialize profile.");

        Store = imported;
        Save();
    }
}
