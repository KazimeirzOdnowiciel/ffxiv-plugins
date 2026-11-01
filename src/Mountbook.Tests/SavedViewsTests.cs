using System;
using System.Collections.Generic;
using System.Linq;
using Mountbook.Services;
using Xunit;

namespace Mountbook.Tests;

/// <summary>
/// The saved views' rules: what "save the current filter" captures, which preset the current filter *is*, and
/// what a rename, a delete and the migration sweep are allowed to do.
/// </summary>
/// <remarks>
/// The list is the player's own work - a name they typed over five settings - so the rules that matter are the
/// ones that refuse to lose or invent something: a rename onto a name already taken is refused, a duplicate in a
/// hand-edited file drops the later entry rather than being renamed, and an entry that cannot be drawn (no name)
/// is dropped instead of rendering as a blank chip.
/// </remarks>
public sealed class SavedViewsTests
{
    [Fact]
    public void ANameIsFoldedAndCutOnceOnTheWayIn()
    {
        Assert.Equal("Farmable now", SavedViewRules.Clean("  Farmable   now  "));
        Assert.Equal(string.Empty, SavedViewRules.Clean(null));
        Assert.Equal(string.Empty, SavedViewRules.Clean("   "));

        // A chip is drawn with the name in it, so a sentence is cut rather than pushing every other chip off
        // the row - and cut on the way in, so the file and the chip cannot disagree.
        string longName = new('x', SavedViewRules.MaxNameLength + 20);

        Assert.Equal(SavedViewRules.MaxNameLength, SavedViewRules.Clean(longName).Length);
    }

    [Fact]
    public void CapturingCopiesAllFiveAxesAndTheName()
    {
        SavedView view = SavedViewRules.Capture(
            "Farmable now",
            "tradeable:y owned:n",
            MountOwnership.Unowned,
            MountExpansion.Dawntrail,
            MountOrder.CommonestFirst,
            MountGrouping.Source);

        Assert.Equal("Farmable now", view.Name);
        Assert.Equal("tradeable:y owned:n", view.Query);
        Assert.Equal(MountOwnership.Unowned, view.Ownership);
        Assert.Equal(MountExpansion.Dawntrail, view.Expansion);
        Assert.Equal(MountOrder.CommonestFirst, view.Order);
        Assert.Equal(MountGrouping.Source, view.GroupBy);
    }

    [Fact]
    public void ACapturedViewIsTheFilterItWasCapturedFrom()
    {
        SavedView view = SavedViewRules.Capture(
            "PvP only",
            "source:pvp",
            MountOwnership.Unowned,
            MountExpansion.All,
            MountOrder.Name,
            MountGrouping.None);

        Assert.True(SavedViewRules.Matches(view, "source:pvp", MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None));

        // Every axis counts: a comparison on the query alone would light the chip up while the list it
        // describes was ordered, filtered or grouped another way.
        Assert.False(SavedViewRules.Matches(view, "source:pvp", MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None));
        Assert.False(SavedViewRules.Matches(view, "source:pvp", MountOwnership.Unowned, MountExpansion.Stormblood, MountOrder.Name, MountGrouping.None));
        Assert.False(SavedViewRules.Matches(view, "source:pvp", MountOwnership.Unowned, MountExpansion.All, MountOrder.RarestFirst, MountGrouping.None));
        Assert.False(SavedViewRules.Matches(view, "source:pvp", MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.Patch));

        // The query is compared ordinally: the field holds the grammar's own characters, and two different
        // queries must never read as the same filter.
        Assert.False(SavedViewRules.Matches(view, "source:PvP", MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None));
        Assert.False(SavedViewRules.Matches(view, string.Empty, MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None));
    }

    [Fact]
    public void TheMatchIndexFindsTheAppliedPresetAndNothingElse()
    {
        var views = new List<SavedView>
        {
            SavedViewRules.Capture("Everything", string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None),
            SavedViewRules.Capture("PvP only", "source:pvp", MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None),
        };

        Assert.Equal(1, SavedViewRules.MatchIndex(views, "source:pvp", MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None));
        Assert.Equal(0, SavedViewRules.MatchIndex(views, string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None));

        // A filter that is not any of them draws no chip as applied, which is the honest answer after one
        // keystroke in the search box.
        Assert.Equal(-1, SavedViewRules.MatchIndex(views, "kamu", MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None));
        Assert.Equal(-1, SavedViewRules.MatchIndex([], string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None));
        Assert.Equal(-1, SavedViewRules.MatchIndex(null, string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None));
    }

