using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Xaml.Behaviors.Core;
using Windows.Win32;
using Windows.Win32.System.Threading;
using Wpf.Ui.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.SoftwareDisabler;
using LenovoLegionToolkit.Lib.Station.Services;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Pages;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Utils;
using CustomNavigationItem = LenovoLegionToolkit.WPF.Controls.Custom.NavigationItem;
using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Windows;

public partial class MainWindow
{
    private readonly ApplicationSettings _applicationSettings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly SpecialKeyListener _specialKeyListener = IoCContainer.Resolve<SpecialKeyListener>();
    private readonly DriverKeyListener _driverKeyListener = IoCContainer.Resolve<DriverKeyListener>();
    private readonly VantageDisabler _vantageDisabler = IoCContainer.Resolve<VantageDisabler>();
    private readonly LegionSpaceDisabler _legionSpaceDisabler = IoCContainer.Resolve<LegionSpaceDisabler>();
    private readonly LegionZoneDisabler _legionZoneDisabler = IoCContainer.Resolve<LegionZoneDisabler>();
    private readonly FnKeysDisabler _fnKeysDisabler = IoCContainer.Resolve<FnKeysDisabler>();
    private readonly INavigationService _extensionNavigationService = IoCContainer.Resolve<INavigationService>();
    private readonly UpdateChecker _updateChecker = IoCContainer.Resolve<UpdateChecker>();

    private const double CompactMinWidth = 550;
    private const double CompactMinHeight = 480;
    private const double CompactDefaultWidth = 900;
    private const double CompactDefaultHeight = 620;

    private TrayHelper? _trayHelper;
    private bool _windowSizeLocked;

    public bool TrayTooltipEnabled { get; init; } = true;
    public bool DisableConflictingSoftwareWarning { get; set; }
    public bool SuppressClosingEventHandler { get; set; }

    public Snackbar Snackbar => _snackbar;

    public MainWindow()
    {
        InitializeComponent();

        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        IsVisibleChanged += MainWindow_IsVisibleChanged;
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += MainWindow_StateChanged;

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
#if DEBUG
        _title.Text += Debugger.IsAttached ? " [DEBUGGER ATTACHED]" : " [DEBUG]";
#else
        if (version is not null && version.IsBeta())
        {
            _title.Text += " [BETA]";
        }
        else
        {
            _title.Text += $" {version}";
        }
#endif
        Focusable = false;
        if (Log.Instance.IsTraceEnabled)
        {
            _title.Text += " [LOGGING ENABLED]";
            _openLogIndicator.Visibility = Visibility.Visible;
        }

        Title = _title.Text;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e) => RestoreSize();

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _contentGrid.Visibility = Visibility.Hidden;

        if (!AppFlags.Instance.Debug)
        {
            if (!await KeyboardBacklightPage.IsSupportedAsync())
            {
                _navigationStore.Items.Remove(_keyboardItem);
            }

            if (!await LampArrayRGBKeyboardPage.IsSupportedAsync())
            {
                _navigationStore.Items.Remove(_lampArrayKeyboardItem);
            }

            var mi = await Compatibility.GetMachineInformationAsync();
            if (!(mi.LegionSeries == LegionSeries.Legion_Pro_7 && mi.Generation >= 10))
            {
                _navigationStore.Items.Remove(_lampArrayKeyboardItem);
            }
        }

        var actionManager = IoCContainer.Resolve<SpecialKeyActionManager>();
        actionManager.WireUp(_specialKeyListener, () => Dispatcher.Invoke(BringToForeground), () => Dispatcher.Invoke(ShowSystemInfoPopup));
        actionManager.WireUp(_driverKeyListener);

        AddExtensionNavigationItems();

        _contentGrid.Visibility = Visibility.Visible;

        LoadDeviceInfo();
        UpdateIndicators();
        CheckForUpdates();

