using SchedulingAssistant.Models;
using SchedulingAssistant.ViewModels.Management;
using SchedulingAssistant.Services;
using Xunit;

namespace SchedulingAssistant.Tests;

/// <summary>
/// Unit tests for <see cref="LegalStartTimeEditViewModel"/> start-time validation,
/// specifically the 07:30 earliest-start and 22:00 latest-end bounds added alongside
/// the dynamic grid range feature.
/// </summary>
public class LegalStartTimeEditViewModelTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal <see cref="LegalStartTimeEditViewModel"/> for the given block length.
    /// The onSave and onCancel callbacks are no-ops since these tests only exercise AddStartTime.
    /// </summary>
    private static LegalStartTimeEditViewModel MakeVm(double blockLengthHours) =>
        new(
            new LegalStartTime { BlockLength = blockLengthHours, StartTimes = [] },
            isNew: true,
            unit: BlockLengthUnit.Hours,
            onSave: _ => Task.CompletedTask,
            onCancel: () => { });

    // ── Lower bound (0730) ────────────────────────────────────────────────────

    [Theory]
    [InlineData("0000")]   // midnight
    [InlineData("0600")]   // well before 0730
    [InlineData("0729")]   // one minute before the hard lower bound
    public void AddStartTime_RejectsTimeBefore0730(string input)
    {
        var vm = MakeVm(2.0);
        vm.NewStartTime = input;
        vm.AddStartTimeCommand.Execute(null);
        Assert.NotNull(vm.ValidationError);
        Assert.Empty(vm.StartTimeRows);
    }

    [Fact]
    public void AddStartTime_Accepts0730_AsEarliestValidStart()
    {
        var vm = MakeVm(2.0);
        vm.NewStartTime = "0730";
        vm.AddStartTimeCommand.Execute(null);
        Assert.Null(vm.ValidationError);
        Assert.Single(vm.StartTimeRows);
    }

    // ── Upper bound (start + block ≤ 2200) ───────────────────────────────────

    [Theory]
    [InlineData("1900", 4.0)]   // 1900 + 4h = 2300 — over by 1h
    [InlineData("2100", 2.0)]   // 2100 + 2h = 2300 — over by 1h
    [InlineData("2001", 2.0)]   // 2001 + 2h = 2201 — just over
    public void AddStartTime_RejectsStartTimeThatExceedsMaxEnd(string input, double blockHours)
    {
        var vm = MakeVm(blockHours);
        vm.NewStartTime = input;
        vm.AddStartTimeCommand.Execute(null);
        Assert.NotNull(vm.ValidationError);
        Assert.Empty(vm.StartTimeRows);
    }

    [Theory]
    [InlineData("1800", 4.0)]   // 1800 + 4h = 2200 — exactly at the boundary
    [InlineData("2000", 2.0)]   // 2000 + 2h = 2200 — exactly at the boundary
    [InlineData("0800", 1.5)]   // 0800 + 1.5h = 0930 — well within bounds
    public void AddStartTime_AcceptsStartTimeThatEndsExactlyAt2200OrEarlier(string input, double blockHours)
    {
        var vm = MakeVm(blockHours);
        vm.NewStartTime = input;
        vm.AddStartTimeCommand.Execute(null);
        Assert.Null(vm.ValidationError);
        Assert.Single(vm.StartTimeRows);
    }

    // ── Colon format (H:MM / HH:MM) also respects bounds ─────────────────────

    [Fact]
    public void AddStartTime_ColonFormat_RejectsTimeBefore0730()
    {
        var vm = MakeVm(2.0);
        vm.NewStartTime = "7:00";   // 07:00 — before 0730
        vm.AddStartTimeCommand.Execute(null);
        Assert.NotNull(vm.ValidationError);
        Assert.Empty(vm.StartTimeRows);
    }

    [Fact]
    public void AddStartTime_ColonFormat_Accepts0730()
    {
        var vm = MakeVm(2.0);
        vm.NewStartTime = "7:30";
        vm.AddStartTimeCommand.Execute(null);
        Assert.Null(vm.ValidationError);
        Assert.Single(vm.StartTimeRows);
    }
}
