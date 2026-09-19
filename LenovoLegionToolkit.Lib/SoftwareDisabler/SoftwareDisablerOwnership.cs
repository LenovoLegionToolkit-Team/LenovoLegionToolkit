using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using TaskService = Microsoft.Win32.TaskScheduler.TaskService;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

internal static class SoftwareDisablerOwnership
{
    private const string SERVICES_SUB_KEY = @"SYSTEM\CurrentControlSet\Services";
    private const string TASK_ROOT = @"\Lenovo";

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private static readonly object _lock = new();

    private static Peer[]? _peers;
    private static Dictionary<string, string>? _serviceImages;
    private static DateTime _serviceImagesAt;
    private static Dictionary<string, string[]>? _taskFolderExecutables;
    private static DateTime _taskFolderExecutablesAt;

    internal sealed record Peer(
        string Name,
        string[] Roots,
        string[] Markers,
        string[] Services,
        string[] TaskFolders);

    internal static Peer[] Peers()
    {
        lock (_lock)
            return _peers ??= BuildPeers();
    }

    private static Peer[] BuildPeers()
    {
        AbstractSoftwareDisabler[] instances =
        [
            new VantageDisabler(),
            new LegionZoneDisabler(),
            new LegionSpaceDisabler(),
            new SmartEngineDisabler(),
            new FnKeysDisabler()
        ];

        return instances.Select(i => i.ToOwnershipPeer()).ToArray();
    }

    internal static void Invalidate()
    {
        lock (_lock)
        {
            _serviceImages = null;
            _taskFolderExecutables = null;
        }
    }

    private static bool IsFresh(DateTime timestamp) => DateTime.UtcNow - timestamp < CacheLifetime;

    internal static string? ResolveOwner(string? binaryPath) => ResolveOwner(binaryPath, Peers());

