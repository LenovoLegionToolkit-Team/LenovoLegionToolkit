using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using BlackSharp.Core.Extensions;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Markdig;
using Markdig.Wpf;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class UpdateWindow : IProgress<float>
{
    private readonly UpdateChecker _updateChecker = IoCContainer.Resolve<UpdateChecker>();

    private CancellationTokenSource? _downloadCancellationTokenSource;

    public UpdateWindow()
    {
        InitializeComponent();
        _markdownViewer.Pipeline = new MarkdownPipelineBuilder().UseSupportedExtensions().Build();
    }

    private void Hyperlink_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            uri.Open();
            e.Handled = true;
        }
    }

    private async void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var updates = await _updateChecker.GetUpdatesAsync();

        if (updates.Length > 0)
        {
            var stringBuilder = new StringBuilder();
            foreach (var update in updates)
            {
                stringBuilder.AppendLine("**" + update.Title + "**   _(" + update.Date.ToString("D") + ")_")
                    .AppendLine()
                    .AppendLine(update.Description)
                    .AppendLine();
            }

            _markdownViewer.Markdown = stringBuilder.ToString();
        }
        else
        {
            if (_updateChecker.UpdateFromServer != null)
            {
                if (_updateChecker.UpdateFromServer?.Description.Length > 0)
                {
                    _markdownViewer.Markdown = _updateChecker.UpdateFromServer?.Description;
                }
            }
        }

        _downloadButton.IsEnabled = true;
    }

    private void UpdateWindow_Closing(object? sender, CancelEventArgs e) => _downloadCancellationTokenSource?.Cancel();

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_downloadCancellationTokenSource is not null)
                await _downloadCancellationTokenSource.CancelAsync();

            _downloadCancellationTokenSource = new();

            SetDownloading(true);

            var path = await _updateChecker.DownloadLatestUpdateAsync(this, _downloadCancellationTokenSource.Token);

            _downloadCancellationTokenSource = null;

            Process.Start(path, $"/SILENT /RESTARTAPPLICATIONS /LANG={Resource.Culture.Name.Replace("-", string.Empty)}");
            await App.Current.ShutdownAsync();
        }
        catch (OperationCanceledException)
        {
            SetDownloading(false);
        }
        catch (SecurityException ex)
        {
            SetDownloading(false);
            var innerMsg = ex.InnerException is not null ? "\n" + ex.InnerException.Message : string.Empty;
            await SnackbarHelper.ShowAsync(Resource.UpdateWindow_SecurityError_Title, ex.Message + innerMsg, SnackbarType.Error);
        }
        catch (Exception ex)
        {
            SetDownloading(false);

            if (_updateChecker.UpdateFromServer != null)
            {
                await SnackbarHelper.ShowAsync(Resource.UpdateWindow_DownloadError_Title, ex.Message, SnackbarType.Error);
            }
            else
            {
                Constants.LatestReleaseUri.Open();
                Close();
            }
        }
    }

    private void CancelDownloadButton_Click(object sender, RoutedEventArgs e) => _downloadCancellationTokenSource?.Cancel();

    private void SetDownloading(bool isDownloading)
    {
        if (isDownloading)
        {
            _downloadProgressBar.Visibility = Visibility.Visible;

            _downloadButton.Visibility = Visibility.Collapsed;
            _downloadButton.IsEnabled = false;

            _cancelDownloadButton.Visibility = Visibility.Visible;
            _cancelDownloadButton.IsEnabled = true;
        }
        else
        {
            _downloadProgressBar.Value = 0;
            _downloadProgressBar.IsIndeterminate = true;
            _downloadProgressBar.Visibility = Visibility.Hidden;

            _downloadButton.Visibility = Visibility.Visible;
            _downloadButton.IsEnabled = true;

            _cancelDownloadButton.Visibility = Visibility.Collapsed;
            _cancelDownloadButton.IsEnabled = false;
        }
    }

    public void Report(float value) => Dispatcher.Invoke(() =>
    {
        _downloadProgressBar.IsIndeterminate = !(value > 0);
        _downloadProgressBar.Value = value;
    });
}
