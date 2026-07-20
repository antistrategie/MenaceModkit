using System;
using System.Linq;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Menace.SDK;
using DataTemplate = Il2CppMenace.Tools.DataTemplate;
using DataTemplateLoader = Il2CppMenace.Tools.DataTemplateLoader;
using Il2CppTemplateMap = Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppMenace.Tools.DataTemplate>;

namespace Menace.ModpackLoader;

/// <summary>
/// Registers runtime template clones into the game's <c>DataTemplateLoader</c> so they
/// behave as first-class templates: <c>Get&lt;T&gt;(id)</c>/<c>TryGet&lt;T&gt;(id)</c> resolve them
/// and <c>GetAll&lt;T&gt;()</c> enumerations include them.
///
/// Technique ported from Jiangyu's <c>TemplateCloneApplier</c>:
/// <list type="bullet">
/// <item>insert the clone into <c>m_TemplateMaps[type][cloneId]</c> through the typed
/// interop dictionary — the previous reflection approach probed <c>GetField</c>, but
/// Il2CppInterop surfaces native fields as PROPERTIES on proxy types, so it never found
/// the map and silently failed;</item>
/// <item>extend the <c>m_TemplateArrays[type]</c> bucket (native array realloc) so
/// <c>GetAll&lt;T&gt;()</c> consumers see the clone;</item>
/// <item>mirror both into every ancestor slot up to <c>DataTemplate</c>,
/// force-materialising each slot via <c>GetAll&lt;Ancestor&gt;()</c> first. Both stores are
/// keyed by EXACT runtime type and gameplay code typically enumerates by a base type
/// (e.g. the black-market pool via <c>GetAll&lt;BaseItemTemplate&gt;()</c>); an
/// unmaterialised ancestor slot would cache a clone-free snapshot and save/load would
/// fail on the missing clone key.</item>
/// </list>
/// </summary>
internal static class TemplateRegistration
{
    private static readonly Type[] IntPtrCtorSignature = { typeof(IntPtr) };

    private static readonly MethodInfo GetAllDefinition = typeof(DataTemplateLoader)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .FirstOrDefault(m => m.Name == "GetAll"
                             && m.IsGenericMethodDefinition
                             && m.GetParameters().Length == 0);

    /// <summary>
    /// Register <paramref name="clone"/> under <paramref name="cloneId"/> in the slot for
    /// <paramref name="templateType"/> and every DataTemplate ancestor slot. Returns false
    /// for non-DataTemplate ScriptableObjects (no registry slot exists — by-name Resources
    /// lookup is their only resolver) and when the loader's maps are unavailable.
    /// </summary>
    public static bool RegisterClone(UnityEngine.Object clone, Type templateType, string cloneId)
    {
        var dataTemplate = (clone as Il2CppObjectBase)?.TryCast<DataTemplate>();
        if (dataTemplate == null)
            return false;

        if (!TryGetTemplateMap(templateType, out var map))
            return false;

        if (!map.ContainsKey(cloneId))
            RegisterIntoSlot(templateType, map, templateType, cloneId, dataTemplate);

        MirrorToAncestors(templateType, cloneId, dataTemplate);
        return true;
    }

    private static bool TryGetTemplateMap(Type templateType, out Il2CppTemplateMap map)
    {
        map = null;
        if (templateType == null)
            return false;

        var singleton = DataTemplateLoader.GetSingleton();
        var templateMaps = singleton?.m_TemplateMaps;
        if (templateMaps == null)
            return false;

        var il2cppType = Il2CppType.From(templateType);
        if (il2cppType == null)
            return false;

        return templateMaps.TryGetValue(il2cppType, out map) && map != null;
    }

    /// <summary>
    /// Inserts the clone into the slot's <c>m_TemplateMaps</c> inner dict and extends the
    /// matching <c>m_TemplateArrays</c> bucket. Array-extend failure is non-fatal:
    /// <c>Get&lt;T&gt;(id)</c> still resolves via the map; only <c>GetAll&lt;slotType&gt;</c>
    /// consumers miss the clone. Caller gates idempotency — registering the same id twice
    /// would double-extend the array.
    /// </summary>
    private static void RegisterIntoSlot(
        Type slotType, Il2CppTemplateMap slotMap, Type declaredType, string cloneId, DataTemplate clone)
    {
        slotMap[cloneId] = clone;

        if (!TryExtendTemplateArray(slotType, clone))
        {
            SdkLogger.Warning(
                $"  Clone '{declaredType.Name}:{cloneId}': failed to extend m_TemplateArrays[{slotType.Name}]; " +
                $"GetAll<{slotType.Name}> consumers won't see this clone (see earlier warnings for cause)");
        }
    }

    /// <summary>
    /// Mirrors the clone into every ancestor <c>m_TemplateMaps</c>/<c>m_TemplateArrays</c>
    /// slot, walking <c>BaseType</c> upward to <c>DataTemplate</c>. The most-derived slot
    /// must already be registered by the caller. Each ancestor slot is force-materialised
    /// (via <c>GetAll&lt;Ancestor&gt;()</c>) before mirroring so lazy-snapshot consumers see
    /// the clone in their first enumeration. Idempotent per ancestor via ContainsKey.
    /// </summary>
    private static void MirrorToAncestors(Type resolvedType, string cloneId, DataTemplate clone)
    {
        var current = resolvedType.BaseType;
        while (current != null && typeof(DataTemplate).IsAssignableFrom(current))
        {
            EnsureSlotMaterialised(current);

            if (TryGetTemplateMap(current, out var ancestorMap) && !ancestorMap.ContainsKey(cloneId))
                RegisterIntoSlot(current, ancestorMap, resolvedType, cloneId, clone);

            current = current.BaseType;
        }
    }

