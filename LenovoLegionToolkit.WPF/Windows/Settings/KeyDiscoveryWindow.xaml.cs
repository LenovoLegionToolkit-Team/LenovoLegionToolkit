using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class KeyDiscoveryWindow
{
    private readonly SpecialKeyListener _specialKeyListener = IoCContainer.Resolve<SpecialKeyListener>();
    private readonly DriverKeyListener _driverKeyListener = IoCContainer.Resolve<DriverKeyListener>();
    private readonly ConsumerControlKeyListener _consumerControlKeyListener = IoCContainer.Resolve<ConsumerControlKeyListener>();
    private readonly SpecialKeySettings _settings = IoCContainer.Resolve<SpecialKeySettings>();
    private readonly ObservableCollection<DetectedKeyEvent> _events = [];
    private bool _isDiscovering;

    public KeyDiscoveryWindow()
    {
        InitializeComponent();
        Title = _title.Text = Resource.SpecialKeyDetailWindow_KeyDiscovery;
        _statusLabel.Text = Resource.SpecialKeyDetailWindow_KeyDiscovery_Message;
        _eventList.ItemsSource = _events;
        Closed += (_, _) => StopKeyDiscovery();
    }

    private void DiscoveryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDiscovering)
            StopKeyDiscovery();
        else
            StartKeyDiscovery();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isDiscovering)
        {
            StopKeyDiscovery();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void StartKeyDiscovery()
    {
        _isDiscovering = true;
        _events.Clear();
        _listeningOverlay.Visibility = Visibility.Visible;

        _statusLabel.Text = Resource.SpecialKeyDetailWindow_KeyDiscovery_Message;
        _discoveryButton.Content = Resource.SpecialKeyDetailWindow_KeyDiscovery_Stop;

        _specialKeyListener.DiscoveryMode = true;
        _specialKeyListener.Changed += OnSpecialKeyDetected;

        _driverKeyListener.DiscoveryMode = true;
        _driverKeyListener.Changed += OnDriverKeyDetected;

        _consumerControlKeyListener.DiscoveryMode = true;
        _consumerControlKeyListener.Changed += OnConsumerControlKeyDetected;
    }

    private void StopKeyDiscovery()
    {
        if (!_isDiscovering)
            return;

        _specialKeyListener.Changed -= OnSpecialKeyDetected;
        _specialKeyListener.DiscoveryMode = false;

        _driverKeyListener.Changed -= OnDriverKeyDetected;
        _driverKeyListener.DiscoveryMode = false;

        _consumerControlKeyListener.Changed -= OnConsumerControlKeyDetected;
        _consumerControlKeyListener.DiscoveryMode = false;

        _isDiscovering = false;
        _listeningOverlay.Visibility = Visibility.Collapsed;
        _statusLabel.Text = Resource.SpecialKeyDetailWindow_KeyDiscovery_Message;
        _discoveryButton.Content = Resource.SpecialKeyDetailWindow_KeyDiscovery_Start;
    }

    private void OnSpecialKeyDetected(object? sender, SpecialKeyListener.ChangedEventArgs e)
    {
        var isKnown = Enum.IsDefined(typeof(SpecialKey), (SpecialKey)e.RawValue);
        var name = GetSpecialKeyDisplayName(e.RawValue, isKnown);
        InsertEvent(e.RawValue, "WMI", name, isKnown);
    }

    private void OnDriverKeyDetected(object? sender, DriverKeyListener.ChangedEventArgs e)
    {
        for (var bit = 1u; bit != 0; bit <<= 1)
        {
            if ((e.RawValue & bit) == 0)
                continue;

            var rawValue = (int)bit;
            var isKnown = Enum.IsDefined(typeof(DriverKey), (DriverKey)bit);
            var name = GetDriverKeyDisplayName(rawValue, isKnown);
            InsertEvent(rawValue, "IOCTL", name, isKnown);
        }
    }

    private void OnConsumerControlKeyDetected(object? sender, ConsumerControlKeyListener.ChangedEventArgs e)
    {
        var rawValue = (int)e.Key;
        var isKnown = Enum.IsDefined(typeof(ConsumerControlKey), e.Key);
        var name = GetConsumerControlKeyDisplayName(rawValue, isKnown);
        InsertEvent(rawValue, "HID", name, isKnown);
    }

    private void InsertEvent(int code, string channel, string displayName, bool isKnown)
    {
        var storageCode = channel switch
        {
            "IOCTL" => code + SpecialKeySettings.SpecialKeySettingsStore.DriverKeyCodeOffset,
            "HID" => code + SpecialKeySettings.SpecialKeySettingsStore.ConsumerControlKeyCodeOffset,
            _ => code,
        };
        var canAdd = !isKnown && !_settings.Store.KeyDescriptions.ContainsKey(storageCode);
        var time = DateTime.Now.ToString("HH:mm:ss");

        var entry = new DetectedKeyEvent
        {
            Code = code,
            StorageCode = storageCode,
            DisplayName = displayName,
            Channel = channel,
            CodeText = string.Format(Resource.KeyDiscovery_CodeText, code),
            Time = time,
            IsKnown = isKnown,
            CanAdd = canAdd
        };

        Dispatcher.Invoke(() =>
        {
            _events.Insert(0, entry);
            _listeningOverlay.Visibility = Visibility.Collapsed;
        });
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _events.Clear();
    }

    private void AddDetectedKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DetectedKeyEvent entry })
            return;

        var name = string.IsNullOrWhiteSpace(entry.DisplayName)
            ? string.Format(Resource.SpecialKey_UnknownKey, entry.Code)
            : entry.DisplayName;

        _settings.Store.KeyDescriptions[entry.StorageCode] = name;
        _settings.Store.KeyModes[entry.StorageCode] = CustomSpecialKey.Default;
        _settings.SynchronizeStore();

        entry.CanAdd = false;
        entry.IsAdded = true;
        entry.DisplayName = name;

        var i = _events.IndexOf(entry);
        if (i >= 0)
            _events[i] = entry;
    }

    private static string GetSpecialKeyDisplayName(int code, bool isKnown) => isKnown
        ? SpecialKeyDisplayName.GetKey((SpecialKey)code)
        : SpecialKeyDisplayName.GetUnknownKey(code, 2);

    private static string GetDriverKeyDisplayName(int code, bool isKnown) => isKnown
        ? SpecialKeyDisplayName.GetKey((DriverKey)code, Resource.SpecialKey_DriverSuffix)
        : SpecialKeyDisplayName.GetUnknownKey(code, 2, Resource.SpecialKey_DriverSuffix);

    private static string GetConsumerControlKeyDisplayName(int code, bool isKnown) => isKnown
        ? SpecialKeyDisplayName.GetKey((ConsumerControlKey)code, Resource.SpecialKey_ConsumerSuffix)
        : SpecialKeyDisplayName.GetUnknownKey(code, 4, Resource.SpecialKey_ConsumerSuffix);
}

public class DetectedKeyEvent : INotifyPropertyChanged
{
    public int Code { get; init; }
    public int StorageCode { get; init; }
    public string Time { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string CodeText { get; init; } = string.Empty;
    public bool IsKnown { get; init; }

    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    private bool _canAdd;
    public bool CanAdd
    {
        get => _canAdd;
        set { _canAdd = value; OnPropertyChanged(); }
    }

    private bool _isAdded;
    public bool IsAdded
    {
        get => _isAdded;
        set { _isAdded = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
