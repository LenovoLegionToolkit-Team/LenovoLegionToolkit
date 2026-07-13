using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib.Utils;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;

namespace LenovoLegionToolkit.WPF.Utils;

public static class DebugUIManager
{
    private static DispatcherTimer? _timer;
    private static readonly HashSet<Type> ExcludedTypes =
    [
        typeof(ProgressBar),
        typeof(ProgressRing),
        typeof(Popup),
        typeof(ToolTip),
        typeof(ScrollViewer),
        typeof(ContentPresenter),
        typeof(Snackbar),
        typeof(Border),
        typeof(SymbolIcon),
        typeof(FontIcon),
        typeof(TickBar)
    ];

    private static readonly HashSet<Type> SkipChildrenTypes =
    [
        typeof(Controls.LampArray.LampArrayKeyboardControl),
        typeof(Controls.LampArray.LampArrayRearAmbientControl),
        typeof(Controls.LampArray.LampArrayAftAmbientControl),
        typeof(Controls.KeyboardBacklight.Spectrum.Device.SpectrumDeviceControl)
    ];

    public static void Start()
    {
        if (!AppFlags.Instance.Debug) return;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += (s, e) => ApplyGlobalDebugVisibility();
        _timer.Start();
    }

    private static void ApplyGlobalDebugVisibility()
    {
        try
        {
            if (Application.Current?.Windows != null)
            {
                var windows = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OfType<Window>(Application.Current.Windows));
                foreach (Window window in windows)
                {
                    if (window.GetType().Assembly != typeof(App).Assembly)
                    {
                        continue;
                    }

                    WalkAndForce(window);

                    if (window is Windows.Overclocking.Amd.AmdOverclocking amdWindow)
                    {
                        if (amdWindow.CcdList.Count == 0)
                        {
                            int totalCores = 16;
                            int coresPerCcd = 8;
                            for (int coreIndex = 0; coreIndex < totalCores; coreIndex++)
                            {
                                int currentCcdIndex = coreIndex / coresPerCcd;
                                if (amdWindow.CcdList.Count <= currentCcdIndex)
                                {
                                    amdWindow.CcdList.Add(new AmdCcdGroup
                                    {
                                        HeaderTitle = $"CCD {currentCcdIndex}",
                                        IsExpanded = true
                                    });
                                }

                                var currentGroup = amdWindow.CcdList[currentCcdIndex];
                                currentGroup.Cores.Add(new AmdCoreItem
                                {
                                    Index = coreIndex,
                                    DisplayName = $"{Resources.Resource.AmdOverclocking_Core_Title} {coreIndex}",
                                    OffsetValue = 0,
                                    IsActive = true,
                                    IsEnabled = true
                                });
                            }
                        }
                    }
            }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Exception during tick: {ex}");
        }
    }

