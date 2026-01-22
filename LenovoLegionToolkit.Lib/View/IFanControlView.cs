namespace LenovoLegionToolkit.Lib.View
{
    public interface IFanControlView
    {
        void UpdateMonitoring(float temperature, int rpm, byte pwmByte);
        void NotifyGlobalSettingsChanged();
    }
}
