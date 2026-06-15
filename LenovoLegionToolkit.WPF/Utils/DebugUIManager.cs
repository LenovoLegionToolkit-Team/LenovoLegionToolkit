using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using LenovoLegionToolkit.Lib.Utils;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Utils;

public static class DebugUIManager
{
    private static DispatcherTimer? _timer;
    private static readonly HashSet<Type> ExcludedTypes = new()
    {
        typeof(System.Windows.Controls.ProgressBar),
        typeof(Wpf.Ui.Controls.ProgressRing),
        typeof(Popup),
        typeof(ToolTip),
        typeof(ScrollViewer),
        typeof(ContentPresenter),
        typeof(Wpf.Ui.Controls.Snackbar),
        typeof(Border),
        typeof(Wpf.Ui.Controls.SymbolIcon),
        typeof(Wpf.Ui.Controls.FontIcon),
        typeof(System.Windows.Controls.Primitives.TickBar)
    };

    private static readonly HashSet<Type> SkipChildrenTypes = new()
    {
        typeof(LenovoLegionToolkit.WPF.Controls.LampArray.LampArrayKeyboardControl),
        typeof(LenovoLegionToolkit.WPF.Controls.LampArray.LampArrayRearAmbientControl),
        typeof(LenovoLegionToolkit.WPF.Controls.LampArray.LampArrayAftAmbientControl),
        typeof(LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum.Device.SpectrumDeviceControl)
    };

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
                    WalkAndForce(window);

