using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;
using Button = Wpf.Ui.Controls.Button;
using CardControl = LenovoLegionToolkit.WPF.Controls.Custom.CardControl;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard.Edit;

public class EditDashboardItemControl : UserControl
{
    public DashboardItem DashboardItem { get; }

    private readonly CardControl _cardControl = new()
    {
        Margin = new(0, 0, 0, 8)
    };

    private readonly CardHeaderControl _cardHeaderControl = new();

    private readonly StackPanel _stackPanel = new()
    {
        Orientation = Orientation.Horizontal,
    };

    private readonly Button _moveUpButton = new()
    {
        Icon = SymbolRegular.ArrowUp24,
        ToolTip = Resource.MoveUp,
        MinWidth = 34,
        Height = 34,
        Margin = new(8, 0, 0, 0),
    };

    private readonly Button _moveDownButton = new()
    {
        Icon = SymbolRegular.ArrowDown24,
        ToolTip = Resource.MoveDown,
        MinWidth = 34,
        Height = 34,
        Margin = new(8, 0, 0, 0),
    };

    private readonly Button _deleteButton = new()
    {
        Icon = SymbolRegular.Dismiss24,
        ToolTip = Resource.Delete,
        MinWidth = 34,
        Height = 34,
        Margin = new(8, 0, 0, 0),
    };

    public event EventHandler? MoveUp;
    public event EventHandler? MoveDown;
    public event EventHandler? Delete;

    public EditDashboardItemControl(DashboardItem dashboardItem)
    {
        DashboardItem = dashboardItem;

        _moveUpButton.Click += (_, _) => MoveUp?.Invoke(this, EventArgs.Empty);
        _moveDownButton.Click += (_, _) => MoveDown?.Invoke(this, EventArgs.Empty);
        _deleteButton.Click += (_, _) => Delete?.Invoke(this, EventArgs.Empty);

        _stackPanel.Children.Add(_moveUpButton);
        _stackPanel.Children.Add(_moveDownButton);
        _stackPanel.Children.Add(_deleteButton);

        _cardHeaderControl.Title = DashboardItem.GetTitle();
        _cardHeaderControl.Accessory = _stackPanel;

        var dragHandle = new SymbolIcon
        {
            Name = "DragHandle",
            Symbol = SymbolRegular.ReOrderDotsVertical24,
            Cursor = Cursors.SizeAll,
            Margin = new Thickness(-8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.55
        };

        var featureIcon = new SymbolIcon
        {
            Symbol = DashboardItem.GetIcon(),
            FontSize = 24,
            Margin = new Thickness(4, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(dragHandle, 0);
        Grid.SetColumn(featureIcon, 1);
        Grid.SetColumn(_cardHeaderControl, 2);

        headerGrid.Children.Add(dragHandle);
        headerGrid.Children.Add(featureIcon);
        headerGrid.Children.Add(_cardHeaderControl);

        _cardControl.Header = headerGrid;

        AutomationProperties.SetName(_moveUpButton, _cardHeaderControl.Title);
        AutomationProperties.SetName(_moveDownButton, _cardHeaderControl.Title);
        AutomationProperties.SetName(_deleteButton, _cardHeaderControl.Title);

        Content = _cardControl;
    }
}
