using System;
using System.Linq;
using System.Reflection;

namespace Menace.ModpackLoader;

/// <summary>
/// Reflection helpers for IL2CPP collection wrappers
/// (<c>Il2CppSystem.Collections.Generic.List`1</c>, <c>Il2CppStructArray`1</c>,
/// <c>Il2CppReferenceArray`1</c>) and their BCL equivalents: identify what's inside a
/// collection member at runtime and rebuild it with copied element refs, so a clone
/// owns its own containers. Ported from Jiangyu's Il2CppCollectionReflection (batch
/// and empty-container variants omitted — only the clone path needs these).
/// </summary>
internal static class Il2CppCollectionReflection
{
    /// <summary>
    /// Element type of a <c>List&lt;T&gt;</c> (BCL or IL2CPP). Returns null for any
    /// non-list type.
    /// </summary>
    public static Type GetListElementType(Type collectionType)
    {
        if (collectionType == null) return null;
        if (!collectionType.IsGenericType) return null;
        var def = collectionType.GetGenericTypeDefinition().FullName ?? string.Empty;
        if (def != "Il2CppSystem.Collections.Generic.List`1"
            && def != "System.Collections.Generic.List`1")
            return null;
        return collectionType.GenericTypeArguments.FirstOrDefault();
    }

    /// <summary>
    /// Element type of an IL2CPP array wrapper (<c>Il2CppStructArray&lt;T&gt;</c>,
    /// <c>Il2CppReferenceArray&lt;T&gt;</c>) or a plain managed <c>T[]</c>. Returns null
    /// for non-array types. <c>Il2CppStringArray</c> is non-generic and not handled here.
    /// </summary>
    public static Type GetArrayElementType(Type collectionType)
    {
        if (collectionType == null) return null;
        if (collectionType.IsArray) return collectionType.GetElementType();
        if (!collectionType.IsGenericType) return null;
        var def = collectionType.GetGenericTypeDefinition().FullName ?? string.Empty;
        if (def != "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1"
            && def != "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1")
            return null;
        return collectionType.GenericTypeArguments.FirstOrDefault();
    }

    /// <summary>
    /// Build a fresh <c>List&lt;T&gt;</c> instance with the same element refs as
    /// <paramref name="source"/>. The container changes but the elements stay shared.
    /// </summary>
    public static bool TryRebuildList(
        object source, Type listType, Type elementType, out object fresh, out string error)
    {
        fresh = null;
        error = null;
        if (source == null) { error = "source list is null."; return false; }

        var srcType = source.GetType();
        var countProp = srcType.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        var indexer = srcType.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
        if (countProp == null || indexer == null)
        {
            error = $"source list type {srcType.FullName} missing Count/Item.";
            return false;
        }

        var addMethod = listType.GetMethod(
            "Add", BindingFlags.Instance | BindingFlags.Public,
            binder: null, types: new[] { elementType }, modifiers: null);
        if (addMethod == null)
        {
            error = $"destination list type {listType.FullName} missing Add({elementType.FullName}).";
            return false;
        }

        int count;
        try { count = (int)countProp.GetValue(source); }
        catch (Exception ex) { error = $"Count read threw: {ex.Message}."; return false; }

        object dest;
        try { dest = Activator.CreateInstance(listType); }
        catch (Exception ex) { error = $"Activator.CreateInstance({listType.FullName}) threw: {ex.Message}."; return false; }
        if (dest == null) { error = $"Activator.CreateInstance({listType.FullName}) returned null."; return false; }

        var readArgs = new object[1];
        var addArgs = new object[1];
        for (var i = 0; i < count; i++)
        {
            readArgs[0] = i;
            try
            {
                addArgs[0] = indexer.GetValue(source, readArgs);
                addMethod.Invoke(dest, addArgs);
            }
            catch (Exception ex)
            {
                error = $"copy of element [{i}] threw: {ex.Message}.";
                return false;
            }
        }

        fresh = dest;
        return true;
    }

    /// <summary>
    /// Build a fresh <c>Il2CppReferenceArray&lt;T&gt;</c> (or managed <c>T[]</c>) with
    /// the same element refs as <paramref name="source"/>.
    /// </summary>
    public static bool TryRebuildReferenceArray(
        object source, Type arrayType, Type elementType, out object fresh, out string error)
    {
        fresh = null;
        error = null;
        if (source == null) { error = "source array is null."; return false; }

        var srcType = source.GetType();
        var lengthProp = srcType.GetProperty("Length", BindingFlags.Instance | BindingFlags.Public);
        var indexer = FindIntIndexer(srcType);
        var ctor = arrayType.GetConstructor(new[] { elementType.MakeArrayType() });
        if (lengthProp == null || indexer == null || ctor == null)
        {
            error = $"source array type {srcType.FullName} missing Length/indexer or destination "
                + $"{arrayType.FullName} missing managed-array ctor.";
            return false;
        }

        int length;
        try { length = (int)lengthProp.GetValue(source); }
        catch (Exception ex) { error = $"Length read threw: {ex.Message}."; return false; }

        var managed = Array.CreateInstance(elementType, length);
        var readArgs = new object[1];
        for (var i = 0; i < length; i++)
        {
            readArgs[0] = i;
            try { managed.SetValue(indexer.GetValue(source, readArgs), i); }
            catch (Exception ex)
            {
                error = $"copy of element [{i}] threw: {ex.Message}.";
                return false;
            }
        }

        try { fresh = ctor.Invoke(new object[] { managed }); return true; }
        catch (Exception ex)
        {
            error = $"{arrayType.FullName} ctor(managed[]) threw: {ex.Message}.";
            return false;
        }
    }

    private static PropertyInfo FindIntIndexer(Type t)
    {
        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var idx = p.GetIndexParameters();
            if (idx.Length == 1 && idx[0].ParameterType == typeof(int))
                return p;
        }
        return null;
    }
}
