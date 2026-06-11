using LenovoLegionToolkit.Lib.Station.Core;
using LenovoLegionToolkit.Lib.Station.Logging;
using LenovoLegionToolkit.Lib.Station.Services;

namespace LenovoLegionToolkit.WPF.Station.Core;

public sealed class ExtensionContextFactory
{
    private readonly INavigationService _navigationService;
    private readonly IAutomationStepRegistry _automationStepRegistry;
    private readonly IAutomationTriggerRegistry _automationTriggerRegistry;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IExtensionLogger _logger;

    public ExtensionContextFactory(INavigationService navigationService, IAutomationStepRegistry automationStepRegistry, IAutomationTriggerRegistry automationTriggerRegistry, IUiDispatcher uiDispatcher, IExtensionLogger logger)
    {
        _navigationService = navigationService;
        _automationStepRegistry = automationStepRegistry;
        _automationTriggerRegistry = automationTriggerRegistry;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
    }

    public IExtensionContext Create(string pluginId) => new ExtensionContext(pluginId, _navigationService, _automationStepRegistry, _automationTriggerRegistry, _uiDispatcher, _logger);
}
