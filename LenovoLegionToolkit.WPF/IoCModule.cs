using Autofac;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.CLI;
using LenovoLegionToolkit.WPF.Settings;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.Lib.Station.Core;
using LenovoLegionToolkit.Lib.Station.Logging;
using LenovoLegionToolkit.Lib.Station.Services;

namespace LenovoLegionToolkit.WPF;

public class IoCModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.Register<MainThreadDispatcher>();

        builder.Register<SpectrumScreenCapture>();

        builder.Register<ThemeManager>().AutoActivate();
        builder.Register<NotificationsManager>().AutoActivate();
        builder.Register<SpecialKeyActionManager>();

        builder.Register<DashboardSettings>();
        builder.Register<SensorsControlSettings>();
        builder.Register<HardwareSensorSettings>();

        builder.Register<IpcServer>();

        builder.RegisterType<Station.Services.NavigationService>().As<INavigationService>().SingleInstance();
        builder.RegisterType<Station.Services.AutomationStepRegistry>().As<IAutomationStepRegistry>().SingleInstance();
        builder.RegisterType<Station.Services.AutomationTriggerRegistry>().As<IAutomationTriggerRegistry>().SingleInstance();
        builder.RegisterType<Station.Core.ExtensionManager>().SingleInstance();
        builder.RegisterType<Station.Core.ExtensionContextFactory>().SingleInstance();
        builder.RegisterType<Station.Logging.ExtensionLogger>().As<IExtensionLogger>();
        builder.RegisterType<Station.Services.UiDispatcher>().As<IUiDispatcher>().SingleInstance();
    }
}
