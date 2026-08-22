using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class AutoColorManagementAutomationStep(AutoColorManagementState state)
    : AbstractFeatureAutomationStep<AutoColorManagementState>(state)
{
    public override IAutomationStep DeepCopy() => new AutoColorManagementAutomationStep(State);
}
