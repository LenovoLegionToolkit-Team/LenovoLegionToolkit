using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.Lib.Utils.Warranty;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Overclocking.Amd;
using Wpf.Ui.Controls;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class DeviceInformationWindow
{
    private readonly WarrantyChecker _warrantyChecker = IoCContainer.Resolve<WarrantyChecker>();

    private int _count = 0;
    private AmdOverclocking? _amdOverclockingWindow;

    public DeviceInformationWindow()
    {
        InitializeComponent();
    }

    private async void DeviceInformationWindow_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync(bool forceRefresh = false)
    {
        var mi = await Compatibility.GetMachineInformationAsync();

        var vendor = mi.Vendor;
        var model = mi.Model;
        var machineType = mi.MachineType;
        var serialNumber = mi.SerialNumber;
        var biosVersion = mi.BiosVersionRaw;

        if (Compatibility.FakeMachineInformationMode)
        {
            var fakeMi = await Compatibility.GetFakeMachineInformationAsync();
            if (fakeMi.HasValue)
            {
                var fake = fakeMi.Value;
                vendor = fake.Manufacturer ?? vendor;
                model = fake.Model ?? model;
                machineType = fake.MachineType ?? machineType;
                serialNumber = fake.SerialNumber ?? serialNumber;
                biosVersion = fake.BiosVersion ?? biosVersion;
            }
        }

        _manufacturerLabel.Text = vendor;
        _modelLabel.Text = model;
        _mtmLabel.Text = machineType;
        _serialNumberLabel.Text = serialNumber;
        _biosLabel.Text = biosVersion;

        try
        {
            _refreshWarrantyButton.IsEnabled = false;
            ResetWarrantyUi();

            var language = await LocalizationHelper.GetLanguageAsync();

            var warrantyInfo = await _warrantyChecker.GetWarrantyInfo(mi, language, forceRefresh);

            if (warrantyInfo.HasValue)
            {
                var info = warrantyInfo.Value;

                _warrantyStartLabel.Text = info.Start?.ToString(LocalizationHelper.ShortDateFormat) ?? "-";
                _warrantyEndLabel.Text = info.End?.ToString(LocalizationHelper.ShortDateFormat) ?? "-";

                _warrantyLinkCardAction.Tag = info.Link;
                _warrantyLinkCardAction.IsEnabled = true;
                _warrantyInfo.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't load warranty info.", ex);
        }
        finally
        {
            _refreshWarrantyButton.IsEnabled = true;
        }
    }

    private void ResetWarrantyUi()
    {
        _warrantyStartLabel.Text = "-";
        _warrantyEndLabel.Text = "-";
        _warrantyLinkCardAction.Tag = null;
        _warrantyLinkCardAction.IsEnabled = false;
    }

    private async void RefreshWarrantyButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync(true);

    private void DeviceCardControl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CardControl card || (card.Content as TextBlock)?.Text is not { } str)
        {
            return;
        }

        if (card.Name == "_biosCard")
        {
            _count++;

            if (_count == 5)
            {
                _count = 0;

                if (!PawnIOHelper.IsPawnIOInnstalled())
                {
                    PawnIOHelper.ShowPawnIONotify();
                }

                if (_amdOverclockingWindow is not { IsLoaded: true })
                {
                    _amdOverclockingWindow = new AmdOverclocking();
                    _amdOverclockingWindow.Show();
                }
                else
                {
                    _amdOverclockingWindow.Activate();
                    if (_amdOverclockingWindow.WindowState == WindowState.Minimized)
                    {
                        _amdOverclockingWindow.BringToForeground();
                    }
                }
            }
        }
        else
        {
            _count = 0;
        }

        try
        {
            Clipboard.SetText(str);
            _ = _snackBar.ShowAsync(Resource.CopiedToClipboard_Title, string.Format(Resource.CopiedToClipboard_Message_WithParam, str));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't copy to clipboard", ex);
        }
    }

    private void WarrantyLinkCardAction_OnClick(object sender, RoutedEventArgs e)
    {
        var link = _warrantyLinkCardAction.Tag as Uri;
        link?.Open();
    }
}
