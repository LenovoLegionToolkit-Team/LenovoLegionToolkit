using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Humanizer;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Application = System.Windows.Application;
using Button = Wpf.Ui.Controls.Button;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class SettingsBackupWindow
{
    private static readonly string[] KnownSettingsFiles = 
    {
        "sensor_dashboard.json", "dashboard.json", "hardware_sensors.json", 
        "automation.json", "macro.json", "settings.json", "balancemode.json", 
        "godmode.json", "gpu_oc.json", "integrations.json", 
        "itsmode_settings.json", "lamp_array.json", "package_downloader.json", 
        "update_settings.json", "notification_settings.json", "special_key.json", "sunrise_sunset.json", "spectrum_keyboard.json", 
        "rgb_keyboard.json", "osd.json", "args.txt", "lang"
    };

    private readonly ObservableCollection<BackupItem> _items = new();

    public SettingsBackupWindow()
    {
        InitializeComponent();
        _list.ItemsSource = _items;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await LoadFilesAsync();
    }

    private async Task LoadFilesAsync()
    {
        _loader.IsLoading = true;
        _items.Clear();

        await Task.Run(() =>
        {
            var appData = Folders.AppData;
            if (!Directory.Exists(appData))
                Directory.CreateDirectory(appData);

            foreach (var fileName in KnownSettingsFiles)
            {
                var filePath = Path.Combine(appData, fileName);
                var fileInfo = new FileInfo(filePath);
                
                var item = new BackupItem(fileInfo);
                item.PropertyChanged += Item_PropertyChanged;
                
                Dispatcher.Invoke(() => _items.Add(item));
            }
        });

        _loader.IsLoading = false;
        UpdateSelectAllState();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BackupItem.IsSelected))
        {
            UpdateSelectAllState();
        }
    }

    private void UpdateSelectAllState()
    {
        var allSelected = _items.All(x => x.IsSelected);
        _selectAllToggle.IsChecked = allSelected;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        ToggleSelectAll();
    }

    private void SelectAllToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleSelectAll();
    }

    private void ToggleSelectAll()
    {
         var newState = _selectAllToggle.IsChecked ?? false;
         foreach (var item in _items)
         {
             item.IsSelected = newState;
         }
    }

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = _items.Where(x => x.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            await SnackbarHelper.ShowAsync(Resource.SettingsBackupWindow_Title, Resource.SettingsBackupWindow_NoFilesSelected, SnackbarType.Info);
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = Resource.SettingsBackupWindow_Title,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        try
        {
            var destDir = dialog.SelectedPath;
            foreach (var item in selectedItems)
            {
                if (!File.Exists(item.FullPath)) continue;

                var destPath = Path.Combine(destDir, item.FileName);
                File.Copy(item.FullPath, destPath, true);
            }
            
            await SnackbarHelper.ShowAsync(Resource.SettingsBackupWindow_Title, Resource.SettingsBackupWindow_BackupSuccess, SnackbarType.Success);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Backup failed", ex);
             await SnackbarHelper.ShowAsync(Resource.SettingsBackupWindow_Title, ex.Message, SnackbarType.Error);
        }
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = _items.Where(x => x.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            await SnackbarHelper.ShowAsync(Resource.SettingsBackupWindow_Title, Resource.SettingsBackupWindow_NoFilesSelected, SnackbarType.Info);
            return;
        }

         var result = await MessageBoxHelper.ShowAsync(this, 
             Resource.SettingsBackupWindow_Restore, 
             Resource.SettingsBackupWindow_RestoreConfirm,
             Resource.Yes, 
             Resource.Cancel);

         if (!result) return;
         
        using var dialog = new FolderBrowserDialog
        {
             Description = Resource.SettingsBackupWindow_Restore,
             UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        try
        {
            var sourceDir = dialog.SelectedPath;
            var appData = Folders.AppData;
            var filesRestored = 0;
            
            foreach (var item in selectedItems)
            {
                var sourcePath = Path.Combine(sourceDir, item.FileName);
                if (File.Exists(sourcePath))
                {
                    File.Copy(sourcePath, item.FullPath, true);
                    filesRestored++;
                }
            }

            if (filesRestored > 0)
            {
                App.IsRestoringSettings = true;
                await MessageBoxHelper.ShowAsync(this, 
                    Resource.SettingsBackupWindow_Restore, 
                    Resource.SettingsBackupWindow_RestoreSuccess, 
                    Resource.OK, 
                    string.Empty);
    
                RestartApp();
            }
            else
            {
                 await SnackbarHelper.ShowAsync(Resource.SettingsBackupWindow_Title, Resource.SettingsBackupWindow_NoMatchingFiles, SnackbarType.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Restore failed", ex);
            await SnackbarHelper.ShowAsync(Resource.SettingsBackupWindow_Title, ex.Message, SnackbarType.Error);
        }
    }

    private void RestartApp()
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        Application.Current.Shutdown();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public class BackupItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string FileName { get; }
    public string DisplayName { get; }
    public string FullPath { get; }
    public string FileSize { get; }
    
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public BackupItem(FileInfo info)
    {
        FileName = info.Name;
        DisplayName = Path.GetFileNameWithoutExtension(info.Name).Titleize();
        FullPath = info.FullName;
        FileSize = info.Exists ? BytesToString(info.Length) : "-";
        IsSelected = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static string BytesToString(long byteCount)
    {
        string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; 
        if (byteCount == 0)
            return "0" + suf[0];
        long bytes = Math.Abs(byteCount);
        int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        double num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return (Math.Sign(byteCount) * num) + suf[place];
    }
}
