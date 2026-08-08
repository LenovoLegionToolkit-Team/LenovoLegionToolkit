using System;
using DiskInfoToolkit;
using LibreHardwareMonitor.Hardware;

namespace LenovoLegionToolkit.Lib.Utils
{
    public class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }
        public void VisitHardware(IHardware hardware)
        {
            if (IsExternalStorageDevice(hardware))
            {
                return;
            }

            try
            {
                hardware.Update();
                foreach (IHardware subHardware in hardware.SubHardware)
                {
                    subHardware.Accept(this);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Safety visit failed for hardware {hardware.Name}: {ex.Message}", ex);
            }
        }

        private static bool IsExternalStorageDevice(IHardware hardware) =>
            hardware is LibreHardwareMonitor.Hardware.Storage.StorageDevice storage &&
            (storage.Storage.TransportKind == StorageTransportKind.Usb ||
             storage.Storage.BusType == StorageBusType.Usb ||
             storage.Storage.IsRemovable);

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
