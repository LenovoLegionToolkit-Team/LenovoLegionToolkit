using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class ArgumentWindow
{
    private static ArgumentWindow? _instance;

    private record FlagMetadata(string ArgKey, string Category, SymbolRegular Icon, string DisplayName);

    private readonly Dictionary<string, FlagMetadata> _metadata = new()
    {
        // Category: General
        {
            nameof(Flags.Minimized),
            new("--minimized", Resource.ArgumentWindow_Category_General, SymbolRegular.Subtract24, Resource.ArgumentWindow_Flag_StartMinimized)
        },
        {
            nameof(Flags.DisableTrayTooltip),
            new("--disable-tray-tooltip", Resource.ArgumentWindow_Category_General, SymbolRegular.TooltipQuote24, Resource.ArgumentWindow_Flag_DisableTrayTooltip)
        },
        {
            nameof(Flags.DisableUpdateChecker),
            new("--disable-update-checker", Resource.ArgumentWindow_Category_General, SymbolRegular.ArrowDownload24, Resource.ArgumentWindow_Flag_DisableUpdateChecker)
        },
        {
            nameof(Flags.DisableConflictingSoftwareWarning),
            new("--disable-conflicting-software-warning", Resource.ArgumentWindow_Category_General, SymbolRegular.Warning24, Resource.ArgumentWindow_Flag_DisableConflictWarning)
        },

        // Category: Hardware & Automation
        {
            nameof(Flags.AllowAllPowerModesOnBattery),
            new("--allow-all-power-modes-on-battery", Resource.ArgumentWindow_Category_Hardware_Automation, SymbolRegular.BatteryCharge24, Resource.ArgumentWindow_Flag_AllowPowerModesBattery)
        },
        {
            nameof(Flags.SkipCompatibilityCheck),
            new("--skip-compat-check", Resource.ArgumentWindow_Category_Hardware_Automation, SymbolRegular.CheckmarkCircle24, Resource.ArgumentWindow_Flag_SkipCompatCheck)
        },
        {
            nameof(Flags.ExperimentalGPUWorkingMode),
            new("--experimental-gpu-working-mode", Resource.ArgumentWindow_Category_Hardware_Automation, SymbolRegular.Desktop24, Resource.ArgumentWindow_Flag_ExpGpuMode)
        },
        {
            nameof(Flags.EnableCustomFanCurve),
            new("--enable-custom-fan-curve", Resource.ArgumentWindow_Category_Hardware_Automation, SymbolRegular.BezierCurveSquare12, Resource.ArgumentWindow_Flag_Enable_Custom_Fan_Curve)
        },

        // Category: Lighting
        {
            nameof(Flags.ForceDisableLenovoLighting),
            new("--force-disable-lenovolighting", Resource.ArgumentWindow_Category_Lighting, SymbolRegular.Lightbulb24, Resource.ArgumentWindow_Flag_DisableLenovoLighting)
        },
        {
            nameof(Flags.ForceDisableRgbKeyboardSupport),
            new("--force-disable-rgbkb", Resource.ArgumentWindow_Category_Lighting, SymbolRegular.Keyboard24, Resource.ArgumentWindow_Flag_DisableRgbKb)
        },
        {
            nameof(Flags.ForceDisableSpectrumKeyboardSupport),
            new("--force-disable-spectrumkb", Resource.ArgumentWindow_Category_Lighting, SymbolRegular.Color24, Resource.ArgumentWindow_Flag_DisableSpectrumKb)
        },

        // Category: Debugging
        {
            nameof(Flags.IsTraceEnabled),
            new("--trace", Resource.ArgumentWindow_Category_Debugging, SymbolRegular.AppsListDetail24, Resource.ArgumentWindow_Flag_TraceEnabled)
        },
        {
            nameof(Flags.Debug),
            new("--debug", Resource.ArgumentWindow_Category_Debugging, SymbolRegular.Bug24, Resource.ArgumentWindow_Flag_DebugMode)
        },

        // Category: Proxy
        {
            nameof(Flags.ProxyUrl),
            new("--proxy-url", Resource.ArgumentWindow_Category_Network_Proxy, SymbolRegular.Globe24, Resource.ArgumentWindow_Flag_ProxyUrl)
        },
        {
            nameof(Flags.ProxyUsername),
            new("--proxy-username", Resource.ArgumentWindow_Category_Network_Proxy, SymbolRegular.Person24, Resource.ArgumentWindow_Flag_ProxyUsername)
        },
        {
            nameof(Flags.ProxyPassword),
            new("--proxy-password", Resource.ArgumentWindow_Category_Network_Proxy, SymbolRegular.Password24, Resource.ArgumentWindow_Flag_ProxyPassword)
        },
        {
            nameof(Flags.ProxyAllowAllCerts),
            new("--proxy-allow-all-certs", Resource.ArgumentWindow_Category_Network_Proxy, SymbolRegular.Certificate24, Resource.ArgumentWindow_Flag_AllowAllCerts)
        }
    };

    private ArgumentWindow()
    {
        InitializeComponent();
        GenerateUi();
    }

    public static void ShowInstance()
    {
        if (_instance == null)
        {
            _instance = new ArgumentWindow();
            _instance.Closed += (s, e) => _instance = null;
            _instance.Show();
        }
        else
        {
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Activate();
        }
    }

    private void GenerateUi()
    {
        _optionsPanel.Children.Clear();

        var currentArgs = LoadCurrentArgs();
        var properties = typeof(Flags).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var validProps = properties
            .Where(p => _metadata.ContainsKey(p.Name))
            .Select(p => new { Property = p, Meta = _metadata[p.Name] })
            .ToList();

        var groups = validProps.GroupBy(x => x.Meta.Category);

        var categoryOrder = new[]
        {
            Resource.ArgumentWindow_Category_General,
            Resource.ArgumentWindow_Category_Hardware_Automation,
            Resource.ArgumentWindow_Category_Lighting,
            Resource.ArgumentWindow_Category_Network_Proxy,
            Resource.ArgumentWindow_Category_Debugging
        };

        foreach (var categoryName in categoryOrder)
        {
            var group = groups.FirstOrDefault(g => g.Key == categoryName);
            if (group == null) continue;

            var header = new TextBlock
            {
                Text = categoryName,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(4, 16, 0, 8)
            };
            _optionsPanel.Children.Add(header);

            foreach (var item in group)
            {
                var card = CreateCard(item.Property, item.Meta, currentArgs);
                _optionsPanel.Children.Add(card);
            }
        }
    }

    private CardControl CreateCard(PropertyInfo prop, FlagMetadata meta, List<string> currentArgs)
    {
        var card = new CardControl
        {
            Header = meta.DisplayName,
            Icon = meta.Icon,
            Margin = new Thickness(0, 0, 0, 4),
            Tag = meta.ArgKey
        };

        if (prop.PropertyType == typeof(bool))
        {
            var toggle = new ToggleSwitch
            {
                IsChecked = currentArgs.Any(x => x == meta.ArgKey)
            };
            card.Content = toggle;
        }
        else if (prop.PropertyType == typeof(string) || prop.PropertyType == typeof(Uri))
        {
            var textBox = new TextBox
            {
                Width = 220,
                PlaceholderText = Resource.ArgumentWindow_NotSet,
                Text = GetStringValue(currentArgs, meta.ArgKey) ?? ""
            };
            card.Content = textBox;
        }

        return card;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var newArgs = new List<string>();

            foreach (var child in _optionsPanel.Children)
            {
                if (child is CardControl { Tag: string argKey } card)
                {
                    if (card.Content is ToggleSwitch { IsChecked: true })
                    {
                        newArgs.Add(argKey);
                    }
                    else if (card.Content is TextBox textBox && !string.IsNullOrWhiteSpace(textBox.Text))
                    {
                        newArgs.Add($"{argKey}={textBox.Text.Trim()}");
                    }
                }
            }

            var argsFile = Path.Combine(Folders.AppData, "args.txt");
            var dir = Path.GetDirectoryName(argsFile);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllLines(argsFile, newArgs);

            Close();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error saving args: {ex.Message}", ex);
        }
    }

    private List<string> LoadCurrentArgs()
    {
        try
        {
            var argsFile = Path.Combine(Folders.AppData, "args.txt");
            return File.Exists(argsFile) ? File.ReadAllLines(argsFile).ToList() : new List<string>();
        }
        catch { return new List<string>(); }
    }

    private string? GetStringValue(List<string> values, string key)
    {
        var value = values.FirstOrDefault(s => s.StartsWith(key));
        if (value != null && value.Length > key.Length + 1)
        {
            return value.Substring(key.Length + 1);
        }
        return null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}