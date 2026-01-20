using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Features.Hybrid.Notify;
using LenovoLegionToolkit.Lib.Features.PanelLogo;
using LenovoLegionToolkit.Lib.Features.WhiteKeyboardBacklight;
using LenovoLegionToolkit.Lib.Integrations;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Macro;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Overclocking.Amd;
using LenovoLegionToolkit.Lib.Services;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.CLI;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows;
using LenovoLegionToolkit.WPF.Windows.FloatingGadgets;
using LenovoLegionToolkit.WPF.Windows.Utils;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using WinFormsApp = System.Windows.Forms.Application;
using WinFormsHighDpiMode = System.Windows.Forms.HighDpiMode;

namespace LenovoLegionToolkit.WPF;

public partial class App
{
    #region P/Invoke

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    private const int ATTACH_PARENT_PROCESS = -1;

    #endregion

    #region Constants & Fields

    private const string MUTEX_NAME = "LenovoLegionToolkit_Mutex_6efcc882-924c-4cbc-8fec-f45c25696f98";
    private const string EVENT_NAME = "LenovoLegionToolkit_Event_6efcc882-924c-4cbc-8fec-f45c25696f98";

    public Window? FloatingGadget;

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _singleInstanceWaitHandle;
    private bool _showPawnIONotify;

    public new static App Current => (App)Application.Current;
    public static MainWindow? MainWindowInstance;
    public static Flags? Flags;

    #endregion

    #region Startup & Exit Logic

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        try
        {
#if DEBUG
            if (Debugger.IsAttached)
            {
                Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)
                    .Where(p => p.Id != Environment.ProcessId)
                    .ToList()
                    .ForEach(p => { p.Kill(); p.WaitForExit(); });
            }
#endif
            Flags = new Flags(e.Args);
            SetupExceptionHandling();

            if (Flags.Debug)
            {
                InitializeDebugConsole();
                Console.WriteLine(@$"[Startup] Parsing Flags complete. TraceEnabled: {Flags.IsTraceEnabled}");
            }

            Log.Instance.IsTraceEnabled = Flags.IsTraceEnabled;

            if (Flags.Debug) Console.WriteLine(@"[Startup] Ensuring Single Instance...");
            EnsureSingleInstance();

            if (Flags.Debug) Console.WriteLine(@"[Startup] Initializing IoC Container...");
            IoCContainer.Initialize(
                new Lib.IoCModule(),
                new Lib.Automation.IoCModule(),
                new Lib.Macro.IoCModule(),
                new IoCModule()
            );

            if (Flags.Debug) Console.WriteLine(@"[Startup] Setting Language and Checking Compatibility...");
            await Task.WhenAll(
                LocalizationHelper.SetLanguageAsync(true),
                CheckCompatibilityAsyncWrapper(Flags)
            );

            Log.Instance.Trace($"Starting... [version={Assembly.GetEntryAssembly()?.GetName().Version}, build={Assembly.GetEntryAssembly()?.GetBuildDateTimeString()}, os={Environment.OSVersion}, dotnet={Environment.Version}]");

            if (Flags.Debug) Console.WriteLine(@"[Startup] Configuring Render Options...");
            WinFormsApp.SetHighDpiMode(WinFormsHighDpiMode.PerMonitorV2);
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            ConfigureFeatureFlags();

            if (Flags.Debug) Console.WriteLine(@"[Startup] Initializing Features...");
            var initTasks = new[]
            {
                InitSensorsGroupControllerFeatureAsync(),
                LogSoftwareStatusAsync(),
                InitPowerModeFeatureAsync(),
                InitITSModeFeatureAsync(),
                InitBatteryFeatureAsync(),
                InitRgbKeyboardControllerAsync(),
                InitSpectrumKeyboardControllerAsync(),
                InitGpuOverclockControllerAsync(),
                InitHybridModeAsync(),
                InitAutomationProcessorAsync(),
            };

            await Task.WhenAll(initTasks);

            if (Flags.Debug) Console.WriteLine(@"[Startup] Starting MacroController...");
            IoCContainer.Resolve<MacroController>().Start();

