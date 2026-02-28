using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

/// <summary>
/// ViewModel for history list items
/// </summary>
public class GpuProcessHistoryItem
{
    public string ProcessName { get; init; } = string.Empty;
    public string TimeInfo { get; init; } = string.Empty;
    public Brush StatusColor { get; init; } = Brushes.Gray;
}

/// <summary>
/// Window showing GPU process history and current status
/// </summary>
public partial class GPUWatcherWindow
{
    private readonly GPUController _gpuController;
    private readonly ObservableCollection<GpuProcessHistoryItem> _historyItems = new();

    public GPUWatcherWindow()
    {
        InitializeComponent();
        
        _gpuController = IoCContainer.Resolve<GPUController>();
        _historyList.ItemsSource = _historyItems;
        
        Loaded += GPUWatcherWindow_Loaded;
        
        // Subscribe to refresh events
        _gpuController.Refreshed += GpuController_Refreshed;
    }

    private void GPUWatcherWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshUI();
    }

    private void GpuController_Refreshed(object? sender, GPUStatus e)
    {
        Dispatcher.Invoke(() =>
        {
            // Update active processes display
            if (e.Processes.Count > 0)
            {
                var names = e.Processes.Select(p => SafeGetProcessName(p)).Where(n => !string.IsNullOrEmpty(n));
                _activeProcessesText.Text = string.Join(", ", names);
            }
            else
            {
                _activeProcessesText.Text = "None";
            }
            
            RefreshHistory();
        });
    }

    private void RefreshUI()
    {
        RefreshHistory();
        UpdateStatus();
    }

    private void RefreshHistory()
    {
        if (_gpuController.WatcherService is null)
        {
            _countText.Text = "Service unavailable";
            return;
        }
        
        var history = _gpuController.WatcherService.History;
        
        _historyItems.Clear();
        
        foreach (var entry in history.Reverse())
        {
            var timeInfo = entry.IsActive 
                ? $"Started {entry.StartTime:HH:mm:ss}"
                : $"{entry.StartTime:HH:mm} - {entry.EndTime:HH:mm}";
                
            var statusColor = entry.IsActive 
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))   // Green
                : new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
            
            _historyItems.Add(new GpuProcessHistoryItem
            {
                ProcessName = entry.ProcessName,
                TimeInfo = timeInfo,
                StatusColor = statusColor
            });
        }
        
        _countText.Text = $"{history.Count} entries";
    }

    private void UpdateStatus()
    {
        var watcherService = _gpuController.WatcherService;
        if (watcherService is null)
        {
            _statusText.Text = "GPU Watcher service not available";
            return;
        }
        
        var activeCount = watcherService.History.Count(h => h.IsActive);
        _statusText.Text = activeCount > 0 
            ? $"{activeCount} active process(es) using GPU"
            : "Tracking processes using discrete GPU";
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _gpuController.WatcherService?.ClearHistory();
        RefreshUI();
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
    
    private static string SafeGetProcessName(System.Diagnostics.Process process)
    {
        try { return process.ProcessName; }
        catch { return string.Empty; }
    }
}
