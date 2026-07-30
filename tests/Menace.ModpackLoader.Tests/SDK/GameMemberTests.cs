using System;
using Menace.SDK;
using Xunit;

namespace Menace.ModpackLoader.Tests.SDK;

/// <summary>
/// Covers the shape-based binding the SDK uses for game members. The fixtures below mirror the
/// real shapes that broke: OwnedItems.AddItem gained a third flag, and ItemContainer exposes both
/// a convenient Add and an index-taking Place.
/// </summary>
public class GameMemberTests
{
    [Fact]
    public void FindMethod_IgnoresOverloadWhoseLeadingParameterDiffers()
    {
        // FakeOwnedItems has AddItem(template, bool, bool) and a reward-table overload starting
        // with an int, exactly like the game. Only the template-first one may match.
        var method = GameMember.FindMethod(typeof(FakeOwnedItems), "AddItem", typeof(FakeTemplate));

        Assert.NotNull(method);
        Assert.Equal(3, method.GetParameters().Length);
        Assert.Equal(typeof(FakeTemplate), method.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void Invoke_DefaultsTrailingFlags()
    {
        var target = new FakeOwnedItems();
        var method = GameMember.FindMethod(typeof(FakeOwnedItems), "AddItem", typeof(FakeTemplate));

        var result = GameMember.Invoke(method, target, new FakeTemplate());

        Assert.NotNull(result);
        Assert.False(target.ShowDialog);
        Assert.False(target.ShowSlot);
    }

    [Fact]
    public void Invoke_PassesExplicitArgsAheadOfDefaults()
    {
        var target = new FakeContainer();
        var method = GameMember.FindMethod(typeof(FakeContainer), "Add", typeof(FakeItem));

        var result = GameMember.Invoke(method, target, new FakeItem(), true);

        Assert.Equal(true, result);
        Assert.True(target.AddedSlotWhenNeeded);
    }

    [Fact]
    public void FindMethod_PrefersFewestExtraParameters()
    {
        var method = GameMember.FindMethod(typeof(FakeContainer), "Store", typeof(FakeItem));

        Assert.NotNull(method);
        Assert.Single(method.GetParameters());
    }

    [Fact]
    public void FindMethod_RejectsExtraParameterThatCannotBeDefaulted()
    {
        // Passing null for a game object would turn a signature change into a null dereference
        // inside the game, so a reference-typed extra is not something to guess at.
        Assert.Null(GameMember.FindMethod(typeof(FakeContainer), "Attach", typeof(FakeItem)));
    }

    [Fact]
    public void FindMethod_AcceptsDerivedArgumentForBaseParameter()
    {
        var method = GameMember.FindMethod(typeof(FakeContainer), "Add", typeof(FakeDerivedItem));

        Assert.NotNull(method);
    }

    [Fact]
    public void FindMethod_DefaultsEnumParameterToZero()
    {
        var target = new FakeContainer();
        var method = GameMember.FindMethod(typeof(FakeContainer), "Place", typeof(FakeItem));

        Assert.NotNull(method);
        GameMember.Invoke(method, target, new FakeItem());

        Assert.Equal(0, target.PlacedIndex);
        Assert.Equal(FakeItemFlags.None, target.PlacedFlags);
    }

    [Fact]
    public void FindMethodWithArity_MatchesOnParameterCountAlone()
    {
        var method = GameMember.FindMethodWithArity(typeof(FakeContainer), "Attach", 2);

        Assert.NotNull(method);
        Assert.Equal(2, method.GetParameters().Length);
    }

    [Fact]
    public void FindMethod_UnknownNameOrNullType_ReturnsNull()
    {
        Assert.Null(GameMember.FindMethod(typeof(FakeContainer), "NoSuchMethod"));
        Assert.Null(GameMember.FindMethod(null, "Add"));
        Assert.Null(GameMember.FindMethod(typeof(FakeContainer), ""));
    }

    [Fact]
    public void BuildArgs_FillsEveryParameter()
    {
        var method = GameMember.FindMethodWithArity(typeof(FakeContainer), "Place", 3);

        var args = GameMember.BuildArgs(method, new FakeItem());

        Assert.Equal(3, args.Length);
        Assert.Equal(0, args[1]);
        Assert.Equal(FakeItemFlags.None, args[2]);
    }
}

// --- Fixtures: shapes that mirror the game's own ---

public enum FakeItemFlags
{
    None = 0,
    Silent = 1
}

public class FakeItem { }

public sealed class FakeDerivedItem : FakeItem { }

public sealed class FakeTemplate { }

public sealed class FakeOwnedItems
{
    public bool ShowDialog;
    public bool ShowSlot;

    public FakeItem AddItem(FakeTemplate template, bool showDialog, bool showItemSlotInDialog)
    {
        ShowDialog = showDialog;
        ShowSlot = showItemSlotInDialog;
        return new FakeItem();
    }

    // The reward-table overload: same name, different leading parameter.
    public FakeItem AddItem(int addItemType, FakeTemplate fixedItem, object[] randomItems,
        object rewardTable, float rarityMult, bool showDialog, bool showItemSlotInDialog)
        => new();
}

public sealed class FakeContainer
{
    public bool AddedSlotWhenNeeded;
    public int PlacedIndex = -1;
    public FakeItemFlags PlacedFlags = FakeItemFlags.Silent;

    public bool Add(FakeItem item, bool addSlotWhenNeeded)
    {
        AddedSlotWhenNeeded = addSlotWhenNeeded;
        return true;
    }

    public bool Place(FakeItem item, int index, FakeItemFlags flags)
    {
        PlacedIndex = index;
        PlacedFlags = flags;
        return true;
    }

    public void Store(FakeItem item) { }

    public void Store(FakeItem item, bool alsoEquip) { }

    // Extra parameter is a game object, which cannot be defaulted safely.
    public void Attach(FakeItem item, FakeContainer owner) { }
}
