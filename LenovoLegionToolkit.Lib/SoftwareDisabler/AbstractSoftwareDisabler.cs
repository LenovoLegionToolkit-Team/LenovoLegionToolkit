using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using TaskService = Microsoft.Win32.TaskScheduler.TaskService;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class SoftwareDisablerException(string message, Exception innerException) : Exception(message, innerException);

public abstract class AbstractSoftwareDisabler
{
    public class AbstractSoftwareDisablerEventArgs : EventArgs
    {
        public SoftwareStatus Status { get; init; }
    }

    private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string STARTUP_APPROVED_RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static readonly string[] Hives = ["HKEY_CURRENT_USER", "HKEY_LOCAL_MACHINE"];
    private static readonly byte[] EnabledStartupEntry = [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    protected abstract IEnumerable<string> ScheduledTasksPaths { get; }
    protected abstract IEnumerable<string> ServiceNames { get; }
    protected abstract IEnumerable<string> ProcessNames { get; }

    protected virtual IEnumerable<string> DriverNamePrefixes => [];
    protected virtual IEnumerable<string> DriverPackageRoots => [];
    protected virtual IEnumerable<string> StartupEntryNames => [];
    protected virtual IEnumerable<string> StartupEntryRoots => [];

    public event EventHandler<AbstractSoftwareDisablerEventArgs>? OnRefreshed;

    public Task<SoftwareStatus> GetStatusAsync() => Task.Run(() =>
    {
        bool isEnabled;
        bool isInstalled;

        try
        {
            var services = RunningServices().ToArray();
            var processes = RunningProcesses().ToArray();

            Log.Instance.Trace($"Running services count: {services.Length}. [type={GetType().Name}, services={string.Join(",", services)}]");
            Log.Instance.Trace($"Running processes count: {processes.Length}. [type={GetType().Name}, processes={string.Join(",", processes)}]");

            isEnabled = services.Length != 0 || processes.Length != 0;
            isInstalled = IsInstalled();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Exception while getting status. [type={GetType().Name}]", ex);

            isEnabled = false;
            isInstalled = false;
        }

        Log.Instance.Trace($"Status: {isEnabled},{isInstalled} [type={GetType().Name}]");

        SoftwareStatus status;

        if (isEnabled)
            status = SoftwareStatus.Enabled;
        else if (!isInstalled)
            status = SoftwareStatus.NotFound;
        else
            status = SoftwareStatus.Disabled;

        OnRefreshed?.Invoke(this, new() { Status = status });

        return status;
    });

    public virtual Task EnableAsync() => Task.Run(async () =>
    {
        Log.Instance.Trace($"Enabling... [type={GetType().Name}]");

        SetScheduledTasksEnabled(true);
        SetServicesEnabled(true);
        SetDriversEnabled(true);
        SetStartupEntriesEnabled(true);

        _ = await GetStatusAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Enabled [type={GetType().Name}]");
    });

    public virtual Task DisableAsync() => Task.Run(async () =>
    {
        Log.Instance.Trace($"Disabling... [type={GetType().Name}]");

        SetScheduledTasksEnabled(false);
        SetServicesEnabled(false);
        await KillProcessesAsync().ConfigureAwait(false);
        SetDriversEnabled(false);
        SetStartupEntriesEnabled(false);

        _ = await GetStatusAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Disabled [type={GetType().Name}]");
    });

    private static IEnumerable<ServiceController> AllServices() =>
        ServiceController.GetServices().Concat(ServiceController.GetDevices());

    private static bool ServiceExists(string serviceName) => AllServices().Any(s => s.ServiceName == serviceName);

    private IEnumerable<string> ActiveDriverNamePrefixes()
    {
        var driverPackageRoots = DriverPackageRoots.ToArray();
        return driverPackageRoots.Length == 0 || driverPackageRoots.Any(Directory.Exists) ? DriverNamePrefixes : [];
    }

    private IEnumerable<string> MatchingDriverNames()
    {
        var driverNamePrefixes = ActiveDriverNamePrefixes().ToArray();

        return driverNamePrefixes.Length == 0
            ? []
            : AllServices()
                .Where(s => driverNamePrefixes.Any(p => s.ServiceName.StartsWith(p, StringComparison.InvariantCultureIgnoreCase)))
                .Select(s => s.ServiceName);
    }

    private IEnumerable<string> AllServiceNames() => ServiceNames.Concat(MatchingDriverNames());

    private bool IsInstalled() => AllServiceNames().Any(ServiceExists);

    private IEnumerable<string> RunningServices()
    {
        var services = AllServices().ToArray();
        return AllServiceNames().Where(s => IsServiceEnabled(s, services));
    }

    protected virtual IEnumerable<string> RunningProcesses()
    {
        foreach (var process in Process.GetProcesses())
        {
            foreach (var processName in ProcessNames)
            {
                var name = string.Empty;

                try
                {
                    name = process.ProcessName;
                    if (!name.StartsWith(processName, StringComparison.InvariantCultureIgnoreCase))
                        continue;
                }
                catch {  /* Ignore */ }

                if (!string.IsNullOrEmpty(name))
                    yield return name;
            }
        }
    }

    private static bool IsServiceEnabled(string serviceName, IEnumerable<ServiceController> services)
    {
        try
        {
            var service = services.FirstOrDefault(s => s.ServiceName == serviceName);
            if (service is null)
                return false;

            return service.Status is not ServiceControllerStatus.Stopped;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string ExtractExecutablePath(string commandLine)
    {
        var value = commandLine.Trim();

        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            if (end > 1)
                value = value[1..end];
        }
        else
        {
            var end = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (end >= 0)
                value = value[..(end + 4)];
        }

        return NormalizePath(value);
    }

    private static string NormalizePath(string path)
    {
        var result = Environment.ExpandEnvironmentVariables(path);

        if (result.StartsWith(@"\??\", StringComparison.Ordinal))
            result = result[4..];

        if (result.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            result = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), result[12..]);

        if (result.StartsWith(@"\\", StringComparison.Ordinal))
            return result;

        while (result.Contains(@"\\", StringComparison.Ordinal))
            result = result.Replace(@"\\", @"\");

        return result;
    }

    private static string[] GetRoots(IEnumerable<string> roots) =>
        roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => NormalizePath(r.Trim()).TrimEnd('\\') + '\\').ToArray();

    private void SetStartupEntriesEnabled(bool enabled)
    {
        var startupEntryNames = StartupEntryNames.ToArray();
        var startupEntryRoots = GetRoots(StartupEntryRoots);

        if (startupEntryNames.Length == 0 && startupEntryRoots.Length == 0)
            return;

        foreach (var hive in Hives)
            foreach (var name in GetStartupEntryNames(hive, startupEntryNames, startupEntryRoots))
                SetStartupEntryEnabled(hive, name, enabled);
    }

    private IEnumerable<string> GetStartupEntryNames(string hive, string[] startupEntryNames, string[] startupEntryRoots)
    {
        var valueNames = Registry.GetValueNames(hive, RUN_KEY);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in startupEntryNames)
        {
            if (valueNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                result.Add(name);
        }

        foreach (var valueName in valueNames)
        {
            var command = Registry.GetValue(hive, RUN_KEY, valueName, string.Empty);
            if (string.IsNullOrWhiteSpace(command))
                continue;

            var path = ExtractExecutablePath(command);
            if (startupEntryRoots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                result.Add(valueName);
        }

        return result;
    }

    private void SetStartupEntryEnabled(string hive, string name, bool enabled)
    {
        Log.Instance.Trace($"Setting autorun entry {name} to {enabled}. [type={GetType().Name}]");

        var value = enabled ? EnabledStartupEntry : CreateDisabledStartupEntry();

        try
        {
            Registry.SetValue(hive, STARTUP_APPROVED_RUN_KEY, name, value, false, Microsoft.Win32.RegistryValueKind.Binary);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set autorun entry {name} in {hive}.", ex);

            throw new SoftwareDisablerException($"Failed to set autorun entry {name} in {hive} [type={GetType().Name}]", ex);
        }
    }

    private static byte[] CreateDisabledStartupEntry() => [0x03, 0x00, 0x00, 0x00, .. BitConverter.GetBytes(DateTime.Now.ToFileTime())];

    private void SetScheduledTasksEnabled(bool enabled)
    {
        var taskService = TaskService.Instance;
        foreach (var path in ScheduledTasksPaths)
            SetTasksInFolderEnabled(taskService, path, enabled);
    }

    private void SetTasksInFolderEnabled(TaskService taskService, string path, bool enabled)
    {
        Log.Instance.Trace($"Setting tasks in folder {path} to {enabled}. [type={GetType().Name}]");

        var folder = taskService.GetFolder(path);
        if (folder is null)
        {
            Log.Instance.Trace($"Folder not found [path={path}, type={GetType().Name}]]");

            return;
        }

        foreach (var task in folder.Tasks.ToArray())
        {
            task.Definition.Settings.Enabled = enabled;
            try
            {
                task.RegisterChanges();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to register changes on task {task.Name} in {task.Path}.", ex);

                throw new SoftwareDisablerException($"Failed to register changes on task {task.Name} in {task.Path} [type={GetType().Name}]", ex);
            }
        }
    }

    private void SetServicesEnabled(bool enabled)
    {
        foreach (var serviceName in ServiceNames)
            SetServiceEnabled(serviceName, enabled);
    }

    private void SetDriversEnabled(bool enabled)
    {
        foreach (var driverName in MatchingDriverNames())
            SetServiceEnabled(driverName, enabled);
    }

    private void SetServiceEnabled(string serviceName, bool enabled)
    {
        try
        {
            Log.Instance.Trace($"Setting service {serviceName} to {enabled}. [type={GetType().Name}]");

            if (!ServiceExists(serviceName))
            {
                Log.Instance.Trace($"Service {serviceName} not found. [type={GetType().Name}]");

                return;
            }

            var service = new ServiceController(serviceName);

            try
            {
                Log.Instance.Trace($"Changing service {serviceName} start mode to {enabled}.  [type={GetType().Name}]");

                service.ChangeStartMode(enabled);

                if (enabled)
                {
                    if (service.Status != ServiceControllerStatus.Running)
                    {
                        Log.Instance.Trace($"Starting service {serviceName}... [type={GetType().Name}]");
                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running);
                    }
                    else
                    {
                        Log.Instance.Trace($"Will not start service {serviceName}. [status={service.Status}, type={GetType().Name}]]");
                    }
                }
                else
                {
                    if (service.CanStop)
                    {
                        Log.Instance.Trace($"Stopping service {serviceName}... [type={GetType().Name}]");
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped);
                    }
                    else
                    {
                        Log.Instance.Trace($"Will not stop service {serviceName}. [status={service.Status}, canStop={service.CanStop}, type={GetType().Name}]]");
                    }
                }
            }
            finally
            {
                service.Close();
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set service {serviceName} to {enabled}.", ex);

            throw new SoftwareDisablerException($"{serviceName} [type={GetType().Name}]", ex);
        }
    }

    protected virtual async Task KillProcessesAsync()
    {
        foreach (var process in Process.GetProcesses())
            foreach (var processName in ProcessNames)
            {
                try
                {
                    if (process.ProcessName.StartsWith(processName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        process.Kill(true);
                        await process.WaitForExitAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Couldn't kill process.", ex);
                }
            }
    }
}
