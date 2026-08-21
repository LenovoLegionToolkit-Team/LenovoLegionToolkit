using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Extensions;

namespace LenovoLegionToolkit.WPF.Windows.Automation.TabItemContent;

public partial class TimeRangeAutomationPipelineTriggerTabItemContent : IAutomationPipelineTriggerTabItemContent<ITimeRangeAutomationPipelineTrigger>
{
    private readonly ITimeRangeAutomationPipelineTrigger _trigger;
    private readonly bool _isSunriseToSunset;
    private readonly bool _isSunsetToSunrise;
    private readonly Time? _startTime;
    private readonly Time? _endTime;
    private readonly DayOfWeek[] _days;

    public TimeRangeAutomationPipelineTriggerTabItemContent(ITimeRangeAutomationPipelineTrigger trigger)
    {
        _trigger = trigger;
        _isSunriseToSunset = trigger.IsSunriseToSunset;
        _isSunsetToSunrise = trigger.IsSunsetToSunrise;
        _startTime = trigger.StartTime;
        _endTime = trigger.EndTime;
        _days = trigger.Days;

        InitializeComponent();
        UpdateCheckBoxes();
    }

    private void UpdateCheckBoxes()
    {
        foreach (var checkBox in _daysOfWeekPanel.Children.OfType<CheckBox>())
            checkBox.Content = Resource.Culture.DateTimeFormat.GetDayName((DayOfWeek)checkBox.Tag);
    }

    private void TimeRangeAutomationPipelineTriggerTabItemContent_Initialized(object? sender, EventArgs e)
    {
        _sunriseToSunsetRadioButton.IsChecked = _isSunriseToSunset;
        _sunsetToSunriseRadioButton.IsChecked = _isSunsetToSunrise;

        var startLocal = _startTime is not null
            ? DateTimeExtensions.UtcFrom(_startTime.Value.Hour, _startTime.Value.Minute, _startTime.Value.Second).ToLocalTime()
            : DateTime.Now;

        _startTimePickerHours.Value = startLocal.Hour;
        _startTimePickerMinutes.Value = startLocal.Minute;
        _startTimePickerSeconds.Value = startLocal.Second;

        var endLocal = _endTime is not null
            ? DateTimeExtensions.UtcFrom(_endTime.Value.Hour, _endTime.Value.Minute, _endTime.Value.Second).ToLocalTime()
            : DateTime.Now.AddHours(1);

        _endTimePickerHours.Value = endLocal.Hour;
        _endTimePickerMinutes.Value = endLocal.Minute;
        _endTimePickerSeconds.Value = endLocal.Second;

        var isCustomRange = _startTime is not null && _endTime is not null;
        if (!_isSunriseToSunset && !_isSunsetToSunrise && !isCustomRange)
            isCustomRange = true;

        _timeRangeRadioButton.IsChecked = isCustomRange;
        _timePickerPanel.IsEnabled = isCustomRange;

        var daysOfWeek = _days.Length != 0 ? _days : Enum.GetValues<DayOfWeek>();
        foreach (var daysOfWeekCheckbox in _daysOfWeekPanel.Children.OfType<CheckBox>())
        {
            if (daysOfWeek.Contains((DayOfWeek)daysOfWeekCheckbox.Tag))
                daysOfWeekCheckbox.IsChecked = true;
        }
    }

    public ITimeRangeAutomationPipelineTrigger GetTrigger()
    {
        var isSunriseToSunset = _sunriseToSunsetRadioButton.IsChecked ?? false;
        var isSunsetToSunrise = _sunsetToSunriseRadioButton.IsChecked ?? false;
        var (startTime, endTime) = GetSelectedTimeRange();
        var days = GetSelectedDays();

        return _trigger.DeepCopy(isSunriseToSunset, isSunsetToSunrise, startTime, endTime, days);
    }

    private (Time?, Time?) GetSelectedTimeRange()
    {
        if (!_timePickerPanel.IsEnabled)
            return (null, null);

        var startHour = (int?)_startTimePickerHours.Value ?? 0;
        var startMinute = (int?)_startTimePickerMinutes.Value ?? 0;
        var startSecond = (int?)_startTimePickerSeconds.Value ?? 0;

        var endHour = (int?)_endTimePickerHours.Value ?? 0;
        var endMinute = (int?)_endTimePickerMinutes.Value ?? 0;
        var endSecond = (int?)_endTimePickerSeconds.Value ?? 0;

        var startUtc = DateTimeExtensions.LocalFrom(startHour, startMinute, startSecond).ToUniversalTime();
        var endUtc = DateTimeExtensions.LocalFrom(endHour, endMinute, endSecond).ToUniversalTime();

        return (new Time(startUtc.Hour, startUtc.Minute, startUtc.Second), new Time(endUtc.Hour, endUtc.Minute, endUtc.Second));
    }

    private DayOfWeek[] GetSelectedDays()
    {
        var days = _daysOfWeekPanel.Children
            .OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => c.Tag)
            .Cast<DayOfWeek>()
            .ToArray();

        if (days.IsEmpty())
            days = Enum.GetValues<DayOfWeek>();

        return days;
    }

    private void RadioButton_Click(object sender, RoutedEventArgs e)
    {
        _timePickerPanel.IsEnabled = _timeRangeRadioButton.IsChecked ?? false;
    }

    private void DayOfWeekCheckBox_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox)
            return;

        var anySelected = _daysOfWeekPanel.Children
            .OfType<CheckBox>()
            .Any(c => c.IsChecked == true);

        if (anySelected)
            return;

        checkBox.IsChecked = true;
    }
}
