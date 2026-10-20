using FyreApp.Models;
using FyreApp.Services.MaintenanceSchedules;
using FyreApp.Services.Tasks;
using FyreApp.Services.Uptick.Mapping;
using Xunit;

namespace FyreApp.Tests.Services;

// Reading Uptick's routines and tasks: frequencies, next occurrence to do, durations, task statuses and scopes
public class UptickRulesTests
{
    private static readonly DateTime Today = new(2026, 10, 6);
    private static DateTime D(string s) => DateTime.Parse(s);

    [Theory]
    [InlineData("10 - Portable and Wheeled Fire Extinguishers: Five-yearly", 60)]
    [InlineData("7 - Emergency Lighting: Six-monthly", 6)]
    [InlineData("Hydrants: Yearly", 12)]
    [InlineData("Sprinklers: Monthly", 1)]
    [InlineData("Hose reels: Three-monthly", 3)]
    [InlineData("Pressure test: Twenty-five-yearly", 300)]
    [InlineData("Annual", 12)]
    public void ParseFrequency_KnownLabels(string routine, int expected)
    {
        Assert.Equal(expected, RoutineRules.ParseFrequency(routine).Months);
    }

    [Theory]
    [InlineData("Pumps: Weekly")]
    [InlineData("Pumps: Fortnightly")]
    public void ParseFrequency_SubMonthly_IsUnsupported(string routine)
    {
        Assert.Null(RoutineRules.ParseFrequency(routine).Months);
    }

    [Theory]
    [InlineData("Annual Evacuation Drill: Annual Evacuation Drill", 12)]   // custom Uptick level, seen on prod
    [InlineData("Annual Evacuation Drill", 12)]
    [InlineData("Fire doors: Six monthly check", 6)]                       // "six monthly" read together, not as "monthly"
    public void ParseFrequency_NamedLevel_UsesFrequencyWordInName(string routine, int expected)
    {
        Assert.Equal(expected, RoutineRules.ParseFrequency(routine).Months);
    }

    [Fact]
    public void ParseFrequency_NamedLevelWithTwoFrequencies_IsUnsupported()
    {
        Assert.Null(RoutineRules.ParseFrequency("Drills: Monthly and Annual checks").Months);
    }

    [Fact]
    public void NextOutstanding_OldGeneratedSupersededByLaterCompletion_IsIgnored()
    {
        // Unfiltered export: a 2021 "G" whose task was never closed, then later occurrences completed
        var next = RoutineRules.NextOutstanding(new[]
        {
            ("Extinguishers: Six-monthly", D("2021-03-01"), false, true),
            ("Extinguishers: Six-monthly", D("2026-04-01"), true, false),
            ("Extinguishers: Six-monthly", D("2026-10-01"), false, false),
            ("Extinguishers: Six-monthly", D("2027-04-01"), false, false)
        }, Today);

        Assert.Equal(D("2026-10-01"), next);
    }

    [Fact]
    public void NextOutstanding_OpenOccurrenceAfterLastCompletion_IsKept()
    {
        var next = RoutineRules.NextOutstanding(new[]
        {
            ("Extinguishers: Six-monthly", D("2025-10-01"), true, false),
            ("Extinguishers: Six-monthly", D("2026-04-01"), false, true),   // overdue, task raised, still open
            ("Extinguishers: Six-monthly", D("2026-10-01"), false, false)
        }, Today);

        Assert.Equal(D("2026-04-01"), next);
    }

    [Fact]
    public void NextOutstanding_CompletionDueInFuture_DoesNotSupersedeEarlierOnes()
    {
        // Five-yearly work done early marks a 2031 occurrence complete
        var next = RoutineRules.NextOutstanding(new[]
        {
            ("Hydrants: Annual", D("2027-01-01"), false, false),
            ("Hydrants: Annual", D("2031-01-01"), true, false)
        }, Today);

        Assert.Equal(D("2027-01-01"), next);
    }

    [Fact]
    public void NextOutstanding_EarlierRaisedOccurrencesBehindALaterRaisedOne_AreIgnored()
    {
        // P-0662 lighting on prod: Uptick left 2024 and 2025 at "G" though their tasks were done;
        // the 2026 one (task still READY) is the one actually outstanding
        var next = RoutineRules.NextOutstanding(new[]
        {
            ("15 - Emergency escape lighting and exit signs: Six-monthly", D("2023-05-30"), true, false),
            ("15 - Emergency Escape Lighting and Exit Signs: Six-monthly", D("2024-05-30"), false, true),
            ("15 - Emergency Escape Lighting and Exit Signs: Six-monthly", D("2025-05-30"), false, true),
            ("15 - Emergency Escape Lighting and Exit Signs: Six-monthly", D("2026-05-30"), false, true),
            ("15 - Emergency Escape Lighting and Exit Signs: Six-monthly", D("2027-05-30"), false, false)
        }, Today);

        Assert.Equal(D("2026-05-30"), next);
    }

    [Fact]
    public void NextOutstanding_IsWorkedOutPerRoutine()
    {
        // One routine's completion doesn't hide another routine's open occurrence in the same schedule
        var next = RoutineRules.NextOutstanding(new[]
        {
            ("Fire panels: Six-monthly", D("2026-08-01"), false, true),
            ("Extinguishers: Six-monthly", D("2026-09-01"), true, false),
            ("Extinguishers: Six-monthly", D("2027-03-01"), false, false)
        }, Today);

        Assert.Equal(D("2026-08-01"), next);
    }

    [Theory]
    [InlineData("0:30:00", 30)]
    [InlineData("1:15:00", 75)]
    [InlineData("0:00:00", null)]
    [InlineData("", null)]
    public void ParseMinutes_ReadsUptickDurations(string input, int? expected) =>
        Assert.Equal(expected, RoutineRules.ParseMinutes(input));

    [Theory]
    [InlineData("- 10 - Portable and Wheeled Fire Extinguishers (AS1851-2012 Section 10): Annual", new[] { 12 })]
    [InlineData("10 - Portable and Wheeled Fire Extinguishers: Six-monthly (1)\n06 - Fire Detection: Monthly (3)", new[] { 6, 1 })]
    [InlineData("Quote to replace failed items as per service report.", new int[0])]
    public void ScopeMonths_ReadsFrequencyPerLine(string scope, int[] expected)
    {
        Assert.Equal(expected, UptickTaskRules.ScopeMonths(scope));
    }

    [Theory]
    [InlineData("COMPLETE", ClientTaskStatus.Completed)]
    [InlineData("CANCELLED", ClientTaskStatus.Cancelled)]
    [InlineData("PERFORMED", ClientTaskStatus.InProgress)]
    [InlineData("READY", ClientTaskStatus.Open)]
    [InlineData("PENDINGEXT", ClientTaskStatus.Open)]
    public void MapStatus_MapsUptickStatuses(string uptick, ClientTaskStatus expected)
    {
        Assert.Equal(expected, UptickTaskRules.MapStatus(uptick));
    }

    [Theory]
    [InlineData(6, "M", 6)]
    [InlineData(2, "Y", 24)]
    [InlineData(14, "D", null)]
    [InlineData(0, "M", null)]
    public void IntervalMonths_ConvertsUnits(int interval, string unit, int? expected)
    {
        Assert.Equal(expected, RoutineServiceLevelMapper.IntervalMonths(interval, unit));
    }
}
