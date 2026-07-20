using System;
using System.Collections.Generic;
using Menace.ModpackLoader;
using Xunit;

namespace Menace.ModpackLoader.Tests;

/// <summary>
/// Tests for the runtime template-cloning helpers. The IL2CPP registration itself needs
/// the live game (verified empirically 2026-07-20: clone present in
/// DataTemplateLoader.m_TemplateMaps incl. ancestor slots, containers independent,
/// survived save/load) — these cover the pure structural logic that decides WHAT gets
/// deep-copied and HOW containers are rebuilt, which is what determines correctness when
/// MENACE adds new template shapes.
/// </summary>
public sealed class Il2CppCollectionReflectionTests
{
    [Fact]
    public void GetListElementType_BclList_ReturnsElement()
    {
        Assert.Equal(typeof(string), Il2CppCollectionReflection.GetListElementType(typeof(List<string>)));
        Assert.Equal(typeof(int), Il2CppCollectionReflection.GetListElementType(typeof(List<int>)));
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(int[]))]
    [InlineData(typeof(Dictionary<string, int>))]
    [InlineData(typeof(HashSet<string>))]
    public void GetListElementType_NonList_ReturnsNull(Type type)
    {
        Assert.Null(Il2CppCollectionReflection.GetListElementType(type));
    }

    [Fact]
    public void GetArrayElementType_ManagedArray_ReturnsElement()
    {
        Assert.Equal(typeof(string), Il2CppCollectionReflection.GetArrayElementType(typeof(string[])));
    }

    [Theory]
    [InlineData(typeof(List<string>))]
    [InlineData(typeof(string))]
    public void GetArrayElementType_NonArray_ReturnsNull(Type type)
    {
        Assert.Null(Il2CppCollectionReflection.GetArrayElementType(type));
    }

    [Fact]
    public void TryRebuildList_ProducesIndependentContainerWithSharedElements()
    {
        var source = new List<string> { "a", "b", "c" };

        var ok = Il2CppCollectionReflection.TryRebuildList(
            source, typeof(List<string>), typeof(string), out var fresh, out var error);

        Assert.True(ok, error);
        var rebuilt = Assert.IsType<List<string>>(fresh);
        Assert.NotSame(source, rebuilt);          // independent container
        Assert.Equal(source, rebuilt);            // same elements
        rebuilt.Add("d");                         // mutating the copy…
        Assert.Equal(3, source.Count);            // …never touches the source
    }

    [Fact]
    public void TryRebuildList_EmptySource_ProducesEmptyIndependentList()
    {
        var source = new List<int>();

        var ok = Il2CppCollectionReflection.TryRebuildList(
            source, typeof(List<int>), typeof(int), out var fresh, out var error);

        Assert.True(ok, error);
        Assert.NotSame(source, fresh);
        Assert.Empty((List<int>)fresh);
    }

    [Fact]
    public void TryRebuildList_NullSource_Fails()
    {
        var ok = Il2CppCollectionReflection.TryRebuildList(
            null, typeof(List<int>), typeof(int), out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryRebuildReferenceArray_ManagedArray_ProducesIndependentCopyWithSharedElements()
    {
        // Managed T[] has no (T[]) wrapper ctor and no Item indexer property, so it must
        // take the direct-copy branch rather than the IL2CPP wrapper path.
        var source = new[] { "a", "b", "c" };

        var ok = Il2CppCollectionReflection.TryRebuildReferenceArray(
            source, typeof(string[]), typeof(string), out var fresh, out var error);

        Assert.True(ok, error);
        var rebuilt = Assert.IsType<string[]>(fresh);
        Assert.NotSame(source, rebuilt);          // independent container
        Assert.Equal(source, rebuilt);            // same elements
        rebuilt[0] = "changed";                   // mutating the copy…
        Assert.Equal("a", source[0]);             // …never touches the source
    }

    [Fact]
    public void TryRebuildReferenceArray_EmptyManagedArray_Succeeds()
    {
        var ok = Il2CppCollectionReflection.TryRebuildReferenceArray(
            Array.Empty<int>(), typeof(int[]), typeof(int), out var fresh, out var error);

        Assert.True(ok, error);
        Assert.Empty((int[])fresh);
    }

    [Fact]
    public void TryRebuildReferenceArray_NullSource_Fails()
    {
        var ok = Il2CppCollectionReflection.TryRebuildReferenceArray(
            null, typeof(string[]), typeof(string), out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}

public sealed class OwnedElementTypeTests
{
    // Synthetic hierarchy standing in for the game's types. The production overload binds
    // the real DataTemplate/ScriptableObject; tests pass these instead so the JIT never
    // touches a game-type token.
    private abstract class FakeScriptableObject { }
    private class FakeDataTemplate : FakeScriptableObject { }
    private class FakeWeaponTemplate : FakeDataTemplate { }

    // The "owned" pattern: abstract base with concrete descendants (EventHandlers shape).
    private abstract class FakeHandlerBase : FakeScriptableObject { }
    private sealed class FakeHandlerA : FakeHandlerBase { }

    // Concrete wrapper with no subtypes (SkillGroup shape).
    private sealed class FakeConcreteWrapper : FakeScriptableObject { }

    // Not a ScriptableObject at all.
    private sealed class FakePlainClass { }

    private static bool Decide(Type element) =>
        TemplateCloneDeepCopy.IsOwnedElementTypeCore(
            element, typeof(FakeDataTemplate), typeof(FakeScriptableObject));

    [Fact]
    public void AbstractPolymorphicScriptableObject_IsOwned()
    {
        // FakeHandlerBase has a strict descendant (FakeHandlerA) → owned → deep-copied.
        Assert.True(Decide(typeof(FakeHandlerBase)));
    }

    [Fact]
    public void DataTemplateElement_IsNotOwned()
    {
        // DataTemplate elements are registry references — sharing is intentional.
        Assert.False(Decide(typeof(FakeWeaponTemplate)));
        Assert.False(Decide(typeof(FakeDataTemplate)));
    }

    [Fact]
    public void ConcreteWrapperWithoutSubtypes_IsNotOwned()
    {
        Assert.False(Decide(typeof(FakeConcreteWrapper)));
    }

    [Fact]
    public void NonScriptableObject_IsNotOwned()
    {
        Assert.False(Decide(typeof(FakePlainClass)));
    }

    [Fact]
    public void HasStrictDescendant_DetectsSubclassAndExcludesSelf()
    {
        Assert.True(TemplateCloneDeepCopy.HasStrictDescendant(typeof(FakeHandlerBase)));
        Assert.False(TemplateCloneDeepCopy.HasStrictDescendant(typeof(FakeHandlerA)));
    }
}