    internal static string? ResolveOwner(string? binaryPath, Peer[] peers)
    {
        if (string.IsNullOrWhiteSpace(binaryPath))
        {
            return null;
        }

        var path = NormalizePath(binaryPath);
        if (path.Length == 0)
        {
            return null;
        }

        var best = null as string;
        var bestLength = -1;

        foreach (var peer in peers)
        {
            foreach (var root in peer.Roots)
            {
                if (root.Length <= bestLength || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                best = peer.Name;
                bestLength = root.Length;
            }
        }

        if (best is not null)
        {
            return best;
        }

        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var bestMarkerLength = -1;

        foreach (var peer in peers)
        {
            foreach (var marker in peer.Markers)
            {
                if (marker.Length <= bestMarkerLength || !HasSegment(segments, marker))
                {
                    continue;
                }

                best = peer.Name;
                bestMarkerLength = marker.Length;
            }
        }

        return best;
    }

    private static bool HasSegment(string[] segments, string marker)
    {
        foreach (var segment in segments)
        {
            var end = segment.LastIndexOf('.');
            var name = end > 0 ? segment[..end] : segment;

            if (name.Equals(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static HashSet<string> ResolveOwnedServices(
        IEnumerable<string> declared,
        IReadOnlyDictionary<string, string> images,
        Peer[] peers,
        string selfName)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var serviceName in declared)
        {
            images.TryGetValue(serviceName, out var imagePath);

            var owner = ResolveOwner(imagePath, peers);
            if (owner is not null && owner != selfName)
            {
                Log.Instance.Trace($"Service {serviceName} is owned by {owner}. [imagePath={imagePath}, type={selfName}]");

                continue;
            }

            result.Add(serviceName);
        }

        foreach (var (serviceName, imagePath) in images)
        {
            if (ResolveOwner(imagePath, peers) == selfName)
            {
                result.Add(serviceName);
            }
        }

        return result;
    }

    internal static HashSet<string> ResolveOwnedTaskFolders(
        IEnumerable<string> declared,
        IReadOnlyDictionary<string, string[]> executablesByFolder,
        Peer[] peers,
        string selfName)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in declared)
        {
            if (executablesByFolder.TryGetValue(NormalizeTaskFolder(path), out var executables))
            {
                var owners = executables.Select(e => ResolveOwner(e, peers)).Where(o => o is not null).Distinct().ToArray();

                if (owners.Length > 0 && !owners.Contains(selfName))
                {
                    Log.Instance.Trace($"Task folder {path} is owned by {string.Join(",", owners)}. [type={selfName}]");

                    continue;
                }
            }

            result.Add(path);
        }

        foreach (var (folder, executables) in executablesByFolder)
        {
            if (executables.Select(e => ResolveOwner(e, peers)).Any(o => o == selfName))
            {
                result.Add(folder);
            }
        }

        return result;
    }

    internal static bool CanDisable(string resourceKey, string selfName) => CanDisable(resourceKey, selfName, Peers(), ServiceImages());

    internal static bool CanDisable(string resourceKey, string selfName, Peer[] peers) => CanDisable(resourceKey, selfName, peers, ServiceImages());

    internal static bool CanDisable(string resourceKey, string selfName, Peer[] peers, IReadOnlyDictionary<string, string> images)
    {
        var blocking = Claimants(resourceKey, selfName, peers)
            .Where(c => IsActive(c, peers, images))
            .Where(c => !SoftwareDisablerStateStore.IsDisabledByUser(c))
            .ToArray();

        if (blocking.Length == 0)
        {
            return true;
        }

        Log.Instance.Trace($"Keeping shared resource {resourceKey} enabled, still used by {string.Join(", ", blocking)}. [type={selfName}]");

        return false;
    }

    private static bool IsActive(string peerName, Peer[] peers, IReadOnlyDictionary<string, string> images)
    {
        var peer = peers.FirstOrDefault(p => p.Name == peerName);
        if (peer is null)
        {
            return false;
        }

        if (ResolveOwnedServices(peer.Services, images, peers, peer.Name).Count > 0)
        {
            return true;
        }

        return peer.Roots.Any(Directory.Exists);
    }

    private static string[] Claimants(string resourceKey, string selfName, Peer[] peers)
    {
        var separator = resourceKey.IndexOf(':');
        if (separator < 0)
        {
            return [];
        }

        var kind = resourceKey[..separator];
        var name = resourceKey[(separator + 1)..];

        return peers
            .Where(p => p.Name != selfName)
            .Where(p => kind switch
            {
                "service" => p.Services.Contains(name, StringComparer.OrdinalIgnoreCase),
                "task" => p.TaskFolders.Contains(name, StringComparer.OrdinalIgnoreCase),
                _ => false
            })
            .Select(p => p.Name)
            .ToArray();
    }

    internal static IReadOnlyDictionary<string, string> ServiceImages()
    {
        lock (_lock)
        {
            if (_serviceImages is not null && IsFresh(_serviceImagesAt))
            {
                return _serviceImages;
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var subKey in Registry.GetSubKeys("HKEY_LOCAL_MACHINE", SERVICES_SUB_KEY))
                {
                    var imagePath = Registry.GetValue("HKEY_LOCAL_MACHINE", subKey, "ImagePath", string.Empty);
                    if (string.IsNullOrWhiteSpace(imagePath))
                    {
                        continue;
                    }

                    result[Path.GetFileName(subKey)] = ExtractExecutablePath(imagePath);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to read service image paths.", ex);
            }

            Log.Instance.Trace($"Cached {result.Count} service image paths.");

            _serviceImagesAt = DateTime.UtcNow;

            return _serviceImages = result;
        }
    }

    internal static IReadOnlyDictionary<string, string[]> TaskFolderExecutables()
    {
        lock (_lock)
        {
            if (_taskFolderExecutables is not null && IsFresh(_taskFolderExecutablesAt))
            {
                return _taskFolderExecutables;
            }

            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var root = TaskService.Instance.GetFolder(TASK_ROOT);
                if (root is not null)
                {
                    CollectTaskFolder(root, result);
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to read scheduled task actions.", ex);
            }

            Log.Instance.Trace($"Cached {result.Count} scheduled task folders.");

            _taskFolderExecutablesAt = DateTime.UtcNow;

            return _taskFolderExecutables = result.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void CollectTaskFolder(Microsoft.Win32.TaskScheduler.TaskFolder folder, Dictionary<string, List<string>> result)
    {
        foreach (var task in folder.Tasks.ToArray())
        {
            var folderPath = Path.GetDirectoryName(task.Path.TrimStart('\\')) ?? string.Empty;
            if (folderPath.Length == 0)
            {
                continue;
            }

            if (!result.TryGetValue(folderPath, out var executables))
            {
                result[folderPath] = executables = [];
            }

            foreach (var action in task.Definition.Actions.OfType<Microsoft.Win32.TaskScheduler.ExecAction>())
            {
                if (string.IsNullOrWhiteSpace(action.Path))
                {
                    continue;
                }

                executables.Add(ExtractExecutablePath(Environment.ExpandEnvironmentVariables(action.Path)));
            }
        }

        foreach (var subFolder in folder.SubFolders.ToArray())
        {
            CollectTaskFolder(subFolder, result);
        }
    }

    internal static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    internal static string NormalizeTaskFolder(string path) => path.TrimStart('\\').ToLowerInvariant();

    internal static string ExtractExecutablePath(string commandLine)
    {
        var value = commandLine.Trim();

        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            if (end > 1)
            {
                value = value[1..end];
            }
        }
        else
        {
            var end = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (end >= 0)
            {
                value = value[..(end + 4)];
            }
        }

        return NormalizePath(value);
    }

    internal static string NormalizePath(string path)
    {
        var result = Environment.ExpandEnvironmentVariables(path);

        if (result.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            result = result[4..];
        }

        if (result.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            result = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), result[12..]);
        }

        if (result.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return result;
        }

        while (result.Contains(@"\\", StringComparison.Ordinal))
        {
            result = result.Replace(@"\\", @"\");
        }

        return result;
    }
}
