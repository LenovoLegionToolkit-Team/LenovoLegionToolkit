using System;
using System.Collections.Generic;
using System.Linq;
using LenovoLegionToolkit.Lib.Station.Services;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF.Station.Services;

public sealed class AutomationTriggerRegistry : IAutomationTriggerRegistry
{
    private readonly List<ExtensionAutomationTriggerInfo> _triggers = [];

    public IReadOnlyCollection<ExtensionAutomationTriggerInfo> Triggers => _triggers.AsReadOnly();

    public event EventHandler? TriggersChanged;

    public void Register(ExtensionAutomationTriggerInfo item)
    {
        Log.Instance.Trace($"[Extension] Registering automation trigger TriggerType={item.TriggerType.FullName}");

        if (_triggers.Any(t => t.TriggerType == item.TriggerType))
        {
            Log.Instance.Trace($"[Extension] Skipping duplicate automation trigger TriggerType={item.TriggerType.FullName}");
            return;
        }

        _triggers.Add(item);
        Log.Instance.Trace($"[Extension] Automation trigger registered successfully. Total triggers: {_triggers.Count}");
        TriggersChanged?.Invoke(this, EventArgs.Empty);
        Log.Instance.Trace($"[Extension] AutomationTriggers TriggersChanged event raised");
    }
}
