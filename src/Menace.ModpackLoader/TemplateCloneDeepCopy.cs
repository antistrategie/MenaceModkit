using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using Menace.SDK;
using UnityEngine;
using DataTemplate = Il2CppMenace.Tools.DataTemplate;

namespace Menace.ModpackLoader;

/// <summary>
/// Breaks the sharing <see cref="UnityEngine.Object.Instantiate"/> leaves between a
/// template clone and its source, so patches applied through the clone cannot leak
/// into the source's live data. Ported from Jiangyu's TemplateCloneApplier. Two passes:
/// <list type="bullet">
/// <item><b>Container reallocation</b> — Instantiate on an IL2CPP ScriptableObject keeps
/// the source's <c>List&lt;T&gt;</c>/array INSTANCE shared with the clone; any clear,
/// append or index-set on the clone mutates the source. Every collection-typed member is
/// reseated with a fresh container holding the same element refs (elements stay shared —
/// intentional registry semantics for DataTemplate/PPtr lists).</item>
/// <item><b>Owned-reference deep copy</b> — Instantiate also shallow-copies PPtr element
/// refs, so abstract-polymorphic owned elements (the EventHandlers pattern:
/// <c>List&lt;AbstractBase&gt;</c> whose live concrete elements are unique to the parent)
/// are still shared. Each such element is replaced with its own Instantiated copy.
/// DataTemplate elements (Skills, Items) and concrete wrapper elements stay shared.</item>
/// </list>
/// </summary>
internal static class TemplateCloneDeepCopy
{
    // Per-element-type decision cache: "is this an abstract-polymorphic non-DataTemplate
    // ScriptableObject owned by its parent?" The element-type set is small.
    private static readonly Dictionary<Type, bool> OwnedElementTypeCache = new();

    /// <summary>
    /// Run both passes on a freshly Instantiated clone. <paramref name="concreteType"/>
    /// is the clone's concrete wrapper type (the clone reference itself may be
    /// base-typed, hiding subclass members from reflection).
    /// </summary>
    public static void Run(UnityEngine.Object clone, Type concreteType, string cloneId)
    {
        if (clone is not Il2CppObjectBase il2cppClone)
            return;

        try
        {
            DeepCopyCollectionContainers(il2cppClone, concreteType, cloneId);
            DeepCopyOwnedReferences(il2cppClone, concreteType, cloneId);
        }
        catch (Exception ex)
        {
            SdkLogger.Warning($"  Clone '{cloneId}': deep-copy pass failed: {ex.Message}");
        }
    }

