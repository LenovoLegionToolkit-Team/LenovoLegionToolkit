using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class DialogWindow
{
    public static new readonly DependencyProperty TitleProperty =
    DependencyProperty.Register("Title", typeof(string), typeof(DialogWindow));

    public static new readonly DependencyProperty ContentProperty =
        DependencyProperty.Register("Content", typeof(string), typeof(DialogWindow));

    public new string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public new string Content
    {
        get => (string)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public (bool, bool) Result { get; private set; }

    public DialogWindow()
    {
        InitializeComponent();
        DataContext = this;

        MessagingCenter.Subscribe<PawnIOStateMessage>(this, (message) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (message.State != PawnIOState.NotInstalled)
                {
                    return;
                }

                var dialog = new DialogWindow
                {
                    Title = Resource.MainWindow_PawnIO_Warning_Title,
                    Content = Resource.MainWindow_PawnIO_Warning_Message,
                    Owner = Application.Current.MainWindow
                };

                if (dialog.ShowDialog() == true && dialog.Result.Item1)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://pawnio.eu/",
                        UseShellExecute = true
                    });
                }
            });
        });
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        Result = (true, DontShowAgainCheckBox.IsChecked!.Value);
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        Result = (false, DontShowAgainCheckBox.IsChecked!.Value);
        Close();
    }

    private void DialogWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        Hide();
    }
}
