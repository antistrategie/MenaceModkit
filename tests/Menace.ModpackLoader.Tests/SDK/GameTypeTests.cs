using System;
using Menace.SDK;
using Xunit;

namespace Menace.ModpackLoader.Tests.SDK;

public class GameTypeTests
{
    [Fact]
    public void Find_EmptyName_ReturnsInvalid()
    {
        var result = GameType.Find("");

        Assert.False(result.IsValid);
        Assert.Equal(IntPtr.Zero, result.ClassPointer);
    }

    [Fact]
    public void Find_NullName_ReturnsInvalid()
    {
        var result = GameType.Find(null);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Find_UnresolvableName_ReturnsInvalid()
    {
        // All IL2CPP stubs return IntPtr.Zero, so no type can be resolved
        var result = GameType.Find("NonExistent.Type.That.Does.Not.Exist");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void FromPointer_Zero_ReturnsInvalid()
    {
        var result = GameType.FromPointer(IntPtr.Zero);

        Assert.False(result.IsValid);
        Assert.Same(GameType.Invalid, result);
    }

    [Fact]
    public void Invalid_IsNotValid()
    {
        Assert.False(GameType.Invalid.IsValid);
        Assert.Equal(IntPtr.Zero, GameType.Invalid.ClassPointer);
        Assert.Equal("", GameType.Invalid.FullName);
    }

    [Fact]
    public void ToString_Invalid_ShowsMessage()
    {
        Assert.Equal("<invalid GameType>", GameType.Invalid.ToString());
    }

    [Fact]
    public void FindManagedProxy_ResolvesGameNameToPrefixedProxy()
    {
        // Callers pass the game's own name; the proxy lives under the Il2Cpp-prefixed
        // namespace. Matching the name as written finds nothing.
        var resolved = GameType.FindManagedProxy("Menace.Testing.FakeGameType");

        Assert.Same(typeof(Il2CppMenace.Testing.FakeGameType), resolved);
    }

    [Fact]
    public void FindManagedProxy_AcceptsAlreadyPrefixedName()
    {
        var resolved = GameType.FindManagedProxy("Il2CppMenace.Testing.FakeGameType");

        Assert.Same(typeof(Il2CppMenace.Testing.FakeGameType), resolved);
    }

    [Fact]
    public void FindManagedProxy_ShortName_NeedsTheGameAssembly()
    {
        // Namespace-less names only resolve through the short-name scan of Assembly-CSharp,
        // which isn't loaded outside the game — so this is null here despite the fixture.
        Assert.Null(GameType.FindManagedProxy("FakeGameType"));
    }

    [Fact]
    public void FindManagedProxy_UnknownOrEmptyName_ReturnsNull()
    {
        Assert.Null(GameType.FindManagedProxy("Menace.Testing.NoSuchType"));
        Assert.Null(GameType.FindManagedProxy(""));
        Assert.Null(GameType.FindManagedProxy(null));
    }
}
