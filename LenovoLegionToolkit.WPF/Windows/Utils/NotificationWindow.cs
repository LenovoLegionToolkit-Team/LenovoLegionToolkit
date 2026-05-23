using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Utils;
using Wpf.Ui.Appearance;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public class NotificationWindow : UiWindow, INotificationWindow
{
    private const double MeasureHeight = 80;
    private const double DefaultMinWidth = 300;
    private const int PositionMargin = 16;

    private readonly ScreenInfo _screenInfo;

    private readonly Grid _mainGrid = new()
    {
        ColumnDefinitions =
        {
            new() { Width = GridLength.Auto, },
            new() { Width = new(1, GridUnitType.Star) },
        },
        Margin = new(16, 16, 32, 16),
    };

    private readonly SymbolIcon _symbolIcon = new()
    {
        FontSize = 32,
        Margin = new(0, 0, 16, 0),
    };

    private readonly SymbolIcon _overlaySymbolIcon = new()
    {
        FontSize = 32,
        Margin = new(0, 0, 16, 0),
    };

    private readonly Label _textBlock = new()
    {
        FontSize = 16,
        FontWeight = FontWeights.Medium,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    public NotificationWindow(SymbolRegular symbol, SymbolRegular? overlaySymbol, Action<SymbolIcon>? symbolTransform, string text, Brush? textColor, Action? clickAction, ScreenInfo screenInfo, NotificationPosition position)
    {
        InitializeStyle();
        InitializeContent(symbol, overlaySymbol, symbolTransform, text, textColor);

        ShowInTaskbar = false;
        SourceInitialized += OnSourceInitialized;


        _screenInfo = screenInfo;

        SourceInitialized += (_, _) => InitializePosition(screenInfo.WorkArea, screenInfo.DpiX, screenInfo.DpiY, position);
        MouseDown += (_, _) =>
        {
            Close();
            clickAction?.Invoke();
        };
    }
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        var hwnd = (HWND)source.Handle;
        var extendedStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)(extendedStyle | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE));
    }

    public void Show(int closeAfter)
    {
        Show();
        Task.Delay(closeAfter).ContinueWith(_ =>
        {
            Close();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }


    public void Close(bool immediate)
    {
        WindowStyle = WindowStyle.None;
        Close();
    }

    private void InitializeStyle()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Topmost = true;

        Focusable = false;
        ExtendsContentIntoTitleBar = false;
        ShowInTaskbar = false;
        ShowActivated = false;

        _mainGrid.FlowDirection = LocalizationHelper.Direction;
        _textBlock.Foreground = (SolidColorBrush)FindResource("TextFillColorPrimaryBrush");
    }

    private void InitializePosition(Rect workArea, uint dpiX, uint dpiY, NotificationPosition position)
    {
        _mainGrid.Measure(new Size(double.PositiveInfinity, MeasureHeight));

        var multiplierX = dpiX / 96d;
        var multiplierY = dpiY / 96d;
        Rect nativeWorkArea = new(workArea.Left, workArea.Top, workArea.Width * multiplierX, workArea.Height * multiplierY);

        Width = MaxWidth = MinWidth = Math.Max(_mainGrid.DesiredSize.Width, DefaultMinWidth);
        Height = MaxHeight = MinHeight = _mainGrid.DesiredSize.Height;

        var nativeWidth = Width * multiplierX;
        var nativeHeight = Height * multiplierY;

        var nativeMarginX = PositionMargin * multiplierX;
        var nativeMarginY = PositionMargin * multiplierY;

        double nativeLeft;
        double nativeTop;

        switch (position)
        {
            case NotificationPosition.BottomRight:
                nativeLeft = nativeWorkArea.Right - nativeWidth - nativeMarginX;
                nativeTop = nativeWorkArea.Bottom - nativeHeight - nativeMarginY;
                break;
            case NotificationPosition.BottomCenter:
                nativeLeft = nativeWorkArea.Left + (nativeWorkArea.Width - nativeWidth) / 2;
                nativeTop = nativeWorkArea.Bottom - nativeHeight - nativeMarginY;
                break;
            case NotificationPosition.BottomLeft:
                nativeLeft = nativeWorkArea.Left + nativeMarginX;
                nativeTop = nativeWorkArea.Bottom - nativeHeight - nativeMarginY;
                break;
            case NotificationPosition.CenterLeft:
                nativeLeft = nativeWorkArea.Left + nativeMarginX;
                nativeTop = nativeWorkArea.Top + (nativeWorkArea.Height - nativeHeight) / 2;
                break;
            case NotificationPosition.TopLeft:
                nativeLeft = nativeWorkArea.Left + nativeMarginX;
                nativeTop = nativeWorkArea.Top + nativeMarginY;
                break;
            case NotificationPosition.TopCenter:
                nativeLeft = nativeWorkArea.Left + (nativeWorkArea.Width - nativeWidth) / 2;
                nativeTop = nativeWorkArea.Top + nativeMarginY;
                break;
            case NotificationPosition.TopRight:
                nativeLeft = nativeWorkArea.Right - nativeWidth - nativeMarginX;
                nativeTop = nativeWorkArea.Top + nativeMarginY;
                break;
            case NotificationPosition.CenterRight:
                nativeLeft = nativeWorkArea.Right - nativeWidth - nativeMarginX;
                nativeTop = nativeWorkArea.Top + (nativeWorkArea.Height - nativeHeight) / 2;
                break;
            case NotificationPosition.Center:
                nativeLeft = nativeWorkArea.Left + (nativeWorkArea.Width - nativeWidth) / 2;
                nativeTop = nativeWorkArea.Top + (nativeWorkArea.Height - nativeHeight) / 2;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(position), position, "Unexpected notification position.");
        }

        var windowInteropHandler = new WindowInteropHelper(this);

        PInvoke.SetWindowPos((HWND)windowInteropHandler.Handle, HWND.Null, (int)nativeLeft, (int)nativeTop, 0, 0, SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
    }

    private void InitializeContent(SymbolRegular symbol, SymbolRegular? overlaySymbol, Action<SymbolIcon>? symbolTransform, string text, Brush? textColor)
    {
        _symbolIcon.Symbol = symbol;
        _textBlock.Content = text;

        if (textColor is not null)
            _textBlock.Foreground = textColor;

        Grid.SetColumn(_symbolIcon, 0);
        Grid.SetColumn(_textBlock, 1);

        _mainGrid.Children.Add(_symbolIcon);
        _mainGrid.Children.Add(_textBlock);

        if (overlaySymbol.HasValue)
        {
            _overlaySymbolIcon.Symbol = overlaySymbol.Value;
            Grid.SetColumn(_overlaySymbolIcon, 0);
            _mainGrid.Children.Add(_overlaySymbolIcon);
        }

        symbolTransform?.Invoke(_symbolIcon);

        Content = _mainGrid;
    }
}
