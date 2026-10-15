using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class PowerOperationAutomationStepControl : AbstractComboBoxAutomationStepCardControl<PowerOperationState>
{
    public PowerOperationAutomationStepControl(IAutomationStep<PowerOperationState> step) : base(step)
    {
        Icon = SymbolRegular.Power24;
        Title = Resource.PowerOperationAutomationStepControl_Title;
    }
}