using System;
using System.Linq;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features.Hybrid;

namespace LenovoLegionToolkit.WPF.Windows.Automation.TabItemContent;

public partial class HybridModeAutomationPipelineTriggerTabItemContent : IAutomationPipelineTriggerTabItemContent<IHybridModeAutomationPipelineTrigger>
{
    private readonly HybridModeFeature _feature = IoCContainer.Resolve<HybridModeFeature>();

    private readonly IHybridModeAutomationPipelineTrigger _trigger;
    private readonly HybridModeState _hybridModeState;

    public HybridModeAutomationPipelineTriggerTabItemContent(IHybridModeAutomationPipelineTrigger trigger)
    {
        _trigger = trigger;
        _hybridModeState = trigger.HybridModeState;

        InitializeComponent();
    }

    public IHybridModeAutomationPipelineTrigger GetTrigger()
    {
        var state = _content.Children
            .OfType<RadioButton>()
            .Where(r => r.IsChecked ?? false)
            .Select(r => (HybridModeState)r.Tag)
            .DefaultIfEmpty(HybridModeState.On)
            .FirstOrDefault();
        return _trigger.DeepCopy(state);
    }

    private async void HybridModeAutomationPipelineTriggerTabItemContent_Initialized(object? sender, EventArgs eventArgs)
    {
        var states = await _feature.GetAllStatesAsync();

        foreach (var state in states)
        {
            var radio = new RadioButton
            {
                Content = state.GetDisplayName(),
                Tag = state,
                IsChecked = state == _hybridModeState,
                Margin = new(0, 0, 0, 8)
            };
            _content.Children.Add(radio);
        }
    }
}
