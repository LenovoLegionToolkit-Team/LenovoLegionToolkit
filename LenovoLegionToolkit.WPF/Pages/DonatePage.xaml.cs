using System.Windows;
using System.Windows.Controls.Primitives;
using LenovoLegionToolkit.WPF.Extensions;

namespace LenovoLegionToolkit.WPF.Pages;

public partial class DonatePage
{
    public DonatePage()
    {
        InitializeComponent();
    }

    private void BartoszPayPal_Click(object sender, RoutedEventArgs e)
    {
        Constants.BartoszPayPalUri.Open();
        e.Handled = true;
    }

    private void Kaguya_Click(object sender, RoutedEventArgs e)
    {
        Constants.KaguyaUri.Open();
        e.Handled = true;
    }

    private void DrSkinnerSponsor_Click(object sender, RoutedEventArgs e)
    {
        if (_drSkinnerButton.ContextMenu is null)
            return;

        _drSkinnerButton.ContextMenu.PlacementTarget = _drSkinnerButton;
        _drSkinnerButton.ContextMenu.Placement = PlacementMode.Bottom;
        _drSkinnerButton.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void DrSkinnerBuyMeACoffee_Click(object sender, RoutedEventArgs e)
    {
        Constants.DrSkinnerBuyMeACoffeeUri.Open();
        e.Handled = true;
    }

    private void DrSkinnerGitHub_Click(object sender, RoutedEventArgs e)
    {
        Constants.DrSkinnerGitHubUri.Open();
        e.Handled = true;
    }
}
