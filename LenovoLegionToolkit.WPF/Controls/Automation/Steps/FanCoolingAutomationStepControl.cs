using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class FanCoolingAutomationStepControl : AbstractAutomationStepControl
{
    public FanCoolingAutomationStepControl(FanCoolingAutomationStep automationStep) : base(automationStep)
    {
        Icon = SymbolRegular.TopSpeed24;
        Title = Resource.FanCoolingAutomationStepControl_Title;
        Subtitle = Resource.FanCoolingAutomationStepControl_Message;
    }

    public override IAutomationStep CreateAutomationStep() => new FanCoolingAutomationStep();

    protected override UIElement? GetCustomControl() => null;

    protected override void OnFinishedLoading() { }

    protected override Task RefreshAsync() => Task.CompletedTask;
}
