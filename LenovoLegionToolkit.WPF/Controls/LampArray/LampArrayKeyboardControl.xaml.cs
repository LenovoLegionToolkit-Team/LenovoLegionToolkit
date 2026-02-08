using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Windows.System;

namespace LenovoLegionToolkit.WPF.Controls.LampArray;

public partial class LampArrayKeyboardControl : UserControl
{
    private readonly Dictionary<VirtualKey, List<LampArrayZoneControl>> _keyMap = new();

    public LampArrayKeyboardControl()
    {
        InitializeComponent();
        InitializeKeyMap();
    }

    private void InitializeKeyMap()
    {
        _keyMap.Clear();
        foreach (var child in KeyboardCanvas.Children)
        {
            if (child is LampArrayZoneControl zone)
            {
                var vk = (VirtualKey)zone.KeyCode;
                if (!_keyMap.ContainsKey(vk))
                {
                    _keyMap[vk] = new List<LampArrayZoneControl>();
                }
                _keyMap[vk].Add(zone);
            }
        }
    }

    public void SetKeyColor(VirtualKey key, Color color)
    {
        if (_keyMap.TryGetValue(key, out var zones))
        {
            foreach (var zone in zones)
            {
                zone.Color = color;
            }
        }
    }

    public void ResetKeyColors()
    {
        foreach (var zones in _keyMap.Values)
        {
            foreach (var zone in zones)
            {
                zone.Color = null;
            }
        }
    }

    public void SetAllKeysColor(Color color)
    {
        foreach (var zones in _keyMap.Values)
        {
            foreach (var zone in zones)
            {
                zone.Color = color;
            }
        }
    }
}
