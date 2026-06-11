using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Automation.Steps;

public class ITSModeAutomationStepControl : AbstractComboBoxAutomationStepCardControl<ITSMode>
{
    private readonly ITSModeFeature _feature = IoCContainer.Resolve<ITSModeFeature>();

    public ITSModeAutomationStepControl(IAutomationStep<ITSMode> step) : base(step)
    {
        Icon = SymbolRegular.Gauge24;
        Title = Resource.ITSModeAutomationStepControl_Title;
        Subtitle = Resource.ITSModeAutomationStepControl_Message;
    }

    protected override string ComboBoxItemDisplayName(ITSMode value) => _feature.GetITSModeDisplayName(value);
}
