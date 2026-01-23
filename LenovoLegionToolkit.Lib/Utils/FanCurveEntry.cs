using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using UniversalFanControl.Lib.Generic.Api;

namespace LenovoLegionToolkit.Lib.Utils;

public class FanCurveEntry : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private FanType _type = FanType.Cpu;
    public FanType Type
    {
        get => _type;
        set
        {
            if (_type != value)
            {
                _type = value;
                OnPropertyChanged();
            }
        }
    }

    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public ObservableCollection<CurveNode> CurveNodes { get; set; } = new();

    private int[]? _rampUpThresholds;
    public int[]? RampUpThresholds
    {
        get => _rampUpThresholds;
        set
        {
            if (_rampUpThresholds != value)
            {
                _rampUpThresholds = value;
                OnPropertyChanged();
            }
        }
    }

    private int[]? _rampDownThresholds;
    public int[]? RampDownThresholds
    {
        get => _rampDownThresholds;
        set
        {
            if (_rampDownThresholds != value)
            {
                _rampDownThresholds = value;
                OnPropertyChanged();
            }
        }
    }

    public int CriticalTemp { get; set; } = 90;
    public bool IsLegion { get; set; } = false;
    public float LegionLowTempThreshold { get; set; } = 40f;
    public int AccelerationDcrReduction { get; set; } = 1;
    public int DecelerationDcrReduction { get; set; } = 2;
    public double MaxPwm { get; set; } = 255.0;

    public FanCurveEntry()
    {
        InitializeDefaultCurve();
        CurveNodes.CollectionChanged += (s, e) => OnPropertyChanged(nameof(CurveNodes));
    }

    private void InitializeDefaultCurve()
    {
        CurveNodes.Clear();
        CurveNodes.Add(new CurveNode { Temperature = 40, TargetPercent = 0 });
        CurveNodes.Add(new CurveNode { Temperature = 50, TargetPercent = 30 });
        CurveNodes.Add(new CurveNode { Temperature = 60, TargetPercent = 50 });
        CurveNodes.Add(new CurveNode { Temperature = 70, TargetPercent = 70 });
        CurveNodes.Add(new CurveNode { Temperature = 80, TargetPercent = 85 });
        CurveNodes.Add(new CurveNode { Temperature = 90, TargetPercent = 100 });
    }

    public FanTable ToFanTable(FanTableData[] tableData)
    {
        if (tableData.Length == 0)
            throw new ArgumentException("Table data cannot be empty", nameof(tableData));

        var temps = tableData[0].Temps;
        var fanSpeeds = tableData[0].FanSpeeds;

        if (temps.Length != 10)
            throw new ArgumentException("Temperature array must have exactly 10 elements", nameof(tableData));

        var result = new ushort[10];
        var sortedNodes = CurveNodes.OrderBy(n => n.Temperature).ToList();

        for (int i = 0; i < 10; i++)
        {
            var temp = temps[i];
            var targetPercent = CalculateTargetPercent(temp, sortedNodes);
            
            var speedIndex = (int)Math.Round((targetPercent / 100.0) * (fanSpeeds.Length - 1));
            speedIndex = Math.Clamp(speedIndex, 0, fanSpeeds.Length - 1);
            
            result[i] = (ushort)speedIndex;
        }

        return new FanTable(result);
    }

    public static FanCurveEntry FromFanTableInfo(FanTableInfo fanTableInfo, ushort fanType)
    {
        var entry = new FanCurveEntry { Type = (FanType)fanType };
        entry.CurveNodes.Clear();

        var tableValues = fanTableInfo.Table.GetTable();
        var tableData = fanTableInfo.Data;

        if (tableData.Length == 0)
            return entry;

        var temps = tableData[0].Temps;
        var fanSpeeds = tableData[0].FanSpeeds;

        for (int i = 0; i < Math.Min(tableValues.Length, temps.Length); i++)
        {
            var speedIndex = tableValues[i];
            if (speedIndex >= fanSpeeds.Length)
                continue;

            var rpm = fanSpeeds[speedIndex];
            var maxRpm = fanSpeeds[^1];
            var percent = maxRpm > 0 ? (int)Math.Round((double)rpm / maxRpm * 100) : 0;

            entry.CurveNodes.Add(new CurveNode
            {
                Temperature = temps[i],
                TargetPercent = percent
            });
        }

        return entry;
    }

    public string ExportToJson()
    {
        var exportData = new
        {
            Type,
            CurveNodes = CurveNodes.ToList(),
            RampUpThresholds,
            RampDownThresholds,
            CriticalTemp,
            IsLegion,
            LegionLowTempThreshold,
            AccelerationDcrReduction,
            DecelerationDcrReduction,
            MaxPwm
        };

        return JsonConvert.SerializeObject(exportData, Formatting.Indented);
    }

    public static FanCurveEntry ImportFromJson(string json)
    {
        dynamic? data = JsonConvert.DeserializeObject(json);
        if (data == null)
            throw new InvalidOperationException("Failed to deserialize JSON");

        var entry = new FanCurveEntry();
        
        if (data.Type != null)
            entry.Type = data.Type;

        if (data.CurveNodes != null)
        {
            entry.CurveNodes.Clear();
            foreach (var node in data.CurveNodes)
            {
                entry.CurveNodes.Add(new CurveNode
                {
                    Temperature = node.Temperature,
                    TargetPercent = node.TargetPercent
                });
            }
        }

        if (data.RampUpThresholds != null)
            entry.RampUpThresholds = data.RampUpThresholds.ToObject<int[]>();

        if (data.RampDownThresholds != null)
            entry.RampDownThresholds = data.RampDownThresholds.ToObject<int[]>();

        if (data.CriticalTemp != null) entry.CriticalTemp = data.CriticalTemp;
        if (data.IsLegion != null) entry.IsLegion = data.IsLegion;
        if (data.LegionLowTempThreshold != null) entry.LegionLowTempThreshold = data.LegionLowTempThreshold;
        if (data.AccelerationDcrReduction != null) entry.AccelerationDcrReduction = data.AccelerationDcrReduction;
        if (data.DecelerationDcrReduction != null) entry.DecelerationDcrReduction = data.DecelerationDcrReduction;
        if (data.MaxPwm != null) entry.MaxPwm = data.MaxPwm;

        return entry;
    }

    internal FanCurveConfig ToConfig()
    {
        return new FanCurveConfig
        {
            CriticalTemp = CriticalTemp,
            MaxPwm = MaxPwm,
            KickstartPwm = 50.0,
            AccelerationDcrReduction = AccelerationDcrReduction,
            DecelerationDcrReduction = DecelerationDcrReduction,
            IsLegion = IsLegion,
            LegionLowTempThreshold = LegionLowTempThreshold,
            RampUpThresholds = RampUpThresholds,
            RampDownThresholds = RampDownThresholds
        };
    }

    private double CalculateTargetPercent(float temp, List<CurveNode> sortedNodes)
    {
        if (sortedNodes.Count == 0)
            return 0;

        if (temp <= sortedNodes.First().Temperature)
            return sortedNodes.First().TargetPercent;

        if (temp >= sortedNodes.Last().Temperature)
            return sortedNodes.Last().TargetPercent;

        for (int i = 0; i < sortedNodes.Count - 1; i++)
        {
            if (temp >= sortedNodes[i].Temperature && temp <= sortedNodes[i + 1].Temperature)
            {
                float ratio = (temp - sortedNodes[i].Temperature) /
                             (sortedNodes[i + 1].Temperature - sortedNodes[i].Temperature);

                return sortedNodes[i].TargetPercent +
                       ratio * (sortedNodes[i + 1].TargetPercent - sortedNodes[i].TargetPercent);
            }
        }

        return sortedNodes.Last().TargetPercent;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
