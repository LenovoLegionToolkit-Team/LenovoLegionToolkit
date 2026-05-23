using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class NotificationTypeCustomizationWindow
{
    private sealed class NotificationTypeRow(
        NotificationType type,
        SymbolRegularPickerControl iconPicker,
        ColorPickerControl iconColorPicker,
        ColorPickerControl textColorPicker,
        ComboBox positionComboBox,
        ComboBox durationComboBox)
    {
        public NotificationType Type { get; } = type;
        public SymbolRegularPickerControl IconPicker { get; } = iconPicker;
        public ColorPickerControl IconColorPicker { get; } = iconColorPicker;
        public ColorPickerControl TextColorPicker { get; } = textColorPicker;
        public ComboBox PositionComboBox { get; } = positionComboBox;
        public ComboBox DurationComboBox { get; } = durationComboBox;
    }

    private readonly NotificationSettings _settings;
    private readonly IReadOnlyList<(NotificationType Type, string DisplayName)> _types;
    private readonly List<NotificationTypeRow> _rows = [];

    public NotificationTypeCustomizationWindow(
        string categoryTitle,
        IReadOnlyList<(NotificationType Type, string DisplayName)> types,
        NotificationSettings settings)
    {
        _settings = settings;
        _types = types;

        InitializeComponent();

        Title = _title.Text = $"{Resource.Customize} {categoryTitle}";

        BuildRows();
    }

    private void BuildRows()
    {
        _tableGrid.Children.Clear();
        _tableGrid.RowDefinitions.Clear();
        _rows.Clear();

        _tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void AddHeader(string text, int column, HorizontalAlignment alignment)
        {
            var secondaryBrush = Application.Current.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;
            var textBlock = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Foreground = secondaryBrush,
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(column == 0 ? 0 : 6, 4, 6, 12)
            };
            Grid.SetRow(textBlock, 0);
            Grid.SetColumn(textBlock, column);
            _tableGrid.Children.Add(textBlock);
        }

        AddHeader(Resource.NotificationsSettingsWindow_NotificationType, 0, HorizontalAlignment.Left);
        AddHeader(Resource.Icon, 1, HorizontalAlignment.Center);
        AddHeader(Resource.IconColor, 2, HorizontalAlignment.Center);
        AddHeader(Resource.TextColor, 3, HorizontalAlignment.Center);
        AddHeader(Resource.NotificationsSettingsWindow_NotificationPosition_Title, 4, HorizontalAlignment.Center);
        AddHeader(Resource.NotificationsSettingsWindow_NotificationDuration_Title, 5, HorizontalAlignment.Center);

        int rowIndex = 1;
        foreach (var (type, displayName) in _types)
        {
            _tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _rows.Add(BuildRow(type, displayName, rowIndex));
            rowIndex++;
        }
    }

    private NotificationTypeRow BuildRow(NotificationType type, string displayName, int rowIndex)
    {
        var notifications = _settings.Store.Notifications;

        var iconPicker = new SymbolRegularPickerControl
        {
            SelectedSymbol = notifications.IconOverrides.TryGetValue(type, out var iconInt)
                && Enum.IsDefined(typeof(SymbolRegular), iconInt)
                ? (SymbolRegular)iconInt
                : NotificationsManager.GetDefaultSymbol(type),
            OverlaySymbol = NotificationsManager.GetDefaultOverlaySymbol(type),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        };
        iconPicker.SymbolChanged += (_, _) =>
        {
            if (iconPicker.SelectedSymbol.HasValue)
                notifications.IconOverrides[type] = (int)iconPicker.SelectedSymbol.Value;
            else
            {
                notifications.IconOverrides.Remove(type);
                iconPicker.SelectedSymbol = NotificationsManager.GetDefaultSymbol(type);
            }
            _settings.SynchronizeStore();
        };

        var iconColorPicker = new ColorPickerControl
        {
            SelectedColor = notifications.ColorOverrides.TryGetValue(type, out var iconColor)
                ? iconColor.ToColor()
                : Colors.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        };
        iconColorPicker.ColorChangedDelayed += (_, _) =>
        {
            var c = iconColorPicker.SelectedColor;
            notifications.ColorOverrides[type] = new RGBColor(c.R, c.G, c.B);
            _settings.SynchronizeStore();
        };

        var textColorPicker = new ColorPickerControl
        {
            SelectedColor = notifications.TextColorOverrides.TryGetValue(type, out var textColor)
                ? textColor.ToColor()
                : Colors.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        };
        textColorPicker.ColorChangedDelayed += (_, _) =>
        {
            var c = textColorPicker.SelectedColor;
            notifications.TextColorOverrides[type] = new RGBColor(c.R, c.G, c.B);
            _settings.SynchronizeStore();
        };

        var positionComboBox = BuildPositionComboBox(type);
        positionComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        positionComboBox.VerticalAlignment = VerticalAlignment.Center;
        positionComboBox.Margin = new Thickness(6, 4, 6, 4);

        var durationComboBox = BuildDurationComboBox(type);
        durationComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        durationComboBox.VerticalAlignment = VerticalAlignment.Center;
        durationComboBox.Margin = new Thickness(6, 4, 6, 4);

        var dividerBrush = Application.Current.TryFindResource("DividerStrokeColorDefaultBrush") as Brush ?? Brushes.LightGray;
        var separator = new Rectangle
        {
            Height = 1,
            Fill = dividerBrush,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };
        Grid.SetRow(separator, rowIndex);
        Grid.SetColumnSpan(separator, 6);
        _tableGrid.Children.Add(separator);

        var textBlock = new TextBlock
        {
            Text = displayName,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = displayName
        };
        Grid.SetRow(textBlock, rowIndex);
        Grid.SetColumn(textBlock, 0);
        _tableGrid.Children.Add(textBlock);

        Grid.SetRow(iconPicker, rowIndex);
        Grid.SetColumn(iconPicker, 1);
        _tableGrid.Children.Add(iconPicker);

        Grid.SetRow(iconColorPicker, rowIndex);
        Grid.SetColumn(iconColorPicker, 2);
        _tableGrid.Children.Add(iconColorPicker);

        Grid.SetRow(textColorPicker, rowIndex);
        Grid.SetColumn(textColorPicker, 3);
        _tableGrid.Children.Add(textColorPicker);

        Grid.SetRow(positionComboBox, rowIndex);
        Grid.SetColumn(positionComboBox, 4);
        _tableGrid.Children.Add(positionComboBox);

        Grid.SetRow(durationComboBox, rowIndex);
        Grid.SetColumn(durationComboBox, 5);
        _tableGrid.Children.Add(durationComboBox);

        return new NotificationTypeRow(type, iconPicker, iconColorPicker, textColorPicker, positionComboBox, durationComboBox);
    }

    private ComboBox BuildPositionComboBox(NotificationType type)
    {
        var comboBox = new ComboBox { MinWidth = 120, VerticalAlignment = VerticalAlignment.Center };

        comboBox.Items.Add(new ComboBoxItem { Content = Resource.Default, Tag = (NotificationPosition?)null });
        foreach (var pos in Enum.GetValues<NotificationPosition>())
            comboBox.Items.Add(new ComboBoxItem { Content = pos.GetDisplayName(), Tag = (NotificationPosition?)pos });

        var hasOverride = _settings.Store.Notifications.PositionOverrides.TryGetValue(type, out var posOverride);
        if (hasOverride)
        {
            var idx = Array.IndexOf(Enum.GetValues<NotificationPosition>(), posOverride);
            comboBox.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        }
        else
        {
            comboBox.SelectedIndex = 0;
        }

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is ComboBoxItem { Tag: NotificationPosition pos })
                _settings.Store.Notifications.PositionOverrides[type] = pos;
            else
                _settings.Store.Notifications.PositionOverrides.Remove(type);
            _settings.SynchronizeStore();
        };

        return comboBox;
    }

    private ComboBox BuildDurationComboBox(NotificationType type)
    {
        var comboBox = new ComboBox { MinWidth = 100, VerticalAlignment = VerticalAlignment.Center };

        comboBox.Items.Add(new ComboBoxItem { Content = Resource.Default, Tag = (NotificationDuration?)null });
        foreach (var dur in Enum.GetValues<NotificationDuration>())
            comboBox.Items.Add(new ComboBoxItem { Content = dur.GetDisplayName(), Tag = (NotificationDuration?)dur });

        var hasOverride = _settings.Store.Notifications.DurationOverrides.TryGetValue(type, out var durOverride);
        if (hasOverride)
        {
            var idx = Array.IndexOf(Enum.GetValues<NotificationDuration>(), durOverride);
            comboBox.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        }
        else
        {
            comboBox.SelectedIndex = 0;
        }

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is ComboBoxItem { Tag: NotificationDuration dur })
                _settings.Store.Notifications.DurationOverrides[type] = dur;
            else
                _settings.Store.Notifications.DurationOverrides.Remove(type);
            _settings.SynchronizeStore();
        };

        return comboBox;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var notifications = _settings.Store.Notifications;
        foreach (var row in _rows)
        {
            notifications.IconOverrides.Remove(row.Type);
            notifications.ColorOverrides.Remove(row.Type);
            notifications.TextColorOverrides.Remove(row.Type);
            notifications.PositionOverrides.Remove(row.Type);
            notifications.DurationOverrides.Remove(row.Type);
        }
        _settings.SynchronizeStore();
        BuildRows();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

}
