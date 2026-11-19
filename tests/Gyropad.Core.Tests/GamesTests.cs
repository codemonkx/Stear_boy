using Gyropad.Core.Config;
using Gyropad.Core.Titles;

namespace Gyropad.Core.Tests;

public class GamesTests
{
    private sealed class FakeProcesses(params Games.ProcInfo[] items) : Games.IProcessSource
    {
        public IReadOnlyList<Games.ProcInfo> List() => items;
    }

    private static Games.ProcInfo Proc(string name, long rss = 0)
        => new() { Pid = 1, Name = name, Title = name, Rss = rss };

    private static void Use(params Games.ProcInfo[] items)
    {
        Games.ProcessSource = new FakeProcesses(items);
        Games.Bound = null;
        Games.Detect(force: true);
    }

    [Fact]
    public void KnownTableIsIntact()
    {
        Assert.Equal(86, Games.Known.Length);
        Assert.NotNull(Games.ByExe("acs"));
        Assert.NotNull(Games.ByExe("AC2-WIN64-SHIPPING"));
        Assert.Null(Games.ByExe("definitely-not-a-game"));
        Assert.Null(Games.ByExe(""));
    }

    [Fact]
    public void DetectsExactTitleCaseInsensitively()
    {
        Use(Proc("explorer"), Proc("ACS"));

        Assert.Equal("Assetto Corsa", Games.Current.Name);
        Assert.False(Games.Current.NeedsPad);
        Assert.Equal("acs", Games.MatchedExe);
        Assert.Equal("", Games.UnknownExe);
    }

    [Fact]
    public void DetectsLongExecutableNames()
    {
        Use(Proc("ac2-win64-shipping"));

        Assert.Equal("Assetto Corsa Competizione", Games.Current.Name);
    }

    [Fact]
    public void PrefixMatchHandlesVaryingExecutables()
    {
        Use(Proc("Car Parking Multiplayer 2"));

        Assert.Equal("Car Parking Multiplayer 2", Games.Current.Name);
        Assert.True(Games.Current.WheelNeedsMapping);
        Assert.NotNull(Games.Current.PadFallback);
        Assert.True(Games.Current.PadFallback!.NeedsPad);
    }

    [Fact]
    public void StoredBindingBeatsTheBuiltInTable()
    {
        Use(Proc("acs"));

        var store = new Store();
        store.Bindings.Add(new Binding { Exe = "acs", Device = Binding.DevPad });
        Games.Bound = store;
        Games.Detect(force: true);

        Assert.Equal("Assetto Corsa", Games.Current.Name);
        Assert.True(Games.Current.NeedsPad);
        Assert.Equal("acs", Games.Current.Exe);
        Assert.False(Games.Current.WheelUntested);
    }

    [Fact]
    public void ForcedWheelBindingClearsNeedsPad()
    {
        Use(Proc("artofrally"));

        var store = new Store();
        store.Bindings.Add(new Binding { Exe = "artofrally", Device = Binding.DevVjoy });
        Games.Bound = store;
        Games.Detect(force: true);

        Assert.False(Games.Current.NeedsPad);
        Assert.Equal("art of rally", Games.Current.Name);
    }

    [Fact]
    public void UnknownProcessFallsBackToGenericAndListsCandidates()
    {
        Use(Proc("explorer", 900), Proc("notagame", 100), Proc("other", 200));

        Assert.Same(Games.Generic, Games.Current);
        Assert.Equal("", Games.RunningName);
        Assert.Equal("other", Games.UnknownExe);

        Games.Candidate[] candidates = Games.Candidates;
        Assert.Equal(["other", "notagame"], candidates.Select(c => c.Exe));
    }

    [Fact]
    public void ShellProcessesAreNeverCandidates()
    {
        Use(Proc("steam", 500), Proc("discord", 400), Proc("minecraft", 300));

        Assert.Equal(["minecraft"], Games.Candidates.Select(c => c.Exe));
        Assert.Equal("minecraft", Games.UnknownExe);
    }

    [Fact]
    public void ShellProcessesAreFilteredRegardlessOfCase()
    {
        // /proc preserves the casing of argv[0], so a shell executable can show
        // up as "Steam" even though the ignore list is lowercase.
        Use(Proc("Steam", 500), Proc("Discord", 400), Proc("MineCraft", 300));

        Assert.Equal(["MineCraft"], Games.Candidates.Select(c => c.Exe));
        Assert.Equal("MineCraft", Games.UnknownExe);
    }

    [Fact]
    public void CandidatesAreRequestedWhenAskedEvenWithAMatch()
    {
        Games.ProcessSource = new FakeProcesses(Proc("acs", 400), Proc("someapp", 200));
        Games.Bound = null;
        Games.Detect(force: true, wantCandidates: true);

        Assert.Equal("Assetto Corsa", Games.Current.Name);
        Assert.Equal(["acs", "someapp"], Games.Candidates.Select(c => c.Exe));
        Assert.Equal("", Games.UnknownExe);
    }
}
