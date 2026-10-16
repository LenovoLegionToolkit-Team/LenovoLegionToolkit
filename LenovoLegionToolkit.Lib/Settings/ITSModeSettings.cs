using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Settings;

public class ITSModeSettings() : AbstractSettings<ITSModeSettings.ITSModeSettingsStore>("itsmode_settings.json")
{
    public class ITSModeSettingsStore
    {
        public ITSMode LastState { get; set; } = ITSMode.None;
        public bool RestoreLastStateOnStartup { get; set; } = true;
        public List<ITSMode> FnQModeOrder { get; set; } = [];
        public List<ITSMode> DisabledModes { get; set; } = [];
    }
}
