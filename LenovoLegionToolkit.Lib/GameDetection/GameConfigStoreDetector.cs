using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.GameDetection;

internal class GameConfigStoreDetector
{
    private const string GAME_CONFIG_STORE_HIVE = "HKEY_CURRENT_USER";
    private const string GAME_CONFIG_STORE_PATH = @"System\GameConfigStore\Children";
    private const string MATCHED_EXE_FULL_PATH_KEY_NAME = "MatchedExeFullPath";

    public class GameDetectedEventArgs(HashSet<ProcessInfo> games) : EventArgs
    {
        public HashSet<ProcessInfo> Games { get; } = games;
    }

    public event EventHandler<GameDetectedEventArgs>? GamesDetected;

    private IAsyncDisposable? _listener;

    public Task StartAsync()
    {
        if (_listener is not null)
            return Task.CompletedTask;

        var lastPaths = GetDetectedGamePaths();

        _listener = Registry.ObserveKey(GAME_CONFIG_STORE_HIVE, GAME_CONFIG_STORE_PATH, true, () =>
        {
            Log.Instance.Trace($"GameConfigStore registry changed...");

            var newPaths = GetDetectedGamePaths();

            if (!newPaths.SetEquals(lastPaths))
            {
                Log.Instance.Trace($"Detected games changed.");

                GamesDetected?.Invoke(this, new(newPaths));
            }

            lastPaths = newPaths;
        });

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_listener is not null)
            await _listener.DisposeAsync().ConfigureAwait(false);
        _listener = null;
    }

    public static HashSet<ProcessInfo> GetDetectedGamePaths()
    {
        Log.Instance.Trace($"Detecting games...");

        var result = new HashSet<ProcessInfo>();

        foreach (var subKey in Registry.GetSubKeys(GAME_CONFIG_STORE_HIVE, GAME_CONFIG_STORE_PATH))
        {
            var exePath = Registry.GetValue(GAME_CONFIG_STORE_HIVE, subKey, MATCHED_EXE_FULL_PATH_KEY_NAME, string.Empty);
            if (string.IsNullOrWhiteSpace(exePath))
                continue;

            exePath = Environment.ExpandEnvironmentVariables(exePath.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(exePath))
                continue;

            try
            {
                exePath = Path.GetFullPath(exePath);
            }
            catch { }

            result.Add(ProcessInfo.FromPath(exePath));
        }

        Log.Instance.Trace($"Detected games:");
        foreach (var r in result)
            Log.Instance.Trace($" - {r}");

        return result;
    }
}
