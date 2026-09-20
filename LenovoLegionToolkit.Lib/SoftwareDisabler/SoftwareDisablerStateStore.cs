using System;
using System.Collections.Generic;
using System.IO;
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
}
