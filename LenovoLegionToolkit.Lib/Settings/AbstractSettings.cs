using System;
using System.IO;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace LenovoLegionToolkit.Lib.Settings;

public abstract class AbstractSettings<T> where T : class, new()
{
    protected readonly JsonSerializerSettings JsonSerializerSettings;
    private readonly string _settingsStorePath;
    private readonly string _fileName;
    private T? _store;

    protected virtual T Default => new();

    public T Store
    {
        get => _store ??= LoadStore() ?? Default;
        protected set => _store = value;
    }

    protected AbstractSettings(string filename)
    {
        JsonSerializerSettings = new()
        {
            Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.Auto,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Converters = { new StringEnumConverter() }
        };

        _fileName = filename;
        _settingsStorePath = Path.Combine(Folders.AppData, _fileName);
    }

    public void Save()
    {
        try
        {
            var settingsSerialized = JsonConvert.SerializeObject(Store, JsonSerializerSettings);
            Folders.EnsureParentDirectoryExists(_settingsStorePath);
            File.WriteAllText(_settingsStorePath, settingsSerialized);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to save {_fileName}", ex);
        }
    }

    public void EnsureFileExists()
    {
        if (!File.Exists(_settingsStorePath))
        {
            Save();
        }
    }

    public void Reset()
    {
        _store = null;
    }

    public virtual T? LoadStore()
    {
        T? store = null;
        try
        {
            if (!File.Exists(_settingsStorePath)) return null;

            var settingsSerialized = File.ReadAllText(_settingsStorePath);
            store = JsonConvert.DeserializeObject<T>(settingsSerialized, JsonSerializerSettings);

            if (store is null)
                TryBackup();
        }
        catch
        {
            TryBackup();
        }

        return store;
    }

    public void SynchronizeStore()
    {
        try
        {
            var settingsSerialized = JsonConvert.SerializeObject(Store, JsonSerializerSettings);
            Folders.EnsureParentDirectoryExists(_settingsStorePath);
            File.WriteAllText(_settingsStorePath, settingsSerialized);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to synchronize {_fileName}", ex);
        }
    }

    private void TryBackup()
    {
        try
        {
            if (!File.Exists(_settingsStorePath))
                return;

            var backupFileName = $"{Path.GetFileNameWithoutExtension(_fileName)}_backup_{DateTime.UtcNow:yyyyMMddHHmmss}{Path.GetExtension(_fileName)}";
            var backupFilePath = Path.Combine(Folders.AppData, backupFileName);
            Folders.EnsureParentDirectoryExists(backupFilePath);
            File.Copy(_settingsStorePath, backupFilePath);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Unable to create backup for {_fileName}", ex);
        }
    }
}
