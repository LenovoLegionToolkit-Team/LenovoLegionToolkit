using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Resource = LenovoLegionToolkit.Lib.Resources.Resource;
using TaskService = Microsoft.Win32.TaskScheduler.TaskService;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class SoftwareDisablerException(string message, Exception? innerException = null) : Exception(message, innerException);

public abstract class AbstractSoftwareDisabler
{
    public class AbstractSoftwareDisablerEventArgs : EventArgs
    {
        public SoftwareStatus Status { get; init; }
    }

    public string? LastFailureReason { get; private set; }

    private readonly List<string> _blockedResources = [];
    private readonly List<string> _notStoppedServices = [];

    private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string STARTUP_APPROVED_RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static readonly SemaphoreSlim _operationLock = new(1, 1);

    private static readonly string[] Hives = ["HKEY_CURRENT_USER", "HKEY_LOCAL_MACHINE"];
    private static readonly byte[] EnabledStartupEntry = [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    protected abstract IEnumerable<string> ScheduledTasksPaths { get; }
    protected abstract IEnumerable<string> ServiceNames { get; }
    protected abstract IEnumerable<string> ProcessNames { get; }

    protected virtual IEnumerable<string> DriverNamePrefixes => [];
    protected virtual IEnumerable<string> DriverPackageRoots => [];
    protected virtual IEnumerable<string> StartupEntryNames => [];
    protected virtual IEnumerable<string> StartupEntryRoots => [];

    protected virtual IEnumerable<string> OwnershipRoots => [];

    protected virtual IEnumerable<string> OwnershipPathMarkers => [];

    public event EventHandler<AbstractSoftwareDisablerEventArgs>? OnRefreshed;

    private string SelfName => GetType().Name;

    internal SoftwareDisablerOwnership.Peer ToOwnershipPeer() => new(
        SelfName,
        GetRoots(OwnershipRoots).Select(r => r.ToLowerInvariant()).ToArray(),
        OwnershipPathMarkers.Select(m => m.ToLowerInvariant()).ToArray(),
        ServiceNames.Select(s => s.ToLowerInvariant()).ToArray(),
        ScheduledTasksPaths.Select(NormalizeTaskFolder).ToArray());

    public async Task<SoftwareStatus> GetStatusAsync()
    {
        var (status, _, _) = await GetStateAsync().ConfigureAwait(false);

        return status;
    }

    private async Task<(SoftwareStatus Status, string[] Services, string[] Processes)> GetStateAsync()
    {
        var state = await Task.Run(EvaluateState).ConfigureAwait(false);

        Log.Instance.Trace($"Status: {state.Status} [type={GetType().Name}]");

        OnRefreshed?.Invoke(this, new() { Status = state.Status });

        return state;
    }

    private (SoftwareStatus Status, string[] Services, string[] Processes) EvaluateState()
    {
        try
        {
            var allServices = AllServices().ToArray();
            var services = RunningServices(allServices).ToArray();
            var processes = RunningProcesses().ToArray();

            Log.Instance.Trace($"Running services count: {services.Length}. [type={GetType().Name}, services={string.Join(",", services)}]");
            Log.Instance.Trace($"Running processes count: {processes.Length}. [type={GetType().Name}, processes={string.Join(",", processes)}]");

            if (services.Length != 0 || processes.Length != 0)
            {
                return (SoftwareStatus.Enabled, services, processes);
            }

            var status = IsInstalled(allServices) ? SoftwareStatus.Disabled : SoftwareStatus.NotFound;

            return (status, services, processes);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Exception while getting status. [type={GetType().Name}]", ex);

            return (SoftwareStatus.NotFound, [], []);
        }
    }

    public Task EnableAsync() => RunAsync(true);

    public Task DisableAsync() => RunAsync(false);

    protected virtual async Task ApplyStateAsync(bool enabled)
    {
        if (enabled)
        {
            SetScheduledTasksEnabled(true);
            SetServicesEnabled(true);
            SetDriversEnabled(true);
            SetStartupEntriesEnabled(true);

            SoftwareDisablerStateStore.SetDisabledByUser(GetType().Name, false);

            return;
        }

        await KillProcessesAsync().ConfigureAwait(false);

        var stillRunning = RunningProcesses().ToArray();
        if (stillRunning.Length > 0)
        {
            throw new SoftwareDisablerException(string.Format(Resource.SoftwareDisabler_ProcessError_Message, string.Join(", ", stillRunning)));
        }

        SetScheduledTasksEnabled(false);
        SetServicesEnabled(false);
        SetDriversEnabled(false);
        SetStartupEntriesEnabled(false);

        SoftwareDisablerStateStore.SetDisabledByUser(GetType().Name, true);
    }

    private async Task RunAsync(bool enabled)
    {
        await _operationLock.WaitAsync().ConfigureAwait(false);

        try
        {
            LastFailureReason = null;
            _blockedResources.Clear();
            _notStoppedServices.Clear();

            Log.Instance.Trace($"{(enabled ? "Enabling" : "Disabling")}... [type={GetType().Name}]");

            await ApplyStateAsync(enabled).ConfigureAwait(false);

            SoftwareDisablerOwnership.Invalidate();

            var (status, runningServices, runningProcesses) = await GetStateAsync().ConfigureAwait(false);

            if (!enabled && status == SoftwareStatus.Enabled)
            {
                LastFailureReason = BuildFailureReason(runningServices, runningProcesses);

                Log.Instance.Trace($"Disabled, restart required. [type={GetType().Name}, services={string.Join(",", runningServices)}, processes={string.Join(",", runningProcesses)}, notStopped={string.Join(",", _notStoppedServices)}, keptEnabled={string.Join(",", _blockedResources)}, reason={LastFailureReason}]");
            }
            else
            {
                Log.Instance.Trace($"{(enabled ? "Enabled" : "Disabled")} [type={GetType().Name}]");
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private string BuildFailureReason(IReadOnlyList<string> runningServices, IReadOnlyList<string> runningProcesses)
    {
        var reasons = new List<string>();

        if (runningServices.Count > 0)
        {
            reasons.Add($"services still running: {string.Join(", ", runningServices)}");
        }

        if (runningProcesses.Count > 0)
        {
            reasons.Add($"processes still running: {string.Join(", ", runningProcesses)}");
        }

        if (_notStoppedServices.Count > 0)
        {
            reasons.Add($"services that do not accept stop and need a restart: {string.Join(", ", _notStoppedServices)}");
        }

        if (_blockedResources.Count > 0)
        {
            reasons.Add($"services kept enabled because another software still uses them: {string.Join(", ", _blockedResources)}");
        }

        return string.Join("; ", reasons);
    }

    private static IEnumerable<ServiceController> AllServices() =>
        ServiceController.GetServices().Concat(ServiceController.GetDevices());

    private static bool ServiceExists(string serviceName, IEnumerable<ServiceController> services) =>
        services.Any(s => s.ServiceName == serviceName);

    private IEnumerable<string> ActiveDriverNamePrefixes()
    {
        var driverPackageRoots = DriverPackageRoots.ToArray();
        return driverPackageRoots.Length == 0 || driverPackageRoots.Any(Directory.Exists) ? DriverNamePrefixes : [];
    }

    private IEnumerable<string> MatchingDriverNames(IEnumerable<ServiceController> services)
    {
        var driverNamePrefixes = ActiveDriverNamePrefixes().ToArray();

        return driverNamePrefixes.Length == 0
            ? []
            : services
                .Where(s => driverNamePrefixes.Any(p => s.ServiceName.StartsWith(p, StringComparison.InvariantCultureIgnoreCase)))
                .Select(s => s.ServiceName)
                .ToArray();
    }

    private IEnumerable<string> OwnedServiceNames() =>
        SoftwareDisablerOwnership.ResolveOwnedServices(ServiceNames, SoftwareDisablerOwnership.ServiceImages(), SoftwareDisablerOwnership.Peers(), SelfName);

    private IEnumerable<string> OwnedScheduledTaskFolderPaths() =>
        SoftwareDisablerOwnership.ResolveOwnedTaskFolders(ScheduledTasksPaths, SoftwareDisablerOwnership.TaskFolderExecutables(), SoftwareDisablerOwnership.Peers(), SelfName);

    private static string NormalizeTaskFolder(string path) => path.TrimStart('\\').ToLowerInvariant();

    private IEnumerable<string> AllServiceNames(IEnumerable<ServiceController> services) =>
        OwnedServiceNames().Concat(MatchingDriverNames(services));

    private bool IsInstalled(IEnumerable<ServiceController> services) =>
        AllServiceNames(services).Any(s => ServiceExists(s, services));

    private IEnumerable<string> RunningServices(IEnumerable<ServiceController> services) =>
        AllServiceNames(services).Where(s => IsServiceEnabled(s, services));

    protected virtual IEnumerable<string> RunningProcesses()
    {
        var names = ProcessNames.ToArray();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var name = string.Empty;

                try
                {
                    name = process.ProcessName;
                    if (!names.Any(n => name.StartsWith(n, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        continue;
                    }
                }
                catch {  /* Ignore */ }

                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var owner = SoftwareDisablerOwnership.ResolveOwner(SoftwareDisablerOwnership.TryGetProcessPath(process));
                if (owner is not null && owner != SelfName)
                {
                    continue;
                }

                yield return name;
            }
        }
    }

    private static bool IsUnderRoots(string? path, string[] roots) =>
        path is not null && roots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));

    private static bool IsServiceEnabled(string serviceName, IEnumerable<ServiceController> services)
    {
        try
        {
            var service = services.FirstOrDefault(s => s.ServiceName == serviceName);
            if (service is null)
            {
                return false;
            }

            return service.Status is not ServiceControllerStatus.Stopped;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string ExtractExecutablePath(string commandLine) => SoftwareDisablerOwnership.ExtractExecutablePath(commandLine);

    private static string NormalizePath(string path) => SoftwareDisablerOwnership.NormalizePath(path);

    private static string[] GetRoots(IEnumerable<string> roots) =>
        roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => NormalizePath(r.Trim()).TrimEnd('\\') + '\\').ToArray();

    private void SetStartupEntriesEnabled(bool enabled)
    {
        var startupEntryNames = StartupEntryNames.ToArray();
        var startupEntryRoots = GetRoots(StartupEntryRoots);

        if (startupEntryNames.Length == 0 && startupEntryRoots.Length == 0)
        {
            return;
        }

        foreach (var hive in Hives)
            foreach (var name in GetStartupEntryNames(hive, startupEntryNames, startupEntryRoots))
            {
                SetStartupEntryEnabled(hive, name, enabled);
            }
    }

    private IEnumerable<string> GetStartupEntryNames(string hive, string[] startupEntryNames, string[] startupEntryRoots)
    {
        var valueNames = Registry.GetValueNames(hive, RUN_KEY);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in startupEntryNames)
        {
            if (valueNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(name);
            }
        }

        foreach (var valueName in valueNames)
        {
            var command = Registry.GetValue(hive, RUN_KEY, valueName, string.Empty);
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            var path = ExtractExecutablePath(command);

            var owner = SoftwareDisablerOwnership.ResolveOwner(path);
            if (startupEntryRoots.Length > 0 && owner is not null && owner != SelfName)
            {
                continue;
            }

            if (startupEntryRoots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(valueName);
            }
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

            throw new SoftwareDisablerException(string.Format(Resource.SoftwareDisabler_StartupEntryError_Message, name), ex);
        }
    }

    private static byte[] CreateDisabledStartupEntry() => [0x03, 0x00, 0x00, 0x00, .. BitConverter.GetBytes(DateTime.Now.ToFileTime())];

    private void SetScheduledTasksEnabled(bool enabled)
    {
        var taskService = TaskService.Instance;

        foreach (var path in OwnedScheduledTaskFolderPaths())
        {
            if (!enabled && !SoftwareDisablerOwnership.CanDisable($"task:{NormalizeTaskFolder(path)}", SelfName))
            {
                continue;
            }

            SetTasksInFolderEnabled(taskService, path, enabled);
        }
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
            if (!IsTaskOwnedBySelf(task))
            {
                Log.Instance.Trace($"Skipping task {task.Name} in {task.Path}, owned by another product. [type={GetType().Name}]");

                continue;
            }

            task.Definition.Settings.Enabled = enabled;
            try
            {
                task.RegisterChanges();
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to register changes on task {task.Name} in {task.Path}.", ex);

                throw new SoftwareDisablerException(string.Format(Resource.SoftwareDisabler_ScheduledTaskError_Message, task.Name), ex);
            }
        }
    }

    private bool IsTaskOwnedBySelf(Microsoft.Win32.TaskScheduler.Task task)
    {
        var owners = task.Definition.Actions
            .OfType<Microsoft.Win32.TaskScheduler.ExecAction>()
            .Select(a => a.Path)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => SoftwareDisablerOwnership.ResolveOwner(SoftwareDisablerOwnership.ExtractExecutablePath(Environment.ExpandEnvironmentVariables(p))))
            .Where(o => o is not null)
            .Distinct()
            .ToArray();

        return owners.Length == 0 || owners.Contains(SelfName);
    }

