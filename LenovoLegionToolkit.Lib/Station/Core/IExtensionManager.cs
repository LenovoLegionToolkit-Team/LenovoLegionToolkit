using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Station.Core;

public interface IExtensionManager
{
    IReadOnlyCollection<IExtensionProvider> Providers { get; }

    bool HasProvider(string capability);

    string? GetProviderVersion(string capability);
}
