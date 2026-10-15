using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

/// <summary>
/// Combo-box control for selecting the power operation (shutdown, restart, sleep, hibernate)
/// performed by a <see cref="PowerOperationAutomationStep"/>.
/// </summary>
public class PowerOperationAutomationStepControl : AbstractComboBoxAutomationStepCardControl<PowerOperationState>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PowerOperationAutomationStepControl"/> class.
    /// </summary>
    /// <param name="step">The power operation automation step to bind to this control.</param>
    public PowerOperationAutomationStepControl(IAutomationStep<PowerOperationState> step) : base(step)
    {
        Icon = SymbolRegular.Power24;
        Title = Resource.PowerOperationAutomationStepControl_Title;
    }
}