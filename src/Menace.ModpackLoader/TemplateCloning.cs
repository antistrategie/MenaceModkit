using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Menace.SDK;
using UnityEngine;

namespace Menace.ModpackLoader;

/// <summary>
/// Template cloning: deep-copies existing game templates (ScriptableObjects) via
/// UnityEngine.Object.Instantiate() and registers them in the DataTemplateLoader
/// registry so the game treats them as first-class templates.
/// </summary>
public partial class ModpackLoaderMod
{
    // Set to true to disable runtime cloning fallback (relies on native assets only)
    // This is used to verify that native asset creation is working correctly.
    private const bool DISABLE_RUNTIME_CLONING = false;

    // Tracks which modpack+templateType clone sets have been applied
    private readonly HashSet<string> _appliedCloneKeys = new();

    /// <summary>
    /// Process all clone definitions in a modpack. Returns true if all types were found.
    /// </summary>
#pragma warning disable CS0162 // Unreachable code (DISABLE_RUNTIME_CLONING is intentionally true)
    private bool ApplyClones(Modpack modpack)
    {
        if (DISABLE_RUNTIME_CLONING)
        {
            SdkLogger.Msg($"[TemplateCloning] Runtime cloning DISABLED - relying on native assets only");
            return true;
        }

        if (modpack.Clones == null || modpack.Clones.Count == 0)
            return true;

        var gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");

        if (gameAssembly == null)
        {
            SdkLogger.Error("Assembly-CSharp not found, cannot apply clones");
            return false;
        }

        var allFound = true;

        foreach (var (templateTypeName, cloneMap) in modpack.Clones)
        {
            var cloneKey = $"{modpack.Name}:clones:{templateTypeName}";
            if (_appliedCloneKeys.Contains(cloneKey))
                continue;

            if (cloneMap == null || cloneMap.Count == 0)
                continue;

            try
            {
                var templateType = gameAssembly.GetTypes()
                    .FirstOrDefault(t => t.Name == templateTypeName && !t.IsAbstract);

                if (templateType == null)
                {
                    SdkLogger.Warning($"  Clone: template type '{templateTypeName}' not found");
                    allFound = false;
                    continue;
                }

                // Ensure templates are loaded by calling GetAll<T>() on the game's DataTemplateLoader
                EnsureTemplatesLoaded(gameAssembly, templateType);

                // Find all existing instances of this type
                var il2cppType = Il2CppType.From(templateType);
                var objects = Resources.FindObjectsOfTypeAll(il2cppType);

                if (objects == null || objects.Length == 0)
                {
                    SdkLogger.Warning($"  Clone: no {templateTypeName} instances found — will retry on next scene");
                    allFound = false;
                    continue;
                }

                // Build name → object lookup
                var lookup = new Dictionary<string, UnityEngine.Object>();
                foreach (var obj in objects)
                {
                    if (obj != null && !string.IsNullOrEmpty(obj.name))
                        lookup[obj.name] = obj;
                }

                int clonedCount = 0;
                var allApplied = true;
                foreach (var (newName, sourceName) in cloneMap)
                {
                    // Skip if a template with this name already exists (already cloned or native)
                    if (lookup.ContainsKey(newName))
                    {
                        SdkLogger.Msg($"  Clone: '{newName}' already exists, skipping");
                        clonedCount++;
                        continue;
                    }

                    if (!lookup.TryGetValue(sourceName, out var source))
                    {
                        SdkLogger.Warning($"  Clone: source '{sourceName}' not found for clone '{newName}' — will retry on next scene");
                        allApplied = false;
                        continue;
                    }

                    try
                    {
                        // Deep-copy via Instantiate — copies all serialized fields
                        var clone = UnityEngine.Object.Instantiate(source);
                        clone.name = newName;
                        clone.hideFlags = HideFlags.DontUnloadUnusedAsset;

                        // Set m_ID on the DataTemplate base class via IL2CPP field write
                        SetTemplateId(clone, newName);

                        // Instantiate shallow-copies collection containers and owned
                        // sub-objects; break the sharing so patches applied through the
                        // clone can't leak into the source template's live data.
                        TemplateCloneDeepCopy.Run(clone, templateType, newName);

                        // Register in DataTemplateLoader's internal dictionaries
                        RegisterInLoader(clone, templateType, newName);

                        // Add to our local lookup so subsequent clones can reference this one
                        lookup[newName] = clone;

                        // Verify registration was successful by trying to look it up
                        var verifyLookup = Resources.FindObjectsOfTypeAll(il2cppType);
                        var verified = verifyLookup?.Any(o => o.name == newName) ?? false;
                        var verifyStatus = verified ? "verified in Resources" : "NOT in Resources (may still work via DataTemplateLoader)";

                        SdkLogger.Msg($"  Cloned: {sourceName} -> {newName} ({verifyStatus})");
                        clonedCount++;
                    }
                    catch (Exception ex)
                    {
                        SdkLogger.Error($"  Clone failed: {sourceName} -> {newName}: {ex.Message}");
                        allApplied = false;
                    }
                }

                if (clonedCount > 0)
                    SdkLogger.Msg($"  Applied {clonedCount} clone(s) for {templateTypeName}");

                // Only mark the whole type applied when every entry cloned (or already
                // existed) — a partial success must stay retryable on later scenes.
                // The already-exists gate above keeps retries from re-cloning winners.
                if (allApplied)
                    _appliedCloneKeys.Add(cloneKey);
                else
                    allFound = false;
            }
            catch (Exception ex)
            {
                SdkLogger.Error($"  Failed to process clones for {templateTypeName}: {ex.Message}");
            }
        }

        return allFound;
    }
#pragma warning restore CS0162

