using System;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public class AutoColorManagementControl : AbstractToggleFeatureCardControl<AutoColorManagementState>
{
    private readonly DisplayConfigurationListener _listener = IoCContainer.Resolve<DisplayConfigurationListener>();

    protected override AutoColorManagementState OnState => AutoColorManagementState.On;

    protected override AutoColorManagementState OffState => AutoColorManagementState.Off;

    public AutoColorManagementControl()
    {
        Icon = SymbolRegular.Color24;
        Title = Resource.AutoColorManagementControl_Title;
        Subtitle = Resource.AutoColorManagementControl_Message;

        _listener.Changed += Listener_Changed;
    }

    protected override async Task OnRefreshAsync()
    {
        await base.OnRefreshAsync();
        Visibility = Visibility.Visible;
    }

    private void Listener_Changed(object? sender, EventArgs e) => Dispatcher.Invoke(async () =>
    {
        if (IsLoaded)
            await RefreshAsync();
    });
}
