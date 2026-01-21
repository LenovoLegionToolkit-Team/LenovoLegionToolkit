using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using UniversalFanControl.Lib.Generic.Api;
using UniversalFanControl.Lib.Generic.Utils;

namespace LenovoLegionToolkit.Lib.View;

public class FanControlViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly FanCurveManager _manager;
    private readonly FanCurveEntry _curveEntry;

    public FanType FanType { get; set; }
    public ObservableCollection<CurveNode> CurveNodes => _curveEntry.CurveNodes;

    #region Configuration Properties

    public int CriticalTemp
    {
        get => _curveEntry.CriticalTemp;
        set
        {
            if (_curveEntry.CriticalTemp != value)
            {
                _curveEntry.CriticalTemp = value;
                OnPropertyChanged();
                UpdateController();
                _manager.UpdateGlobalSettings(_curveEntry);
            }
        }
    }

    public int AccelerationDcrReduction
    {
        get => _curveEntry.AccelerationDcrReduction;
        set
        {
            if (_curveEntry.AccelerationDcrReduction != value)
            {
                _curveEntry.AccelerationDcrReduction = value;
                OnPropertyChanged();
                UpdateController();
                _manager.UpdateGlobalSettings(_curveEntry);
            }
        }
    }

    public int DecelerationDcrReduction
    {
        get => _curveEntry.DecelerationDcrReduction;
        set
        {
            if (_curveEntry.DecelerationDcrReduction != value)
            {
                _curveEntry.DecelerationDcrReduction = value;
                OnPropertyChanged();
                UpdateController();
                _manager.UpdateGlobalSettings(_curveEntry);
            }
        }
    }

    public bool IsLegion
    {
        get => _curveEntry.IsLegion;
        set
        {
            if (_curveEntry.IsLegion != value)
            {
                _curveEntry.IsLegion = value;
                OnPropertyChanged();
                UpdateController();
                _manager.UpdateGlobalSettings(_curveEntry);
            }
        }
    }

    public float LegionLowTempThreshold
    {
        get => _curveEntry.LegionLowTempThreshold;
        set
        {
            if (Math.Abs(_curveEntry.LegionLowTempThreshold - value) > 0.01f)
            {
                _curveEntry.LegionLowTempThreshold = value;
                OnPropertyChanged();
                UpdateController();
                _manager.UpdateGlobalSettings(_curveEntry);
            }
        }
    }

    private double _maxPwm = 255.0;
    public double MaxPwm
    {
        get => _maxPwm;
        set
        {
            if (Math.Abs(_maxPwm - value) > 0.01)
            {
                _maxPwm = value;
                // Also update entry
                _curveEntry.MaxPwm = value;
                OnPropertyChanged();
                UpdateController();
                // Not syncing MaxPwm globally as it might be fan specific?
                // User asked for: Legion Mode / Critical Temp / Acc / Dec / Low Temp
                // So MaxPwm is not included in the sync list.
            }
        }
    }

    #endregion

    #region Monitoring Properties

    private string _displayTemp = "--";
    public string DisplayTemp
    {
        get => _displayTemp;
        set
        {
            if (_displayTemp != value)
            {
                _displayTemp = value;
                OnPropertyChanged();
            }
        }
    }

    private string _actualRpmDisplay = "--";
    public string ActualRpmDisplay
    {
        get => _actualRpmDisplay;
        set
        {
            if (_actualRpmDisplay != value)
            {
                _actualRpmDisplay = value;
                OnPropertyChanged();
            }
        }
    }

    private string _currentPwmDisplay = "--";
    public string CurrentPwmDisplay
    {
        get => _currentPwmDisplay;
        set
        {
            if (_currentPwmDisplay != value)
            {
                _currentPwmDisplay = value;
                OnPropertyChanged();
            }
        }
    }

    private int _currentPwmByte;
    public int CurrentPwmByte
    {
        get => _currentPwmByte;
        set
        {
            if (_currentPwmByte != value)
            {
                _currentPwmByte = value;
                OnPropertyChanged();
            }
        }
    }

    #endregion

    #region Commands

    public ICommand AddPointCommand { get; private set; }
    public ICommand RemovePointCommand { get; private set; }

    #endregion

    public int CalculatedIndex => 0; // _fanController?.CalculatedIndex ?? 0;

    public FanControlViewModel(ushort fanType, FanCurveEntry curveEntry, FanCurveManager manager)
    {
        FanType = (FanType)fanType;
        _curveEntry = curveEntry;
        _manager = manager;

        // Initialize MaxPwm from entry
        _maxPwm = _curveEntry.MaxPwm;

        manager.RegisterViewModel(FanType, this);

        AddPointCommand = new RelayCommand(_ => AddPoint(), _ => true);
        RemovePointCommand = new RelayCommand(RemovePoint, _ => _curveEntry.CurveNodes.Count > 2);

        _curveEntry.CurveNodes.CollectionChanged += (s, e) =>
        {
            UpdateController();
            SubscribeToNodeChanges();
        };

        SubscribeToNodeChanges();
    }

    private void SubscribeToNodeChanges()
    {
        foreach (var node in _curveEntry.CurveNodes)
        {
            node.PropertyChanged -= OnNodePropertyChanged;
            node.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CurveNode node) return;

        if (e.PropertyName == nameof(CurveNode.Temperature))
        {
            ValidateNodeTemperature(node);
        }
        else if (e.PropertyName == nameof(CurveNode.TargetPercent))
        {
            ValidateNodeSpeed(node);
        }

        UpdateController();
    }

    private void ValidateNodeTemperature(CurveNode node)
    {
        int index = _curveEntry.CurveNodes.IndexOf(node);
        if (index < 0) return;

        float min = (index > 0) ? _curveEntry.CurveNodes[index - 1].Temperature : 0;
        float max = (index < _curveEntry.CurveNodes.Count - 1) ? _curveEntry.CurveNodes[index + 1].Temperature : 120;

        if (node.Temperature < min) node.Temperature = min;
        if (node.Temperature > max) node.Temperature = max;
    }

    private void ValidateNodeSpeed(CurveNode node)
    {
        int index = _curveEntry.CurveNodes.IndexOf(node);
        if (index < 0) return;

        int min = (index > 0) ? _curveEntry.CurveNodes[index - 1].TargetPercent : 0;
        int max = (index < _curveEntry.CurveNodes.Count - 1) ? _curveEntry.CurveNodes[index + 1].TargetPercent : 100;

        if (node.TargetPercent < min) node.TargetPercent = min;
        if (node.TargetPercent > max) node.TargetPercent = max;
    }

    private void AddPoint()
    {
        var lastNode = _curveEntry.CurveNodes.LastOrDefault();
        float newTemp = lastNode != null ? Math.Min(lastNode.Temperature + 5, 100) : 50;
        int newPercent = lastNode?.TargetPercent ?? 50;

        _curveEntry.CurveNodes.Add(new CurveNode { Temperature = newTemp, TargetPercent = newPercent });
    }

    private void RemovePoint(object? parameter)
    {
        if (parameter is CurveNode node && _curveEntry.CurveNodes.Count > 2)
        {
            _curveEntry.CurveNodes.Remove(node);
        }
    }

    private void UpdateController()
    {
        _manager.UpdateConfig(FanType, _curveEntry);
    }

    public void UpdateMonitoring(float temperature, int rpm, byte pwmByte)
    {
        DisplayTemp = $"{temperature:F1} °C";
        ActualRpmDisplay = $"{rpm} RPM";
        CurrentPwmDisplay = $"{pwmByte}";
        CurrentPwmByte = pwmByte;
    }

    public FanCurveEntry GetCurveEntry()
    {
        _curveEntry.Type = FanType;
        return _curveEntry;
    }

    public void NotifyGlobalSettingsChanged()
    {
        OnPropertyChanged(nameof(IsLegion));
        OnPropertyChanged(nameof(CriticalTemp));
        OnPropertyChanged(nameof(LegionLowTempThreshold));
        OnPropertyChanged(nameof(AccelerationDcrReduction));
        OnPropertyChanged(nameof(DecelerationDcrReduction));
    }

    public void ResetToDefault(FanTableInfo defaultInfo)
    {
        var defaultEntry = FanCurveEntry.FromFanTableInfo(defaultInfo, (ushort)FanType);
        
        // Reset properties
        _curveEntry.IsLegion = defaultEntry.IsLegion;
        _curveEntry.CriticalTemp = defaultEntry.CriticalTemp;
        _curveEntry.LegionLowTempThreshold = defaultEntry.LegionLowTempThreshold;
        _curveEntry.AccelerationDcrReduction = defaultEntry.AccelerationDcrReduction;
        _curveEntry.DecelerationDcrReduction = defaultEntry.DecelerationDcrReduction;
        // MaxPwm is usually specific and maybe shouldn't be reset to 255 if user tuned it, but "Default" usually implies full reset. 
        // defaultEntry.MaxPwm is initialized to 255.0 in constructor.
        _curveEntry.MaxPwm = defaultEntry.MaxPwm;

        // Reset nodes
        _curveEntry.CurveNodes.Clear();
        foreach (var node in defaultEntry.CurveNodes)
        {
            _curveEntry.CurveNodes.Add(node);
        }

        NotifyGlobalSettingsChanged();
        OnPropertyChanged(nameof(MaxPwm));
        UpdateController();
        _manager.UpdateGlobalSettings(_curveEntry);
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