    [Fact]
    public void SavingUnderAnExistingNameUpdatesThatChipInPlace()
    {
        var views = new List<SavedView>
        {
            SavedViewRules.Capture("Farmable now", "trial", MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None),
            SavedViewRules.Capture("Event mounts", "event", MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None),
        };

        SavedView updated = SavedViewRules.Capture("farmable now", "source:trial", MountOwnership.Owned, MountExpansion.Dawntrail, MountOrder.RarestFirst, MountGrouping.Patch);

        Assert.True(SavedViewRules.TrySave(views, updated, out int index));

        // The chip keeps its corner - a row that reordered itself under the pointer is one the player has to
        // re-read - and the name keeps the spelling it was saved with.
        Assert.Equal(0, index);
        Assert.Equal(2, views.Count);
        Assert.Equal("Farmable now", views[0].Name);
        Assert.Equal("source:trial", views[0].Query);
        Assert.Equal(MountOwnership.Owned, views[0].Ownership);
        Assert.Equal(MountExpansion.Dawntrail, views[0].Expansion);
        Assert.Equal(MountOrder.RarestFirst, views[0].Order);
        Assert.Equal(MountGrouping.Patch, views[0].GroupBy);
    }

    [Fact]
    public void ANewNameIsAppendedAndABlankOneIsRefused()
    {
        List<SavedView> views = [];

        Assert.True(SavedViewRules.TrySave(views, SavedViewRules.Capture("First", "a", MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None), out int first));
        Assert.True(SavedViewRules.TrySave(views, SavedViewRules.Capture("Second", "b", MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None), out int second));

        Assert.Equal(0, first);
        Assert.Equal(1, second);

        Assert.False(SavedViewRules.TrySave(views, SavedViewRules.Capture("   ", "c", MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None), out int blank));
        Assert.Equal(-1, blank);
        Assert.Equal(2, views.Count);
    }

    [Fact]
    public void TheListStopsAtTheCapAndKeepsItsFirstEntries()
    {
        List<SavedView> views = [];

        for (var index = 0; index < SavedViewRules.MaxViews; index++)
        {
            Assert.True(SavedViewRules.TrySave(views, SavedViewRules.Capture($"View {index}", string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None), out _));
        }

        Assert.False(SavedViewRules.TrySave(views, SavedViewRules.Capture("One too many", string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None), out _));
        Assert.Equal(SavedViewRules.MaxViews, views.Count);

        // A full list still accepts an update: the cap is on how many presets exist, not on whether one can be
        // changed.
        Assert.True(SavedViewRules.TrySave(views, SavedViewRules.Capture("View 3", "changed", MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None), out int updated));
        Assert.Equal(3, updated);
        Assert.Equal("changed", views[3].Query);
    }

    [Fact]
    public void RenamingKeepsThePositionAndItsFiveAxes()
    {
        var views = new List<SavedView>
        {
            SavedViewRules.Capture("Farmable now", "trial", MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None),
            SavedViewRules.Capture("Event mounts", "event", MountOwnership.All, MountExpansion.All, MountOrder.RarestFirst, MountGrouping.Patch),
        };

        Assert.Equal(SavedViewRules.RenameResult.Renamed, SavedViewRules.Rename(views, 1, " Seasonal "));

        Assert.Equal("Farmable now", views[0].Name);
        Assert.Equal("Seasonal", views[1].Name);
        Assert.Equal("event", views[1].Query);
        Assert.Equal(MountOrder.RarestFirst, views[1].Order);
        Assert.Equal(MountGrouping.Patch, views[1].GroupBy);
    }

    [Fact]
    public void ANameAnotherChipAlreadyReadsIsRefused()
    {
        var views = new List<SavedView>
        {
            SavedViewRules.Capture("Farmable now", "trial", MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None),
            SavedViewRules.Capture("Event mounts", "event", MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None),
        };

        // Two chips reading the same word is indistinguishable on the row, so it is refused with the reason the
        // window turns into a toast rather than being made unique behind the player's back.
        Assert.Equal(SavedViewRules.RenameResult.NameTaken, SavedViewRules.Rename(views, 1, "farmable  NOW"));
        Assert.Equal("Event mounts", views[1].Name);

        // Renaming a chip to the name it already has is not a collision with itself.
        Assert.Equal(SavedViewRules.RenameResult.Renamed, SavedViewRules.Rename(views, 1, "Event mounts"));
    }

