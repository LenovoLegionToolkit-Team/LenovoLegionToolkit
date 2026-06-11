using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Station.Logging;
using LenovoLegionToolkit.Lib.Station.Services;

namespace LenovoLegionToolkit.Lib.Station.Core;

public interface IExtensionContext
{
    INavigationService Navigation { get; }
    IAutomationStepRegistry AutomationSteps { get; }
    IAutomationTriggerRegistry AutomationTriggers { get; }
    IUiDispatcher UiDispatcher { get; }
    IExtensionLogger Logger { get; }

    string GetPluginStoragePath(string pluginId);

    Task<T?> GetSettingAsync<T>(string key);
    Task<bool> SetSettingAsync<T>(string key, T value);
}
