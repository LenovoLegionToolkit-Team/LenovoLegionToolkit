using System;
using System.IO;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Station.Logging;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Station.Logging;

public sealed class ExtensionLogger : IExtensionLogger
{
    private readonly string _pluginId;
    private readonly string _logPath;
    private readonly object _lock = new();

    public ExtensionLogger(string pluginId)
    {
        _pluginId = pluginId;
        var logFolder = Path.Combine(Folders.AppData, "log");
        Directory.CreateDirectory(logFolder);

        _logPath = Path.Combine(logFolder, $"plugin_{_pluginId}.log");
        var oldLogPath = Path.Combine(logFolder, $"plugin_{_pluginId}.old.log");

        try
        {
            if (File.Exists(_logPath))
            {
                File.Move(_logPath, oldLogPath, overwrite: true);
            }
            File.WriteAllText(_logPath, $"--- Plugin '{_pluginId}' Log Started at {DateTime.Now} ---\n");
        }
        catch
        {
            try { File.Delete(_logPath); } catch { }
        }
    }

    public void Trace(string message)
    {
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:dd/MM/yyyy HH:mm:ss.fff}] {message}\n");
            }
            catch (DirectoryNotFoundException)
            {
                try
                {
                    Folders.EnsureParentDirectoryExists(_logPath);
                    File.AppendAllText(_logPath, $"[{DateTime.Now:dd/MM/yyyy HH:mm:ss.fff}] {message}\n");
                }
                catch
                {
                    Log.Instance.Trace($"[Extension.{_pluginId}] {message}");
                }
            }
            catch
            {
                Log.Instance.Trace($"[Extension.{_pluginId}] {message}");
            }
        }
    }

    public void Error(string message, Exception exception)
    {
        lock (_lock)
        {
            var formatted = $"[{DateTime.Now:dd/MM/yyyy HH:mm:ss.fff}] ERROR: {message}\n{exception}";
            try
            {
                File.AppendAllText(_logPath, formatted + "\n");
            }
            catch (DirectoryNotFoundException)
            {
                try
                {
                    Folders.EnsureParentDirectoryExists(_logPath);
                    File.AppendAllText(_logPath, formatted + "\n");
                }
                catch
                {
                    Log.Instance.ErrorReport($"[Extension.{_pluginId}] {message}", exception);
                }
            }
            catch
            {
                Log.Instance.ErrorReport($"[Extension.{_pluginId}] {message}", exception);
            }
        }
    }
}