            var deferredInitTask = Task.Run(async () =>
            {
                if (Flags.Debug) Console.WriteLine(@"[AsyncWorker] Starting AI/HWiNFO/IPC...");
                await IoCContainer.Resolve<AIController>().StartIfNeededAsync();
                await IoCContainer.Resolve<HWiNFOIntegration>().StartStopIfNeededAsync();
                await IoCContainer.Resolve<IpcServer>().StartStopIfNeededAsync();
            });

            await InitSetPowerMode();

#if !DEBUG
            Autorun.Validate();
#endif
            if (Flags.Debug) Console.WriteLine(@"[Startup] Creating MainWindow...");
            var mainWindow = new MainWindow
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                TrayTooltipEnabled = !Flags.DisableTrayTooltip,
                DisableConflictingSoftwareWarning = Flags.DisableConflictingSoftwareWarning
            };

            MainWindow = mainWindow;
            MainWindowInstance = mainWindow;

            IoCContainer.Resolve<ThemeManager>().Apply();
            InitSetLogIndicator();

            _ = new DialogWindow();

            await InitAMDOverclocking();

            if (Flags.Minimized)
            {
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.Show();
                mainWindow.SendToTray();
            }
            else
            {
                mainWindow.Show();
                if (_showPawnIONotify)
                {
                    PawnIOHelper.ShowPawnIONotify();
                }
            }

            await deferredInitTask;

            await Dispatcher.InvokeAsync(() =>
            {
                if (Log.Instance.IsTraceEnabled)
                {
                    Log.Instance.Trace($"Lenovo Legion Toolkit Version {Assembly.GetEntryAssembly()?.GetName().Version}");
                }

                Compatibility.PrintControllerVersionAsync().ConfigureAwait(false);
                InitFloatingGadget();
                if (Flags.Debug)
                {
                    Console.WriteLine(@"[Startup] Startup Complete.");
                }
            });
        }
        catch (Exception ex)
        {
            if (Flags?.Debug == true)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(@$"CRITICAL EXCEPTION: {ex}");
                Console.ResetColor();
            }
            HandleCriticalStartupError(ex);
        }
    }

    private void InitializeDebugConsole()
    {
        if (!AttachConsole(ATTACH_PARENT_PROCESS))
        {
            AllocConsole();
        }
    }

    private void ConfigureFeatureFlags()
    {
        IoCContainer.Resolve<HttpClientFactory>().SetProxy(Flags!.ProxyUrl, Flags.ProxyUsername, Flags.ProxyPassword, Flags.ProxyAllowAllCerts);
        IoCContainer.Resolve<PowerModeFeature>().AllowAllPowerModesOnBattery = Flags.AllowAllPowerModesOnBattery;
        IoCContainer.Resolve<RGBKeyboardBacklightController>().ForceDisable = Flags.ForceDisableRgbKeyboardSupport;
        IoCContainer.Resolve<SpectrumKeyboardBacklightController>().ForceDisable = Flags.ForceDisableSpectrumKeyboardSupport;
        IoCContainer.Resolve<WhiteKeyboardLenovoLightingBacklightFeature>().ForceDisable = Flags.ForceDisableLenovoLighting;
        IoCContainer.Resolve<PanelLogoLenovoLightingBacklightFeature>().ForceDisable = Flags.ForceDisableLenovoLighting;
        IoCContainer.Resolve<PortsBacklightFeature>().ForceDisable = Flags.ForceDisableLenovoLighting;
        IoCContainer.Resolve<IGPUModeFeature>().ExperimentalGPUWorkingMode = Flags.ExperimentalGPUWorkingMode;
        IoCContainer.Resolve<DGPUNotify>().ExperimentalGPUWorkingMode = Flags.ExperimentalGPUWorkingMode;
        IoCContainer.Resolve<UpdateChecker>().Disable = Flags.DisableUpdateChecker;
    }

    private void HandleCriticalStartupError(Exception ex)
    {
        var errorMsg = $"CRITICAL STARTUP ERROR:\n{ex}";

        if (Flags is { Debug: true })
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(@$"\n{new string('=', 30)}\n{errorMsg}\n{new string('=', 30)}");
            Console.ResetColor();
            Console.WriteLine(@"\nPress ENTER to exit...");
            try
            {
                Console.ReadLine();
            }
            catch { /* Ignore */ }
        }
        else
        {
            try
            {
                MessageBox.Show(errorMsg, "Lenovo Legion Toolkit - Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* Ignore */ }
        }

        Environment.Exit(-1);
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _singleInstanceMutex?.Close();
    }

    public async Task ShutdownAsync()
    {
        await SafeExecuteAsync<AIController>(c => c.StopAsync());

        await SafeExecuteAsync<RGBKeyboardBacklightController>(async c =>
        {
            if (await c.IsSupportedAsync())
            {
                await c.SetLightControlOwnerAsync(false);
            }
        });

        await SafeExecuteAsync<SpectrumKeyboardBacklightController>(async c =>
        {
            if (await c.IsSupportedAsync())
            {
                await c.StopAuroraIfNeededAsync();
            }
        });

        await SafeExecuteAsync<NativeWindowsMessageListener>(c => c.StopAsync());
        await SafeExecuteAsync<SessionLockUnlockListener>(c => c.StopAsync());
        await SafeExecuteAsync<HWiNFOIntegration>(c => c.StopAsync());
        await SafeExecuteAsync<IpcServer>(c => c.StopAsync());
        await SafeExecuteAsync<BatteryDischargeRateMonitorService>(c => c.StopAsync());

        var feature = IoCContainer.Resolve<AmdOverclockingController>();

        if (feature.IsActive())
        {
            var cleanInfo = new ShutdownInfo
            {
                Status = "Normal",
                AbnormalCount = 0
            };

            feature.SaveShutdownInfo(cleanInfo);
        }

        Shutdown();
    }

    private static async Task SafeExecuteAsync<T>(Func<T, Task> action) where T : class
    {
        try
        {
            if (IoCContainer.TryResolve<T>() is { } service)
            {
                await action(service);
            }
        }
        catch { /* Ignore */ }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        try
        {
            string reason = e.ReasonSessionEnding == ReasonSessionEnding.Logoff ? "Logoff" : "Shutdown";
            Log.Instance.Trace($"System SessionEnding triggered. Reason: {reason}");

            ExecuteShutdownLogic();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"CRITICAL ERROR during SessionEnding: {ex}");
        }

        base.OnSessionEnding(e);
    }

    private void ExecuteShutdownLogic()
    {
        var overclockController = IoCContainer.Resolve<AmdOverclockingController>();

        if (!overclockController.IsActive())
        {
            return;
        }

        var cleanInfo = new ShutdownInfo
        {
            Status = "Normal",
            AbnormalCount = 0
        };

        overclockController.SaveShutdownInfo(cleanInfo);

        Log.Instance.Trace($"Shutdown info saved successfully.");
    }

    #endregion

    #region Compatibility Check

    private async Task CheckCompatibilityAsyncWrapper(Flags flags)
    {
        if (flags.SkipCompatibilityCheck) return;

        try
        {
            if (!await CheckBasicCompatibilityAsync())
            {
                return;
            }

            if (!await CheckCompatibilityAsync())
            {
                return;
            }
        }
        catch (Exception ex)
        {
            if (flags.Debug) Console.WriteLine(@$"[Compatibility] Check failed: {ex.Message}");
            Log.Instance.Trace($"Failed to check device compatibility", ex);
            MessageBox.Show(Resource.CompatibilityCheckError_Message, Resource.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(200);
        }
    }

    private async Task<bool> CheckBasicCompatibilityAsync()
    {
        if (await Compatibility.CheckBasicCompatibilityAsync())
        {
            return true;
        }

        MessageBox.Show(Resource.IncompatibleDevice_Message, Resource.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(201);
        return false;
    }

    private async Task<bool> CheckCompatibilityAsync()
    {
        var (isCompatible, mi) = await Compatibility.IsCompatibleAsync();
        if (isCompatible)
        {
            Log.Instance.Trace($"Compatibility check passed. [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}, BIOS={mi.BiosVersion}]");
            return true;
        }

        Log.Instance.Trace($"Incompatible system detected. [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}, BIOS={mi.BiosVersion}]");

        var unsupportedWindow = new UnsupportedWindow(mi);
        unsupportedWindow.Show();

        if (await unsupportedWindow.ShouldContinue)
        {
            Log.Instance.IsTraceEnabled = true;
            Log.Instance.Trace($"Compatibility check OVERRIDE. [Vendor={mi.Vendor}, Model={mi.Model}, MachineType={mi.MachineType}]");
            return true;
        }

        Shutdown(202);
        return false;
    }

    #endregion

    #region Instance Management

    public void RestartMainWindow()
    {
        if (MainWindow is MainWindow mw)
        {
            mw.SuppressClosingEventHandler = true;
            mw.Close();
        }

        MainWindow = MainWindowInstance = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        MainWindow.Show();

        if (FloatingGadget != null)
        {
            FloatingGadget.Hide();
            FloatingGadget.Close();
            FloatingGadget = null;
        }

        var settingsStore = IoCContainer.Resolve<ApplicationSettings>().Store;

        if (!settingsStore.ShowFloatingGadgets)
        {
            return;
        }

        FloatingGadget = settingsStore.SelectedStyleIndex switch
        {
            1 => new FloatingGadgetUpper(),
            _ => new FloatingGadget()
        };

        FloatingGadget.Show();
    }

    private void EnsureSingleInstance()
    {
        _singleInstanceMutex = new Mutex(true, MUTEX_NAME, out var isOwned);
        _singleInstanceWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EVENT_NAME);

        if (!isOwned)
        {
            _singleInstanceWaitHandle.Set();
            Shutdown();
            return;
        }

        Task.Factory.StartNew(() =>
        {
            while (_singleInstanceWaitHandle.WaitOne())
            {
                Dispatcher.BeginInvoke(async () =>
                {
                    if (MainWindow is { } window)
                        window.BringToForeground();
                    else
                        await ShutdownAsync();
                });
            }
        }, TaskCreationOptions.LongRunning);
    }

    #endregion

    #region Exception Handling

    private void LogUnhandledException(Exception exception)
    {
        if (exception == null)
        {
            return;
        }

        if (Flags is { Debug: true })
        {
            Console.WriteLine(@$"[UnhandledException] {exception}");
        }

        Log.Instance.Trace($"Exception in LogUnhandledException {exception.Message}", exception);
        string userMessage = GetFriendlyErrorMessage(exception);

        if (Application.Current == null)
        {
            return;
        }

        Action showSnackbarAction = () =>
            SnackbarHelper.Show(Resource.UnexpectedException, userMessage, SnackbarType.Error);

        if (Dispatcher.CheckAccess())
        {
            showSnackbarAction();
        }

        else
        {
            Dispatcher.BeginInvoke(showSnackbarAction);
        }
    }

    private Exception GetInnermostException(Exception ex)
    {
        if (ex is AggregateException { InnerExceptions.Count: > 0 } aggEx)
        {
            return GetInnermostException(aggEx.InnerExceptions[0]);
        }

        return ex.InnerException != null ? GetInnermostException(ex.InnerException) : ex;
    }

    private string GetFriendlyErrorMessage(Exception ex)
    {
        if (ex == null)
        {
            return "An unknown error occurred.";
        }

        Exception inner = GetInnermostException(ex);
        return string.IsNullOrWhiteSpace(inner.Message) ? "An unexpected error occurred, please try again." : inner.Message;
    }

    private bool ShouldIgnoreException(Exception ex)
    {
        return ex switch
        {
            ManagementException or OperationCanceledException or TaskCanceledException => true,
            AggregateException aggregateException => aggregateException.InnerExceptions.Any(ShouldIgnoreException),
            _ => ex.InnerException != null && ShouldIgnoreException(ex.InnerException)
        };
    }

    private void SetupExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (!ShouldIgnoreException((Exception)e.ExceptionObject))
            {
                LogUnhandledException((Exception)e.ExceptionObject);
            }
        };

        DispatcherUnhandledException += (s, e) =>
        {
            if (!ShouldIgnoreException(e.Exception))
            {
                LogUnhandledException(e.Exception);
            }

            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            if (!ShouldIgnoreException(e.Exception))
            {
                LogUnhandledException(e.Exception);
            }

            e.SetObserved();
        };
    }

    #endregion

    #region Feature Initialization

    private static async Task LogSoftwareStatusAsync()
    {
        if (!Log.Instance.IsTraceEnabled)
        {
            return;
        }

        Log.Instance.Trace($"Vantage status: {await IoCContainer.Resolve<VantageDisabler>().GetStatusAsync()}");
        Log.Instance.Trace($"LegionSpace status: {await IoCContainer.Resolve<LegionSpaceDisabler>().GetStatusAsync()}");
        Log.Instance.Trace($"LegionZone status: {await IoCContainer.Resolve<LegionZoneDisabler>().GetStatusAsync()}");
        Log.Instance.Trace($"FnKeys status: {await IoCContainer.Resolve<FnKeysDisabler>().GetStatusAsync()}");
    }

    private static async Task InitHybridModeAsync()
    {
        try
        {
            await IoCContainer.Resolve<HybridModeFeature>().EnsureDGPUEjectedIfNeededAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't initialize hybrid mode.", ex);
        }
    }

    private static async Task InitAutomationProcessorAsync()
    {
        try
        {
            var ap = IoCContainer.Resolve<AutomationProcessor>();
            await ap.InitializeAsync();
            ap.RunOnStartup();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't initialize automation processor.", ex);
        }
    }

    private static async Task InitSetPowerMode()
    {
        try
        {
            var feature = IoCContainer.Resolve<PowerModeFeature>();
            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

            if (await Power.IsPowerAdapterConnectedAsync() == PowerAdapterStatus.Connected
                && await feature.GetStateAsync().ConfigureAwait(false) == PowerModeState.GodMode
                && mi.Properties.HasReapplyParameterIssue)
            {
                await feature.SetStateAsync(PowerModeState.Balance).ConfigureAwait(false);
                await Task.Delay(500).ConfigureAwait(false);
                await feature.SetStateAsync(PowerModeState.GodMode).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't reapply parameters.", ex);
        }
    }

    private static void InitSetLogIndicator()
    {
        try
        {
            var settings = IoCContainer.Resolve<ApplicationSettings>();
            if (!settings.Store.EnableLogging || Current.MainWindow is not MainWindow mainWindow)
            {
                return;
            }

            Log.Instance.IsTraceEnabled = true;
            mainWindow._openLogIndicator.Visibility = Visibility.Visible;
            Compatibility.PrintMachineInfo();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't reapply parameters.", ex);
        }
    }

    private static async Task InitITSModeFeatureAsync()
    {
        try
        {
            var feature = IoCContainer.Resolve<ITSModeFeature>();
            if (await feature.IsSupportedAsync())
            {
                await feature.SetStateAsync(await feature.GetStateAsync());
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't ensure its mode state.", ex);
        }
    }

    private static async Task InitPowerModeFeatureAsync()
    {
        try
        {
            var feature = IoCContainer.Resolve<PowerModeFeature>();
            if (await feature.IsSupportedAsync())
            {
                await feature.EnsureGodModeStateIsAppliedAsync();
                await feature.EnsureCorrectWindowsPowerSettingsAreSetAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"InitPowerModeFeatureAsync failed.", ex);
        }
    }

    private static async Task InitBatteryFeatureAsync()
    {
        try
        {
            var feature = IoCContainer.Resolve<BatteryFeature>();
            if (await feature.IsSupportedAsync())
            {
                await feature.EnsureCorrectBatteryModeIsSetAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't ensure correct battery mode.", ex);
        }
    }

    private static async Task InitSensorsGroupControllerFeatureAsync()
    {
        var settings = IoCContainer.Resolve<ApplicationSettings>();
        try
        {
            if (settings.Store is { UseNewSensorDashboard: false, ShowFloatingGadgets: false })
            {
                return;
            }

            var state = await IoCContainer.Resolve<SensorsGroupController>().IsSupportedAsync();
            if (state is not (LibreHardwareMonitorInitialState.Initialized or LibreHardwareMonitorInitialState.Success))
            {
                Current._showPawnIONotify = true;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"InitSensorsGroupControllerFeatureAsync() raised exception:", ex);
            if (!ex.Message.Contains("LibreHardwareMonitor initialization failed"))
            {
                Current._showPawnIONotify = true;
            }
        }
    }

    private static async Task InitRgbKeyboardControllerAsync()
    {
        try
        {
            var controller = IoCContainer.Resolve<RGBKeyboardBacklightController>();
            if (await controller.IsSupportedAsync())
            {
                await controller.SetLightControlOwnerAsync(true, true);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't set light control owner or current preset.", ex);
        }
    }

    private static async Task InitSpectrumKeyboardControllerAsync()
    {
        try
        {
            var controller = IoCContainer.Resolve<SpectrumKeyboardBacklightController>();
            if (await controller.IsSupportedAsync())
            {
                await controller.StartAuroraIfNeededAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't start Aurora if needed.", ex);
        }
    }

    private static async Task InitGpuOverclockControllerAsync()
    {
        try
        {
            var controller = IoCContainer.Resolve<GPUOverclockController>();
            if (await controller.IsSupportedAsync())
            {
                await controller.EnsureOverclockIsAppliedAsync();
            }
        }
        catch (Exception ex) { Log.Instance.Trace($"Couldn't overclock GPU.", ex); }
    }

    private static async Task InitAMDOverclocking()
    {
        try
        {
            var feature = IoCContainer.Resolve<AmdOverclockingController>();

            if (feature.IsActive())
            {
                await feature.InitializeAsync().ConfigureAwait(false);

                if (!feature.DoNotApply)
                {
                    await feature.ApplyInternalProfileAsync().ConfigureAwait(false);
                }

                Log.Instance.Trace($"AMD Overclocking Controller initialization task finished.");
            }
        }
        catch (InvalidOperationException)
        {
            Log.Instance.Trace($"Profile apply has been canceled due to AC issue.");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to apply profile on startup: {ex.Message}", ex);
        }
    }

    #endregion

    #region UI Helpers
    public void InitFloatingGadget()
    {
        MessagingCenter.Subscribe<FloatingGadgetChangedMessage>(this, message =>
        {
            Dispatcher.Invoke(() => HandleFloatingGadgetCommand(message.State));
        });

        var settings = IoCContainer.Resolve<ApplicationSettings>();
        if (settings.Store.ShowFloatingGadgets)
        {
            HandleFloatingGadgetCommand(FloatingGadgetState.Show);
        }
    }

    private void HandleFloatingGadgetCommand(FloatingGadgetState state)
    {
        switch (state)
        {
            case FloatingGadgetState.Hidden:
                FloatingGadget?.Hide();
                return;
            case FloatingGadgetState.Show:
            case FloatingGadgetState.Toggle when FloatingGadget is not { IsVisible: true }:
                {
                    var settings = IoCContainer.Resolve<ApplicationSettings>();
                    bool shouldBeUpper = settings.Store.SelectedStyleIndex == 1;

                    if (FloatingGadget != null && (FloatingGadget is FloatingGadgetUpper) != shouldBeUpper)
                    {
                        FloatingGadget.Close();
                        FloatingGadget = null;
                    }

                    EnsureGadgetCreated(shouldBeUpper);
                    FloatingGadget?.Show();
                    break;
                }
            case FloatingGadgetState.Toggle when FloatingGadget is { IsVisible: true }:
                FloatingGadget.Hide();
                break;
        }
    }

    private void EnsureGadgetCreated(bool isUpper)
    {
        if (FloatingGadget != null)
        {
            return;
        }

        FloatingGadget = isUpper ? new FloatingGadgetUpper() : new FloatingGadget();
        FloatingGadget.Closed += (s, e) => FloatingGadget = null;
    }

    #endregion
}