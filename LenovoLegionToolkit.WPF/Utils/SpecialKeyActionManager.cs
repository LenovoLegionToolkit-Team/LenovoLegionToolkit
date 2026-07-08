using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Utils;

public class SpecialKeyActionManager
{
    private readonly SpecialKeySettings _settings;
    private readonly AutomationProcessor _automationProcessor;
    private Action? _bringToForeground;
    private Action? _showSystemInfo;

    private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _pendingPresses = new();
    private static readonly TimeSpan DoublePressInterval = TimeSpan.FromMilliseconds(500);

    public SpecialKeyActionManager(SpecialKeySettings settings, AutomationProcessor automationProcessor)
    {
        _settings = settings;
        _automationProcessor = automationProcessor;
    }

    public void WireUp(SpecialKeyListener listener, Action? bringToForeground = null, Action? showSystemInfo = null)
    {
        _bringToForeground = bringToForeground;
        _showSystemInfo = showSystemInfo;
        listener.CustomKeyHandler = ExecuteAsync;
    }

    public void WireUp(DriverKeyListener listener)
    {
        listener.CustomKeyHandler = ExecuteDriverKeyAsync;
    }

    private async Task<bool> ExecuteAsync(SpecialKey key)
    {
        var keyInt = (int)key;
        if (await ExecuteByCodeAsync(keyInt, key.ToString()).ConfigureAwait(false))
            return true;

        if (key == SpecialKey.FnN)
        {
            _showSystemInfo?.Invoke();
            return true;
        }

        return false;
    }

    private async Task<bool> ExecuteDriverKeyAsync(DriverKey key)
    {
        var keyInt = (int)key + SpecialKeySettings.SpecialKeySettingsStore.DriverKeyCodeOffset;
        return await ExecuteByCodeAsync(keyInt, key.ToString()).ConfigureAwait(false);
    }

    private async Task<bool> ExecuteByCodeAsync(int keyInt, string keyName)
    {
        if (!_settings.Store.KeyModes.TryGetValue(keyInt, out var mode) || mode != CustomSpecialKey.Custom)
            return false;

        Log.Instance.Trace($"Custom action triggered for {keyName} [code={keyInt}]");

        var doubleActions = _settings.Store.KeyDoublePressActions.GetValueOrDefault(keyInt);

        if (doubleActions is null || doubleActions.Count == 0)
        {
            return await ExecuteSinglePressAsync(keyInt, keyName).ConfigureAwait(false);
        }

        if (_pendingPresses.TryGetValue(keyInt, out var tcs))
        {
            tcs.TrySetResult(true);
            return true;
        }

        var newTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_pendingPresses.TryAdd(keyInt, newTcs))
        {
            var delayTask = Task.Delay(DoublePressInterval);
            var completedTask = await Task.WhenAny(newTcs.Task, delayTask).ConfigureAwait(false);

            _pendingPresses.TryRemove(keyInt, out _);

            if (completedTask == delayTask)
            {
                return await ExecuteSinglePressAsync(keyInt, keyName).ConfigureAwait(false);
            }
            else
            {
                return await ExecuteDoublePressAsync(keyInt, keyName).ConfigureAwait(false);
            }
        }

        return false;
    }

    private async Task<bool> ExecuteSinglePressAsync(int keyInt, string keyName)
    {
        var actions = _settings.Store.KeySinglePressActions.GetValueOrDefault(keyInt);

        if (actions is null || actions.Count == 0)
        {
            return true;
        }

        var currentId = actions[0];

        try
        {
            var pipelines = await _automationProcessor.GetPipelinesAsync().ConfigureAwait(false);
            var pipeline = pipelines.FirstOrDefault(p => p.Id == currentId);
            if (pipeline is not null)
            {
                Log.Instance.Trace($"Running pipeline {currentId} for {keyName} (single press)");
                MessagingCenter.Publish(new NotificationMessage(NotificationType.SmartKeySinglePress, pipeline.Name ?? string.Empty));
                await _automationProcessor.RunNowAsync(pipeline.Id).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Running pipeline {currentId} for {keyName} failed.", ex);
        }

        actions.RemoveAt(0);
        actions.Add(currentId);
        _settings.SynchronizeStore();

        return true;
    }

    private async Task<bool> ExecuteDoublePressAsync(int keyInt, string keyName)
    {
        var actions = _settings.Store.KeyDoublePressActions.GetValueOrDefault(keyInt);

        if (actions is null || actions.Count == 0)
            return false;

        var currentId = actions[0];

        try
        {
            var pipelines = await _automationProcessor.GetPipelinesAsync().ConfigureAwait(false);
            var pipeline = pipelines.FirstOrDefault(p => p.Id == currentId);
            if (pipeline is not null)
            {
                Log.Instance.Trace($"Running pipeline {currentId} for {keyName} (double press)");
                MessagingCenter.Publish(new NotificationMessage(NotificationType.SmartKeyDoublePress, pipeline.Name ?? string.Empty));
                await _automationProcessor.RunNowAsync(pipeline.Id).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Running pipeline {currentId} for {keyName} failed.", ex);
        }

        actions.RemoveAt(0);
        actions.Add(currentId);
        _settings.SynchronizeStore();

        return true;
    }
}
