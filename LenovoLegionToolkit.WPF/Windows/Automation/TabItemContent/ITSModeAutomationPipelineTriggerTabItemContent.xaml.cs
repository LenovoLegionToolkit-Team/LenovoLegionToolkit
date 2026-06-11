using System;
using System.Linq;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;

namespace LenovoLegionToolkit.WPF.Windows.Automation.TabItemContent;

public partial class ITSModeAutomationPipelineTriggerTabItemContent : IAutomationPipelineTriggerTabItemContent<IITSModeAutomationPipelineTrigger>
{
    private readonly ITSModeFeature _feature = IoCContainer.Resolve<ITSModeFeature>();

    private readonly IITSModeAutomationPipelineTrigger _trigger;
    private readonly ITSMode _powerModeState;

    public ITSModeAutomationPipelineTriggerTabItemContent(IITSModeAutomationPipelineTrigger trigger)
    {
        _trigger = trigger;
        _powerModeState = trigger.ITSModeState;

        InitializeComponent();
    }

    public IITSModeAutomationPipelineTrigger GetTrigger()
    {
        var state = _content.Children
            .OfType<RadioButton>()
            .Where(r => r.IsChecked ?? false)
            .Select(r => (ITSMode)r.Tag)
            .DefaultIfEmpty(ITSMode.ItsAuto)
            .FirstOrDefault();
        return _trigger.DeepCopy(state);
    }

    private async void ITSModeAutomationPipelineTriggerTabItemContent_Initialized(object? sender, EventArgs eventArgs)
    {
        var states = await _feature.GetAllStatesAsync();

        foreach (var state in states)
        {
            var radio = new RadioButton
            {
                Content = _feature.GetITSModeDisplayName(state),
                Tag = state,
                IsChecked = state == _powerModeState,
                Margin = new(0, 0, 0, 8)
            };
            _content.Children.Add(radio);
        }
    }
}
