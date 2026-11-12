using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Custom;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;
using Wpf.Ui.Common;
using CardAction = LenovoLegionToolkit.WPF.Controls.Custom.CardAction;
using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class SpecialKeysWindow
{
    private readonly SpecialKeySettings _settings = IoCContainer.Resolve<SpecialKeySettings>();

    private bool _showingHidden;

    public SpecialKeysWindow()
    {
        InitializeComponent();
        Title = _title.Text = Resource.SettingsPage_Category_SmartKeys;
        IsVisibleChanged += SpecialKeysWindow_IsVisibleChanged;
    }

    private async void SpecialKeysWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    private Task RefreshAsync()
    {
        BuildKeyList();
        return Task.CompletedTask;
    }

    private void BuildKeyList()
    {
        _keyList.Items.Clear();
        _hiddenArea.Children.Clear();

        var displayedCodes = new HashSet<int>();
        var entries = new List<(int Code, string Name)>();

        foreach (var key in Enum.GetValues<SpecialKey>())
        {
            var code = (int)key;
            if (_settings.Store.HiddenKeys.Contains(code)) continue;
            if (!displayedCodes.Add(code)) continue;
            entries.Add((code, GetSpecialKeyDisplayName(code)));
        }

        foreach (var key in Enum.GetValues<DriverKey>())
        {
            var code = (int)key + SpecialKeySettings.SpecialKeySettingsStore.DriverKeyCodeOffset;
            if (_settings.Store.HiddenKeys.Contains(code)) continue;
            if (!displayedCodes.Add(code)) continue;
            entries.Add((code, SpecialKeyDisplayName.GetKey(key, Resource.SpecialKey_DriverSuffix)));
        }

        foreach (var key in Enum.GetValues<ConsumerControlKey>())
        {
            var code = (int)key + SpecialKeySettings.SpecialKeySettingsStore.ConsumerControlKeyCodeOffset;
            if (_settings.Store.HiddenKeys.Contains(code)) continue;
            if (!displayedCodes.Add(code)) continue;
            entries.Add((code, SpecialKeyDisplayName.GetKey(key, Resource.SpecialKey_ConsumerSuffix)));
        }

        foreach (var key in Enum.GetValues<KeyboardKey>())
        {
            var code = (int)key + SpecialKeySettings.SpecialKeySettingsStore.KeyboardKeyCodeOffset;
            if (_settings.Store.HiddenKeys.Contains(code)) continue;
            if (!displayedCodes.Add(code)) continue;
            entries.Add((code, SpecialKeyDisplayName.GetKey(key, Resource.SpecialKey_KeyboardSuffix)));
        }

        foreach (var (code, name) in _settings.Store.KeyDescriptions)
        {
            if (_settings.Store.HiddenKeys.Contains(code)) continue;
            if (!displayedCodes.Add(code)) continue;
            if (Enum.IsDefined(typeof(SpecialKey), (SpecialKey)code)) continue;
            if (SpecialKeyCodes.IsDriverKey(code)) continue;
            if (SpecialKeyCodes.IsConsumerControlKey(code)) continue;
            if (SpecialKeyCodes.IsKeyboardKey(code)) continue;
            entries.Add((code, name));
        }

        foreach (var (code, name) in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            AddKeyCard(code, name);

        var hiddenCount = _settings.Store.HiddenKeys.Count;
        _showHiddenButton.Visibility = hiddenCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (hiddenCount > 0)
        {
            _showHiddenButton.Icon = _showingHidden ? SymbolRegular.EyeOff24 : SymbolRegular.Eye24;
            _showHiddenButton.ToolTip = _showingHidden
                ? string.Format(Resource.SpecialKeysWindow_HideHiddenKeys, hiddenCount)
                : string.Format(Resource.SpecialKeysWindow_ShowHiddenKeys, hiddenCount);

            if (_showingHidden)
            {
                var hiddenEntries = _settings.Store.HiddenKeys
                    .Select(code =>
                    {
                        string name;
                        if (_settings.Store.KeyDescriptions.TryGetValue(code, out var desc))
                            name = desc;
                        else if (SpecialKeyCodes.IsDriverKey(code))
                            name = SpecialKeyDisplayName.GetKey(SpecialKeyCodes.ToDriverKey(code), Resource.SpecialKey_DriverSuffix);
                        else if (SpecialKeyCodes.IsConsumerControlKey(code))
                            name = SpecialKeyDisplayName.GetKey(SpecialKeyCodes.ToConsumerControlKey(code), Resource.SpecialKey_ConsumerSuffix);
                        else if (SpecialKeyCodes.IsKeyboardKey(code))
                            name = SpecialKeyDisplayName.GetKey(SpecialKeyCodes.ToKeyboardKey(code), Resource.SpecialKey_KeyboardSuffix);
                        else
                            name = GetSpecialKeyDisplayName(code);
                        return (Code: code, Name: name);
                    })
                    .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

                foreach (var (code, name) in hiddenEntries)
                    AddHiddenKeyCard(code, name);
            }
        }
    }

    private void AddKeyCard(int code, string displayName)
    {
        var mode = _settings.Store.KeyModes.TryGetValue(code, out var m)
            ? m : CustomSpecialKey.Default;

        var subtitleText = mode.GetDisplayName();

        var card = new CardAction
        {
            Margin = new(0, 0, 0, 4),
            Icon = SymbolRegular.Keyboard24,
            Content = new CardHeaderControl
            {
                Title = displayName,
                Subtitle = subtitleText
            },
            Tag = code,
            Cursor = Cursors.Hand,
            ContextMenu = CreateHideContextMenu(code,
                !Enum.IsDefined(typeof(SpecialKey), (SpecialKey)code)
                && !SpecialKeyCodes.IsDriverKey(code)
                && !SpecialKeyCodes.IsConsumerControlKey(code)
                && !SpecialKeyCodes.IsKeyboardKey(code))
        };
        card.Click += (_, _) => OpenKeyDetailWindow(code, displayName);

        _keyList.Items.Add(card);
    }

    private void AddHiddenKeyCard(int code, string displayName)
    {
        var isCustom = !Enum.IsDefined(typeof(SpecialKey), (SpecialKey)code)
            && !SpecialKeyCodes.IsDriverKey(code)
            && !SpecialKeyCodes.IsConsumerControlKey(code)
            && !SpecialKeyCodes.IsKeyboardKey(code);

        var card = new CardAction
        {
            Margin = new(0, 4, 0, 0),
            Opacity = 0.5,
            Icon = SymbolRegular.Keyboard24,
            Content = new CardHeaderControl
            {
                Title = displayName
            },
            Cursor = Cursors.Arrow,
            ContextMenu = CreateUnhideContextMenu(code, isCustom)
        };
        _hiddenArea.Children.Add(card);
    }

    private ContextMenu CreateHideContextMenu(int code, bool isCustom)
    {
        var hideItem = new MenuItem
        {
            SymbolIcon = SymbolRegular.EyeOff24,
            Header = Resource.Hide,
        };
        hideItem.Click += (_, _) =>
        {
            _settings.Store.HiddenKeys.Add(code);
            _settings.SynchronizeStore();
            BuildKeyList();
        };

        var renameItem = new MenuItem { SymbolIcon = SymbolRegular.Edit24, Header = Resource.Rename };
        renameItem.Click += async (_, _) =>
        {
            var current = _settings.Store.KeyDescriptions.TryGetValue(code, out var n) ? n : null;
            var newName = await MessageBoxHelper.ShowInputAsync(this, Resource.Rename, null, current, allowEmpty: true);
            if (newName is null)
                return;
            if (string.IsNullOrWhiteSpace(newName))
                _settings.Store.KeyDescriptions.Remove(code);
            else
                _settings.Store.KeyDescriptions[code] = newName.Trim();
            _settings.SynchronizeStore();
            BuildKeyList();
        };

        var menu = new ContextMenu { Items = { hideItem, renameItem } };

        if (isCustom)
        {
            var deleteItem = new MenuItem { SymbolIcon = SymbolRegular.Delete24, Header = Resource.Delete };
            deleteItem.Click += (_, _) =>
            {
                _settings.Store.KeyDescriptions.Remove(code);
                _settings.Store.KeyModes.Remove(code);
                _settings.Store.KeySinglePressActions.Remove(code);
                _settings.Store.KeyDoublePressActions.Remove(code);
                _settings.SynchronizeStore();
                BuildKeyList();
            };
            menu.Items.Add(deleteItem);
        }

        return menu;
    }

    private ContextMenu CreateUnhideContextMenu(int code, bool isCustom)
    {
        var unhideItem = new MenuItem
        {
            SymbolIcon = SymbolRegular.Eye24,
            Header = Resource.Unhide,
        };
        unhideItem.Click += (_, _) =>
        {
            _settings.Store.HiddenKeys.Remove(code);
            _settings.SynchronizeStore();
            BuildKeyList();
        };

        var renameItem2 = new MenuItem { SymbolIcon = SymbolRegular.Edit24, Header = Resource.Rename };
        renameItem2.Click += async (_, _) =>
        {
            var current = _settings.Store.KeyDescriptions.TryGetValue(code, out var n) ? n : null;
            var newName = await MessageBoxHelper.ShowInputAsync(this, Resource.Rename, null, current, allowEmpty: true);
            if (newName is null)
                return;
            if (string.IsNullOrWhiteSpace(newName))
                _settings.Store.KeyDescriptions.Remove(code);
            else
                _settings.Store.KeyDescriptions[code] = newName.Trim();
            _settings.SynchronizeStore();
            BuildKeyList();
        };

        var menu = new ContextMenu { Items = { unhideItem, renameItem2 } };

        if (isCustom)
        {
            var deleteItem = new MenuItem { SymbolIcon = SymbolRegular.Delete24, Header = Resource.Delete };
            deleteItem.Click += (_, _) =>
            {
                _settings.Store.HiddenKeys.Remove(code);
                _settings.Store.KeyDescriptions.Remove(code);
                _settings.Store.KeyModes.Remove(code);
                _settings.Store.KeySinglePressActions.Remove(code);
                _settings.Store.KeyDoublePressActions.Remove(code);
                _settings.SynchronizeStore();
                BuildKeyList();
            };
            menu.Items.Add(deleteItem);
        }

        return menu;
    }

    private void ShowHiddenButton_Click(object sender, RoutedEventArgs e)
    {
        _showingHidden = !_showingHidden;
        BuildKeyList();
    }

    private void OpenKeyDetailWindow(int keyCode, string displayName)
    {
        var window = new SpecialKeyDetailWindow(keyCode, displayName) { Owner = this };
        window.ShowDialog();
        BuildKeyList();
    }

    private string GetSpecialKeyDisplayName(int code)
    {
        if (_settings.Store.KeyDescriptions.TryGetValue(code, out var customName))
            return customName;

        if (Enum.IsDefined(typeof(SpecialKey), (SpecialKey)code))
            return SpecialKeyDisplayName.GetKey((SpecialKey)code);

        return string.Format(Resource.SpecialKey_UnknownKey, code);
    }
}
