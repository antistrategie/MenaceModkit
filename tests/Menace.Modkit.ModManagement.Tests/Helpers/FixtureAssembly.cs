using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Menace.Modkit.ModManagement.Tests.Helpers;

/// <summary>
/// Compiles tiny throwaway assemblies to a temp directory so inspector tests have
/// real DLLs to read — no committed binaries, no game dependency.
/// </summary>
internal static class FixtureAssembly
{
    private static readonly IReadOnlyList<MetadataReference> CoreRefs = BuildCoreRefs();

    private static IReadOnlyList<MetadataReference> BuildCoreRefs()
    {
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? string.Empty;
        var wanted = new[] { "System.Private.CoreLib.dll", "System.Runtime.dll", "netstandard.dll" };
        return tpa.Split(Path.PathSeparator)
            .Where(p => wanted.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }

    /// <summary>
    /// Compile <paramref name="source"/> into <paramref name="outputDir"/>/<paramref name="assemblyName"/>.dll.
    /// Optional <paramref name="extraReferences"/> lets a fixture reference another emitted fixture
    /// (e.g. a dummy "Jiangyu" assembly). Returns the output path; throws on compile error.
    /// </summary>
    public static string Emit(
        string outputDir,
        string assemblyName,
        string source,
        IEnumerable<string>? extraReferences = null)
    {
        Directory.CreateDirectory(outputDir);

        var refs = CoreRefs.ToList();
        if (extraReferences != null)
            refs.AddRange(extraReferences.Select(p => MetadataReference.CreateFromFile(p)));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var outputPath = Path.Combine(outputDir, assemblyName + ".dll");
        var result = compilation.Emit(outputPath);
        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"Fixture '{assemblyName}' failed to compile:\n{errors}");
        }

        return outputPath;
    }
}