    private static void DeepCopyCollectionContainers(Il2CppObjectBase clone, Type concreteType, string cloneId)
    {
        var reflectionTarget = ReflectionTargetForConcreteType(clone, concreteType, cloneId);
        if (reflectionTarget == null) return;

        var type = reflectionTarget.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.GetIndexParameters().Length != 0) continue;
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (!seen.Add("P:" + prop.Name)) continue;
            TryReallocCollectionContainer(
                () => prop.GetValue(reflectionTarget),
                v => prop.SetValue(reflectionTarget, v),
                prop.PropertyType, prop.Name, cloneId);
        }

        foreach (var field in type.GetFields(flags))
        {
            if (!seen.Add("F:" + field.Name)) continue;
            if (field.IsInitOnly) continue;
            TryReallocCollectionContainer(
                () => field.GetValue(reflectionTarget),
                v => field.SetValue(reflectionTarget, v),
                field.FieldType, field.Name, cloneId);
        }
    }

    private static void TryReallocCollectionContainer(
        Func<object> reader, Action<object> writer, Type memberType, string memberName, string cloneId)
    {
        var listElement = Il2CppCollectionReflection.GetListElementType(memberType);
        if (listElement != null)
        {
            TryRebuildAndWrite(
                reader, writer,
                src => Il2CppCollectionReflection.TryRebuildList(src, memberType, listElement, out var f, out var e)
                    ? (f, (string)null) : ((object)null, e),
                "list", memberName, cloneId);
            return;
        }

        var arrayElement = Il2CppCollectionReflection.GetArrayElementType(memberType);
        if (arrayElement != null)
        {
            TryRebuildAndWrite(
                reader, writer,
                src => Il2CppCollectionReflection.TryRebuildReferenceArray(src, memberType, arrayElement, out var f, out var e)
                    ? (f, (string)null) : ((object)null, e),
                "array", memberName, cloneId);
        }
    }

    // Common read-rebuild-write skeleton; the rebuild step is injected so this serves
    // both list and array variants.
    private static void TryRebuildAndWrite(
        Func<object> reader, Action<object> writer,
        Func<object, (object fresh, string error)> rebuild,
        string kind, string memberName, string cloneId)
    {
        object source;
        try { source = reader(); }
        catch { return; }
        if (source == null) return;

        var (fresh, error) = rebuild(source);
        if (fresh == null)
        {
            if (error != null)
                SdkLogger.Warning($"  Clone '{cloneId}': rebuilding {kind} for '{memberName}' failed: {error}");
            return;
        }

        try { writer(fresh); }
        catch (Exception ex)
        {
            SdkLogger.Warning($"  Clone '{cloneId}': writing fresh {kind} back to '{memberName}' threw: {ex.Message}");
        }
    }

    // The clone reference may be base-typed; re-cast to the concrete wrapper so the
    // subclass's own collection members become visible to GetProperties/GetFields.
    private static object ReflectionTargetForConcreteType(Il2CppObjectBase clone, Type concreteType, string cloneId)
    {
        object target = clone;
        if (concreteType == null || concreteType == clone.GetType()) return target;
        try
        {
            var tryCast = FindTryCast(concreteType);
            if (tryCast == null) return target;
            var cast = tryCast.Invoke(clone, null);
            if (cast != null) target = cast;
        }
        catch (Exception ex)
        {
            SdkLogger.Warning($"  Clone '{cloneId}': TryCast<{concreteType.FullName}> threw: {ex.Message}");
        }
        return target;
    }

    private static void DeepCopyOwnedReferences(Il2CppObjectBase clone, Type concreteType, string cloneId)
    {
        var reflectionTarget = ReflectionTargetForConcreteType(clone, concreteType, cloneId);
        if (reflectionTarget == null) return;

        var type = reflectionTarget.GetType();
        // GetProperties/GetFields without DeclaredOnly already return inherited members
        // on the concrete wrapper; walking the BaseType chain would revisit members.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deepCopied = 0;

        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.GetIndexParameters().Length != 0) continue;
            if (!prop.CanRead) continue;
            if (!seen.Add("P:" + prop.Name)) continue;

            var elementType = Il2CppCollectionReflection.GetListElementType(prop.PropertyType);
            if (elementType == null || !IsOwnedElementType(elementType)) continue;

            deepCopied += DeepCopyListElements(elementType, () => prop.GetValue(reflectionTarget), prop.Name, cloneId);
        }

        foreach (var field in type.GetFields(flags))
        {
            if (!seen.Add("F:" + field.Name)) continue;

            var elementType = Il2CppCollectionReflection.GetListElementType(field.FieldType);
            if (elementType == null || !IsOwnedElementType(elementType)) continue;

            deepCopied += DeepCopyListElements(elementType, () => field.GetValue(reflectionTarget), field.Name, cloneId);
        }

        if (deepCopied > 0)
            SdkLogger.Msg($"    Clone '{cloneId}': deep-copied {deepCopied} owned element(s) so patches don't leak into the source");
    }

    private static int DeepCopyListElements(Type elementType, Func<object> reader, string memberName, string cloneId)
    {
        object listObject;
        try { listObject = reader(); }
        catch { return 0; }
        if (listObject == null) return 0;

        var listType = listObject.GetType();
        var countProp = listType.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        var indexer = listType.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
        if (countProp == null || indexer == null) return 0;

        int count;
        try { count = (int)countProp.GetValue(listObject); }
        catch { return 0; }

        // Indexer.SetValue needs the wrapper type matching the list's declared element;
        // Instantiate hands back a UnityEngine.Object wrapper, so TryCast each copy back.
        var tryCastToElement = FindTryCast(elementType);
        if (tryCastToElement == null)
        {
            SdkLogger.Warning($"  Clone '{cloneId}': TryCast<{elementType.Name}> not found");
            return 0;
        }

        var copied = 0;
        for (var i = 0; i < count; i++)
        {
            object element;
            try { element = indexer.GetValue(listObject, new object[] { i }); }
            catch (Exception ex)
            {
                SdkLogger.Warning($"  Clone '{cloneId}': read of '{memberName}[{i}]' threw: {ex.Message}");
                continue;
            }
            if (element is not Il2CppObjectBase il2cpp) continue;

            // A mod-injected (ClassInjector) element must stay SHARED: Instantiate memcpys
            // the native object including the GC handle linking it to its managed instance,
            // so the copy's finaliser would tear down a handle it doesn't own (boot crash).
            if (IsInjectedManagedType(il2cpp.GetType()))
                continue;

            UnityEngine.Object instance;
            try
            {
                var asUnity = il2cpp.Cast<UnityEngine.Object>();
                instance = UnityEngine.Object.Instantiate(asUnity);
                instance.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            catch (Exception ex)
            {
                SdkLogger.Warning($"  Clone '{cloneId}': deep-copy of '{memberName}[{i}]' threw: {ex.Message}");
                continue;
            }

            object instanceAsElement;
            try { instanceAsElement = tryCastToElement.Invoke(instance, null); }
            catch (Exception ex)
            {
                SdkLogger.Warning($"  Clone '{cloneId}': TryCast<{elementType.Name}> on '{memberName}[{i}]' threw: {(ex.InnerException ?? ex).Message}");
                continue;
            }
            if (instanceAsElement == null) continue;

            try { indexer.SetValue(listObject, instanceAsElement, new object[] { i }); }
            catch (Exception ex)
            {
                SdkLogger.Warning($"  Clone '{cloneId}': write of '{memberName}[{i}]' threw: {ex.Message}");
                continue;
            }

            copied++;
        }

        return copied;
    }

    /// <summary>
    /// Element types treated as "owned by parent" and therefore deep-copied when cloning
    /// the parent: abstract-polymorphic ScriptableObject subclasses that aren't
    /// DataTemplate. The EventHandlers pattern matches (abstract base, many subclasses,
    /// no m_ID); concrete wrappers don't (no subtypes); DataTemplate elements don't
    /// (intentional registry sharing).
    /// </summary>
    private static bool IsOwnedElementType(Type elementType)
        => IsOwnedElementTypeCore(elementType, typeof(DataTemplate), typeof(ScriptableObject));

    // Parameterised core, factored out so tests can pass synthetic base types instead of
    // the real game types — resolving typeof(DataTemplate) at JIT time pulls in
    // Assembly-CSharp's Il2Cpp transitive closure, which only works at game runtime.
    internal static bool IsOwnedElementTypeCore(
        Type elementType, Type dataTemplateBase, Type scriptableObjectBase)
    {
        if (OwnedElementTypeCache.TryGetValue(elementType, out var cached))
            return cached;

        bool decision;
        try
        {
            if (dataTemplateBase != null && dataTemplateBase.IsAssignableFrom(elementType))
                decision = false;
            else if (scriptableObjectBase == null || !scriptableObjectBase.IsAssignableFrom(elementType))
                decision = false;
            else
                decision = HasStrictDescendant(elementType);
        }
        catch
        {
            decision = false;
        }

        OwnedElementTypeCache[elementType] = decision;
        return decision;
    }

    internal static bool HasStrictDescendant(Type baseType)
    {
        Type[] types;
        try { types = baseType.Assembly.GetTypes(); }
        catch { return false; }

        foreach (var t in types)
        {
            if (ReferenceEquals(t, baseType)) continue;
            if (baseType.IsAssignableFrom(t)) return true;
        }
        return false;
    }

    private static bool IsInjectedManagedType(Type type)
    {
        try
        {
            return Il2CppInterop.Runtime.Injection.ClassInjector.IsTypeRegisteredInIl2Cpp(type);
        }
        catch
        {
            return false;
        }
    }

    private static MethodInfo FindTryCast(Type targetType)
    {
        return typeof(Il2CppObjectBase)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "TryCast"
                                 && m.IsGenericMethodDefinition
                                 && m.GetParameters().Length == 0)
            ?.MakeGenericMethod(targetType);
    }
}
