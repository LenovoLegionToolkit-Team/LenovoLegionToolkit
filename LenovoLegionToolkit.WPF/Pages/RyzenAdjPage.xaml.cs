using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Utils;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Pages;

/// <summary>
/// RyzenAdj page for advanced AMD CPU tuning.
/// Provides independent control of power limits and undervolting via SMU.
/// </summary>
public partial class RyzenAdjPage
{
    private readonly RyzenAdjController _controller;
    private readonly RyzenAdjSettings _settings;
    private bool _isLoading = true;

    public RyzenAdjPage()
    {
        _settings = IoCContainer.Resolve<RyzenAdjSettings>();
        _controller = IoCContainer.Resolve<RyzenAdjController>();

        InitializeComponent();
        IsVisibleChanged += RyzenAdjPage_IsVisibleChanged;
    }

    private async void RyzenAdjPage_Initialized(object? sender, EventArgs e)
    {
        await RefreshAsync();
    }

    private async void RyzenAdjPage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            await LoadTurboBoostStatus();
            await LoadMaxCpuFrequency();
        }
    }

    private async Task RefreshAsync()
    {
        _isLoading = true;

        try
        {
            // Initialize controller (detects CPU)
            _controller.Initialize();

            // Update CPU info
            _cpuNameText.Text = _controller.CpuName;
            _cpuCodenameText.Text = $"Codename: {_controller.CpuCodename}";

            // Check if supported
            if (!_controller.IsSupported)
            {
                ShowNotSupported();
                return;
            }

            // Check driver status
            var driverAvailable = _controller.CheckDriverAvailable();
            UpdateDriverStatus(driverAvailable);

            if (!driverAvailable)
            {
                _driverNotFoundBar.IsOpen = true;
                SetControlsEnabled(false);
                return;
            }

            // Show controls
            _notSupportedBar.IsOpen = false;
            _driverNotFoundBar.IsOpen = false;
            SetControlsEnabled(true);

            // Update UV support
            _cpuUvCard.Visibility = _controller.SupportsUndervolting ? Visibility.Visible : Visibility.Collapsed;
            _igpuUvCard.Visibility = _controller.SupportsIgpuUndervolting ? Visibility.Visible : Visibility.Collapsed;

            // Hide UV warning if no UV support
            if (!_controller.SupportsUndervolting && !_controller.SupportsIgpuUndervolting)
            {
                _undervoltHeader.Visibility = Visibility.Collapsed;
                _undervoltWarningBar.Visibility = Visibility.Collapsed;
            }

            // Load settings into UI
            LoadSettings();
            await LoadTurboBoostStatus();
            await LoadMaxCpuFrequency();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[RyzenAdjPage] Refresh failed: {ex.Message}");
            ShowNotSupported();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ShowNotSupported()
    {
        _notSupportedBar.IsOpen = true;
        SetControlsEnabled(false);
        _powerLimitsHeader.Visibility = Visibility.Collapsed;
        _undervoltHeader.Visibility = Visibility.Collapsed;
        _undervoltWarningBar.Visibility = Visibility.Collapsed;
    }

    private void SetControlsEnabled(bool enabled)
    {
        _stapmCard.IsEnabled = enabled;
        _slowCard.IsEnabled = enabled;
        _fastCard.IsEnabled = enabled;
        _tempCard.IsEnabled = enabled;
        _cpuUvCard.IsEnabled = enabled;
        _igpuUvCard.IsEnabled = enabled;
        _autoApplyStartupToggle.IsEnabled = enabled;
        _autoApplyPowerModeToggle.IsEnabled = enabled;
        _applyButton.IsEnabled = enabled;
        _resetAllButton.IsEnabled = enabled;
        _presetsButton.IsEnabled = enabled;
        _maxFreqCard.IsEnabled = enabled;
    }

    private void UpdateDriverStatus(bool available)
    {
        if (available)
        {
            _driverStatusIcon.Symbol = SymbolRegular.CheckmarkCircle24;
            _driverStatusIcon.Foreground = (System.Windows.Media.Brush)FindResource("SystemFillColorSuccessBrush");
            _driverStatusText.Text = "WinRing0 Driver: Loaded";
        }
        else
        {
            _driverStatusIcon.Symbol = SymbolRegular.DismissCircle24;
            _driverStatusIcon.Foreground = (System.Windows.Media.Brush)FindResource("SystemFillColorCriticalBrush");
            _driverStatusText.Text = "WinRing0 Driver: Not Found";
        }
    }

    private void LoadSettings()
    {
        var store = _settings.Store;

        // Enable checkboxes
        _stapmEnabled.IsChecked = store.StapmLimitEnabled;
        _slowEnabled.IsChecked = store.SlowLimitEnabled;
        _fastEnabled.IsChecked = store.FastLimitEnabled;
        _tempEnabled.IsChecked = store.CpuTempLimitEnabled;
        _cpuUvEnabled.IsChecked = store.CpuUndervoltEnabled;
        _igpuUvEnabled.IsChecked = store.IgpuUndervoltEnabled;

        // Power limits
        _stapmSlider.Value = store.StapmLimit ?? 45;
        _slowSlider.Value = store.SlowLimit ?? 54;
        _fastSlider.Value = store.FastLimit ?? 65;
        _tempSlider.Value = store.CpuTempLimit ?? 90;

        // Undervolting
        _cpuUvSlider.Value = store.CpuUndervolt ?? 0;
        _igpuUvSlider.Value = store.IgpuUndervolt ?? 0;

        // Options
        _autoApplyStartupToggle.IsChecked = store.AutoApplyOnStartup;
        _autoApplyPowerModeToggle.IsChecked = store.AutoApplyOnPowerModeChange;

        // Update value texts
        UpdateValueTexts();
    }

    private void UpdateValueTexts()
    {
        _stapmValueText.Text = $"{(int)_stapmSlider.Value} W";
        _slowValueText.Text = $"{(int)_slowSlider.Value} W";
        _fastValueText.Text = $"{(int)_fastSlider.Value} W";
        _tempValueText.Text = $"{(int)_tempSlider.Value} °C";
        _cpuUvValueText.Text = $"{(int)_cpuUvSlider.Value}";
        _igpuUvValueText.Text = $"{(int)_igpuUvSlider.Value}";
    }

    private void SaveSettings()
    {
        var store = _settings.Store;

        // Enable flags
        store.StapmLimitEnabled = _stapmEnabled.IsChecked ?? false;
        store.SlowLimitEnabled = _slowEnabled.IsChecked ?? false;
        store.FastLimitEnabled = _fastEnabled.IsChecked ?? false;
        store.CpuTempLimitEnabled = _tempEnabled.IsChecked ?? false;
        store.CpuUndervoltEnabled = _cpuUvEnabled.IsChecked ?? false;
        store.IgpuUndervoltEnabled = _igpuUvEnabled.IsChecked ?? false;

        // Values
        store.StapmLimit = (int)_stapmSlider.Value;
        store.SlowLimit = (int)_slowSlider.Value;
        store.FastLimit = (int)_fastSlider.Value;
        store.CpuTempLimit = (int)_tempSlider.Value;
        store.CpuUndervolt = (int)_cpuUvSlider.Value;
        store.IgpuUndervolt = (int)_igpuUvSlider.Value;
        store.AutoApplyOnStartup = _autoApplyStartupToggle.IsChecked ?? false;
        store.AutoApplyOnPowerModeChange = _autoApplyPowerModeToggle.IsChecked ?? false;

        _controller.SaveSettings();
    }

    // Slider event handlers
    private void StapmSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        _stapmValueText.Text = $"{(int)e.NewValue} W";
    }

    private void SlowSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        _slowValueText.Text = $"{(int)e.NewValue} W";
    }

    private void FastSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        _fastValueText.Text = $"{(int)e.NewValue} W";
    }

    private void TempSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        _tempValueText.Text = $"{(int)e.NewValue} °C";
    }

    private void CpuUvSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        _cpuUvValueText.Text = $"{(int)e.NewValue}";
    }

    private void IgpuUvSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        _igpuUvValueText.Text = $"{(int)e.NewValue}";
    }

    // Toggle event handlers
    private void AutoApplyStartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SaveSettings();
    }

    private void AutoApplyPowerModeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SaveSettings();
    }



    // P/Invoke for reading power settings
    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(
        IntPtr RootPowerKey,
        ref Guid SchemeGuid,
        ref Guid SubGroupOfPowerSettingsGuid,
        ref Guid PowerSettingGuid,
        out uint AcValueIndex);

    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static readonly Guid GUID_PROCESSOR_SETTINGS_SUBGROUP = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid GUID_PERFBOOSTMODE = new("be337238-0d82-4146-a960-4f3749d470c7");

    private async Task LoadTurboBoostStatus()
    {
        try
        {
            await Task.Run(() =>
            {
                // Get active power scheme
                if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr activePolicyGuid) != 0)
                {
                    Log.Instance.Trace($"[RyzenAdjPage] Failed to get active power scheme");
                    return;
                }

                try
                {
                    var schemeGuid = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(activePolicyGuid);
                    var subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
                    var setting = GUID_PERFBOOSTMODE;

                    var result = PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, out uint value);
                    if (result != 0)
                    {
                        Log.Instance.Trace($"[RyzenAdjPage] PowerReadACValueIndex failed: {result}");
                        return;
                    }

                    Log.Instance.Trace($"[RyzenAdjPage] Detected Turbo Boost mode: {value}");

                    // Map value to Tag
                    var tag = value switch
                    {
                        0 => "Disabled",
                        1 => "Enabled",
                        2 => "Aggressive",
                        3 => "EfficientEnabled",
                        4 => "EfficientAggressive",
                        5 => "AggressiveGuaranteed",
                        6 => "EfficientAggressiveGuaranteed",
                        _ => "Enabled"
                    };

                    Dispatcher.Invoke(() =>
                    {
                        foreach (ComboBoxItem item in _turboBoostComboBox.Items)
                        {
                            if (item.Tag?.ToString() == tag)
                            {
                                var prevLoading = _isLoading;
                                _isLoading = true;
                                _turboBoostComboBox.SelectedItem = item;
                                _isLoading = prevLoading;
                                break;
                            }
                        }
                    });
                }
                finally
                {
                    LocalFree(activePolicyGuid);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[RyzenAdjPage] Failed to load Turbo Boost status: {ex.Message}");
        }
    }

    private async Task LoadMaxCpuFrequency()
    {
        try
        {
            await Task.Run(() =>
            {
                if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr activePolicyGuidPtr) != 0) return;
                
                try
                {
                    var schemeGuid = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(activePolicyGuidPtr);
                    var subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
                    var setting = GUID_PROCFREQMAX;

                    var result = PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, out uint value);
                    if (result != 0)
                    {
                        Log.Instance.Trace($"[RyzenAdjPage] PowerReadACValueIndex for PROCFREQMAX failed: {result}");
                        return;
                    }

                    Log.Instance.Trace($"[RyzenAdjPage] Detected Max CPU Freq: {value} MHz");

                    Dispatcher.Invoke(() =>
                    {
                        var prevLoading = _isLoading;
                        _isLoading = true;
                        
                        if (value == 0)
                        {
                            // Unlimited
                            _maxFreqEnabled.IsChecked = false;
                            _maxFreqSlider.IsEnabled = false;
                            _maxFreqValueText.Text = "Unlimited";
                        }
                        else
                        {
                            _maxFreqEnabled.IsChecked = true;
                            _maxFreqSlider.IsEnabled = true;
                            _maxFreqSlider.Value = Math.Clamp(value, 1400, 5400);
                            _maxFreqValueText.Text = $"{value} MHz";
                        }
                        
                        _isLoading = prevLoading;
                    });
                }
                finally
                {
                    LocalFree(activePolicyGuidPtr);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[RyzenAdjPage] Failed to load Max CPU Freq: {ex.Message}");
        }
    }

    private async void TurboBoostComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        
        if (_turboBoostComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            try 
            {
                int value = tag switch
                {
                    "Disabled" => 0,
                    "Enabled" => 1,
                    "Aggressive" => 2,
                    "EfficientEnabled" => 3,
                    "EfficientAggressive" => 4,
                    "AggressiveGuaranteed" => 5,
                    "EfficientAggressiveGuaranteed" => 6,
                    _ => 2
                };

                // Apply to AC and DC for consistency
                await CMD.RunAsync("powercfg", $"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {value}");
                await CMD.RunAsync("powercfg", $"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {value}");
                await CMD.RunAsync("powercfg", "/setactive SCHEME_CURRENT");
                
                Log.Instance.Trace($"[RyzenAdjPage] Turbo Mode set to {tag} ({value})");
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"[RyzenAdjPage] Failed to set Turbo Boost: {ex.Message}");
                SnackbarHelper.Show("Turbo Boost", "Failed to apply settings", SnackbarType.Error);
            }
        }
    }

    private static Task RunPowerCfgAsync(string arguments)
    {
        return Task.Run(() =>
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(5000);
        });
    }

    // Max CPU Frequency handlers
    private void MaxFreqEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _maxFreqSlider.IsEnabled = _maxFreqEnabled.IsChecked ?? false;
        
        if (!(_maxFreqEnabled.IsChecked ?? false))
        {
            // Unchecked = unlimited
            _maxFreqValueText.Text = "Unlimited";
            _ = Task.Run(() => SetMaxCpuFrequency(0));
        }
        else
        {
            UpdateMaxFreqText();
            _ = Task.Run(() => SetMaxCpuFrequency((uint)_maxFreqSlider.Value));
        }
    }

    private void MaxFreqSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading) return;
        UpdateMaxFreqText();
        
        if (_maxFreqEnabled.IsChecked ?? false)
        {
            _ = Task.Run(() => SetMaxCpuFrequency((uint)_maxFreqSlider.Value));
        }
    }

    private void UpdateMaxFreqText()
    {
        if (!(_maxFreqEnabled.IsChecked ?? false))
        {
            _maxFreqValueText.Text = "Unlimited";
        }
        else
        {
            _maxFreqValueText.Text = $"{(int)_maxFreqSlider.Value} MHz";
        }
    }

    // P/Invoke for Max CPU Frequency
    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(
        IntPtr RootPowerKey,
        ref Guid SchemeGuid,
        ref Guid SubGroupOfPowerSettingsGuid,
        ref Guid PowerSettingGuid,
        uint AcValueIndex);

    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    private static readonly Guid GUID_PROCFREQMAX = new("75b0ae3f-bce0-45a7-8c89-c9611c25e100");

    /// <summary>
    /// Sets maximum CPU frequency in MHz. 0 = unlimited.
    /// </summary>
    private static void SetMaxCpuFrequency(uint mhz)
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr activePolicyGuidPtr) != 0) return;
            
            try
            {
                var schemeGuid = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(activePolicyGuidPtr);
                var subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
                var setting = GUID_PROCFREQMAX;

                PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, mhz);
                PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
                
                Log.Instance.Trace($"[RyzenAdjPage] Max CPU Freq set to {(mhz == 0 ? "Unlimited" : $"{mhz} MHz")}");
            }
            finally
            {
                LocalFree(activePolicyGuidPtr);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[RyzenAdjPage] Failed to set Max CPU Freq: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets Turbo Boost mode: 0=Disabled, 1=Enabled, 2=Aggressive, etc.
    /// </summary>
    private static void SetTurboBoostMode(uint mode)
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr activePolicyGuidPtr) != 0) return;
            
            try
            {
                var schemeGuid = System.Runtime.InteropServices.Marshal.PtrToStructure<Guid>(activePolicyGuidPtr);
                var subGroup = GUID_PROCESSOR_SETTINGS_SUBGROUP;
                var setting = GUID_PERFBOOSTMODE;

                PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, mode);
                PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
                
                Log.Instance.Trace($"[RyzenAdjPage] Turbo Boost mode set to {mode}");
            }
            finally
            {
                LocalFree(activePolicyGuidPtr);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[RyzenAdjPage] Failed to set Turbo Boost: {ex.Message}");
        }
    }

    // Checkbox enable handlers
    private void StapmEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _stapmSlider.IsEnabled = _stapmEnabled.IsChecked ?? false;
    }

    private void SlowEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _slowSlider.IsEnabled = _slowEnabled.IsChecked ?? false;
    }

    private void FastEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _fastSlider.IsEnabled = _fastEnabled.IsChecked ?? false;
    }

    private void TempEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _tempSlider.IsEnabled = _tempEnabled.IsChecked ?? false;
    }

    private void CpuUvEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _cpuUvSlider.IsEnabled = _cpuUvEnabled.IsChecked ?? false;
    }

    private void IgpuUvEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _igpuUvSlider.IsEnabled = _igpuUvEnabled.IsChecked ?? false;
    }

    // Button event handlers
    private async void LoadBatteryDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        // Recommended Battery Saver Settings - Low power + UV for max autonomy
        _stapmEnabled.IsChecked = true;
        _stapmSlider.Value = 15;

        _slowEnabled.IsChecked = true;
        _slowSlider.Value = 20;

        _fastEnabled.IsChecked = true;
        _fastSlider.Value = 25;

        _tempEnabled.IsChecked = true;
        _tempSlider.Value = 70;

        // Enable UV for better battery life
        _cpuUvEnabled.IsChecked = true;
        _cpuUvSlider.Value = -10;
        
        _igpuUvEnabled.IsChecked = true;
        _igpuUvSlider.Value = -10;
        
        _autoApplyPowerModeToggle.IsChecked = true;

        UpdateValueTexts();
        SnackbarHelper.Show("Battery Saver", "Applying optimized settings for max battery life...", SnackbarType.Info);

        // Auto-apply immediately
        try
        {
            SaveSettings();
            var success = await _controller.ApplySettingsAsync();
            if (success) 
                SnackbarHelper.Show("Battery Saver", "Applied: 15W / 70°C / UV -10", SnackbarType.Success);
            else 
                SnackbarHelper.Show("RyzenAdj", "Failed to apply some settings.", SnackbarType.Warning);
        }
        catch (Exception ex)
        {
             Log.Instance.Trace($"[RyzenAdjPage] Auto-apply failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Gaming Optimized preset: Higher power limits with aggressive UV for lower temps and better performance.
    /// </summary>
    private async void LoadGamingPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        // Gaming Optimized Settings - High performance + UV for thermal headroom
        _stapmEnabled.IsChecked = true;
        _stapmSlider.Value = 45;

        _slowEnabled.IsChecked = true;
        _slowSlider.Value = 54;

        _fastEnabled.IsChecked = true;
        _fastSlider.Value = 65;

        _tempEnabled.IsChecked = true;
        _tempSlider.Value = 95;

        // Aggressive UV for lower temps and more boost
        _cpuUvEnabled.IsChecked = true;
        _cpuUvSlider.Value = -15;
        
        _igpuUvEnabled.IsChecked = true;
        _igpuUvSlider.Value = -10;
        
        _autoApplyPowerModeToggle.IsChecked = true;

        UpdateValueTexts();
        SnackbarHelper.Show("Gaming Optimized", "Applying performance settings with undervolt...", SnackbarType.Info);

        // Auto-apply immediately
        try
        {
            SaveSettings();
            var success = await _controller.ApplySettingsAsync();
            if (success) 
                SnackbarHelper.Show("Gaming Optimized", "Applied: 45W / 95°C / UV -15 (lower temps, more boost)", SnackbarType.Success);
            else 
                SnackbarHelper.Show("RyzenAdj", "Failed to apply some settings.", SnackbarType.Warning);
        }
        catch (Exception ex)
        {
             Log.Instance.Trace($"[RyzenAdjPage] Gaming preset failed: {ex.Message}");
        }
    }
    
    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _applyButton.IsEnabled = false;

        try
        {
            // Save settings first
            SaveSettings();

            // Apply to hardware
            var success = await _controller.ApplySettingsAsync();

            if (success)
            {
                SnackbarHelper.Show("RyzenAdj", "Settings applied successfully", SnackbarType.Success);
            }
            else
            {
                SnackbarHelper.Show("RyzenAdj", "Some settings could not be applied. Check logs for details.", SnackbarType.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[RyzenAdjPage] Apply failed: {ex.Message}");
            SnackbarHelper.Show("RyzenAdj", $"Failed to apply settings: {ex.Message}", SnackbarType.Error);
        }
        finally
        {
            _applyButton.IsEnabled = true;
        }
    }

    private void ResetAllButton_Click(object sender, RoutedEventArgs e)
    {
        _isLoading = true;

        // Reset checkboxes (all disabled by default)
        _stapmEnabled.IsChecked = false;
        _slowEnabled.IsChecked = false;
        _fastEnabled.IsChecked = false;
        _tempEnabled.IsChecked = false;
        _cpuUvEnabled.IsChecked = false;
        _igpuUvEnabled.IsChecked = false;
        _maxFreqEnabled.IsChecked = false;

        // Reset to defaults
        _stapmSlider.Value = 45;
        _slowSlider.Value = 54;
        _fastSlider.Value = 65;
        _tempSlider.Value = 90;
        _cpuUvSlider.Value = 0;
        _igpuUvSlider.Value = 0;
        _maxFreqSlider.Value = 5400;
        _autoApplyStartupToggle.IsChecked = false;
        _autoApplyPowerModeToggle.IsChecked = false;

        UpdateValueTexts();
        UpdateMaxFreqText();

        _isLoading = false;

        // Save defaults
        SaveSettings();

        // Reset Turbo Boost to Aggressive and Max Freq to Unlimited
        _ = Task.Run(() => SetTurboBoostMode(2)); // Aggressive
        _ = Task.Run(() => SetMaxCpuFrequency(0)); // Unlimited

        // Select Aggressive in combo
        foreach (ComboBoxItem item in _turboBoostComboBox.Items)
        {
            if (item.Tag?.ToString() == "Aggressive")
            {
                _turboBoostComboBox.SelectedItem = item;
                break;
            }
        }

        SnackbarHelper.Show("RyzenAdj", "Все настройки сброшены", SnackbarType.Info);
    }

    private void PresetsButton_Click(object sender, RoutedEventArgs e)
    {
        var contextMenu = new ContextMenu
        {
            PlacementTarget = _presetsButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        var batteryItem = new MenuItem { Header = "🔋 Режим батареи (15W / 70°C)" };
        batteryItem.Click += (s, args) => ApplyPreset_Battery();
        contextMenu.Items.Add(batteryItem);

        var gamingItem = new MenuItem { Header = "🎮 Игровой (45W / 95°C)" };
        gamingItem.Click += (s, args) => ApplyPreset_Gaming();
        contextMenu.Items.Add(gamingItem);

        var maxPerfItem = new MenuItem { Header = "⚡ Макс. производительность" };
        maxPerfItem.Click += (s, args) => ApplyPreset_MaxPerformance();
        contextMenu.Items.Add(maxPerfItem);

        _presetsButton.ContextMenu = contextMenu;
        _presetsButton.ContextMenu.IsOpen = true;
    }

    private void ApplyPreset_Battery()
    {
        _isLoading = true;
        _stapmEnabled.IsChecked = true; _stapmSlider.Value = 15;
        _slowEnabled.IsChecked = true; _slowSlider.Value = 20;
        _fastEnabled.IsChecked = true; _fastSlider.Value = 25;
        _tempEnabled.IsChecked = true; _tempSlider.Value = 70;
        _cpuUvEnabled.IsChecked = true; _cpuUvSlider.Value = -10;
        _maxFreqEnabled.IsChecked = true; _maxFreqSlider.Value = 1800;
        UpdateValueTexts();
        UpdateMaxFreqText();
        _isLoading = false;
        SaveSettings();
        _ = Task.Run(() => SetTurboBoostMode(0)); // Disabled
        _ = Task.Run(() => SetMaxCpuFrequency(1800));
        SnackbarHelper.Show("RyzenAdj", "🔋 Применён режим батареи", SnackbarType.Success);
    }

    private void ApplyPreset_Gaming()
    {
        _isLoading = true;
        _stapmEnabled.IsChecked = true; _stapmSlider.Value = 45;
        _slowEnabled.IsChecked = true; _slowSlider.Value = 54;
        _fastEnabled.IsChecked = true; _fastSlider.Value = 65;
        _tempEnabled.IsChecked = true; _tempSlider.Value = 95;
        _cpuUvEnabled.IsChecked = true; _cpuUvSlider.Value = -15;
        _maxFreqEnabled.IsChecked = false;
        UpdateValueTexts();
        UpdateMaxFreqText();
        _isLoading = false;
        SaveSettings();
        _ = Task.Run(() => SetTurboBoostMode(2)); // Aggressive
        _ = Task.Run(() => SetMaxCpuFrequency(0)); // Unlimited
        SnackbarHelper.Show("RyzenAdj", "🎮 Применён игровой режим", SnackbarType.Success);
    }

    private void ApplyPreset_MaxPerformance()
    {
        _isLoading = true;
        _stapmEnabled.IsChecked = false;
        _slowEnabled.IsChecked = false;
        _fastEnabled.IsChecked = false;
        _tempEnabled.IsChecked = false;
        _cpuUvEnabled.IsChecked = false;
        _maxFreqEnabled.IsChecked = false;
        UpdateValueTexts();
        UpdateMaxFreqText();
        _isLoading = false;
        SaveSettings();
        _ = Task.Run(() => SetTurboBoostMode(2)); // Aggressive
        _ = Task.Run(() => SetMaxCpuFrequency(0)); // Unlimited
        SnackbarHelper.Show("RyzenAdj", "⚡ Макс. производительность", SnackbarType.Success);
    }
}
