using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;
using Resource = LenovoLegionToolkit.Lib.Resources.Resource;

namespace LenovoLegionToolkit.Lib.SoftwareDisabler;

public static class SoftwareDisablerStateStore
{
    private class Store
    {
        public Dictionary<string, bool> DisabledByUser { get; set; } = [];
        public Dictionary<string, SoftwareDisablerSnapshot> Snapshots { get; set; } = [];
    }

    private static readonly object _lock = new();
    private static readonly string StorePath = Path.Combine(Folders.AppData, "software_disabler_state.json");

    private static Store? _store;

    public static bool IsDisabledByUser(string disablerName)
    {
        lock (_lock)
            return LoadStore().DisabledByUser.TryGetValue(disablerName, out var disabled) && disabled;
    }

    public static bool TryGetDisabledByUser(string disablerName, out bool disabled)
    {
        lock (_lock)
            return LoadStore().DisabledByUser.TryGetValue(disablerName, out disabled);
    }

    public static void SetDisabledByUser(string disablerName, bool disabled)
    {
        lock (_lock)
        {
            var store = LoadStore();

            if (store.DisabledByUser.TryGetValue(disablerName, out var current) && current == disabled)
            {
                return;
            }

            store.DisabledByUser[disablerName] = disabled;
            SaveStore(store, disablerName);

            Log.Instance.Trace($"Disable intent saved. [type={disablerName}, disabled={disabled}]");
        }
    }

    internal static bool TryGetSnapshot(string disablerName, out SoftwareDisablerSnapshot snapshot)
    {
        lock (_lock)
        {
            if (!LoadStore().Snapshots.TryGetValue(disablerName, out snapshot!))
            {
                return false;
            }

            NormalizeSnapshot(snapshot);
            return true;
        }
    }

    internal static void SetSnapshot(string disablerName, SoftwareDisablerSnapshot snapshot)
    {
        lock (_lock)
        {
            var store = LoadStore();
            store.Snapshots[disablerName] = snapshot;
            SaveStore(store, disablerName);
        }
    }

    internal static bool TryGetCapturedServiceState(string serviceName, out SoftwareDisablerServiceSnapshot serviceSnapshot)
    {
        lock (_lock)
        {
            var store = LoadStore();
            foreach (var (disablerName, snapshot) in store.Snapshots)
            {
                if (!SoftwareDisablerOwnership.Peers().Any(peer => peer.Name == disablerName) ||
                    !store.DisabledByUser.TryGetValue(disablerName, out var disabled) || !disabled)
                {
                    continue;
                }

                NormalizeSnapshot(snapshot);
                var state = snapshot.Services.FirstOrDefault(kv => kv.Key.Equals(serviceName, StringComparison.OrdinalIgnoreCase)).Value;
                if (state is null)
                {
                    continue;
                }

                serviceSnapshot = new()
                {
                    StartMode = state.StartMode,
                    Running = state.Running
                };
                return true;
            }

            serviceSnapshot = null!;
            return false;
        }
    }

    internal static void ClearSnapshot(string disablerName)
    {
        lock (_lock)
        {
            var store = LoadStore();
            if (!store.Snapshots.Remove(disablerName))
            {
                return;
            }

            SaveStore(store, disablerName);
        }
    }

    public static bool ResolveToggleState(string disablerName, SoftwareStatus status)
    {
        if (!TryGetDisabledByUser(disablerName, out var disabledByUser))
        {
            return status == SoftwareStatus.Disabled;
        }

        if (disabledByUser && status == SoftwareStatus.Enabled)
        {
            Log.Instance.Trace($"Disabled by user but still running, a restart may be required. [type={disablerName}]");
        }

        return disabledByUser;
    }

    private static Store LoadStore()
    {
        if (_store is not null)
        {
            return _store;
        }

        try
        {
            if (File.Exists(StorePath))
            {
                _store = JsonConvert.DeserializeObject<Store>(File.ReadAllText(StorePath)) ?? new Store();
                _store.DisabledByUser ??= [];
                _store.Snapshots ??= [];
            }
            else
            {
                _store = new Store();
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to load software disabler state.", ex);
            _store = new Store();
        }

        return _store;
    }

    private static void SaveStore(Store store, string disablerName)
    {
        var tempPath = StorePath + ".tmp";

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Folders.EnsureParentDirectoryExists(StorePath);

                File.WriteAllText(tempPath, JsonConvert.SerializeObject(store, Formatting.Indented));
                File.Move(tempPath, StorePath, true);

                _store = store;

                return;
            }
            catch (Exception ex) when (attempt < 3)
            {
                Log.Instance.Trace($"Failed to save software disabler state, retrying. [attempt={attempt}, type={disablerName}]", ex);

                DeleteTemp(tempPath);

                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to save software disabler state. [type={disablerName}]", ex);

                DeleteTemp(tempPath);

                throw new SoftwareDisablerException(Resource.SoftwareDisabler_StateError_Message, ex);
            }
        }
    }

    private static void DeleteTemp(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to delete temporary software disabler state. [path={tempPath}]", ex);
        }
    }

    private static void NormalizeSnapshot(SoftwareDisablerSnapshot snapshot)
    {
        snapshot.Services ??= [];
        snapshot.ScheduledTasks ??= [];
        snapshot.StartupEntries ??= [];
        snapshot.AppxPackagesDisabled ??= [];
        snapshot.AdditionalValues ??= [];
    }
}
