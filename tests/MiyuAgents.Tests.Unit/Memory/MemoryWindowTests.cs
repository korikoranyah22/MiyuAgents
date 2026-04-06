using FluentAssertions;
using MiyuAgents.Memory;
using Xunit;

namespace MiyuAgents.Tests.Unit.Memory;

// ── UpdateWith adds new entries ───────────────────────────────────────────────

public class MemoryWindow_UpdateWith_AddsNewEntries
{
    private readonly MemoryWindow<string> _window = new(defaultTurns: 3);

    [Fact]
    public void ActiveEntries_ContainsAddedItems()
    {
        _window.UpdateWith([("id-1", "alpha"), ("id-2", "beta")]);

        _window.ActiveEntries.Should().Contain("alpha").And.Contain("beta");
    }

    [Fact]
    public void UpdateWith_ReturnsCorrectNewCount()
    {
        var (newCount, refreshed) = _window.UpdateWith([("id-1", "a"), ("id-2", "b")]);

        newCount.Should().Be(2);
        refreshed.Should().Be(0);
    }

    [Fact]
    public void Contains_ReturnsTrueForAddedId()
    {
        _window.UpdateWith([("id-x", "value")]);
        _window.Contains("id-x").Should().BeTrue();
    }
}

// ── UpdateWith reconsolidates (resets counter) ───────────────────────────────

public class MemoryWindow_UpdateWith_Reconsolidates
{
    [Fact]
    public void RetrievingExistingEntry_IncrementsRefreshedCount()
    {
        var window = new MemoryWindow<string>(defaultTurns: 3);
        window.UpdateWith([("id-1", "alpha")]);

        // Second update with same id = reconsolidation
        var (newCount, refreshed) = window.UpdateWith([("id-1", "alpha-updated")]);

        newCount.Should().Be(0);
        refreshed.Should().Be(1);
    }

    [Fact]
    public void ReconsolidatedEntry_SurvivesAdditionalDecayCycles()
    {
        var window = new MemoryWindow<string>(defaultTurns: 2);
        window.UpdateWith([("id-1", "memory")]);

        window.ApplyDecay(); // turns remaining: 1
        window.UpdateWith([("id-1", "memory")]); // reconsolidate → resets to 2

        window.ApplyDecay(); // turns remaining: 1
        window.ApplyDecay(); // turns remaining: 0 → expires

        window.Contains("id-1").Should().BeFalse();
    }
}

// ── ApplyDecay removes expired entries ────────────────────────────────────────

public class MemoryWindow_ApplyDecay_RemovesExpiredEntries
{
    [Fact]
    public void EntryExpires_AfterDefaultTurns()
    {
        var window = new MemoryWindow<string>(defaultTurns: 2);
        window.UpdateWith([("id-1", "test")]);

        window.ApplyDecay(); // turns remaining: 1
        window.ActiveEntries.Should().Contain("test");

        window.ApplyDecay(); // turns remaining: 0 → evicted
        window.ActiveEntries.Should().BeEmpty();
    }

    [Fact]
    public void ApplyDecay_ReturnsExpiredCount()
    {
        var window = new MemoryWindow<string>(defaultTurns: 1);
        window.UpdateWith([("a", "1"), ("b", "2"), ("c", "3")]);

        var expired = window.ApplyDecay();

        expired.Should().Be(3);
        window.ActiveEntries.Should().BeEmpty();
    }

    [Fact]
    public void ApplyDecay_OnEmptyWindow_ReturnsZero()
    {
        var window = new MemoryWindow<string>();
        window.ApplyDecay().Should().Be(0);
    }

    [Fact]
    public void ApplyDecay_OnlyRemovesExpired_LeavesOthers()
    {
        var window = new MemoryWindow<string>(defaultTurns: 3);
        window.UpdateWith([("short", "s")]); // 3 turns

        window.ApplyDecay();                 // short: 2 turns left

        var window2 = new MemoryWindow<string>(defaultTurns: 1);
        window2.UpdateWith([("quick", "q"), ("slow", "s2")]);
        window2.UpdateWith([]);              // slow was added again at turn 1
        // after 1 decay, quick expires

        var expired = window2.ApplyDecay();
        expired.Should().Be(2);
    }
}

// ── Theory: decay + reconsolidation scenarios ────────────────────────────────

public class MemoryWindow_DecayScenarios
{
    // (defaultTurns, decayRoundsWithoutRetrieve, expectRemaining)
    public static TheoryData<int, int, bool> Scenarios => new()
    {
        { 3, 2, true  },  // 3 turns, 2 decays → still alive
        { 3, 3, false },  // 3 turns, 3 decays → expired
        { 1, 1, false },  // 1 turn, 1 decay → expired immediately
        { 5, 4, true  },  // 5 turns, 4 decays → still alive
    };

    [Theory, MemberData(nameof(Scenarios))]
    public void Entry_ExpiresOrSurvives_AsExpected(int defaultTurns, int decays, bool expectRemaining)
    {
        var window = new MemoryWindow<string>(defaultTurns);
        window.UpdateWith([("id-1", "value")]);

        for (int i = 0; i < decays; i++)
            window.ApplyDecay();

        window.Contains("id-1").Should().Be(expectRemaining);
    }
}

// ── ActiveEntries reflects current state ─────────────────────────────────────

public class MemoryWindow_ActiveEntries_ReflectsCurrentState
{
    [Fact]
    public void ActiveEntries_IsEmpty_Initially()
    {
        var window = new MemoryWindow<int>();
        window.ActiveEntries.Should().BeEmpty();
    }

    [Fact]
    public void ActiveEntries_Updates_AfterAddAndDecay()
    {
        var window = new MemoryWindow<string>(defaultTurns: 2);
        window.UpdateWith([("a", "alpha"), ("b", "beta")]);
        window.ActiveEntries.Should().HaveCount(2);

        window.ApplyDecay();
        window.ApplyDecay(); // both expire

        window.ActiveEntries.Should().BeEmpty();
    }
}