    [Fact]
    public void RenamingRefusesABlankNameAndAnIndexOffTheList()
    {
        var views = new List<SavedView>
        {
            SavedViewRules.Capture("Farmable now", "trial", MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None),
        };

        Assert.Equal(SavedViewRules.RenameResult.Refused, SavedViewRules.Rename(views, 0, "  "));
        Assert.Equal(SavedViewRules.RenameResult.Refused, SavedViewRules.Rename(views, 1, "Somewhere else"));
        Assert.Equal(SavedViewRules.RenameResult.Refused, SavedViewRules.Rename(views, -1, "Somewhere else"));
        Assert.Equal("Farmable now", views[0].Name);
    }

    [Fact]
    public void DeletingTheLastViewLeavesAnEmptyList()
    {
        var views = new List<SavedView>
        {
            SavedViewRules.Capture("Farmable now", "trial", MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None),
        };

        Assert.True(SavedViewRules.Delete(views, 0));
        Assert.Empty(views);

        // The row draws nothing once the list is empty, so the delete has to actually shorten it.
        Assert.False(SavedViewRules.Delete(views, 0));
    }

    [Fact]
    public void TheSweepDropsWhatCannotBeDrawnAndClampsWhatCanBe()
    {
        var views = new List<SavedView>
        {
            SavedViewRules.Capture("Farmable now", "trial", MountOwnership.Unowned, MountExpansion.All, MountOrder.Name, MountGrouping.None),
            null!,
            SavedViewRules.Capture("   ", "blank name", MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None),
            SavedViewRules.Capture("farmable now", "duplicate name", MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None),
            new SavedView
            {
                Name = "  broken  ",
                Query = null!,
                Ownership = (MountOwnership)99,
                Expansion = (MountExpansion)42,
                Order = (MountOrder)77,
                GroupBy = (MountGrouping)(-1),
            },
        };

        SavedViewRules.Normalise(views);

        // Kept, in order: the first name wins, the blank and the duplicate are gone, and the null entry never
        // reached a member read (this runs from the plugin's constructor, so it must not throw).
        Assert.Equal(2, views.Count);
        Assert.Equal("Farmable now", views[0].Name);
        Assert.Equal("trial", views[0].Query);
        Assert.Equal("broken", views[1].Name);
        Assert.Equal(string.Empty, views[1].Query);
        Assert.Equal(MountOwnership.All, views[1].Ownership);
        Assert.Equal(MountExpansion.All, views[1].Expansion);
        Assert.Equal(MountOrder.Name, views[1].Order);
        Assert.Equal(MountGrouping.None, views[1].GroupBy);
    }

    [Fact]
    public void TheSweepKeepsTheFirstEntriesUpToTheCap()
    {
        var views = new List<SavedView>();

        for (var index = 0; index < SavedViewRules.MaxViews + 4; index++)
        {
            views.Add(SavedViewRules.Capture($"View {index}", string.Empty, MountOwnership.All, MountExpansion.All, MountOrder.Name, MountGrouping.None));
        }

        SavedViewRules.Normalise(views);

        Assert.Equal(SavedViewRules.MaxViews, views.Count);
        Assert.Equal(
            new[] { "View 0", "View 1", "View 2", "View 3", "View 4", "View 5", "View 6", "View 7" },
            views.Select(view => view.Name).ToArray());
    }

    [Fact]
    public void AnEmptyOrNullListIsLeftAlone()
    {
        var empty = new List<SavedView>();

        SavedViewRules.Normalise(empty);
        SavedViewRules.Normalise(null!);

        Assert.Empty(empty);
    }
    [Fact]
    public void TheShippedViewIsTheFarmableQueryOverTheUnownedList()
    {
        SavedView view = SavedViewRules.FarmableView();

        Assert.Equal("Farmable now", view.Name);
        Assert.Equal("avail:available", view.Query);
        Assert.Equal(MountOwnership.Unowned, view.Ownership);
        Assert.Equal(MountExpansion.All, view.Expansion);
        Assert.Equal(MountOrder.Name, view.Order);
        Assert.Equal(MountGrouping.None, view.GroupBy);

        // The name is folded like a typed one, so a player who saves over it finds the same chip rather than a
        // second one differing by whitespace.
        Assert.Equal(view.Name, SavedViewRules.Clean(view.Name));
    }

}