using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using Wpf.Ui.Common;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Interop;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

public partial class DiscreteGPUManagementWindow : BaseWindow
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly GPUController _gpuController = IoCContainer.Resolve<GPUController>();
    private readonly ObservableCollection<DiscreteGPUAppViewModel> _apps = new();
    private bool _isInitializing = true;

    public DiscreteGPUManagementWindow()
    {
        InitializeComponent();

        _intervalSlider.Value = _settings.Store.GPUMonitoringInterval / 1000.0;
        _intervalText.Text = string.Format(Resource.Seconds, (int)_intervalSlider.Value);

        _startupDelaySlider.Value = Math.Round(_settings.Store.GPUMonitoringStartupDelay / 1000.0, 1);
        _startupDelayText.Text = string.Format(Resource.Seconds, _startupDelaySlider.Value);

        _killDelaySlider.Value = Math.Round(_settings.Store.GPUKillProcessDelay / 1000.0, 1);
        _killDelayText.Text = string.Format(Resource.Seconds, _killDelaySlider.Value);

        _processListView.ItemsSource = _apps;

        Loaded += DiscreteGPUManagementWindow_Loaded;
        _gpuController.Refreshed += GpuController_Refreshed;

        _isInitializing = false;
    }

    private void DiscreteGPUManagementWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshAppList();
    }

    private void GpuController_Refreshed(object? sender, GPUStatus e)
    {
        Dispatcher.Invoke(RefreshAppList);
    }

    private bool _isRefreshing;

    private void RefreshAppList()
    {
        _isRefreshing = true;
        try
        {
            var activeProcesses = _gpuController.AllActiveProcesses.ToList();
            var hasMultipleGpus = Displays.HasMultipleGpus();

            var configuredApps = new List<string>();
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\DirectX\UserGpuPreferences", false);
                if (key != null)
                {
                    configuredApps.AddRange(key.GetValueNames());
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to read registry preferences.", ex);
            }

            var allPaths = activeProcesses.Select(p =>
            {
                try { return p.MainModule?.FileName; } catch { return null; }
            }).Where(path => !string.IsNullOrEmpty(path))
            .Concat(configuredApps.Where(System.IO.Path.IsPathRooted))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            foreach (var path in allPaths)
            {
                if (path == null) continue;

                var existing = _apps.FirstOrDefault(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                var processIds = activeProcesses.Where(p =>
                {
                    try { return p.MainModule?.FileName?.Equals(path, StringComparison.OrdinalIgnoreCase) == true; } catch { return false; }
                }).Select(p => p.Id).ToList();
                var isActive = processIds.Count > 0;

                var preference = _gpuController.GetGpuPreference(path).ToString();

                if (existing != null)
                {
                    existing.ProcessIds = processIds;
                    existing.IsActive = isActive;
                    existing.Preference = preference;
                }
                else
                {
                    var icon = ExtractIcon(path);
                    _apps.Add(new DiscreteGPUAppViewModel
                    {
                        Name = System.IO.Path.GetFileNameWithoutExtension(path),
                        Path = path,
                        IsActive = isActive,
                        ProcessIds = processIds,
                        Preference = preference,
                        IsPreferenceEnabled = hasMultipleGpus,
                        Icon = icon
                    });
                }
            }

            var toRemove = _apps.Where(a => !allPaths.Contains(a.Path, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (var app in toRemove)
                _apps.Remove(app);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;

        var seconds = (int)e.NewValue;
        _intervalText.Text = string.Format(Resource.Seconds, seconds);
        _settings.Store.GPUMonitoringInterval = seconds * 1000;
        _gpuController.Interval = _settings.Store.GPUMonitoringInterval;
        _settings.SynchronizeStore();
    }

    private void StartupDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;

        _startupDelayText.Text = string.Format(Resource.Seconds, Math.Round(e.NewValue, 1));
        _settings.Store.GPUMonitoringStartupDelay = (int)(e.NewValue * 1000);
        _settings.SynchronizeStore();
    }

    private void KillDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;

        _killDelayText.Text = string.Format(Resource.Seconds, Math.Round(e.NewValue, 1));
        _settings.Store.GPUKillProcessDelay = (int)(e.NewValue * 1000);
        _settings.SynchronizeStore();
    }

    private void PreferenceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _isRefreshing || sender is not ComboBox comboBox || comboBox.DataContext is not DiscreteGPUAppViewModel vm)
            return;

        if (!comboBox.IsLoaded || e.RemovedItems.Count == 0)
            return;

        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not ComboBoxItem selectedItem || selectedItem.Tag is not string tag)
            return;

        var preference = tag switch
        {
            "Integrated" => GPUController.GpuPreference.Integrated,
            "Discrete" => GPUController.GpuPreference.Discrete,
            _ => GPUController.GpuPreference.Default
        };

        try
        {
            _gpuController.SetGpuPreference(vm.Path, preference);
            SnackbarHelper.Show(Resource.DiscreteGPUControl_Title, Resource.SettingsPage_RestartRequired_Message, SnackbarType.Success);
            RefreshAppList();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set GPU preference for {vm.Name}.", ex);
        }
    }

    private ImageSource? ExtractIcon(string path)
    {
        try
        {
            if (!System.IO.File.Exists(path)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch { return null; }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _gpuController.Refreshed -= GpuController_Refreshed;
        base.OnClosed(e);
    }

    private void KillProcessMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.CommandParameter is List<int> pids)
        {
            foreach (var pid in pids)
            {
                try
                {
                    var process = Process.GetProcessById(pid);
                    process.Kill(true);
                }
                catch (Exception) { }
            }
            Task.Run(async () =>
            {
                await Task.Delay(_settings.Store.GPUKillProcessDelay);
                await _gpuController.RefreshNowAsync();
            });
        }
    }
}

public class DiscreteGPUAppViewModel : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsPreferenceEnabled { get; set; }

    public List<int> ProcessIds { get; set; } = [];
    public bool CanKill => IsActive && ProcessIds.Count > 0;

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(CanKill));
        }
    }

    private string _preference = "Default";
    public string Preference
    {
        get => _preference;
        set
        {
            if (_preference == value) return;
            _preference = value;
            OnPropertyChanged();
        }
    }

    public string Status => IsActive ? Resource.Active : Resource.Inactive;

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (_icon == value) return;
            _icon = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? binding = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(binding));
}


