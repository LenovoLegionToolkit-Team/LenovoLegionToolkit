using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.WPF.Converters;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Settings;

namespace LenovoLegionToolkit.WPF.Windows.Dashboard;

public partial class EditSensorGroupWindow
{
    private static EditSensorGroupWindow? _instance;

    private readonly SensorsControlSettings _settings = IoCContainer.Resolve<SensorsControlSettings>();
    public event EventHandler? Apply;

    public EditSensorGroupWindow()
    {
        InitializeComponent();
        InitializeCheckboxes();
    }

    public static void ShowInstance()
    {
        if (_instance == null)
        {
            _instance = new EditSensorGroupWindow();
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

    private void InitializeCheckboxes()
    {
        _groupsStackPanel.Children.Clear();
        var activeItems = new HashSet<SensorItem>(_settings.Store.VisibleItems ?? []);

        foreach (var group in SensorGroup.DefaultGroups)
        {
            var groupBox = new GroupBox
            {
                Header = GetLocalizedGroupName(group.Type),
                Padding = new Thickness(10, 5, 5, 10)
            };

            var stackPanel = new StackPanel();

            foreach (var item in group.Items)
            {
                if (item == SensorItem.GpuTemperatures)
                {
                    continue;
                }

                var checkBox = new CheckBox
                {
                    Content = EnumToLocalizedStringConverter.Convert(item),
                    Tag = item,
                    IsChecked = activeItems.Contains(item)
                };

                stackPanel.Children.Add(checkBox);
            }

            groupBox.Content = stackPanel;
            _groupsStackPanel.Children.Add(groupBox);
        }
    }

    private string GetLocalizedGroupName(SensorGroupType type) => type switch
    {
        SensorGroupType.CPU => Resource.SensorsControl_CPU_Title,
        SensorGroupType.GPU => Resource.SensorsControl_GPU_Title,
        SensorGroupType.Motherboard => Resource.SensorsControl_Motherboard_Title,
        SensorGroupType.Battery => Resource.SensorsControl_Battery_Title,
        SensorGroupType.Memory => Resource.SensorsControl_Memory_Title,
        SensorGroupType.Disk => Resource.SensorsControl_Disk_Title,
        _ => type.ToString()
    };

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = new List<SensorItem>();

        foreach (var groupBox in _groupsStackPanel.Children.OfType<GroupBox>())
        {
            if (groupBox.Content is StackPanel stackPanel)
            {
                foreach (var child in stackPanel.Children.OfType<CheckBox>())
                {
                    if (child is { IsChecked: true, Tag: SensorItem item })
                    {
                        selectedItems.Add(item);
                    }
                }
            }
        }

        if (selectedItems.Contains(SensorItem.GpuCoreTemperature) ||
            selectedItems.Contains(SensorItem.GpuVramTemperature))
        {
            if (!selectedItems.Contains(SensorItem.GpuTemperatures))
            {
                selectedItems.Add(SensorItem.GpuTemperatures);
            }
        }

        _settings.Store.VisibleItems = selectedItems.ToArray();
        _settings.SynchronizeStore();

        MessagingCenter.Publish(new DashboardElementChangedMessage(_settings.Store.VisibleItems));

        Apply?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DefaultButton_Click(object sender, RoutedEventArgs e)
    {
        var defaultItems = SensorGroup.DefaultGroups.SelectMany(g => g.Items).ToArray();
        _settings.Store.VisibleItems = defaultItems;
        InitializeCheckboxes();
    }
}