        InputBindings.Add(new KeyBinding(new ActionCommand(_navigationStore.NavigateToNext), Key.Tab, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new ActionCommand(_navigationStore.NavigateToPrevious), Key.Tab, ModifierKeys.Control | ModifierKeys.Shift));

        var key = (int)Key.D1;
        foreach (var item in _navigationStore.Items.OfType<CustomNavigationItem>())
            InputBindings.Add(new KeyBinding(new ActionCommand(() => _navigationStore.Navigate(item.PageTag)), (Key)key++, ModifierKeys.Control));

        var trayHelper = new TrayHelper(_navigationStore, BringToForeground, TrayTooltipEnabled);
        await trayHelper.InitializeAsync();
        trayHelper.MakeVisible();
        _trayHelper = trayHelper;

        ApplyCompactLayout();
    }

    private void ApplyCompactLayout()
    {
        if (_applicationSettings.Store.CompactMode)
        {
            _contentGrid.Margin = new Thickness(4, 2, 0, 0);

            _contentGrid.ColumnDefinitions[0].Width = GridLength.Auto;
            _navigationStore.Margin = new Thickness(0, 0, 0, 4);

            _title.FontSize = 11;
            if (_title.Parent is Grid titleGrid)
            {
                titleGrid.Margin = new Thickness(8, 2, 150, 2);
            }

            _openLogIndicator.LayoutTransform = new ScaleTransform(0.8, 0.8);
            _openLogIndicator.Margin = new Thickness(0, 0, 4, 0);

            _deviceInfoIndicator.LayoutTransform = new ScaleTransform(0.8, 0.8);
            _deviceInfoIndicator.Margin = new Thickness(0, 0, 4, 0);

            foreach (var item in _navigationStore.Items.OfType<Wpf.Ui.Controls.NavigationItem>())
            {
                if (item.Content != null)
                {
                    item.ToolTip = item.Content;
                    item.Content = null;
                }
                item.HorizontalAlignment = HorizontalAlignment.Left;
                item.Margin = new Thickness(6, 2, 6, 2);
            }
            foreach (var item in _navigationStore.Footer.OfType<Wpf.Ui.Controls.NavigationItem>())
            {
                if (item.Content != null)
                {
                    item.ToolTip = item.Content;
                    item.Content = null;
                }
                item.HorizontalAlignment = HorizontalAlignment.Left;
                item.Margin = new Thickness(6, 2, 6, 2);
            }

            _vantageIndicator.Padding = new Thickness(4, 2, 4, 2);
            _legionZoneIndicator.Padding = new Thickness(4, 2, 4, 2);
            _legionSpaceIndicator.Padding = new Thickness(4, 2, 4, 2);
            _fnKeysIndicator.Padding = new Thickness(4, 2, 4, 2);
            _updateIndicator.Padding = new Thickness(4, 2, 4, 2);
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!App.IsRestoringSettings)
            SaveSize();

        if (SuppressClosingEventHandler)
            return;

        if (_applicationSettings.Store.MinimizeOnClose)
        {
            Log.Instance.Trace($"Minimizing...");

            WindowState = WindowState.Minimized;
            e.Cancel = true;
        }
        else
        {
            Log.Instance.Trace($"Closing...");

            await App.Current.ShutdownAsync();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs args)
    {
        _trayHelper?.Dispose();
        _trayHelper = null;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        Log.Instance.Trace($"Window state changed to {WindowState}");

        if (_windowSizeLocked && WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            return;
        }

        switch (WindowState)
        {
            case WindowState.Minimized:
                SetEfficiencyMode(true);
                SendToTray();
                break;
            case WindowState.Normal:
                SetEfficiencyMode(false);
                BringToForeground();
                break;
        }
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var settings = IoCContainer.Resolve<ApplicationSettings>();
        ApplyWindowLock(settings.Store.LockWindowSize);
        Topmost = settings.Store.AlwaysOnTop;

        if (!IsVisible)
            return;

        CheckForUpdates();
        SetVisual();
    }

    public void ApplyWindowLock(bool locked)
    {
        _windowSizeLocked = locked;
        if (locked)
        {
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Manual;
        }
        else
        {
            ResizeMode = ResizeMode.CanResize;
        }

        var chrome = WindowChrome.GetWindowChrome(this);
        if (chrome is not null)
        {
            var newChrome = (WindowChrome)chrome.Clone();
            newChrome.ResizeBorderThickness = locked ? new Thickness(0) : SystemParameters.WindowResizeBorderThickness;
            WindowChrome.SetWindowChrome(this, newChrome);
        }
    }

    private void AddExtensionNavigationItems()
    {
        foreach (var item in _extensionNavigationService.Items.Where(i => !i.IsFooter))
        {
            if (_navigationStore.Items.OfType<CustomNavigationItem>().Any(existing => string.Equals(existing.PageTag, item.PageTag, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _navigationStore.Items.Add(new CustomNavigationItem
            {
                Content = item.Title,
                Icon = item.Icon switch
                {
                    ExtensionIcon.Gauge => SymbolRegular.Gauge24,
                    _ => SymbolRegular.Empty,
                },
                PageTag = item.PageTag,
                PageType = item.PageType
            });
        }
    }

    private void OpenLogIndicator_Click(object sender, MouseButtonEventArgs e) => OpenLog();

    private void OpenLogIndicator_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter and not Key.Space)
            return;

        OpenLog();
    }

    private void DeviceInfoIndicator_Click(object sender, MouseButtonEventArgs e) => ShowDeviceInfoWindow();

    private void DeviceInfoIndicator_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter and not Key.Space)
            return;

        ShowDeviceInfoWindow();
    }

    private void UpdateIndicator_Click(object sender, RoutedEventArgs e) => ShowUpdateWindow();

    private void UpdateIndicator_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter and not Key.Space)
            return;

        ShowUpdateWindow();
    }

    private async void LoadDeviceInfo()
    {
        string? modelName;

        if (Compatibility.FakeMachineInformationMode)
        {
            var fakeMi = await Compatibility.GetFakeMachineInformationAsync();

            if (fakeMi.HasValue)
            {
                modelName = fakeMi.Value.Model;
            }
            else
            {
                var realMi = await Compatibility.GetMachineInformationAsync();
                modelName = realMi.Model;
            }
        }
        else
        {
            var mi = await Compatibility.GetMachineInformationAsync();
            modelName = mi.Model;
        }

        if (string.IsNullOrEmpty(modelName))
        {
            return;
        }

        _deviceInfoIndicator.Content = modelName;
        _deviceInfoIndicator.Visibility = Visibility.Visible;
    }

    private void UpdateIndicators()
    {
        if (DisableConflictingSoftwareWarning)
            return;

        _vantageDisabler.OnRefreshed += async (_, e) => await Dispatcher.InvokeAsync(() =>
        {
            _vantageIndicator.Visibility = e.Status == SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        });

        _legionSpaceDisabler.OnRefreshed += async (_, e) => await Dispatcher.InvokeAsync(() =>
        {
            _legionSpaceIndicator.Visibility = e.Status == SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        });

        _legionZoneDisabler.OnRefreshed += async (_, e) => await Dispatcher.InvokeAsync(() =>
        {
            _legionZoneIndicator.Visibility = e.Status == SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        });

        _fnKeysDisabler.OnRefreshed += async (_, e) => await Dispatcher.InvokeAsync(() =>
        {
            _fnKeysIndicator.Visibility = e.Status == SoftwareStatus.Enabled ? Visibility.Visible : Visibility.Collapsed;
        });

        Task.Run(async () =>
        {
            _ = await _vantageDisabler.GetStatusAsync().ConfigureAwait(false);
            _ = await _legionSpaceDisabler.GetStatusAsync().ConfigureAwait(false);
            _ = await _legionZoneDisabler.GetStatusAsync().ConfigureAwait(false);
            _ = await _fnKeysDisabler.GetStatusAsync().ConfigureAwait(false);
        });
    }

    public void CheckForUpdates(bool manualCheck = false)
    {
        UpdateSettings _updateSettings = IoCContainer.Resolve<UpdateSettings>();
        if (_updateSettings.Store.UpdateCheckFrequency != UpdateCheckFrequency.Never || manualCheck)
        {
            Task.Run(() => _updateChecker.CheckAsync(manualCheck))
                        .ContinueWith(async updatesAvailable =>
                        {
                            var result = updatesAvailable.Result;
                            if (result is null)
                            {
                                _updateIndicator.Visibility = Visibility.Collapsed;

                                if (manualCheck && WindowState != WindowState.Minimized)
                                {
                                    switch (_updateChecker.Status)
                                    {
                                        case UpdateCheckStatus.Success:
                                            await SnackbarHelper.ShowAsync(Resource.MainWindow_CheckForUpdates_Success_Title);
                                            break;
                                        case UpdateCheckStatus.RateLimitReached:
                                            await SnackbarHelper.ShowAsync(Resource.MainWindow_CheckForUpdates_Error_Title, Resource.MainWindow_CheckForUpdates_Error_ReachedRateLimit_Message, SnackbarType.Error);
                                            break;
                                        case UpdateCheckStatus.Error:
                                            await SnackbarHelper.ShowAsync(Resource.MainWindow_CheckForUpdates_Error_Title, Resource.MainWindow_CheckForUpdates_Error_Unknown_Message, SnackbarType.Error);
                                            break;
                                    }
                                }
                            }
                            else
                            {
                                var versionNumber = result.ToString();

                                _updateIndicatorText.Text =
                                    string.Format(Resource.MainWindow_UpdateAvailableWithVersion, versionNumber);
                                _updateIndicator.Visibility = Visibility.Visible;

                                if (WindowState == WindowState.Minimized)
                                    MessagingCenter.Publish(new NotificationMessage(NotificationType.UpdateAvailable, versionNumber));
                            }
                        }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    private void RestoreSize()
    {
        if (_applicationSettings.Store.CompactMode)
        {
            MinWidth = CompactMinWidth;
            MinHeight = CompactMinHeight;
        }

        if (!_applicationSettings.Store.WindowSize.HasValue)
        {
            if (_applicationSettings.Store.CompactMode)
            {
                Width = CompactDefaultWidth;
                Height = CompactDefaultHeight;
            }
            return;
        }

        Width = Math.Max(MinWidth, _applicationSettings.Store.WindowSize.Value.Width);
        Height = Math.Max(MinHeight, _applicationSettings.Store.WindowSize.Value.Height);

        ScreenHelper.UpdateScreenInfos();
        var primaryScreen = ScreenHelper.PrimaryScreen;

        if (!primaryScreen.HasValue)
            return;

        var desktopWorkingArea = primaryScreen.Value.WorkArea;

        if (_applicationSettings.Store.WindowPosition.HasValue)
        {
            var pos = _applicationSettings.Store.WindowPosition.Value;
            Left = Math.Max(desktopWorkingArea.Left, Math.Min(pos.Left, desktopWorkingArea.Right - Width));
            Top = Math.Max(desktopWorkingArea.Top, Math.Min(pos.Top, desktopWorkingArea.Bottom - Height));
        }
        else
        {
            Left = (desktopWorkingArea.Width - Width) / 2 + desktopWorkingArea.Left;
            Top = (desktopWorkingArea.Height - Height) / 2 + desktopWorkingArea.Top;
        }
    }

    private void SaveSize()
    {
        _applicationSettings.Store.WindowSize = WindowState != WindowState.Normal
            ? new(RestoreBounds.Width, RestoreBounds.Height)
            : new(Width, Height);
        _applicationSettings.Store.WindowPosition = WindowState != WindowState.Normal
            ? new(RestoreBounds.Left, RestoreBounds.Top)
            : new(Left, Top);
        _applicationSettings.SynchronizeStore();
    }

    private void BringToForeground() => WindowExtensions.BringToForeground(this);

    private Utils.StatusWindow? _statusWindow;

    private void ShowSystemInfoPopup()
    {
        if (_statusWindow != null && _statusWindow.IsVisible)
        {
            _statusWindow.Activate();
            _statusWindow.Focus();
            _statusWindow.Highlight();
            return;
        }

        _statusWindow = new Utils.StatusWindow();
        _statusWindow.Closed += (s, e) => _statusWindow = null;
        _statusWindow.ShowCentered();
        _ = _statusWindow.InitializeAsync();
    }

    private static void OpenLog()
    {
        try
        {
            if (!Directory.Exists(Folders.AppData))
                return;

            Process.Start("explorer", Log.Instance.LogPath);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to open log.", ex);
        }
    }

    private void ShowDeviceInfoWindow()
    {
        var window = new DeviceInformationWindow { Owner = this };
        window.ShowDialog();
    }

    public void ShowUpdateWindow()
    {
        var window = new UpdateWindow { Owner = this };
        window.ShowDialog();
    }

    public void SendToTray()
    {
        if (!_applicationSettings.Store.MinimizeToTray)
            return;

        SetEfficiencyMode(true);
        Hide();
        ShowInTaskbar = true;
    }

    public void SetMainWindowBackgroundImage(string filePath)
    {
        BitmapImage bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(filePath);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        _backgroundImage.ImageSource = bitmap;
    }

    private static unsafe void SetEfficiencyMode(bool enabled)
    {
        var ptr = IntPtr.Zero;

        try
        {
            var priorityClass = enabled
                ? PROCESS_CREATION_FLAGS.IDLE_PRIORITY_CLASS
                : PROCESS_CREATION_FLAGS.NORMAL_PRIORITY_CLASS;
            PInvoke.SetPriorityClass(PInvoke.GetCurrentProcess(), priorityClass);

            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PInvoke.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                ControlMask = PInvoke.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = enabled ? PInvoke.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0,
            };

            var size = Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>();
            ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(state, ptr, false);

            PInvoke.SetProcessInformation(PInvoke.GetCurrentProcess(),
                PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
                ptr.ToPointer(),
                (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public void SetWindowOpacity(double opacity)
    {
        _backgroundDimOverlay.Opacity = opacity;
    }

    public void SetBackgroundBlur(int radius)
    {
        _backgroundBlurEffect.Radius = radius;
    }

    public void SetBackgroundStretch(BackgroundImageStretchMode stretch)
    {
        _backgroundImage.Stretch = stretch switch
        {
            BackgroundImageStretchMode.Fit => Stretch.Uniform,
            BackgroundImageStretchMode.Crop => Stretch.UniformToFill,
            _ => Stretch.Fill
        };
    }

    public void SetVisual()
    {
        var settings = IoCContainer.Resolve<ApplicationSettings>();
        var result = settings.Store.BackGroundImageFilePath;
        var opacity = settings.Store.Opacity;
        try
        {
            if (result != string.Empty)
            {
                SetMainWindowBackgroundImage(result);
                SetWindowOpacity(opacity);
                SetBackgroundBlur(settings.Store.BackgroundImageBlur);
                SetBackgroundStretch(settings.Store.BackgroundImageStretch);
            }
            else
            {
                _backgroundImage.ImageSource = null;
                SetWindowOpacity(0);
                SetBackgroundBlur(0);
            }
        }
        catch (Exception ex)
        {
            SnackbarHelper.Show(Resource.Warning, ex.Message, SnackbarType.Error);
            Log.Instance.Trace($"Exception occured when executing SetBackgroundImage().", ex);
        }
    }

    private void NavigationStore_Navigated(INavigation sender, RoutedNavigationEventArgs e)
    {
        SetVisual();
    }

    private void SoftwareIndicator_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SettingsPage.PendingTabKey = "SoftwareControl";
        _navigationStore.Navigate("settings");
        SettingsPage.Instance?.ApplyPendingTab();
    }
}
