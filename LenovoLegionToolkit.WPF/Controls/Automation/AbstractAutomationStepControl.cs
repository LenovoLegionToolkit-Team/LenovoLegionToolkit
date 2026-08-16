using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib.Automation.Steps;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using CardControl = LenovoLegionToolkit.WPF.Controls.Custom.CardControl;

namespace LenovoLegionToolkit.WPF.Controls.Automation;

public abstract class AbstractAutomationStepControl<T>(T automationStep) : AbstractAutomationStepControl(automationStep)
    where T : IAutomationStep
{
    protected new T AutomationStep => (T)base.AutomationStep;
}

public abstract class AbstractAutomationStepControl : UserControl
{
    protected IAutomationStep AutomationStep { get; }

    private readonly CardControl _cardControl = new()
    {
        Margin = new(0, 0, 0, 8),
    };

    private readonly CardHeaderControl _cardHeaderControl = new();

    private readonly StackPanel _stackPanel = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly SymbolIcon _dragHandle = new()
    {
        Symbol = SymbolRegular.ReOrderDotsVertical24,
        Margin = new(-8, 0, 0, 0),
        Cursor = Cursors.SizeAll,
        Opacity = 0.5,
    };

    private readonly SymbolIcon _iconControl = new()
    {
        FontSize = 24,
        Margin = new(4, 0, 12, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Button _deleteButton = new()
    {
        Icon = SymbolRegular.Dismiss24,
        ToolTip = Resource.AbstractAutomationStepControl_Delete,
        MinWidth = 34,
        Height = 34,
        Margin = new(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    public SymbolRegular Icon
    {
        get => _iconControl.Symbol;
        set => _iconControl.Symbol = value;
    }

    public string Title
    {
        get => _cardHeaderControl.Title;
        set => _cardHeaderControl.Title = value;
    }

    public string Subtitle
    {
        get => _cardHeaderControl.Subtitle;
        set => _cardHeaderControl.Subtitle = value;
    }

    public VerticalAlignment TitleVerticalAlignment
    {
        get => _cardHeaderControl.TitleVerticalAlignment;
        set => _cardHeaderControl.TitleVerticalAlignment = value;
    }

    public VerticalAlignment SubtitleVerticalAlignment
    {
        get => _cardHeaderControl.SubtitleVerticalAlignment;
        set => _cardHeaderControl.SubtitleVerticalAlignment = value;
    }

    public event EventHandler? Changed;
    public event EventHandler? Delete;
    public event EventHandler? DragEnded;

    protected AbstractAutomationStepControl(IAutomationStep automationStep)
    {
        AutomationStep = automationStep;

        InitializeComponent();

        Loaded += RefreshingControl_Loaded;
    }

    private void InitializeComponent()
    {
        _dragHandle.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount > 1) return;
            try
            {
                DragDrop.DoDragDrop(this, new DataObject("AutomationStep", this), DragDropEffects.Move);
            }
            finally
            {
                DragEnded?.Invoke(this, EventArgs.Empty);
            }
        };

        _deleteButton.Click += (_, _) => Delete?.Invoke(this, EventArgs.Empty);

        var control = GetCustomControl();
        if (control is not null)
        {
            if (control is FrameworkElement fe)
                fe.VerticalAlignment = VerticalAlignment.Center;
            _stackPanel.Children.Add(control);
        }
        _stackPanel.Children.Add(_deleteButton);

        _cardHeaderControl.Accessory = _stackPanel;
        
        var headerPanel = new Grid();
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _dragHandle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_dragHandle, 0);
        headerPanel.Children.Add(_dragHandle);

        Grid.SetColumn(_iconControl, 1);
        headerPanel.Children.Add(_iconControl);

        _cardHeaderControl.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_cardHeaderControl, 2);
        headerPanel.Children.Add(_cardHeaderControl);

        _cardControl.Header = headerPanel;

        Content = _cardControl;
    }

    private async void RefreshingControl_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshAsync();
            OnFinishedLoading();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Error loading automation step control {GetType().Name}", ex);
        }
    }

    public abstract IAutomationStep CreateAutomationStep();

    protected abstract UIElement? GetCustomControl();

    protected abstract void OnFinishedLoading();

    protected abstract Task RefreshAsync();

    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