    /// <summary>
    /// Call DataTemplateLoader.GetAll&lt;T&gt;() to ensure the type's templates are loaded
    /// into the internal registry before we try to register clones.
    /// </summary>
    private void EnsureTemplatesLoaded(Assembly gameAssembly, Type templateType)
    {
        try
        {
            var loaderType = gameAssembly.GetTypes()
                .FirstOrDefault(t => t.Name == "DataTemplateLoader");

            if (loaderType == null)
            {
                SdkLogger.Warning("  DataTemplateLoader class not found in Assembly-CSharp");
                return;
            }

            var getAllMethod = loaderType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetAll" && m.IsGenericMethodDefinition);

            if (getAllMethod == null)
            {
                SdkLogger.Warning("  DataTemplateLoader.GetAll method not found");
                return;
            }

            var genericMethod = getAllMethod.MakeGenericMethod(templateType);
            genericMethod.Invoke(null, null);
        }
        catch (Exception ex)
        {
            SdkLogger.Warning($"  EnsureTemplatesLoaded({templateType.Name}): {ex.Message}");
        }
    }

    /// <summary>
    /// Write the m_ID field on a DataTemplate-derived ScriptableObject via IL2CPP field offset.
    /// m_ID is not serialized by Instantiate (it's decorated with [NonSerialized] or similar),
    /// so we must set it manually.
    /// </summary>
    private void SetTemplateId(UnityEngine.Object clone, string id)
    {
        try
        {
            if (clone is not Il2CppObjectBase il2cppObj)
                return;

            IntPtr objectPointer = il2cppObj.Pointer;
            if (objectPointer == IntPtr.Zero)
                return;

            IntPtr klass = IL2CPP.il2cpp_object_get_class(objectPointer);
            if (klass == IntPtr.Zero)
                return;

            // Walk the class hierarchy to find m_ID (defined on DataTemplate base class)
            IntPtr idField = FindField(klass, "m_ID");
            if (idField == IntPtr.Zero)
            {
                SdkLogger.Warning($"  SetTemplateId: m_ID field not found on {clone.name}");
                return;
            }

            uint offset = IL2CPP.il2cpp_field_get_offset(idField);
            if (offset == 0)
            {
                SdkLogger.Warning($"  SetTemplateId: m_ID offset is 0 for {clone.name}");
                return;
            }

            // Write the IL2CPP string pointer at the field offset
            IntPtr il2cppString = IL2CPP.ManagedStringToIl2Cpp(id);
            Marshal.WriteIntPtr(objectPointer + (int)offset, il2cppString);
        }
        catch (Exception ex)
        {
            SdkLogger.Error($"  SetTemplateId failed for {clone.name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Walk class hierarchy to find a field by name.
    /// Same pattern used in DataExtractor and CombinedArms.
    /// </summary>
    private static IntPtr FindField(IntPtr klass, string fieldName)
    {
        IntPtr searchKlass = klass;
        while (searchKlass != IntPtr.Zero)
        {
            IntPtr field = IL2CPP.il2cpp_class_get_field_from_name(searchKlass, fieldName);
            if (field != IntPtr.Zero)
                return field;
            searchKlass = IL2CPP.il2cpp_class_get_parent(searchKlass);
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Register a cloned template in DataTemplateLoader's registry (template maps +
    /// arrays, including every ancestor slot) so game systems resolve it like a native
    /// template. See <see cref="TemplateRegistration"/> for the mechanism.
    /// </summary>
    private void RegisterInLoader(UnityEngine.Object clone, Type templateType, string name)
    {
        try
        {
            if (TemplateRegistration.RegisterClone(clone, templateType, name))
            {
                SdkLogger.Msg($"    Registered '{name}' in DataTemplateLoader (incl. ancestor slots)");
            }
            else
            {
                SdkLogger.Warning($"  RegisterInLoader: '{name}' not registered in DataTemplateLoader — " +
                    "non-DataTemplate type or template map unavailable; clone resolves by name only");
            }
        }
        catch (Exception ex)
        {
            SdkLogger.Warning($"  RegisterInLoader failed for '{name}': {ex.Message}");
        }
    }
}
