using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using Microsoft.Win32;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using XamlAnimatedGif;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class BootLogoWindow
{
    private static BitmapImage? _defaultLogo;
    private MemoryStream? _activeCustomLogoStream;

    public BootLogoWindow()
    {
        InitializeComponent();

        Loaded += BootLogoWindow_Loaded;
        Closed += BootLogoWindow_Closed;
    }

    private void BootLogoWindow_Closed(object? sender, EventArgs e)
    {
        ClearActivePreview();
    }

    private async void BootLogoWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private static BitmapImage GetDefaultLogo()
    {
        if (_defaultLogo == null)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/Assets/Lenovo_logo.png", UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            _defaultLogo = image;
        }

        return _defaultLogo;
    }

    private void ClearActivePreview()
    {
        AnimationBehavior.SetSourceStream(_previewImage, null);
        _previewImage.Source = null;
        _activeCustomLogoStream?.Dispose();
        _activeCustomLogoStream = null;
    }

    private async Task RefreshAsync()
    {
        var (enabled, resolution, formats, _) = BootLogo.GetStatus();

        var ratio = resolution.Height > 0 ? (double)resolution.Width / resolution.Height : 16.0 / 10.0;
        var innerWidth = 448.0;
        var innerHeight = innerWidth / ratio;
        _previewScreenBorder.Height = Math.Round(innerHeight + 22.0);

        _statusBadge.Content = enabled ? Resource.BootLogoWindow_CustomLogoSet : Resource.BootLogoWindow_DefaultLogoSet;
        _statusBadge.Appearance = enabled ? ControlAppearance.Primary : ControlAppearance.Secondary;

        _resolutionBadge.Content = string.Format(Resource.BootLogoWindow_Requirements_MaxResolution, resolution.DisplayName);
        _formatsBadge.Content = string.Format(Resource.BootLogoWindow_Requirements_Formats, string.Join(", ", formats.Select(f => f.ToString().ToUpper())));

        _revertToDefaultButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _customizeButton.Visibility = Visibility.Visible;

        var isAnimationDisabled = await BootLogo.IsWindowsBootAnimationDisabledAsync();
        _animationToggle.IsChecked = !isAnimationDisabled;
        _previewSpinner.Visibility = _animationToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        await UpdatePreviewAsync(enabled);
    }

    private async Task UpdatePreviewAsync(bool enabled)
    {
        ClearActivePreview();

        if (enabled)
        {
            try
            {
                var bytes = await BootLogo.GetActiveCustomLogoBytesAsync();
                if (bytes is { Length: > 0 })
                {
                    _activeCustomLogoStream = new MemoryStream(bytes);
                    AnimationBehavior.SetSourceStream(_previewImage, _activeCustomLogoStream);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to load custom boot logo for preview.", ex);
            }
        }

        _previewImage.Source = GetDefaultLogo();
    }

    private async void AnimationToggle_Click(object sender, RoutedEventArgs e)
    {
        _previewSpinner.Visibility = _animationToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        await HandleAnimationAsync();
    }

    private async void RevertToDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _revertToDefaultButton.IsEnabled = false;

            await BootLogo.DisableAsync();
            await HandleAnimationAsync();

            _resultInfoBar.Title = Resource.Success;
            _resultInfoBar.Message = Resource.BootLogoWindow_SetDefaultSuccess;
            _resultInfoBar.Severity = InfoBarSeverity.Success;
            _resultInfoBar.IsOpen = true;

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Default logo could not be set.", ex);
            _resultInfoBar.Title = Resource.Error;
            _resultInfoBar.Message = string.Format(Resource.BootLogoWindow_SetDefaultFailed, GetDescription(ex));
            _resultInfoBar.Severity = InfoBarSeverity.Error;
            _resultInfoBar.IsOpen = true;
        }
        finally
        {
            _revertToDefaultButton.IsEnabled = true;
        }
    }

    private async void CustomizeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _customizeButton.IsEnabled = false;

            var (_, _, _, filters) = BootLogo.GetStatus();

            var ofd = new OpenFileDialog
            {
                Title = "Open",
                Filter = $"Images|{string.Join(";", filters)}",
                CheckFileExists = true,
            };

            var result = ofd.ShowDialog() ?? false;
            if (!result)
                return;

            var file = ofd.FileName;

            await BootLogo.EnableAsync(file);
            await HandleAnimationAsync();

            _resultInfoBar.Title = Resource.Success;
            _resultInfoBar.Message = Resource.BootLogoWindow_SetCustomSuccess;
            _resultInfoBar.Severity = InfoBarSeverity.Success;
            _resultInfoBar.IsOpen = true;

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Custom logo could not be set.", ex);

            _resultInfoBar.Title = Resource.Error;
            _resultInfoBar.Message = string.Format(Resource.BootLogoWindow_SetCustomFailed, GetDescription(ex));
            _resultInfoBar.Severity = InfoBarSeverity.Error;
            _resultInfoBar.IsOpen = true;
        }
        finally
        {
            _customizeButton.IsEnabled = true;
        }
    }

    private async Task HandleAnimationAsync()
    {
        try
        {
            var isAnimationEnabled = _animationToggle.IsChecked == true;
            var disable = !isAnimationEnabled;
            await BootLogo.SetWindowsBootAnimationAsync(disable).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to set boot animation. [ex={ex.Message}]");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static string GetDescription(Exception exception) => exception switch
    {
        CantSetUEFIPrivilegeException => Resource.BootLogoWindow_SetError_Cannot_Set_UEFI_Privilege,
        CantMountUEFIPartitionException => Resource.BootLogoWindow_SetError_Cannot_Mount_EFI_Partition,
        NotEnoughSpaceOnUEFIPartitionException => Resource.BootLogoWindow_SetError_Not_Enough_Free_Space_On_EFI_Partition,
        InvalidBootLogoImageSizeException => Resource.BootLogoWindow_SetError_Invalid_Image_Size,
        InvalidBootLogoImageFormatException => Resource.BootLogoWindow_SetError_Invalid_Image_Format,
        _ => exception.Message
    };
}
