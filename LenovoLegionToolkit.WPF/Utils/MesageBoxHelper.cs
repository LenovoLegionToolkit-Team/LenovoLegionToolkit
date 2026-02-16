using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace LenovoLegionToolkit.WPF.Utils;

public static class MessageBoxHelper
{
    public static Task<bool> ShowAsync(DependencyObject dependencyObject,
        string title,
        string message,
        string? leftButton = null,
        string? rightButton = null
    )
    {
        return ShowAsync(title, message, leftButton, rightButton);
    }

    public static async Task<bool> ShowAsync(
        string title,
        string message,
        string? primaryButton = null,
        string? secondaryButton = null)
    {
        var (result, _) = await ShowInternalAsync(title, message, false, primaryButton, secondaryButton);
        return result;
    }

    public static Task<(bool Result, bool DontShowAgain)> ShowAsync(
        DependencyObject dependencyObject,
        string title,
        string message,
        bool showDontShowAgain,
        string? leftButton = null,
        string? rightButton = null
    )
    {
        return ShowInternalAsync(title, message, showDontShowAgain, leftButton, rightButton);
    }

    public static Task<(bool Result, bool DontShowAgain)> ShowAsync(
        string title,
        string message,
        bool showDontShowAgain,
        string? primaryButton = null,
        string? secondaryButton = null)
    {
        return ShowInternalAsync(title, message, showDontShowAgain, primaryButton, secondaryButton);
    }

    private static Task<(bool Result, bool DontShowAgain)> ShowInternalAsync(
        string title,
        string message,
        bool showDontShowAgain,
        string? primaryButton,
        string? secondaryButton)
    {
        var tcs = new TaskCompletionSource<(bool Result, bool DontShowAgain)>();

        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center
        };

        stackPanel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var checkBox = new CheckBox
        {
            Content = Resource.MessageBoxHelper_DontShowAgain,
            IsChecked = false,
            Visibility = showDontShowAgain ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 0)
        };

        if (showDontShowAgain) stackPanel.Children.Add(checkBox);

        var messageBox = new MessageBox
        {
            Title = title,
            Content = stackPanel,
            ButtonLeftName = primaryButton ?? Resource.Yes,
            ButtonRightName = secondaryButton ?? Resource.No,
            ShowInTaskbar = false,
            Topmost = false,
            SizeToContent = SizeToContent.Height,
            MinHeight = 160,
            MaxHeight = double.PositiveInfinity,
            ResizeMode = ResizeMode.NoResize
        };

        messageBox.ButtonLeftClick += (_, _) =>
        {
            tcs.SetResult((true, checkBox.IsChecked ?? false));
            messageBox.Close();
        };
        messageBox.ButtonRightClick += (_, _) =>
        {
            tcs.SetResult((false, checkBox.IsChecked ?? false));
            messageBox.Close();
        };
        messageBox.Closing += (_, _) => { tcs.TrySetResult((false, false)); };
        messageBox.Show();

        return tcs.Task;
    }

    public static Task<string?> ShowInputAsync(
        DependencyObject dependencyObject,
        string title,
        string? placeholder = null,
        string? text = null,
        string? primaryButton = null,
        string? secondaryButton = null,
        bool allowEmpty = false
    )
    {
        var window = GetWindow(dependencyObject);
        return ShowInputAsync(window, title, placeholder, text, primaryButton, secondaryButton, allowEmpty);
    }

    public static Task<string?> ShowInputAsync(
        Window window,
        string title,
        string? placeholder = null,
        string? text = null,
        string? primaryButton = null,
        string? secondaryButton = null,
        bool allowEmpty = false
    )
    {
        var tcs = new TaskCompletionSource<string?>();

        var textBox = new TextBox
        {
            MaxLines = 1,
            MaxLength = 50,
            PlaceholderText = placeholder,
            TextWrapping = TextWrapping.Wrap
        };
        var messageBox = new MessageBox
        {
            Owner = window,
            Title = title,
            Content = textBox,
            ButtonLeftAppearance = ControlAppearance.Transparent,
            ButtonLeftName = primaryButton ?? Resource.OK,
            ButtonRightName = secondaryButton ?? Resource.Cancel,
            ShowInTaskbar = false,
            Topmost = false,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize
        };

        textBox.TextChanged += (_, _) =>
        {
            var isEmpty = !allowEmpty && string.IsNullOrWhiteSpace(textBox.Text);
            messageBox.ButtonLeftAppearance = isEmpty ? ControlAppearance.Transparent : ControlAppearance.Primary;
        };
        messageBox.ButtonLeftClick += (_, _) =>
        {
            var content = textBox.Text?.Trim();
            var newText = string.IsNullOrWhiteSpace(content) ? null : content;
            if (!allowEmpty && newText is null)
                return;
            tcs.SetResult(newText);
            messageBox.Close();
        };
        messageBox.ButtonRightClick += (_, _) =>
        {
            tcs.SetResult(null);
            messageBox.Close();
        };
        messageBox.Closing += (_, _) => { tcs.TrySetResult(null); };
        messageBox.Show();

        textBox.Text = text ?? string.Empty;
        textBox.SelectionStart = text?.Length ?? 0;
        textBox.SelectionLength = 0;

        FocusManager.SetFocusedElement(window, textBox);

        return tcs.Task;
    }

    private static Window GetWindow(DependencyObject dependencyObject)
    {
        return Window.GetWindow(dependencyObject)
               ?? Application.Current.MainWindow
               ?? throw new InvalidOperationException("Cannot show message without window");
    }
}
