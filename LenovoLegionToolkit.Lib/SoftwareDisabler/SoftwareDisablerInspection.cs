using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using LenovoLegionToolkit.Lib.System;
using Windows.Management.Deployment;
using TaskService = Microsoft.Win32.TaskScheduler.TaskService;
using WindowsPackage = Windows.ApplicationModel.Package;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

internal sealed class SoftwareDisablerInspection : IDisposable
{
    [ThreadStatic]
    private static SoftwareDisablerInspection? _current;

    private readonly SoftwareDisablerInspection? _previous = _current;
    private readonly Lazy<ServiceController[]> _services = new(() =>
        ServiceController.GetServices().Concat(ServiceController.GetDevices()).ToArray());

    private readonly Lazy<SoftwareDisablerProcessEntry[]> _processes = new(ReadProcesses);

    private readonly Lazy<WindowsPackage[]> _packages = new(() =>
        new PackageManager().FindPackagesForUser(string.Empty).ToArray());

    private readonly Dictionary<string, SoftwareDisablerTaskEntry[]> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _valueNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _serviceStates = new(StringComparer.OrdinalIgnoreCase);

    internal static SoftwareDisablerInspection? Current => _current;

    internal ServiceController[] Services => _services.Value;

    internal SoftwareDisablerProcessEntry[] Processes => _processes.Value;

    internal WindowsPackage[] Packages => _packages.Value;

    internal SoftwareDisablerInspection() => _current = this;

    internal static SoftwareDisablerProcessEntry[] ReadProcesses()
    {
        var peers = SoftwareDisablerOwnership.Peers();
        var result = new List<SoftwareDisablerProcessEntry>();
        var processes = Process.GetProcesses();

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var name = process.ProcessName;
                    var path = SoftwareDisablerOwnership.TryGetProcessPath(process);
                    result.Add(new(name, SoftwareDisablerOwnership.ResolveOwner(path, peers)));
                }
                catch{ /* Ignore */ }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return result.ToArray();
    }

    internal bool ServiceEnabled(string name, Func<bool> read)
    {
        if (_serviceStates.TryGetValue(name, out var enabled))
        {
            return enabled;
        }

        enabled = read();
        _serviceStates[name] = enabled;
        return enabled;
    }

    internal string[] ValueNames(string hive, string key)
    {
        var cacheKey = $@"{hive}\{key}";
        if (_valueNames.TryGetValue(cacheKey, out var names))
        {
            return names;
        }

        names = Registry.GetValueNames(hive, key);
        _valueNames[cacheKey] = names;
        return names;
    }

    internal T Value<T>(string hive, string key, string name, T fallback)
    {
        var cacheKey = $@"{hive}\{key}\{name}";
        if (_values.TryGetValue(cacheKey, out var value))
        {
            return (T)value;
        }

        var result = Registry.GetValue(hive, key, name, fallback);
        _values[cacheKey] = result!;
        return result;
    }

    internal SoftwareDisablerTaskEntry[] TasksInFolder(string path)
    {
        path = SoftwareDisablerOwnership.NormalizeTaskFolder(path);
        if (_tasks.TryGetValue(path, out var cached))
        {
            return cached;
        }

        using var folder = TaskService.Instance.GetFolder(path);
        if (folder is null)
        {
            _tasks[path] = [];
            return [];
        }

        var result = new List<SoftwareDisablerTaskEntry>();
        foreach (var task in folder.Tasks)
        {
            using (task)
            {
                var definition = task.Definition;
                var owners = definition.Actions
                    .OfType<Microsoft.Win32.TaskScheduler.ExecAction>()
                    .Select(a => a.Path)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => SoftwareDisablerOwnership.ResolveOwner(
                        SoftwareDisablerOwnership.ExtractExecutablePath(Environment.ExpandEnvironmentVariables(p))))
                    .OfType<string>()
                    .Distinct()
                    .ToArray();

                result.Add(new(task.Path, definition.Settings.Enabled, owners));
            }
        }

        foreach (var child in folder.SubFolders)
        {
            using (child)
            {
                result.AddRange(TasksInFolder(child.Path));
            }
        }

        var tasks = result.ToArray();
        _tasks[path] = tasks;
        return tasks;
    }

    public void Dispose()
    {
        _current = _previous;

        if (!_services.IsValueCreated)
        {
            return;
        }

        foreach (var service in _services.Value)
        {
            service.Dispose();
        }
    }
}
