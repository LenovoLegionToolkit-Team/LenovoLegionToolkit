using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class ExcludeProcessesWindow
{
    private readonly ApplicationSettings _settings = IoCContainer.Resolve<ApplicationSettings>();
    private readonly ObservableCollection<ExcludeProcessViewModel> _excludedProcesses = [];
    private readonly ObservableCollection<ExcludeProcessViewModel> _runningProcesses = [];
    private ICollectionView _runningView = null!;

    public ExcludeProcessesWindow()
    {
        InitializeComponent();

        _excludedList.ItemsSource = _excludedProcesses;

        _runningView = CollectionViewSource.GetDefaultView(_runningProcesses);
        _runningView.Filter = FilterRunningProcess;
        _runningList.ItemsSource = _runningView;

        IsVisibleChanged += ExcludeProcessesWindow_IsVisibleChanged;
    }

    private async void ExcludeProcessesWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _loader.IsLoading = true;
        var loadingTask = Task.Delay(200);

        _excludedProcesses.Clear();
        _runningProcesses.Clear();

        var savedExcluded = _settings.Store.ExcludedProcesses.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var data = await Task.Run(() =>
        {
            var currentSessionId = Process.GetCurrentProcess().SessionId;
            var allProcesses = Process.GetProcesses();

            var sessionProcesses = new List<Process>();
            foreach (var p in allProcesses)
            {
                try
                {
                    if (p.SessionId == currentSessionId)
                    {
                        sessionProcesses.Add(p);
                        continue;
                    }
                }
                catch { }
                p.Dispose();
            }

            var processGroups = sessionProcesses.GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase).ToList();
            var results = new List<ExcludeProcessViewModel>();

            foreach (var group in processGroups)
            {
                var name = group.Key;
                string? path = null;

                foreach (var p in group)
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            path = p.GetFileName();
                        }
                        catch { }
                    }
                    p.Dispose();
                }

                ImageSource? icon = null;
                if (!string.IsNullOrEmpty(path))
                {
                    icon = ExtractIcon(path);
                }

                results.Add(new ExcludeProcessViewModel
                {
                    Name = name,
                    Path = path ?? string.Empty,
                    Icon = icon
                });
            }

            return results;
        });

        foreach (var name in savedExcluded.OrderBy(n => n))
        {
            var match = data.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _excludedProcesses.Add(match);
                data.Remove(match);
            }
            else
            {
                _excludedProcesses.Add(new ExcludeProcessViewModel { Name = name });
            }
        }

        foreach (var p in data.OrderBy(p => p.Name))
        {
            _runningProcesses.Add(p);
        }

        UpdateHeaders();

        await loadingTask;
        _loader.IsLoading = false;
        _searchBox.Focus();
    }

    private bool FilterRunningProcess(object obj)
    {
        if (obj is not ExcludeProcessViewModel vm)
            return false;

        var query = _searchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return vm.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateHeaders()
    {
        _excludedExpander.Header = string.Format(Resource.ExcludeProcessesWindow_ExcludedProcesses_Format, _excludedProcesses.Count);
        _noExcludedProcessesText.Visibility = _excludedProcesses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _excludedList.Visibility = _excludedProcesses.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var runningCount = _runningView?.Cast<object>().Count() ?? _runningProcesses.Count;
        _runningExpander.Header = string.Format(Resource.ExcludeProcessesWindow_RunningProcesses_Format, runningCount);
    }

    private void AddProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button el && el.CommandParameter is ExcludeProcessViewModel vm)
        {
            _runningProcesses.Remove(vm);

            var insertIndex = 0;
            while (insertIndex < _excludedProcesses.Count && string.Compare(_excludedProcesses[insertIndex].Name, vm.Name, StringComparison.OrdinalIgnoreCase) < 0)
            {
                insertIndex++;
            }
            _excludedProcesses.Insert(insertIndex, vm);

            UpdateHeaders();
        }
    }

    private void RemoveProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button el && el.CommandParameter is ExcludeProcessViewModel vm)
        {
            _excludedProcesses.Remove(vm);

            if (!string.IsNullOrEmpty(vm.Path))
            {
                var insertIndex = 0;
                while (insertIndex < _runningProcesses.Count && string.Compare(_runningProcesses[insertIndex].Name, vm.Name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    insertIndex++;
                }
                _runningProcesses.Insert(insertIndex, vm);
                _runningView.Refresh();
            }

            UpdateHeaders();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _runningView.Refresh();
        UpdateHeaders();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var processes = _excludedProcesses.Select(x => x.Name).ToList();

        _settings.Store.ExcludedProcesses = processes;
        _settings.SynchronizeStore();

        Task.Run(async () =>
        {
            try
            {
                var automationProcessor = IoCContainer.Resolve<AutomationProcessor>();
                await automationProcessor.RestartListenersAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to restart listeners after updating excluded processes.", ex);
            }
        });

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static ImageSource? ExtractIcon(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            var imageSource = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            imageSource.Freeze();
            return imageSource;
        }
        catch { return null; }
    }
}

public class ExcludeProcessViewModel
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public ImageSource? Icon { get; set; }
}