    private void SetServicesEnabled(bool enabled)
    {
        var services = AllServices().ToArray();

        foreach (var serviceName in OwnedServiceNames())
        {
            if (!enabled && !SoftwareDisablerOwnership.CanDisable($"service:{serviceName}", SelfName))
            {
                _blockedResources.Add(serviceName);

                Log.Instance.Trace($"Service {serviceName} kept enabled, it is still used by another software. [type={GetType().Name}]");

                continue;
            }

            SetServiceEnabled(serviceName, enabled, services);
        }
    }

    private void SetDriversEnabled(bool enabled)
    {
        var services = AllServices().ToArray();

        foreach (var driverName in MatchingDriverNames(services))
        {
            SetServiceEnabled(driverName, enabled, services);
        }
    }

    private void SetServiceEnabled(string serviceName, bool enabled, IEnumerable<ServiceController> services)
    {
        try
        {
            Log.Instance.Trace($"Setting service {serviceName} to {enabled}. [type={GetType().Name}]");

            if (!ServiceExists(serviceName, services))
            {
                Log.Instance.Trace($"Service {serviceName} not found. [type={GetType().Name}]");

                return;
            }

            var service = new ServiceController(serviceName);

            try
            {
                Log.Instance.Trace($"Changing service {serviceName} start mode to {enabled}. [startType={service.StartType}, status={service.Status}, type={GetType().Name}]");

                service.ChangeStartMode(enabled);
                service.Refresh();

                if (enabled)
                {
                    if (service.Status != ServiceControllerStatus.Running)
                    {
                        Log.Instance.Trace($"Starting service {serviceName}... [type={GetType().Name}]");

                        service.Start();
                        service.WaitForStatus(ServiceControllerStatus.Running);

                        Log.Instance.Trace($"Service {serviceName} started. [startType={service.StartType}, status={service.Status}, type={GetType().Name}]");
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
                        Log.Instance.Trace($"Stopping service {serviceName}... [status={service.Status}, type={GetType().Name}]");

                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped);

                        Log.Instance.Trace($"Service {serviceName} stopped. [startType={service.StartType}, status={service.Status}, type={GetType().Name}]");
                    }
                    else
                    {
                        _notStoppedServices.Add(serviceName);

                        Log.Instance.Trace($"Will not stop service {serviceName}, it does not accept stop and requires a restart. [status={service.Status}, canStop={service.CanStop}, type={GetType().Name}]]");
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

            throw new SoftwareDisablerException(string.Format(Resource.SoftwareDisabler_ServiceError_Message, serviceName), ex);
        }
    }

    protected virtual async Task KillProcessesAsync()
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var remaining = 0;

            foreach (var process in OwnedProcesses())
            {
                string? name = null;

                try
                {
                    name = process.ProcessName;

                    Log.Instance.Trace($"Killing process {name}... [attempt={attempt}, type={GetType().Name}]");

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                    process.Kill(true);
                    await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    remaining++;

                    Log.Instance.Trace($"Couldn't kill process {name}. [attempt={attempt}, type={GetType().Name}]", ex);
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (remaining == 0)
            {
                return;
            }

            await Task.Delay(300).ConfigureAwait(false);
        }
    }

    private IEnumerable<Process> OwnedProcesses()
    {
        var names = ProcessNames.ToArray();
        var roots = GetRoots(OwnershipRoots);

        foreach (var process in Process.GetProcesses())
        {
            string? name;

            try
            {
                name = process.ProcessName;
            }
            catch
            {
                process.Dispose();

                continue;
            }

            var declared = names.Any(n => name.StartsWith(n, StringComparison.InvariantCultureIgnoreCase));
            var path = SoftwareDisablerOwnership.TryGetProcessPath(process);
            var owner = SoftwareDisablerOwnership.ResolveOwner(path);

            var isOwned = declared
                ? owner is null || owner == SelfName
                : owner == SelfName && IsUnderRoots(path, roots);

            if (isOwned)
            {
                yield return process;
            }
            else
            {
                process.Dispose();
            }
        }
    }
}
