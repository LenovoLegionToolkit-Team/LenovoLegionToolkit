using System;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

namespace LenovoLegionToolkit.WPF.Windows.Automation.TabItemContent;

public partial class BatteryPercentageAutomationPipelineTriggerTabItemContent : IAutomationPipelineTriggerTabItemContent<IBatteryPercentageAutomationPipelineTrigger>
{
    private readonly IBatteryPercentageAutomationPipelineTrigger _trigger;

    public BatteryPercentageAutomationPipelineTriggerTabItemContent(IBatteryPercentageAutomationPipelineTrigger trigger)
    {
        _trigger = trigger;
        InitializeComponent();
    }

    private void BatteryPercentageAutomationPipelineTriggerTabItemContent_Initialized(object? sender, EventArgs e)
    {
        _percentageValuePicker.Value = _trigger.Percentage;
        _comparisonComboBox.SelectedIndex = _trigger.IsBelow ? 0 : 1;
    }

    public IBatteryPercentageAutomationPipelineTrigger GetTrigger()
    {
        var percentage = (int)(_percentageValuePicker.Value ?? 50);
        var isBelow = _comparisonComboBox.SelectedIndex == 0;

        return _trigger.DeepCopy(percentage, isBelow);
    }
}
