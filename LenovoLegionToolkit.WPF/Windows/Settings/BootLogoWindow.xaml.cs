using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;
using Microsoft.Win32;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class BootLogoWindow
{
    public BootLogoWindow()
    {
        InitializeComponent();

        Loaded += BootLogoWindow_Loaded;
    }

    private void BootLogoWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        var (enabled, resolution, formats, _) = BootLogo.GetStatus();

        _statusBadge.Content = enabled ? Resource.BootLogoWindow_CustomLogoSet : Resource.BootLogoWindow_DefaultLogoSet;
        _statusBadge.Appearance = enabled ? ControlAppearance.Primary : ControlAppearance.Secondary;

        _resolutionBadge.Content = string.Format(Resource.BootLogoWindow_Requirements_MaxResolution, resolution.DisplayName);
        _formatsBadge.Content = string.Format(Resource.BootLogoWindow_Requirements_Formats, string.Join(", ", formats.Select(f => f.ToString().ToUpper())));

        _customizeButton.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        _revertToDefaultButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _disableAnimationToggle.IsEnabled = !enabled;
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

            Refresh();
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

            Refresh();
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
            var disable = _disableAnimationToggle.IsChecked == true;
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