                    if (window is LenovoLegionToolkit.WPF.Windows.Overclocking.Amd.AmdOverclocking amdWindow)
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
                                    amdWindow.CcdList.Add(new LenovoLegionToolkit.WPF.AmdCcdGroup
                                    {
                                        HeaderTitle = $"CCD {currentCcdIndex}",
                                        IsExpanded = true
                                    });
                                }

                                var currentGroup = amdWindow.CcdList[currentCcdIndex];
                                currentGroup.Cores.Add(new LenovoLegionToolkit.WPF.AmdCoreItem
                                {
                                    Index = coreIndex,
                                    DisplayName = $"{LenovoLegionToolkit.WPF.Resources.Resource.AmdOverclocking_Core_Title} {coreIndex}",
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
            LenovoLegionToolkit.Lib.Utils.Log.Instance.Trace($"[DebugUIManager] Exception during tick: {ex}");
        }
    }

    private static void WalkAndForce(DependencyObject root)
    {
        if (root == null || SkipChildrenTypes.Contains(root.GetType()))
            return;

        if (root is LenovoLegionToolkit.WPF.Controls.Dashboard.DashboardGroupControl dgc)
        {
            if (dgc.Content is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock tb && tb.Text == LenovoLegionToolkit.WPF.Resources.Resource.DashboardPage_Power_Title)
            {
                bool hasItsMode = false;
                foreach (var child in sp.Children)
                {
                    if (child is LenovoLegionToolkit.WPF.Controls.Dashboard.ITSModeControl)
                    {
                        hasItsMode = true;
                        break;
                    }
                }
                if (!hasItsMode)
                {
                    var itsControl = new LenovoLegionToolkit.WPF.Controls.Dashboard.ITSModeControl();
                    if (sp.Children.Count > 1)
                        sp.Children.Insert(1, itsControl);
                    else
                        sp.Children.Add(itsControl);
                }
            }
        }

        if (root is LenovoLegionToolkit.WPF.Controls.Settings.SettingsUpdateControl suc)
        {
            var comboBox = (System.Windows.Controls.ComboBox)suc.FindName("_updateChannelComboBox");
            if (comboBox != null && comboBox.Items.Count > 0)
            {
                int totalChannels = Enum.GetValues<LenovoLegionToolkit.Lib.UpdateChannel>().Length;
                if (comboBox.Items.Count < totalChannels)
                {
                    if (LenovoLegionToolkit.WPF.Extensions.ComboBoxExtensions.TryGetSelectedItem<LenovoLegionToolkit.Lib.UpdateChannel>(comboBox, out var selected))
                    {
                        var allChannels = Enum.GetValues<LenovoLegionToolkit.Lib.UpdateChannel>();
                        LenovoLegionToolkit.WPF.Extensions.ComboBoxExtensions.SetItems(comboBox, allChannels, selected, t => LenovoLegionToolkit.Lib.Extensions.EnumExtensions.GetDisplayName(t));
                    }
                }
            }
        }

        if (root is LenovoLegionToolkit.WPF.Pages.KeyboardBacklightPage kbPage)
        {
            var contentPanel = (System.Windows.Controls.StackPanel)kbPage.FindName("_content");
            if (contentPanel != null)
            {
                if (kbPage.Tag == null)
                {
                    kbPage.Tag = "DebugInjected";
                    contentPanel.Children.Clear();

                var header = new System.Windows.Controls.TextBlock
                {
                    Text = "Debug UI Selector",
                    FontSize = 20,
                    Margin = new Thickness(0, 0, 0, 10),
                    FontWeight = FontWeights.Bold
                };
                contentPanel.Children.Add(header);

                var comboBox = new System.Windows.Controls.ComboBox
                {
                    Margin = new Thickness(0, 0, 0, 20),
                    FontSize = 16
                };
                comboBox.Items.Add("Spectrum (Per-Key)");
                comboBox.Items.Add("Spectrum (24-Zone)");
                comboBox.Items.Add("RGB (4-Zone)");

                var container = new System.Windows.Controls.ContentControl();

                comboBox.SelectionChanged += (s, e) =>
                {
                    container.Content = null;
                    if (comboBox.SelectedIndex == 0)
                    {
                        var settings = LenovoLegionToolkit.Lib.IoCContainer.Resolve<LenovoLegionToolkit.Lib.Settings.SpectrumKeyboardSettings>();
                        settings.Store.KeyboardLayout = LenovoLegionToolkit.Lib.KeyboardLayout.Ansi;
                        settings.SynchronizeStore();
                        container.Content = new LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum.SpectrumKeyboardBacklightControl();
                    }
                    else if (comboBox.SelectedIndex == 1)
                    {
                        var settings = LenovoLegionToolkit.Lib.IoCContainer.Resolve<LenovoLegionToolkit.Lib.Settings.SpectrumKeyboardSettings>();
                        settings.Store.KeyboardLayout = LenovoLegionToolkit.Lib.KeyboardLayout.Keyboard24Zone;
                        settings.SynchronizeStore();
                        container.Content = new LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum.SpectrumKeyboardBacklightControl();
                    }
                    else if (comboBox.SelectedIndex == 2)
                    {
                        container.Content = new LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.RGB.RGBKeyboardBacklightControl();
                    }
                };

                contentPanel.Children.Add(comboBox);
                contentPanel.Children.Add(container);
                
                kbPage.Tag = new List<UIElement> { header, comboBox, container };
                
                comboBox.SelectedIndex = 0;
                }

                if (kbPage.Tag is List<UIElement> allowedElements)
                {
                    var toRemove = new System.Collections.Generic.List<UIElement>();
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

        if (root is LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum.SpectrumKeyboardBacklightControl spectrumControl)
        {
            var device = (LenovoLegionToolkit.WPF.Controls.KeyboardBacklight.Spectrum.Device.SpectrumDeviceControl)spectrumControl.FindName("_device");
            if (device != null)
            {
                if (System.Linq.Enumerable.Count(device.GetVisibleKeyboardButtons()) == 0)
                {
                    var dummyKeys = new System.Collections.Generic.HashSet<ushort>();
                    for (ushort i = 1; i <= 2000; i++) dummyKeys.Add(i);
                    
                    var settings = LenovoLegionToolkit.Lib.IoCContainer.Resolve<LenovoLegionToolkit.Lib.Settings.SpectrumKeyboardSettings>();
                    var currentLayout = settings.Store.KeyboardLayout ?? LenovoLegionToolkit.Lib.KeyboardLayout.Ansi;
                    
                    device.SetLayout(LenovoLegionToolkit.Lib.SpectrumLayout.Full, currentLayout, dummyKeys);
                }
            }
        }

        if (root is FrameworkElement fe)
        {
            if (!ExcludedTypes.Contains(fe.GetType()))
            {
                var name = fe.Name;
                bool hasValidName = !string.IsNullOrEmpty(name) && !name.StartsWith("PART_");
                bool isSettingsButton = fe is Wpf.Ui.Controls.Button btn && btn.Icon == Wpf.Ui.Common.SymbolRegular.Settings24;
                bool isAlwaysShowType = fe is UserControl 
                    || fe is Wpf.Ui.Controls.CardControl || fe is LenovoLegionToolkit.WPF.Controls.Custom.CardControl
                    || fe is Wpf.Ui.Controls.CardAction || fe is LenovoLegionToolkit.WPF.Controls.Custom.CardAction
                    || fe is Wpf.Ui.Controls.CardExpander || fe is LenovoLegionToolkit.WPF.Controls.Custom.CardExpander;

                if (hasValidName || isAlwaysShowType || isSettingsButton)
                {
                    if (fe.Visibility != Visibility.Visible)
                        fe.Visibility = Visibility.Visible;
                }

                if (!fe.IsEnabled)
                    fe.IsEnabled = true;

                if (fe is Wpf.Ui.Controls.InfoBar infoBar && !infoBar.IsOpen)
                    infoBar.IsOpen = true;
                if (fe is System.Windows.Controls.Expander expander && !expander.IsExpanded)
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
