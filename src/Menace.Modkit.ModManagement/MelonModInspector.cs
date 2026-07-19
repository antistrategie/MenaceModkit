using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Menace.Modkit.ModManagement;

/// <summary>
/// Reads mod metadata from a .NET assembly on disk <em>without loading it</em>, using
/// <see cref="System.Reflection.Metadata"/>. Used to give raw MelonLoader / Jiangyu mods
/// (which have no <c>modpack.json</c>) real names/versions/authors and to classify them.
///
/// Loading assemblies to read attributes would run module initializers, lock the file,
/// and risk pulling in dependencies that are not present — hence the metadata-only read.
/// </summary>
public static class MelonModInspector
{
    // Assembly-level attributes MelonLoader uses to declare a mod/plugin.
    private static readonly string[] MelonInfoAttributeNames =
    {
        "MelonInfoAttribute",
        "MelonModInfoAttribute",
        "MelonPluginInfoAttribute",
    };

    /// <summary>
    /// Simple assembly-name substrings that mark a mod as targeting the Jiangyu SDK/loader.
    /// Heuristic (matched case-insensitively); confirm against a real Jiangyu mod's references.
    /// </summary>
    public static IReadOnlyList<string> JiangyuMarkers { get; set; } = new[] { "Jiangyu" };

    /// <summary>
    /// Inspect a DLL. Returns null if the file is not a readable managed assembly
    /// (e.g. a native DLL), in which case callers should treat it as an opaque binary.
    /// </summary>
    public static MelonModInfo? Inspect(string dllPath)
    {
        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return null;

            var reader = peReader.GetMetadataReader();
            if (!reader.IsAssembly)
                return null;

            var referenced = reader.AssemblyReferences
                .Select(h => reader.GetString(reader.GetAssemblyReference(h).Name))
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var (name, version, author, hasMelonInfo) = ReadMelonInfo(reader);

            var referencesMelon = referenced.Any(
                r => r.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase));

            var isJiangyu = referenced.Any(
                r => JiangyuMarkers.Any(m => r.Contains(m, StringComparison.OrdinalIgnoreCase)));

            return new MelonModInfo
            {
                Name = name,
                Version = version,
                Author = author,
                HasMelonInfo = hasMelonInfo,
                ReferencesMelonLoader = referencesMelon,
                IsJiangyu = isJiangyu,
                ReferencedAssemblies = referenced,
            };
        }
        catch (Exception)
        {
            // Unreadable / corrupt / not a PE file — caller treats as opaque.
            return null;
        }
    }

    private static (string? Name, string? Version, string? Author, bool Found) ReadMelonInfo(MetadataReader reader)
    {
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attr = reader.GetCustomAttribute(handle);
            var typeName = GetAttributeTypeName(reader, attr);
            if (typeName == null)
                continue;

            if (!MelonInfoAttributeNames.Any(n => typeName.EndsWith(n, StringComparison.Ordinal)))
                continue;

            CustomAttributeValue<string> value;
            try
            {
                value = attr.DecodeValue(StringAttributeTypeProvider.Instance);
            }
            catch
            {
                continue;
            }

            // MelonInfo(Type type, string name, string version, string author, ...)
            var args = value.FixedArguments;
            string? At(int i) => i < args.Length ? args[i].Value as string : null;

            return (At(1), At(2), At(3), true);
        }

        return (null, null, null, false);
    }

    private static string? GetAttributeTypeName(MetadataReader reader, CustomAttribute attr)
    {
        switch (attr.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var mref = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                return mref.Parent.Kind switch
                {
                    HandleKind.TypeReference => TypeRefName(reader, (TypeReferenceHandle)mref.Parent),
                    HandleKind.TypeDefinition => TypeDefName(reader, (TypeDefinitionHandle)mref.Parent),
                    _ => null,
                };

            case HandleKind.MethodDefinition:
                var mdef = reader.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor);
                return TypeDefName(reader, mdef.GetDeclaringType());

            default:
                return null;
        }
    }

    private static string TypeRefName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var tr = reader.GetTypeReference(handle);
        return Combine(reader.GetString(tr.Namespace), reader.GetString(tr.Name));
    }

    private static string TypeDefName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var td = reader.GetTypeDefinition(handle);
        return Combine(reader.GetString(td.Namespace), reader.GetString(td.Name));
    }

    private static string Combine(string ns, string name) =>
        string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

    /// <summary>
    /// Minimal type provider for decoding custom-attribute values as strings. Attribute
    /// arguments here are strings and a <see cref="Type"/>; we only need their textual form.
    /// </summary>
    private sealed class StringAttributeTypeProvider : ICustomAttributeTypeProvider<string>
    {
        public static readonly StringAttributeTypeProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSystemType() => "System.Type";
        public bool IsSystemType(string type) => type == "System.Type";
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromSerializedName(string name) => name;

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => TypeDefName(reader, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => TypeRefName(reader, handle);

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
    }
}
