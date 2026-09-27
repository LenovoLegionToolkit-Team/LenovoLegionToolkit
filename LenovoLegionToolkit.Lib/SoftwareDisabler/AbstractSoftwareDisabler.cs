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
using Windows.Management.Deployment;
using DeploymentPackageStatus = Windows.Management.Deployment.PackageStatus;
using Resource = LenovoLegionToolkit.Lib.Resources.Resource;
using TaskService = Microsoft.Win32.TaskScheduler.TaskService;
using WindowsPackage = Windows.ApplicationModel.Package;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public class SoftwareDisablerException(string message, Exception? innerException = null) : Exception(message, innerException);

public abstract class AbstractSoftwareDisabler
{
    private sealed record EvaluatedState(
        SoftwareStatus Status,
        string[] Services,
        string[] Processes,
        string[] ScheduledTasks,
        string[] AppxPackages,
        string[] StartupEntries,
        string[] Errors);

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

    protected abstract SoftwareDisablerPolicy Policy { get; }

    private IEnumerable<string> ScheduledTasksPaths => Policy.ScheduledTaskPaths;
    private IEnumerable<string> ServiceNames => Policy.ServiceNames;
    private IEnumerable<string> ProcessNames => Policy.ProcessNames;
    private IEnumerable<string> DriverNamePrefixes => Policy.DriverNamePrefixes;
    private IEnumerable<string> DriverPackageRoots => Policy.DriverPackageRoots;
    private IEnumerable<string> StartupEntryNames => Policy.StartupEntryNames;
    private IEnumerable<string> StartupEntryRoots => Policy.StartupEntryRoots;
    private IEnumerable<string> AppxPackageNames => Policy.AppxPackageNames;
    private IEnumerable<string> RepairBlockingServiceNames => Policy.RepairBlockingServiceNames;
    private IEnumerable<string> RepairBlockingProcessNames => Policy.RepairBlockingProcessNames;
    private IEnumerable<string> OwnershipRoots => Policy.OwnershipRoots;
    private IEnumerable<string> OwnershipPathMarkers => Policy.OwnershipPathMarkers;
    private IEnumerable<string> OwnershipPriorityPathFragments => Policy.OwnershipPriorityPathFragments;

    public event EventHandler<AbstractSoftwareDisablerEventArgs>? OnRefreshed;

    private string SelfName => GetType().Name;

    internal SoftwareDisablerOwnership.Peer ToOwnershipPeer() => new(
        SelfName,
        GetRoots(OwnershipRoots).Select(r => r.ToLowerInvariant()).ToArray(),
        OwnershipPathMarkers.Select(m => m.ToLowerInvariant()).ToArray(),
        OwnershipPriorityPathFragments.Select(f => f.ToLowerInvariant()).ToArray(),
        ServiceNames.Select(s => s.ToLowerInvariant()).ToArray(),
        ScheduledTasksPaths.Select(NormalizeTaskFolder).ToArray());

    public async Task<SoftwareStatus> GetStatusAsync()
    {
        var status = (await GetStateAsync().ConfigureAwait(false)).Status;

        return status;
    }

    private async Task<EvaluatedState> GetStateAsync()
    {
        var state = await Task.Run(EvaluateState).ConfigureAwait(false);

        Log.Instance.Trace($"Status: {state.Status} [type={GetType().Name}]");

        OnRefreshed?.Invoke(this, new() { Status = state.Status });

        return state;
    }