    private static void EnsureSlotMaterialised(Type templateType)
    {
        try
        {
            GetAllDefinition?.MakeGenericMethod(templateType).Invoke(null, null);
        }
        catch
        {
            // Slot stays unmaterialised; the mirror loop simply skips it.
        }
    }

    /// <summary>
    /// Rebuilds the <c>m_TemplateArrays</c> bucket for <paramref name="slotType"/> as a
    /// native IL2CPP array one element longer, with the clone appended. The bucket's value
    /// type is a game-generic interop array, so access goes through reflection; the array
    /// itself is allocated natively (<c>il2cpp_array_new</c>) and wrapped.
    /// </summary>
    private static bool TryExtendTemplateArray(Type slotType, DataTemplate clone)
    {
        try
        {
            var singleton = DataTemplateLoader.GetSingleton();
            if (singleton == null)
                return false;

            var arraysProperty = typeof(DataTemplateLoader).GetProperty(
                "m_TemplateArrays", BindingFlags.Public | BindingFlags.Instance);
            if (arraysProperty == null)
            {
                SdkLogger.Warning("  Array extend: DataTemplateLoader.m_TemplateArrays property not found");
                return false;
            }

            var arrays = arraysProperty.GetValue(singleton);
            if (arrays == null)
                return false;

            var arraysType = arrays.GetType();
            var il2cppType = Il2CppType.From(slotType);
            if (il2cppType == null)
                return false;

            var tryGetValue = FindTryGetValue(arraysType);
            if (tryGetValue == null)
            {
                SdkLogger.Warning($"  Array extend: TryGetValue not found on {arraysType.FullName}");
                return false;
            }

            var lookup = new object[] { il2cppType, null };
            if (!(bool)tryGetValue.Invoke(arrays, lookup) || lookup[1] is not Il2CppObjectBase oldArray)
                return false;

            var oldArrayType = oldArray.GetType();
            var lengthProperty = oldArrayType.GetProperty("Length") ?? oldArrayType.GetProperty("Count");
            if (lengthProperty == null)
            {
                SdkLogger.Warning($"  Array extend: no Length/Count on {oldArrayType.FullName}");
                return false;
            }

            var oldLength = (int)lengthProperty.GetValue(oldArray)!;
            if (oldArray.Pointer == IntPtr.Zero)
                return false;

            var arrayClass = IL2CPP.il2cpp_object_get_class(oldArray.Pointer);
            var elementClass = IL2CPP.il2cpp_class_get_element_class(arrayClass);
            if (elementClass == IntPtr.Zero)
            {
                SdkLogger.Warning($"  Array extend: no element class for {oldArrayType.FullName}");
                return false;
            }

            var newNativeArray = IL2CPP.il2cpp_array_new(elementClass, (ulong)(oldLength + 1));
            if (newNativeArray == IntPtr.Zero)
            {
                SdkLogger.Warning($"  Array extend: il2cpp_array_new failed for {oldArrayType.FullName}");
                return false;
            }

            var wrapperCtor = oldArrayType.GetConstructor(IntPtrCtorSignature);
            if (wrapperCtor == null)
            {
                SdkLogger.Warning($"  Array extend: {oldArrayType.FullName} has no (IntPtr) ctor");
                return false;
            }
            var newArray = wrapperCtor.Invoke(new object[] { newNativeArray });

            var indexer = FindIntIndexer(oldArrayType);
            if (indexer == null)
            {
                SdkLogger.Warning($"  Array extend: no int indexer on {oldArrayType.FullName}");
                return false;
            }

            var slot = new object[1];
            for (var i = 0; i < oldLength; i++)
            {
                slot[0] = i;
                indexer.SetValue(newArray, indexer.GetValue(oldArray, slot), slot);
            }
            slot[0] = oldLength;
            indexer.SetValue(newArray, clone, slot);

            var dictIndexer = FindIndexerByKeyType(arraysType, il2cppType.GetType());
            if (dictIndexer == null)
            {
                SdkLogger.Warning($"  Array extend: no type-keyed indexer on {arraysType.FullName}");
                return false;
            }
            dictIndexer.SetValue(arrays, newArray, new object[] { il2cppType });

            return true;
        }
        catch (Exception ex)
        {
            SdkLogger.Warning($"  Array extend failed for {slotType.Name}: {ex.InnerException?.Message ?? ex.Message}");
            return false;
        }
    }

    private static MethodInfo FindTryGetValue(Type dictType)
    {
        foreach (var method in dictType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.Name != "TryGetValue")
                continue;
            var parameters = method.GetParameters();
            if (parameters.Length == 2 && parameters[1].ParameterType.IsByRef)
                return method;
        }
        return null;
    }

    private static PropertyInfo FindIntIndexer(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var parameters = property.GetIndexParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                return property;
        }
        return null;
    }

    private static PropertyInfo FindIndexerByKeyType(Type dictType, Type keyType)
    {
        foreach (var property in dictType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var parameters = property.GetIndexParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(keyType))
                return property;
        }
        return null;
    }
}
