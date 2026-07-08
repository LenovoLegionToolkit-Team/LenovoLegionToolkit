using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Extensions;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Controls.Dashboard;

public partial class ITSModeLoopControl
{
    private readonly ITSModeFeature _feature = IoCContainer.Resolve<ITSModeFeature>();
    private readonly ITSModeSettings _settings = IoCContainer.Resolve<ITSModeSettings>();
    private readonly Dictionary<ITSMode, CardControl> _modeCards = [];
    private readonly Dictionary<ITSMode, Wpf.Ui.Controls.ToggleSwitch> _toggles = [];

    private Point _dragStartPoint;
    private CardControl? _draggedCard;

    public ITSModeLoopControl()
    {
        InitializeComponent();
        Loaded += ITSModeLoopControl_Loaded;
        _modeCardsPanel.Drop += ModeCardsPanel_Drop;
    }

    private async void ITSModeLoopControl_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        try
        {
            var supportedStates = await _feature.GetAllStatesAsync().ConfigureAwait(true);

            var userOrder = _settings.Store.FnQModeOrder;
            if (userOrder == null || userOrder.Count == 0)
                userOrder = [ITSMode.MmcCool, ITSMode.ItsAuto, ITSMode.MmcPerformance, ITSMode.MmcGeek];

            var disabledSet = new HashSet<ITSMode>(_settings.Store.DisabledModes ?? []);

            var orderedModes = userOrder
                .Where(supportedStates.Contains)
                .Concat(supportedStates.Where(s => !userOrder.Contains(s)))
                .ToList();

            Dispatcher.Invoke(() => BuildModeCards(orderedModes, disabledSet));
        }
        catch (Exception ex)
        {
            Lib.Utils.Log.Instance.Trace($"Failed to load ITS mode config: {ex}");
        }
    }

    public void Save()
    {
        var order = GetCurrentOrder();
        var disabled = _toggles
            .Where(kv => kv.Value.IsChecked != true)
            .Select(kv => kv.Key)
            .ToList();

        _settings.Store.FnQModeOrder = order;
        _settings.Store.DisabledModes = disabled;
        _settings.SynchronizeStore();
    }

    private void BuildModeCards(List<ITSMode> orderedModes, HashSet<ITSMode> disabledSet)
    {
        _modeCardsPanel.Children.Clear();
        _modeCards.Clear();
        _toggles.Clear();

        foreach (var mode in orderedModes)
        {
            var card = CreateModeCard(mode, !disabledSet.Contains(mode));
            _modeCards[mode] = card;
            _modeCardsPanel.Children.Add(card);
        }
    }

    private CardControl CreateModeCard(ITSMode mode, bool enabled)
    {
        var colorBrush = mode.GetSolidColorBrush();
        var displayName = _feature.GetITSModeDisplayName(mode);

        var colorDot = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = colorBrush,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var dragHandle = new Border
        {
            Width = 20,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Child = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = SymbolRegular.LineHorizontal320,
                FontSize = 16,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
            },
        };

        var toggle = new Wpf.Ui.Controls.ToggleSwitch
        {
            IsChecked = enabled,
        };
        _toggles[mode] = toggle;

        var cardHeader = new CardHeaderControl
        {
            Title = displayName,
            Accessory = toggle,
        };

        var outerGrid = new Grid();
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(dragHandle, 0);
        Grid.SetColumn(colorDot, 1);
        Grid.SetColumn(cardHeader, 2);

        outerGrid.Children.Add(dragHandle);
        outerGrid.Children.Add(colorDot);
        outerGrid.Children.Add(cardHeader);

        var card = new CardControl
        {
            Margin = new Thickness(0, 0, 0, 8),
            Header = outerGrid,
        };

        if (!enabled)
            card.Opacity = 0.45;

        toggle.Click += (_, _) =>
        {
            card.Opacity = toggle.IsChecked == true ? 1.0 : 0.45;
        };

        dragHandle.MouseLeftButtonDown += (_, e) =>
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedCard = card;
            dragHandle.CaptureMouse();
            e.Handled = true;
        };

        dragHandle.MouseMove += (_, e) =>
        {
            if (_draggedCard == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var currentPoint = e.GetPosition(null);
            if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var dragged = _draggedCard;
            _draggedCard = null;
            dragHandle.ReleaseMouseCapture();
            DragDrop.DoDragDrop(dragged, dragged, DragDropEffects.Move);
        };

        dragHandle.MouseLeftButtonUp += (_, _) =>
        {
            _draggedCard = null;
            dragHandle.ReleaseMouseCapture();
        };

        return card;
    }

    private void ModeCardsPanel_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(CardControl)) is not CardControl droppedCard)
            return;
        if (!_modeCardsPanel.Children.Contains(droppedCard))
            return;

        var dropPosition = e.GetPosition(_modeCardsPanel);
        int newIndex = -1;

        for (var i = 0; i < _modeCardsPanel.Children.Count; i++)
        {
            if (_modeCardsPanel.Children[i] is not FrameworkElement child || child == droppedCard)
                continue;

            var childCenter = child.TransformToAncestor(_modeCardsPanel)
                .Transform(new Point(child.ActualWidth / 2, child.ActualHeight / 2));

            if (dropPosition.Y < childCenter.Y)
            {
                newIndex = i;
                break;
            }
        }

        var currentIndex = _modeCardsPanel.Children.IndexOf(droppedCard);
        if (newIndex < 0 || currentIndex < 0 || newIndex == currentIndex)
            return;

        _modeCardsPanel.Children.RemoveAt(currentIndex);
        if (newIndex > currentIndex)
            newIndex--;
        _modeCardsPanel.Children.Insert(newIndex, droppedCard);
    }

    private List<ITSMode> GetCurrentOrder()
    {
        var order = new List<ITSMode>();
        foreach (var child in _modeCardsPanel.Children)
        {
            var mode = _modeCards.FirstOrDefault(kv => kv.Value == child).Key;
            if (!Equals(mode, default(ITSMode)))
                order.Add(mode);
        }
        return order;
    }

    private void DefaultButton_Click(object sender, RoutedEventArgs e)
    {
        var defaultOrder = new List<ITSMode>
        {
            ITSMode.MmcCool,
            ITSMode.ItsAuto,
            ITSMode.MmcPerformance,
            ITSMode.MmcGeek,
        };

        _modeCardsPanel.Children.Clear();
        _modeCards.Clear();
        _toggles.Clear();

        foreach (var mode in defaultOrder)
        {
            var card = CreateModeCard(mode, true);
            _modeCards[mode] = card;
            _modeCardsPanel.Children.Add(card);
        }
    }
}
