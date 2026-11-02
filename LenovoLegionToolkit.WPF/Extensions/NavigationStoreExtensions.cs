using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Wpf.Ui.Controls.Interfaces;
using CustomNavigationItem = LenovoLegionToolkit.WPF.Controls.Custom.NavigationItem;

namespace LenovoLegionToolkit.WPF.Extensions;

public static class NavigationStoreExtensions
{
    public static async Task<bool> TryAddAsync(this NavigationStore navigationStore,
        string pageTag,
        Type pageType,
        string title,
        SymbolRegular icon,
        Func<Task<bool>>? isSupportedAsync = null)
    {
        if (isSupportedAsync is not null && !await isSupportedAsync())
        {
            Log.Instance.Trace($"Feature not supported, skipping. [pageTag={pageTag}]");
            return false;
        }

        navigationStore.Items.Add(new CustomNavigationItem
        {
            Content = title,
            Icon = icon,
            PageTag = pageTag,
            PageType = pageType,
        });

        return true;
    }

    public static void NavigateToNext(this NavigationStore navigationStore)
    {
        var navigationItems = navigationStore.Items.OfType<INavigationItem>().ToList();
        var current = navigationStore.Current ?? navigationItems.FirstOrDefault();

        if (current is null)
            return;

        var index = (navigationItems.IndexOf(current) + 1) % navigationItems.Count;
        var next = navigationItems[index];

        navigationStore.Navigate(next.PageTag);
    }

    public static void NavigateToPrevious(this NavigationStore navigationStore)
    {
        var navigationItems = navigationStore.Items.OfType<INavigationItem>().ToList();
        var current = navigationStore.Current ?? navigationItems.FirstOrDefault();

        if (current is null)
            return;

        var index = navigationItems.IndexOf(current) - 1;
        if (index < 0)
            index = navigationItems.Count - 1;
        var next = navigationItems[index];

        navigationStore.Navigate(next.PageTag);
    }
}
