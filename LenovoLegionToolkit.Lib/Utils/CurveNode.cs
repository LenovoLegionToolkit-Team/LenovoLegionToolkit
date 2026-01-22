using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LenovoLegionToolkit.Lib.Utils;

public class CurveNode : INotifyPropertyChanged
{
    private float _temperature;
    public float Temperature
    {
        get => _temperature;
        set
        {
            if (Math.Abs(_temperature - value) > 0.01f)
            {
                _temperature = value;
                OnPropertyChanged();
            }
        }
    }

    private int _targetPercent;
    public int TargetPercent
    {
        get => _targetPercent;
        set
        {
            if (_targetPercent != value)
            {
                _targetPercent = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
