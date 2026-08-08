using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using CustomControls = LenovoLegionToolkit.WPF.Controls.Custom;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Controls;

public class CardHeaderControl : UserControl
{
    private readonly TextBlock _titleTextBlock = new()
    {
        FontSize = 14,
        FontWeight = FontWeights.Medium,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly TextBlock _subtitleTextBlock = new()
    {
        FontSize = 12,
        Margin = new(0, 4, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private readonly SymbolIcon _infoIcon = new() { FontSize = 12, Margin = new(0, 2, 4, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _infoTextBlock = new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Grid _infoGrid = new() { Margin = new(0, 4, 0, 0) };

    private readonly SymbolIcon _warningIcon = new() { FontSize = 12, Margin = new(0, 2, 4, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _warningTextBlock = new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Grid _warningGrid = new() { Margin = new(0, 4, 0, 0) };

    private readonly SymbolIcon _errorIcon = new() { FontSize = 12, Margin = new(0, 2, 4, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _errorTextBlock = new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Grid _errorGrid = new() { Margin = new(0, 4, 0, 0) };

    private readonly SymbolIcon _successIcon = new() { FontSize = 12, Margin = new(0, 2, 4, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _successTextBlock = new()
    {
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Grid _successGrid = new() { Margin = new(0, 4, 0, 0) };

    private readonly StackPanel _stackPanel = new();

    private readonly Grid _grid = new()
    {
        ColumnDefinitions =
        {
            new ColumnDefinition { Width = new(1, GridUnitType.Star) },
            new ColumnDefinition { Width = GridLength.Auto },
        },
        RowDefinitions =
        {
            new RowDefinition { Height = GridLength.Auto },
            new RowDefinition { Height = GridLength.Auto },
        },
        VerticalAlignment = VerticalAlignment.Center,
    };

    private UIElement? _accessory;

    public string Title
    {
        get => _titleTextBlock.Text;
        set
        {
            _titleTextBlock.Text = value;
            RefreshLayout();
        }
    }

    public string Subtitle
    {
        get => _subtitleTextBlock.Text;
        set
        {
            _subtitleTextBlock.Text = value;
            RefreshLayout();
        }
    }

    public VerticalAlignment TitleVerticalAlignment
    {
        get => _titleTextBlock.VerticalAlignment;
        set => _titleTextBlock.VerticalAlignment = value;
    }

    public VerticalAlignment SubtitleVerticalAlignment
    {
        get => _subtitleTextBlock.VerticalAlignment;
        set => _subtitleTextBlock.VerticalAlignment = value;
    }

    public string Warning
    {
        get => _warningTextBlock.Text;
        set
        {
            _warningTextBlock.Text = value;
            RefreshLayout();
        }
    }

    public string Info
    {
        get => _infoTextBlock.Text;
        set
        {
            _infoTextBlock.Text = value;
            RefreshLayout();
        }
    }

    public string Error
    {
        get => _errorTextBlock.Text;
        set
        {
            _errorTextBlock.Text = value;
            RefreshLayout();
        }
    }

    public string Success
    {
        get => _successTextBlock.Text;
        set
        {
            _successTextBlock.Text = value;
            RefreshLayout();
        }
    }

    public string? SubtitleToolTip
    {
        get => _subtitleTextBlock.ToolTip as string;
        set
        {
            _subtitleTextBlock.ToolTip = value;
            ToolTipService.SetIsEnabled(_subtitleTextBlock, value is not null);
            RefreshLayout();
        }
    }

    public UIElement? Accessory
    {
        get => _accessory;
        set
        {
            if (_accessory is not null)
                _grid.Children.Remove(_accessory);

            _accessory = value;

            if (_accessory is not null)
            {
                Grid.SetColumn(_accessory, 1);
                Grid.SetRow(_accessory, 0);
                Grid.SetRowSpan(_accessory, 2);

                _grid.Children.Add(_accessory);
            }

            RefreshLayout();
        }
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);

        if (CustomControls.CardControl.IsCompact)
        {
            _titleTextBlock.FontSize = 12;
            _subtitleTextBlock.FontSize = 11;
            _subtitleTextBlock.Margin = new(0, 2, 0, 0);
            _infoGrid.Margin = new(0, 2, 0, 0);
            _warningGrid.Margin = new(0, 2, 0, 0);
            _errorGrid.Margin = new(0, 2, 0, 0);
            _successGrid.Margin = new(0, 2, 0, 0);
        }

        Grid.SetColumn(_titleTextBlock, 0);
        Grid.SetColumn(_stackPanel, 0);

        Grid.SetRow(_titleTextBlock, 0);
        Grid.SetRow(_stackPanel, 1);

        ConfigureMessageGrid(_infoGrid, _infoIcon, _infoTextBlock);
        ConfigureMessageGrid(_warningGrid, _warningIcon, _warningTextBlock);
        ConfigureMessageGrid(_errorGrid, _errorIcon, _errorTextBlock);
        ConfigureMessageGrid(_successGrid, _successIcon, _successTextBlock);

        _stackPanel.Children.Add(_subtitleTextBlock);
        _stackPanel.Children.Add(_infoGrid);
        _stackPanel.Children.Add(_warningGrid);
        _stackPanel.Children.Add(_errorGrid);
        _stackPanel.Children.Add(_successGrid);

        _grid.Children.Add(_titleTextBlock);
        _grid.Children.Add(_stackPanel);

        Content = _grid;

        UpdateTextStyle();
        RefreshLayout();

        IsEnabledChanged += (_, _) => UpdateTextStyle();
    }

    private static void ConfigureMessageGrid(Grid grid, SymbolIcon icon, TextBlock text)
    {
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(text, 1);

        grid.Children.Add(icon);
        grid.Children.Add(text);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CardHeaderControlAutomationPeer(this);

    private void RefreshLayout()
    {
        var isCompact = CustomControls.CardControl.IsCompact;

        if (isCompact)
        {
            var tooltipLines = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(Subtitle)) tooltipLines.Add(Subtitle);
            if (!string.IsNullOrWhiteSpace(Info)) tooltipLines.Add(Info);
            if (!string.IsNullOrWhiteSpace(Warning)) tooltipLines.Add(Warning);
            if (!string.IsNullOrWhiteSpace(Error)) tooltipLines.Add(Error);
            if (!string.IsNullOrWhiteSpace(Success)) tooltipLines.Add(Success);

            ToolTip = tooltipLines.Count > 0 ? string.Join(Environment.NewLine, tooltipLines) : null;

            _subtitleTextBlock.Visibility = Visibility.Collapsed;
            _infoGrid.Visibility = Visibility.Collapsed;
            _warningGrid.Visibility = Visibility.Collapsed;
            _errorGrid.Visibility = Visibility.Collapsed;
            _successGrid.Visibility = Visibility.Collapsed;

            Grid.SetRowSpan(_titleTextBlock, 2);
        }
        else
        {
            ToolTip = null;

            _subtitleTextBlock.Visibility = string.IsNullOrWhiteSpace(Subtitle) ? Visibility.Collapsed : Visibility.Visible;
            _infoGrid.Visibility = string.IsNullOrWhiteSpace(Info) ? Visibility.Collapsed : Visibility.Visible;
            _warningGrid.Visibility = string.IsNullOrWhiteSpace(Warning) ? Visibility.Collapsed : Visibility.Visible;
            _errorGrid.Visibility = string.IsNullOrWhiteSpace(Error) ? Visibility.Collapsed : Visibility.Visible;
            _successGrid.Visibility = string.IsNullOrWhiteSpace(Success) ? Visibility.Collapsed : Visibility.Visible;

            var hasStatusContent = !string.IsNullOrWhiteSpace(Warning) || !string.IsNullOrWhiteSpace(Info) || !string.IsNullOrWhiteSpace(Error) || !string.IsNullOrWhiteSpace(Success);
            var hasVisibleRow2 = !string.IsNullOrWhiteSpace(Subtitle) || hasStatusContent;

            if (hasVisibleRow2)
                Grid.SetRowSpan(_titleTextBlock, 1);
            else
                Grid.SetRowSpan(_titleTextBlock, 2);
        }
    }

    private void UpdateTextStyle()
    {
        if (IsEnabled)
        {
            _titleTextBlock.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
            _subtitleTextBlock.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");

            _infoIcon.Symbol = SymbolRegular.Info24;
            _infoIcon.SetResourceReference(ForegroundProperty, "SystemAccentColorSecondaryBrush");
            _infoTextBlock.SetResourceReference(ForegroundProperty, "SystemAccentColorSecondaryBrush");

            _warningIcon.Symbol = SymbolRegular.ErrorCircle24;
            _warningIcon.SetResourceReference(ForegroundProperty, "SystemFillColorCautionBrush");
            _warningTextBlock.SetResourceReference(ForegroundProperty, "SystemFillColorCautionBrush");

            _errorIcon.Symbol = SymbolRegular.DismissCircle24;
            _errorIcon.SetResourceReference(ForegroundProperty, "SystemFillColorCriticalBrush");
            _errorTextBlock.SetResourceReference(ForegroundProperty, "SystemFillColorCriticalBrush");

            _successIcon.Symbol = SymbolRegular.CheckmarkCircle24;
            _successIcon.SetResourceReference(ForegroundProperty, "SystemFillColorSuccessBrush");
            _successTextBlock.SetResourceReference(ForegroundProperty, "SystemFillColorSuccessBrush");
        }
        else
        {
            _titleTextBlock.SetResourceReference(ForegroundProperty, "TextFillColorDisabledBrush");
            _subtitleTextBlock.SetResourceReference(ForegroundProperty, "TextFillColorDisabledBrush");
            _infoIcon.SetResourceReference(ForegroundProperty, "TextFillColorDisabledBrush");
            _infoTextBlock.SetResourceReference(ForegroundProperty, "TextFillColorDisabledBrush");
            _warningIcon.SetResourceReference(ForegroundProperty, "TextFillColorDisabledBrush");
            _warningTextBlock.SetResourceReference(ForegroundProperty, "TextFillColorDisabledBrush");
            _errorIcon.SetResourceReference(ForegroundProperty, "TextFillColorDisabledBrush");
            _errorTextBlock.SetResourceReference(ForegroundProperty, "TextFillColorDisabledBrush");
            _successIcon.SetResourceReference(ForegroundProperty, "TextFillColorDisabledBrush");
            _successTextBlock.SetResourceReference(ForegroundProperty, "TextFillColorDisabledBrush");
        }
    }

    private class CardHeaderControlAutomationPeer(CardHeaderControl owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(CardHeaderControl);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;

        public override object? GetPattern(PatternInterface patternInterface)
        {
            if (patternInterface == PatternInterface.ItemContainer)
                return this;

            return base.GetPattern(patternInterface);
        }

        protected override string GetNameCore()
        {
            var result = base.GetNameCore() ?? string.Empty;

            if (result == string.Empty)
                result = AutomationProperties.GetName(owner);

            if (result == string.Empty && !string.IsNullOrWhiteSpace(owner._titleTextBlock.Text))
            {
                result = owner._titleTextBlock.Text;

                if (!string.IsNullOrWhiteSpace(owner._subtitleTextBlock.Text))
                    result += $", {owner._subtitleTextBlock.Text}";
            }

            return result;
        }
    }
}
