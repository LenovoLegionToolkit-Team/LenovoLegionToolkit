using System;
using Humanizer;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.WPF.Extensions;

namespace LenovoLegionToolkit.WPF.Windows.Automation.TabItemContent;

public partial class UserInactivityPipelineTriggerTabItemContent : IAutomationPipelineTriggerTabItemContent<IUserInactivityPipelineTrigger>
{
    /// <summary>
    /// The inactivity timeout options presented to the user, ranging from 10 seconds to 2 hours.
    /// </summary>
    private static readonly TimeSpan[] TimeSpans =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(3),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(60),
        TimeSpan.FromMinutes(90),
        TimeSpan.FromMinutes(120)
    ];

    private readonly IUserInactivityPipelineTrigger _trigger;

    /// <summary>
    /// Initializes the inactivity trigger tab item, populating the timeout combo box with
    /// human-readable labels (e.g. "1 hour 30 minutes" for a 90-minute timeout).
    /// </summary>
    /// <param name="trigger">The user inactivity pipeline trigger to edit.</param>
    public UserInactivityPipelineTriggerTabItemContent(IUserInactivityPipelineTrigger trigger)
    {
        _trigger = trigger;
        InitializeComponent();

        _timeoutComboBox.SetItems(TimeSpans, trigger.InactivityTimeSpan, t => t.Humanize(2, collectionSeparator: " "));
    }

    /// <summary>
    /// Builds a new inactivity trigger using the timeout selected in the combo box,
    /// defaulting to 30 seconds when no valid selection is present.
    /// </summary>
    /// <returns>A deep copy of the trigger configured with the selected timeout.</returns>
    public IUserInactivityPipelineTrigger GetTrigger()
    {
        var state = _timeoutComboBox.TryGetSelectedItem(out TimeSpan tt)
            ? tt
            : TimeSpan.FromSeconds(30);
        return _trigger.DeepCopy(state);
    }
}