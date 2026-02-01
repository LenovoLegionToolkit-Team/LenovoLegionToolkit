using System;
using System.IO;
using System.Threading.Tasks;
using System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.AutoListeners;

public class InstanceStartedEventAutoAutoListener : AbstractAutoListener<InstanceStartedEventAutoAutoListener.ChangedEventArgs>
{
    public class ChangedEventArgs(int processId, int parentProcessId, string processName) : EventArgs
    {
        public int ProcessId { get; } = processId;
        public int ParentProcessId { get; } = parentProcessId;
        public string ProcessName { get; } = processName;
    }

    private ManagementEventWatcher? _watcher;

    protected override Task StartAsync()
    {
        try
        {
            var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");

            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += Watcher_EventArrived;
            _watcher.Start();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to start process watcher.", ex);
        }

        return Task.CompletedTask;
    }

    protected override Task StopAsync()
    {
        if (_watcher != null)
        {
            try
            {
                _watcher.Stop();
                _watcher.Dispose();
            }
            catch { /* Ignore */ }
            _watcher = null;
        }

        return Task.CompletedTask;
    }

    private void Watcher_EventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var processId = Convert.ToInt32(e.NewEvent["ProcessID"]);
            var parentProcessId = Convert.ToInt32(e.NewEvent["ParentProcessID"]);
            var processName = (string)e.NewEvent["ProcessName"];

            var nameWithoutExt = Path.GetFileNameWithoutExtension(processName);

            RaiseChanged(new ChangedEventArgs(processId, parentProcessId, nameWithoutExt));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error processing process start event.", ex);
        }
    }
}