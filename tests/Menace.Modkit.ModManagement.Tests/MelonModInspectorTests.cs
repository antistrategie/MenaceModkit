using System.IO;
using Menace.Modkit.ModManagement.Tests.Helpers;
using Xunit;

namespace Menace.Modkit.ModManagement.Tests;

/// <summary>
/// Verifies static [MelonInfo] reading and Jiangyu/MelonLoader classification against
/// real (throwaway) assemblies emitted at test time.
/// </summary>
public sealed class MelonModInspectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mmtests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Inspect_MelonInfoAttribute_ReadsNameVersionAuthor()
    {
        // Assembly attributes must precede type declarations (CS1730), so order matters.
        var source = @"
using System;
[assembly: MelonLoader.MelonInfoAttribute(typeof(MyMod.Entry), ""My Cool Mod"", ""2.3.4"", ""Jane Modder"")]
namespace MelonLoader {
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class MelonInfoAttribute : Attribute {
        public MelonInfoAttribute(Type type, string name, string version, string author) { }
    }
}
namespace MyMod { public class Entry { } }";

        var dll = FixtureAssembly.Emit(_dir, "CoolMod", source);

        var info = MelonModInspector.Inspect(dll);

        Assert.NotNull(info);
        Assert.True(info!.HasMelonInfo);
        Assert.True(info.IsMelonMod);
        Assert.Equal("My Cool Mod", info.Name);
        Assert.Equal("2.3.4", info.Version);
        Assert.Equal("Jane Modder", info.Author);
    }

    [Fact]
    public void Inspect_NoMelonInfo_ReportsAbsent()
    {
        var dll = FixtureAssembly.Emit(_dir, "PlainLib", "namespace Plain { public class Thing { } }");

        var info = MelonModInspector.Inspect(dll);

        Assert.NotNull(info);
        Assert.False(info!.HasMelonInfo);
        Assert.Null(info.Name);
    }

    [Fact]
    public void Inspect_JiangyuReference_FlagsJiangyu()
    {
        // Emit a dummy assembly named "Jiangyu", then a mod that references it.
        var jiangyu = FixtureAssembly.Emit(_dir, "Jiangyu", "namespace Jiangyu { public class Sdk { } }");

        var modSource = @"
namespace JMod { public class Uses { public static object R() => new Jiangyu.Sdk(); } }";
        var dll = FixtureAssembly.Emit(_dir, "JiangyuMod", modSource, new[] { jiangyu });

        var info = MelonModInspector.Inspect(dll);

        Assert.NotNull(info);
        Assert.True(info!.IsJiangyu);
        Assert.Contains("Jiangyu", info.ReferencedAssemblies);
    }

    [Fact]
    public void Inspect_NonAssemblyFile_ReturnsNull()
    {
        Directory.CreateDirectory(_dir);
        var junk = Path.Combine(_dir, "notreal.dll");
        File.WriteAllText(junk, "this is not a PE file");

        Assert.Null(MelonModInspector.Inspect(junk));
    }
}
