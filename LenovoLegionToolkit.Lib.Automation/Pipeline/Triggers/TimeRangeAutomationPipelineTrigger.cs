using System;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

[method: JsonConstructor]
public class TimeRangeAutomationPipelineTrigger(bool isSunriseToSunset, bool isSunsetToSunrise, Time? startTime, Time? endTime, DayOfWeek[]? days)
    : ITimeRangeAutomationPipelineTrigger
{
    public bool IsSunriseToSunset { get; } = isSunriseToSunset;

    public bool IsSunsetToSunrise { get; } = isSunsetToSunrise;

    public Time? StartTime { get; } = startTime;

    public Time? EndTime { get; } = endTime;

    public DayOfWeek[] Days { get; } = days ?? [];

    public string DisplayName => Resource.TimeRangeAutomationPipelineTrigger_DisplayName;

    private readonly SunriseSunset _sunriseSunset = IoCContainer.Resolve<SunriseSunset>();

    public async Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is StartupAutomationEvent)
            return await IsMatchingState().ConfigureAwait(false);

        if (automationEvent is not TimeAutomationEvent e)
            return false;

        if (IsSunriseToSunset)
        {
            var (sunrise, sunset) = await _sunriseSunset.GetSunriseSunsetAsync().ConfigureAwait(false);
            if (sunrise is not null && sunset is not null)
                return e.Time == sunrise && IsDayMatching(sunrise.Value, e.Day, sunset.Value);
        }

        if (IsSunsetToSunrise)
        {
            var (sunrise, sunset) = await _sunriseSunset.GetSunriseSunsetAsync().ConfigureAwait(false);
            if (sunrise is not null && sunset is not null)
                return e.Time == sunset && IsDayMatching(sunset.Value, e.Day, sunrise.Value);
        }

        if (StartTime is not null && EndTime is not null)
            return e.Time == StartTime.Value && IsDayMatching(StartTime.Value, e.Day, EndTime.Value);

        return false;
    }

    public async Task<bool> IsMatchingState()
    {
        var now = DateTime.UtcNow;
        var time = new Time(now.Hour, now.Minute, now.Second);
        var day = now.DayOfWeek;

        return await IsMatching(time, day).ConfigureAwait(false);
    }

    public void UpdateEnvironment(AutomationEnvironment environment)
    {
        environment.IsSunriseToSunset = IsSunriseToSunset;
        environment.IsSunsetToSunrise = IsSunsetToSunrise;
        environment.StartTime = StartTime;
        environment.EndTime = EndTime;
        environment.Days = Days;
    }

    private async Task<bool> IsMatching(Time time, DayOfWeek dayOfWeek)
    {
        if (IsSunriseToSunset)
        {
            var (sunrise, sunset) = await _sunriseSunset.GetSunriseSunsetAsync().ConfigureAwait(false);
            if (sunrise is null || sunset is null)
                return false;

            return time.IsBetween(sunrise.Value, sunset.Value) && IsDayMatching(sunrise.Value, dayOfWeek, sunset.Value, time);
        }

        if (IsSunsetToSunrise)
        {
            var (sunrise, sunset) = await _sunriseSunset.GetSunriseSunsetAsync().ConfigureAwait(false);
            if (sunrise is null || sunset is null)
                return false;

            return time.IsBetween(sunset.Value, sunrise.Value) && IsDayMatching(sunset.Value, dayOfWeek, sunrise.Value, time);
        }

        if (StartTime is not null && EndTime is not null)
            return time.IsBetween(StartTime.Value, EndTime.Value) && IsDayMatching(StartTime.Value, dayOfWeek, EndTime.Value, time);

        return false;
    }

    private bool IsDayMatching(Time start, DayOfWeek currentDay, Time end, Time? current = null)
    {
        if (Days.IsEmpty())
            return true;

        if (start <= end || current is null)
            return Days.Contains(currentDay);

        // Cross-midnight: if current time is in the post-midnight portion (00:00 <= current <= end),
        // the active window originated on the previous day.
        if (current.Value <= end)
        {
            var yesterday = (DayOfWeek)(((int)currentDay + 6) % 7);
            return Days.Contains(yesterday);
        }

        return Days.Contains(currentDay);
    }

    public IAutomationPipelineTrigger DeepCopy() =>
        new TimeRangeAutomationPipelineTrigger(IsSunriseToSunset, IsSunsetToSunrise, StartTime, EndTime, Days);

    public ITimeRangeAutomationPipelineTrigger DeepCopy(bool isSunriseToSunset, bool isSunsetToSunrise, Time? startTime, Time? endTime, DayOfWeek[] days) =>
        new TimeRangeAutomationPipelineTrigger(isSunriseToSunset, isSunsetToSunrise, startTime, endTime, days);

    public override bool Equals(object? obj)
    {
        return obj is TimeRangeAutomationPipelineTrigger t &&
               IsSunriseToSunset == t.IsSunriseToSunset &&
               IsSunsetToSunrise == t.IsSunsetToSunrise &&
               StartTime == t.StartTime &&
               EndTime == t.EndTime &&
               Days.SequenceEqual(t.Days);
    }

    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(IsSunriseToSunset);
        hc.Add(IsSunsetToSunrise);
        hc.Add(StartTime);
        hc.Add(EndTime);
        Days.ForEach(d => hc.Add(d));
        return hc.ToHashCode();
    }

    public override string ToString() =>
        $"{nameof(IsSunriseToSunset)}: {IsSunriseToSunset}," +
        $" {nameof(IsSunsetToSunrise)}: {IsSunsetToSunrise}," +
        $" {nameof(StartTime)}: {StartTime}," +
        $" {nameof(EndTime)}: {EndTime}," +
        $" {nameof(Days)}: [{string.Join(", ", Days)}]";
}