    private static void WalkAndForce(DependencyObject root)
    {
        if (root == null || SkipChildrenTypes.Contains(root.GetType()))
            return;

        if (root is Controls.Dashboard.DashboardGroupControl dgc)
        {
            if (dgc.Content is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock tb && tb.Text == Resources.Resource.DashboardPage_Power_Title)
            {
                bool hasItsMode = false;
                foreach (var child in sp.Children)
                {
                    if (child is Controls.Dashboard.ITSModeControl)
                    {
                        hasItsMode = true;
                        break;
                    }
                }
                if (!hasItsMode)
                {
                    var itsControl = new Controls.Dashboard.ITSModeControl();
                    if (sp.Children.Count > 1)
                        sp.Children.Insert(1, itsControl);
                    else
                        sp.Children.Add(itsControl);
                }
            }
        }

        if (root is Controls.Settings.SettingsUpdateControl suc)
        {
            var comboBox = (ComboBox)suc.FindName("_updateChannelComboBox");
            if (comboBox != null && comboBox.Items.Count > 0)
            {
                int totalChannels = Enum.GetValues<Lib.UpdateChannel>().Length;
                if (comboBox.Items.Count < totalChannels)
                {
                    if (Extensions.ComboBoxExtensions.TryGetSelectedItem<Lib.UpdateChannel>(comboBox, out var selected))
                    {
                        var allChannels = Enum.GetValues<Lib.UpdateChannel>();
                        Extensions.ComboBoxExtensions.SetItems(comboBox, allChannels, selected, t => Lib.Extensions.EnumExtensions.GetDisplayName(t));
                    }
                }
            }
        }

        if (root is Pages.KeyboardBacklightPage kbPage)
        {
            var contentPanel = (StackPanel)kbPage.FindName("_content");
            if (contentPanel != null)
            {
                if (kbPage.Tag == null)
                {
                    kbPage.Tag = "DebugInjected";
                    contentPanel.Children.Clear();

                var header = new TextBlock
                {
                    Text = "Debug UI Selector",
                    FontSize = 20,
                    Margin = new Thickness(0, 0, 0, 10),
                    FontWeight = FontWeights.Bold
                };
                contentPanel.Children.Add(header);

                var comboBox = new ComboBox
                {
                    Margin = new Thickness(0, 0, 0, 20),
                    FontSize = 16
                };
                comboBox.Items.Add("Spectrum (Per-Key)");
                comboBox.Items.Add("Spectrum (24-Zone)");
                comboBox.Items.Add("RGB (4-Zone)");

                var container = new ContentControl();

                comboBox.SelectionChanged += (s, e) =>
                {
                    container.Content = null;
                    if (comboBox.SelectedIndex == 0)
                    {
                        var settings = Lib.IoCContainer.Resolve<Lib.Settings.SpectrumKeyboardSettings>();
                        settings.Store.KeyboardLayout = Lib.KeyboardLayout.Ansi;
                        settings.SynchronizeStore();
                        container.Content = new Controls.KeyboardBacklight.Spectrum.SpectrumKeyboardBacklightControl();
                    }
                    else if (comboBox.SelectedIndex == 1)
                    {
                        var settings = Lib.IoCContainer.Resolve<Lib.Settings.SpectrumKeyboardSettings>();
                        settings.Store.KeyboardLayout = Lib.KeyboardLayout.Keyboard24Zone;
                        settings.SynchronizeStore();
                        container.Content = new Controls.KeyboardBacklight.Spectrum.SpectrumKeyboardBacklightControl();
                    }
                    else if (comboBox.SelectedIndex == 2)
                    {
                        container.Content = new Controls.KeyboardBacklight.RGB.RGBKeyboardBacklightControl();
                    }
                };

                contentPanel.Children.Add(comboBox);
                contentPanel.Children.Add(container);
                
                kbPage.Tag = new List<UIElement> { header, comboBox, container };
                
                comboBox.SelectedIndex = 0;
                }

                if (kbPage.Tag is List<UIElement> allowedElements)
                {
                    var toRemove = new List<UIElement>();
                    foreach (UIElement child in contentPanel.Children)
                    {
                        if (!allowedElements.Contains(child))
                        {
                            toRemove.Add(child);
                        }
                    }
                    foreach (var child in toRemove)
                    {
                        contentPanel.Children.Remove(child);
                    }
                }
            }
        }

        if (root is Controls.KeyboardBacklight.Spectrum.SpectrumKeyboardBacklightControl spectrumControl)
        {
            var device = (Controls.KeyboardBacklight.Spectrum.Device.SpectrumDeviceControl)spectrumControl.FindName("_device");
            if (device != null)
            {
                if (System.Linq.Enumerable.Count(device.GetVisibleKeyboardButtons()) == 0)
                {
                    var dummyKeys = new HashSet<ushort>();
                    for (ushort i = 1; i <= 2000; i++) dummyKeys.Add(i);
                    
                    var settings = Lib.IoCContainer.Resolve<Lib.Settings.SpectrumKeyboardSettings>();
                    var currentLayout = settings.Store.KeyboardLayout ?? Lib.KeyboardLayout.Ansi;
                    
                    device.SetLayout(Lib.SpectrumLayout.Full, currentLayout, dummyKeys);
                }
            }
        }

        if (root is FrameworkElement fe)
        {
            if (!ExcludedTypes.Contains(fe.GetType()))
            {
                var name = fe.Name;
                bool hasValidName = !string.IsNullOrEmpty(name) && !name.StartsWith("PART_");
                bool isSettingsButton = fe is Button btn && btn.Icon == Wpf.Ui.Common.SymbolRegular.Settings24;
                bool isAlwaysShowType = fe is UserControl 
                    || fe is CardControl || fe is Controls.Custom.CardControl
                    || fe is CardAction || fe is Controls.Custom.CardAction
                    || fe is CardExpander || fe is Controls.Custom.CardExpander;

                if (hasValidName || isAlwaysShowType || isSettingsButton)
                {
                    if (fe.Visibility != Visibility.Visible)
                        fe.Visibility = Visibility.Visible;
                }

                if (!fe.IsEnabled)
                    fe.IsEnabled = true;

                if (fe is InfoBar infoBar && !infoBar.IsOpen)
                    infoBar.IsOpen = true;
                if (fe is Expander expander && !expander.IsExpanded)
                    expander.IsExpanded = true;
            }
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child != null)
            {
                WalkAndForce(child);
            }
        }
    }
}
