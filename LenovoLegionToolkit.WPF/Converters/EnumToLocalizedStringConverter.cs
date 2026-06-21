using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Converters
{
    public static class EnumToLocalizedStringConverter
    {
        public static string Convert(SensorItem value)
        {
            return value switch
            {
                SensorItem.CpuUtilization => Resource.SensorItem_CpuUtilitzation,
                SensorItem.CpuFrequency => Resource.SensorItem_CpuFrequency,
                SensorItem.CpuFanSpeed => Resource.SensorItem_CpuFanSpeed,
                SensorItem.CpuTemperature => Resource.SensorItem_CpuTemperature,
                SensorItem.CpuPower => Resource.SensorItem_CpuPower,
                SensorItem.GpuUtilization => Resource.SensorItem_GpuUtilization,
                SensorItem.GpuVramUtilization => Resource.SensorsControl_GpuVramUtilization_Title,
                SensorItem.GpuFrequency => Resource.SensorItem_GpuFrequency,
                SensorItem.GpuFanSpeed => Resource.SensorItem_GpuFanSpeed,
                SensorItem.GpuCoreTemperature => Resource.SensorsControl_GpuCoreTemperature_Title,
                SensorItem.GpuVramTemperature => Resource.SensorsControl_GpuMemoryTemperature_Title,
                SensorItem.GpuTemperatures => Resource.SensorItem_GpuTemperatures,
                SensorItem.GpuPower => Resource.SensorsControl_GPU_Power,
                SensorItem.PchFanSpeed => Resource.SensorItem_PchFanSpeed,
                SensorItem.PchTemperature => Resource.SensorItem_PchTemperature,
                SensorItem.BatteryState => Resource.SensorItem_BatteryState,
                SensorItem.BatteryLevel => Resource.SensorItem_BatteryLevel,
                SensorItem.MemoryUtilization => Resource.SensorItem_MemoryUtilization,
                SensorItem.MemoryTemperature => Resource.SensorItem_MemoryTemperature,
                SensorItem.Disk1Temperature => Resource.SensorItem_Disk1Temperature,
                SensorItem.Disk2Temperature => Resource.SensorItem_Disk2Temperature,
                _ => value.ToString()
            };
        }
    }
}