    private EvaluatedState EvaluateState()
    {
        ServiceController[] allServices = [];

        try
        {
            allServices = AllServices().ToArray();
            var serviceNames = AllServiceNames(allServices).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var packages = MatchingAppxPackages();
            var installed = IsInstalled(allServices, serviceNames, packages);
            var services = EnabledServices(allServices, serviceNames, installed).ToArray();
            var processes = RunningProcesses()
                .Concat(installed ? RunningRepairBlockingProcesses() : [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var scheduledTasks = EnabledScheduledTasks().ToArray();
            var appxPackages = EnabledAppxPackages(packages).ToArray();
            var startupEntries = EnabledStartupEntries()
                .Concat(GetAdditionalEnabledResources())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Log.Instance.Trace($"Enabled services count: {services.Length}. [type={GetType().Name}, services={string.Join(",", services)}]");
            Log.Instance.Trace($"Running processes count: {processes.Length}. [type={GetType().Name}, processes={string.Join(",", processes)}]");
            Log.Instance.Trace($"Enabled launch sources. [type={GetType().Name}, tasks={string.Join(",", scheduledTasks)}, appx={string.Join(",", appxPackages)}, startup={string.Join(",", startupEntries)}]");

            if (services.Length != 0 || processes.Length != 0 || scheduledTasks.Length != 0 || appxPackages.Length != 0 || startupEntries.Length != 0)
            {
                return new(SoftwareStatus.Enabled, services, processes, scheduledTasks, appxPackages, startupEntries, []);
            }

            var status = installed ? SoftwareStatus.Disabled : SoftwareStatus.NotFound;

            return new(status, services, processes, scheduledTasks, appxPackages, startupEntries, []);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Exception while getting status. [type={GetType().Name}]", ex);

            return new(SoftwareStatus.NotFound, [], [], [], [], [], [ex.Message]);
        }
        finally
        {
            DisposeServices(allServices);
        }
    }

    public Task EnableAsync() => RunAsync(true, captureSnapshot: false);

    public Task DisableAsync() => RunAsync(false, captureSnapshot: true);

    internal async Task ReconcileDisableIntentAsync()
    {
        if (!SoftwareDisablerStateStore.IsDisabledByUser(SelfName))
        {
            return;
        }

        var state = await Task.Run(EvaluateState).ConfigureAwait(false);
        if (state.Status != SoftwareStatus.Enabled && state.Errors.Length == 0)
        {
            Log.Instance.Trace($"Software disable intent is already satisfied. [type={SelfName}, status={state.Status}]");
            return;
        }

        await RunAsync(false, captureSnapshot: false).ConfigureAwait(false);
    }

    private SoftwareDisablerSnapshot CaptureSnapshot()
    {
        var snapshot = new SoftwareDisablerSnapshot();
        var services = AllServices().ToArray();

        try
        {
            var serviceNames = OwnedServiceNames()
                .Concat(MatchingDriverNames(services))
                .Concat(RepairBlockingServiceNames)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var serviceName in serviceNames)
            {
                if (SoftwareDisablerStateStore.TryGetCapturedServiceState(serviceName, out var capturedState))
                {
                    snapshot.Services[serviceName] = capturedState;
                    continue;
                }

                var service = services.FirstOrDefault(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
                if (service is null)
                {
                    continue;
                }

                snapshot.Services[serviceName] = new SoftwareDisablerServiceSnapshot
                {
                    StartMode = service.StartType,
                    Running = service.Status is not ServiceControllerStatus.Stopped
                };
            }
        }
        finally
        {
            DisposeServices(services);
        }

        var taskService = TaskService.Instance;
        var processedTaskPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folderPath in OwnedScheduledTaskFolderPaths())
        {
            var folder = taskService.GetFolder(folderPath);
            if (folder is null)
            {
                continue;
            }

            foreach (var task in TasksInFolder(folder))
            {
                if (processedTaskPaths.Add(task.Path) && IsTaskOwnedBySelf(task))
                {
                    snapshot.ScheduledTasks[task.Path] = task.Definition.Settings.Enabled;
                }
            }
        }

        var startupEntryNames = StartupEntryNames.ToArray();
        var startupEntryRoots = GetRoots(StartupEntryRoots);
        foreach (var hive in Hives)
        {
            foreach (var name in GetStartupEntryNames(hive, startupEntryNames, startupEntryRoots))
            {
                var key = StartupEntryKey(hive, name);
                var hadValue = Registry.ValueExists(hive, STARTUP_APPROVED_RUN_KEY, name);
                snapshot.StartupEntries[key] = new SoftwareDisablerStartupEntrySnapshot
                {
                    HadValue = hadValue,
                    Value = hadValue ? Registry.GetValue<byte[]>(hive, STARTUP_APPROVED_RUN_KEY, name, []) : null
                };
            }
        }

        foreach (var package in MatchingAppxPackages())
        {
            snapshot.AppxPackagesDisabled[package.Id.Name] = package.Status.Disabled;
        }

        CaptureAdditionalSnapshot(snapshot);

        return snapshot;
    }

    private protected virtual void CaptureAdditionalSnapshot(SoftwareDisablerSnapshot snapshot) { }

    private protected virtual IEnumerable<string> GetAdditionalEnabledResources() => [];

    private protected virtual Task ApplyAdditionalStateAsync(bool enabled, SoftwareDisablerSnapshot? snapshot) => Task.CompletedTask;

    private async Task ApplyStateAsync(bool enabled, SoftwareDisablerSnapshot? snapshot)
    {
        if (enabled)
        {
            SetDriversEnabled(true, snapshot);
            SetServicesEnabled(true, snapshot);
            RestoreRepairBlockingServices(snapshot);
            SetAppxPackagesEnabled(true, snapshot);
            SetScheduledTasksEnabled(true, snapshot);
            SetStartupEntriesEnabled(true, snapshot);
            await ApplyAdditionalStateAsync(true, snapshot).ConfigureAwait(false);

            return;
        }

        SetScheduledTasksEnabled(false, null);
        SetStartupEntriesEnabled(false, null);
        SetAppxPackagesEnabled(false, null);
        await ApplyAdditionalStateAsync(false, null).ConfigureAwait(false);
        DisableRepairBlockingServices();
        SetServicesEnabled(false, null);
        SetDriversEnabled(false, null);

        await KillProcessesAsync().ConfigureAwait(false);

        SetScheduledTasksEnabled(false, null);
        SetStartupEntriesEnabled(false, null);
        SetAppxPackagesEnabled(false, null);
        await ApplyAdditionalStateAsync(false, null).ConfigureAwait(false);
        DisableRepairBlockingServices();
        SetServicesEnabled(false, null);
        SetDriversEnabled(false, null);

        await KillProcessesAsync().ConfigureAwait(false);

        var stillRunning = RunningProcesses()
            .Concat(RunningRepairBlockingProcesses())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (stillRunning.Length > 0)
        {
            throw new SoftwareDisablerException(string.Format(Resource.SoftwareDisabler_ProcessError_Message, string.Join(", ", stillRunning)));
        }

    }

    private async Task RunAsync(bool enabled, bool captureSnapshot)
    {
        await _operationLock.WaitAsync().ConfigureAwait(false);

        try
        {
            LastFailureReason = null;
            _blockedResources.Clear();
            _notStoppedServices.Clear();

            Log.Instance.Trace($"{(enabled ? "Enabling" : "Disabling")}... [type={GetType().Name}]");

            SoftwareDisablerSnapshot? snapshot = null;

            if (enabled)
            {
                SoftwareDisablerStateStore.TryGetSnapshot(SelfName, out snapshot);
            }
            else
            {
                if (captureSnapshot && !SoftwareDisablerStateStore.IsDisabledByUser(SelfName))
                {
                    snapshot = CaptureSnapshot();
                    SoftwareDisablerStateStore.SetSnapshot(SelfName, snapshot);
                }

                SoftwareDisablerStateStore.SetDisabledByUser(SelfName, true);
            }

            await ApplyStateAsync(enabled, snapshot).ConfigureAwait(false);

            if (enabled)
            {
                SoftwareDisablerStateStore.SetDisabledByUser(SelfName, false);
                SoftwareDisablerStateStore.ClearSnapshot(SelfName);
            }

            SoftwareDisablerOwnership.Invalidate();

            var state = await GetStateAsync().ConfigureAwait(false);

            if (!enabled && (state.Status == SoftwareStatus.Enabled || state.Errors.Length > 0))
            {
                LastFailureReason = BuildFailureReason(state);

                Log.Instance.Trace($"Disabled, restart required. [type={GetType().Name}, services={string.Join(",", state.Services)}, processes={string.Join(",", state.Processes)}, tasks={string.Join(",", state.ScheduledTasks)}, appx={string.Join(",", state.AppxPackages)}, startup={string.Join(",", state.StartupEntries)}, notStopped={string.Join(",", _notStoppedServices)}, keptEnabled={string.Join(",", _blockedResources)}, reason={LastFailureReason}]");
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

    private string BuildFailureReason(EvaluatedState state)
    {
        var reasons = new List<string>();

        if (state.Services.Length > 0)
        {
            reasons.Add($"Services still enabled or running: {string.Join(", ", state.Services)}");
        }

        if (state.Processes.Length > 0)
        {
            reasons.Add($"Processes still running: {string.Join(", ", state.Processes)}");
        }

        if (state.ScheduledTasks.Length > 0)
        {
            reasons.Add($"Scheduled tasks still enabled: {string.Join(", ", state.ScheduledTasks)}");
        }

        if (state.AppxPackages.Length > 0)
        {
            reasons.Add($"AppX packages still enabled: {string.Join(", ", state.AppxPackages)}");
        }

        if (state.StartupEntries.Length > 0)
        {
            reasons.Add($"Startup entries still enabled: {string.Join(", ", state.StartupEntries)}");
        }

        if (state.Errors.Length > 0)
        {
            reasons.Add($"State inspection failed: {string.Join("; ", state.Errors)}");
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

    private static void DisposeServices(IEnumerable<ServiceController> services)
    {
        foreach (var service in services)
        {
            service.Dispose();
        }
    }

    private static bool ServiceExists(string serviceName, IEnumerable<ServiceController> services) =>
        services.Any(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

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

    private static bool IsInstalled(
        IEnumerable<ServiceController> services,
        IEnumerable<string> serviceNames,
        IReadOnlyCollection<WindowsPackage> packages) =>
        serviceNames.Any(s => ServiceExists(s, services)) || packages.Count > 0;

    private IEnumerable<string> EnabledServices(
        IEnumerable<ServiceController> services,
        IEnumerable<string> serviceNames,
        bool includeRepairBlockers) =>
        serviceNames
            .Concat(includeRepairBlockers ? RepairBlockingServiceNames : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(s => IsServiceEnabled(s, services));

    protected virtual IEnumerable<string> RunningProcesses()
    {
        var ownedNames = ProcessNames.ToArray();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var name = string.Empty;
                string? path = null;

                try
                {
                    name = process.ProcessName;
                    path = SoftwareDisablerOwnership.TryGetProcessPath(process);
                }
                catch { }

                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var declared = ownedNames.Any(n => name.StartsWith(n, StringComparison.InvariantCultureIgnoreCase));
                var owner = SoftwareDisablerOwnership.ResolveOwner(path);
                if ((!declared && owner != SelfName) || (declared && owner is not null && owner != SelfName))
                {
                    continue;
                }

                yield return name;
            }
        }
    }

    private static bool IsServiceEnabled(string serviceName, IEnumerable<ServiceController> services)
    {
        try
        {
            var service = services.FirstOrDefault(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
            if (service is null)
            {
                return false;
            }

            return service.Status is not ServiceControllerStatus.Stopped ||
                   service.StartType is not ServiceStartMode.Disabled;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to inspect service state. [service={serviceName}]", ex);
            return true;
        }
    }

    private static string ExtractExecutablePath(string commandLine) => SoftwareDisablerOwnership.ExtractExecutablePath(commandLine);

    private static string NormalizePath(string path) => SoftwareDisablerOwnership.NormalizePath(path);

    private static string[] GetRoots(IEnumerable<string> roots) =>
        roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => NormalizePath(r.Trim()).TrimEnd('\\') + '\\').ToArray();

    private void SetStartupEntriesEnabled(bool enabled, SoftwareDisablerSnapshot? snapshot)
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
                SetStartupEntryEnabled(hive, name, enabled, snapshot);
            }
    }

    private IEnumerable<string> EnabledStartupEntries()
    {
        var startupEntryNames = StartupEntryNames.ToArray();
        var startupEntryRoots = GetRoots(StartupEntryRoots);

        foreach (var hive in Hives)
        {
            foreach (var name in GetStartupEntryNames(hive, startupEntryNames, startupEntryRoots))
            {
                var state = Registry.GetValue<byte[]>(hive, STARTUP_APPROVED_RUN_KEY, name, []);
                if (state.Length == 0 || state[0] != 0x03)
                {
                    yield return $"{hive}\\{name}";
                }
            }
        }
    }

    private IEnumerable<string> RunningRepairBlockingProcesses()
    {
        var names = RepairBlockingProcessNames.ToArray();
        if (names.Length == 0)
        {
            return [];
        }

        return Process.GetProcesses()
            .Select(process =>
            {
                using (process)
                {
                    try
                    {
                        return process.ProcessName;
                    }
                    catch
                    {
                        return null;
                    }
                }
            })
            .Where(name => name is not null && names.Any(n => name.StartsWith(n, StringComparison.InvariantCultureIgnoreCase)))
            .Cast<string>()
            .ToArray();
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

    private static string StartupEntryKey(string hive, string name) => $"{hive}|{name}";

    private void SetStartupEntryEnabled(string hive, string name, bool enabled, SoftwareDisablerSnapshot? snapshot)
    {
        Log.Instance.Trace($"Setting autorun entry {name} to {enabled}. [type={GetType().Name}]");

        try
        {
            if (enabled && snapshot?.StartupEntries.TryGetValue(StartupEntryKey(hive, name), out var state) == true)
            {
                if (state.HadValue)
                {
                    Registry.SetValue(hive, STARTUP_APPROVED_RUN_KEY, name, state.Value ?? [], false, Microsoft.Win32.RegistryValueKind.Binary);
                }
                else
                {
                    Registry.DeleteValue(hive, STARTUP_APPROVED_RUN_KEY, name);
                }
            }
            else
            {
                var value = enabled ? EnabledStartupEntry : CreateDisabledStartupEntry();
                Registry.SetValue(hive, STARTUP_APPROVED_RUN_KEY, name, value, false, Microsoft.Win32.RegistryValueKind.Binary);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set autorun entry {name} in {hive}.", ex);

            throw new SoftwareDisablerException(string.Format(Resource.SoftwareDisabler_StartupEntryError_Message, name), ex);
        }
    }

    private static byte[] CreateDisabledStartupEntry() => [0x03, 0x00, 0x00, 0x00, .. BitConverter.GetBytes(DateTime.Now.ToFileTime())];

    private WindowsPackage[] MatchingAppxPackages()
    {
        var names = AppxPackageNames.ToArray();
        if (names.Length == 0)
        {
            return [];
        }

        return new PackageManager()
            .FindPackagesForUser(string.Empty)
            .Where(p => names.Any(n =>
                p.Id.Name.Equals(n, StringComparison.OrdinalIgnoreCase) ||
                p.Id.Name.EndsWith($".{n}", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static IEnumerable<string> EnabledAppxPackages(IEnumerable<WindowsPackage> packages)
    {
        foreach (var package in packages)
        {
            if (!package.Status.Disabled)
            {
                yield return package.Id.Name;
            }
        }
    }

    private void SetAppxPackagesEnabled(bool enabled, SoftwareDisablerSnapshot? snapshot)
    {
        WindowsPackage[] packages;
        PackageManager packageManager;

        try
        {
            packages = MatchingAppxPackages();
            packageManager = new PackageManager();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to enumerate AppX packages. [type={GetType().Name}]", ex);
            throw new SoftwareDisablerException("Could not enumerate AppX packages.", ex);
        }

        if (packages.Length == 0)
        {
            return;
        }

        foreach (var package in packages)
        {
            try
            {
                Log.Instance.Trace($"Setting AppX package {package.Id.FullName} to {enabled}. [type={GetType().Name}]");

                if (enabled)
                {
                    var wasDisabled = snapshot?.AppxPackagesDisabled.TryGetValue(package.Id.Name, out var disabled) == true && disabled;
                    if (wasDisabled)
                    {
                        packageManager.SetPackageStatus(package.Id.FullName, DeploymentPackageStatus.Disabled);
                    }
                    else
                    {
                        packageManager.ClearPackageStatus(package.Id.FullName, DeploymentPackageStatus.Disabled);
                    }
                }
                else
                {
                    packageManager.SetPackageStatus(package.Id.FullName, DeploymentPackageStatus.Disabled);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to set AppX package {package.Id.FullName} to {enabled}. [type={GetType().Name}]", ex);
                throw new SoftwareDisablerException($"Could not {(enabled ? "enable" : "disable")} AppX package {package.Id.Name}.", ex);
            }
        }
    }

    private void SetScheduledTasksEnabled(bool enabled, SoftwareDisablerSnapshot? snapshot)
    {
        var taskService = TaskService.Instance;
        var processedTaskPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in OwnedScheduledTaskFolderPaths())
        {
            if (!enabled && !SoftwareDisablerOwnership.CanDisable($"task:{NormalizeTaskFolder(path)}", SelfName))
            {
                continue;
            }

            SetTasksInFolderEnabled(taskService, path, enabled, snapshot, processedTaskPaths);
        }
    }

    private IEnumerable<string> EnabledScheduledTasks()
    {
        var taskService = TaskService.Instance;
        var processedTaskPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in OwnedScheduledTaskFolderPaths())
        {
            var folder = taskService.GetFolder(path);
            if (folder is null)
            {
                continue;
            }

            foreach (var task in TasksInFolder(folder))
            {
                if (processedTaskPaths.Add(task.Path) && IsTaskOwnedBySelf(task) && task.Definition.Settings.Enabled)
                {
                    yield return task.Path;
                }
            }
        }
    }

    private void SetTasksInFolderEnabled(
        TaskService taskService,
        string path,
        bool enabled,
        SoftwareDisablerSnapshot? snapshot,
        HashSet<string> processedTaskPaths)
    {
        Log.Instance.Trace($"Setting tasks in folder {path} to {enabled}. [type={GetType().Name}]");

        var folder = taskService.GetFolder(path);
        if (folder is null)
        {
            Log.Instance.Trace($"Folder not found [path={path}, type={GetType().Name}]]");

            return;
        }

        foreach (var task in TasksInFolder(folder))
        {
            if (!processedTaskPaths.Add(task.Path))
            {
                continue;
            }

            if (!IsTaskOwnedBySelf(task))
            {
                Log.Instance.Trace($"Skipping task {task.Name} in {task.Path}, owned by another product. [type={GetType().Name}]");

                continue;
            }

            var targetEnabled = enabled;
            if (enabled && snapshot?.ScheduledTasks.TryGetValue(task.Path, out var wasEnabled) == true)
            {
                targetEnabled = wasEnabled;
            }

            task.Definition.Settings.Enabled = targetEnabled;
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

    private static IEnumerable<Microsoft.Win32.TaskScheduler.Task> TasksInFolder(Microsoft.Win32.TaskScheduler.TaskFolder folder)
    {
        foreach (var task in folder.Tasks.ToArray())
        {
            yield return task;
        }

        foreach (var subFolder in folder.SubFolders.ToArray())
        {
            foreach (var task in TasksInFolder(subFolder))
            {
                yield return task;
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

    private void SetServicesEnabled(bool enabled, SoftwareDisablerSnapshot? snapshot)
    {
        var services = AllServices().ToArray();
        try
        {
            foreach (var serviceName in OwnedServiceNames())
            {
                if (!enabled && !SoftwareDisablerOwnership.CanDisable($"service:{serviceName}", SelfName))
                {
                    _blockedResources.Add(serviceName);

                    Log.Instance.Trace($"Service {serviceName} kept enabled, it is still used by another software. [type={GetType().Name}]");

                    continue;
                }

                SoftwareDisablerServiceSnapshot? serviceSnapshot = null;
                snapshot?.Services.TryGetValue(serviceName, out serviceSnapshot);
                SetServiceEnabled(serviceName, enabled, services, serviceSnapshot);
            }
        }
        finally
        {
            DisposeServices(services);
        }
    }

    private void DisableRepairBlockingServices()
    {
        var services = AllServices().ToArray();
        try
        {
            foreach (var serviceName in RepairBlockingServiceNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                SetServiceEnabled(serviceName, false, services, null);
            }
        }
        finally
        {
            DisposeServices(services);
        }
    }

    private void RestoreRepairBlockingServices(SoftwareDisablerSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        var services = AllServices().ToArray();
        try
        {
            foreach (var serviceName in RepairBlockingServiceNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!snapshot.Services.TryGetValue(serviceName, out var serviceSnapshot))
                {
                    continue;
                }

                var owner = SoftwareDisablerOwnership.Peers()
                    .FirstOrDefault(p => p.Name != SelfName && p.Services.Contains(serviceName, StringComparer.OrdinalIgnoreCase));
                if (owner is not null && SoftwareDisablerStateStore.IsDisabledByUser(owner.Name))
                {
                    Log.Instance.Trace($"Skipping repair-blocking service restore because its owner is disabled. [service={serviceName}, owner={owner.Name}, type={SelfName}]");
                    continue;
                }

                SetServiceEnabled(serviceName, true, services, serviceSnapshot);
            }
        }
        finally
        {
            DisposeServices(services);
        }
    }

    private void SetDriversEnabled(bool enabled, SoftwareDisablerSnapshot? snapshot)
    {
        var services = AllServices().ToArray();
        try
        {
            foreach (var driverName in MatchingDriverNames(services))
            {
                SoftwareDisablerServiceSnapshot? serviceSnapshot = null;
                snapshot?.Services.TryGetValue(driverName, out serviceSnapshot);
                SetServiceEnabled(driverName, enabled, services, serviceSnapshot);
            }
        }
        finally
        {
            DisposeServices(services);
        }
    }

    private void SetServiceEnabled(
        string serviceName,
        bool enabled,
        IEnumerable<ServiceController> services,
        SoftwareDisablerServiceSnapshot? snapshot)
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

                var targetStartMode = enabled && snapshot is not null
                    ? snapshot.StartMode
                    : enabled ? ServiceStartMode.Automatic : ServiceStartMode.Disabled;
                var restoreRunningDisabledService = enabled &&
                                                    snapshot?.Running == true &&
                                                    targetStartMode == ServiceStartMode.Disabled;

                service.ChangeStartMode(restoreRunningDisabledService ? ServiceStartMode.Manual : targetStartMode);
                service.Refresh();

                if (enabled)
                {
                    var shouldRun = snapshot?.Running ?? true;

                    if (shouldRun && service.Status != ServiceControllerStatus.Running)
                    {
                        Log.Instance.Trace($"Starting service {serviceName}... [type={GetType().Name}]");

                        try
                        {
                            service.Start();
                            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));

                            Log.Instance.Trace($"Service {serviceName} started. [startType={service.StartType}, status={service.Status}, type={GetType().Name}]");
                        }
                        catch (Exception ex)
                        {
                            Log.Instance.Trace($"Could not start service {serviceName}, it may be missing its executable. Continuing. [type={GetType().Name}]", ex);
                        }
                    }
                    else if (!shouldRun && service.Status != ServiceControllerStatus.Stopped)
                    {
                        if (!service.CanStop)
                        {
                            throw new InvalidOperationException($"Service {serviceName} cannot be restored to its previous stopped state without a restart.");
                        }

                        Log.Instance.Trace($"Restoring stopped state for service {serviceName}... [type={GetType().Name}]");

                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
                    else
                    {
                        Log.Instance.Trace($"Will not start service {serviceName}. [status={service.Status}, type={GetType().Name}]]");
                    }

                    if (restoreRunningDisabledService)
                    {
                        service.ChangeStartMode(ServiceStartMode.Disabled);
                    }
                }
                else
                {
                    if (service.Status == ServiceControllerStatus.Stopped)
                    {
                        Log.Instance.Trace($"Service {serviceName} is already stopped. [type={GetType().Name}]");
                    }
                    else if (service.CanStop)
                    {
                        Log.Instance.Trace($"Stopping service {serviceName}... [status={service.Status}, type={GetType().Name}]");

                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));

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
        var repairBlockingNames = RepairBlockingProcessNames.ToArray();

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
            var repairBlocking = repairBlockingNames.Any(n => name.StartsWith(n, StringComparison.InvariantCultureIgnoreCase));
            var path = SoftwareDisablerOwnership.TryGetProcessPath(process);
            var owner = SoftwareDisablerOwnership.ResolveOwner(path);

            var isOwned = repairBlocking || (declared
                ? owner is null || owner == SelfName
                : owner == SelfName);

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
