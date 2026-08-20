using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class SpectrumKeyboardBacklightProfileAutomationStep(int state)
    : IAutomationStep<int>
{
    private static readonly int[] OneZoneProfiles = [1, 2, 3];
    private static readonly int[] FullLayoutProfiles = [1, 2, 3, 4, 5, 6];

    private readonly SpectrumKeyboardBacklightController _controller = IoCContainer.Resolve<SpectrumKeyboardBacklightController>();

    public int State { get; } = state;

    public async Task<int[]> GetAllStatesAsync()
    {
        var result = await _controller.Is1ZoneKeyboardAsync().ConfigureAwait(false);
        return result ? OneZoneProfiles : FullLayoutProfiles;
    }

    public Task<bool> IsSupportedAsync() => _controller.IsSupportedAsync();

    public async Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        if (!await _controller.IsSupportedAsync().ConfigureAwait(false))
            return;

        if (!(await GetAllStatesAsync().ConfigureAwait(false)).Contains(State))
            throw new InvalidOperationException(nameof(State));

        await _controller.SetProfileAsync(State).ConfigureAwait(false);

        MessagingCenter.Publish(new SpectrumBacklightChangedMessage());
    }

    public IAutomationStep DeepCopy() => new SpectrumKeyboardBacklightProfileAutomationStep(State);
}
