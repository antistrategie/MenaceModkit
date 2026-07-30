using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Menace.SDK;

/// <summary>
/// Result of applying a PatchSet. Contains success/failure counts
/// and details about any failed patches.
/// </summary>
public class PatchResult
{
    /// <summary>Number of patches that were successfully applied.</summary>
    public int SuccessCount { get; internal set; }

    /// <summary>Number of patches that failed to apply.</summary>
    public int FailureCount { get; internal set; }

    /// <summary>Total number of patches attempted.</summary>
    public int TotalCount => SuccessCount + FailureCount;

    /// <summary>True if all patches were applied successfully.</summary>
    public bool AllSucceeded => FailureCount == 0;

    /// <summary>List of failed patch descriptions for diagnostics.</summary>
    public List<string> FailedPatches { get; } = new();

    internal void RecordSuccess()
    {
        SuccessCount++;
    }

    internal void RecordFailure(string description)
    {
        FailureCount++;
        FailedPatches.Add(description);
    }
}

/// <summary>
/// Fluent builder for batching multiple Harmony patches together.
/// Reduces boilerplate and provides consistent error handling.
///
/// Usage:
/// <code>
/// var result = new PatchSet(harmony, "MyMod")
///     .Postfix&lt;Entity&gt;("GetCurrentProperties", MyPostfix)
///     .Prefix&lt;EntityProperties&gt;("GetConcealment", MyPrefix)
///     .PrefixPostfix&lt;UnitPanel&gt;("Update",
///         new[] { typeof(BaseUnitLeader) },
///         MyPrefix, MyPostfix,
///         optional: true)
///     .Apply();
/// </code>
/// </summary>
public class PatchSet
{
    private readonly HarmonyLib.Harmony _harmony;
    private readonly string _modName;
    private readonly List<PatchEntry> _entries = new();

    /// <summary>
    /// Create a new PatchSet for the specified Harmony instance.
    /// </summary>
    /// <param name="harmony">The Harmony instance to use for patching.</param>
    /// <param name="modName">Name of the mod (used for error logging).</param>
    public PatchSet(HarmonyLib.Harmony harmony, string modName)
    {
        _harmony = harmony ?? throw new ArgumentNullException(nameof(harmony));
        _modName = modName ?? "Unknown";
    }

    #region Prefix Methods

    /// <summary>
    /// Add a Prefix patch to the set.
    /// </summary>
    /// <typeparam name="T">The type containing the target method.</typeparam>
    /// <param name="methodName">Name of the method to patch.</param>
    /// <param name="patch">Delegate pointing to the patch method.</param>
    /// <param name="optional">If true, log warning instead of error when method not found.</param>
    public PatchSet Prefix<T>(string methodName, Delegate patch, bool optional = false)
    {
        return Prefix(typeof(T), methodName, null, patch, optional);
    }

    /// <summary>
    /// Add a Prefix patch to the set with parameter types for overload resolution.
    /// </summary>
    /// <typeparam name="T">The type containing the target method.</typeparam>
    /// <param name="methodName">Name of the method to patch.</param>
    /// <param name="parameterTypes">Parameter types for overload resolution.</param>
    /// <param name="patch">Delegate pointing to the patch method.</param>
    /// <param name="optional">If true, log warning instead of error when method not found.</param>
    public PatchSet Prefix<T>(string methodName, Type[] parameterTypes, Delegate patch, bool optional = false)
    {
        return Prefix(typeof(T), methodName, parameterTypes, patch, optional);
    }

    /// <summary>
    /// Add a Prefix patch to the set using a Type instance.
    /// </summary>
    public PatchSet Prefix(Type targetType, string methodName, Type[] parameterTypes, Delegate patch, bool optional = false)
    {
        _entries.Add(new PatchEntry
        {
            TargetType = targetType,
            MethodName = methodName,
            ParameterTypes = parameterTypes,
            Prefix = patch?.Method,
            Optional = optional
        });
        return this;
    }

    #endregion

    #region Postfix Methods

    /// <summary>
    /// Add a Postfix patch to the set.
    /// </summary>
    /// <typeparam name="T">The type containing the target method.</typeparam>
    /// <param name="methodName">Name of the method to patch.</param>
    /// <param name="patch">Delegate pointing to the patch method.</param>
    /// <param name="optional">If true, log warning instead of error when method not found.</param>
    public PatchSet Postfix<T>(string methodName, Delegate patch, bool optional = false)
    {
        return Postfix(typeof(T), methodName, null, patch, optional);
    }

    /// <summary>
    /// Add a Postfix patch to the set with parameter types for overload resolution.
    /// </summary>
    /// <typeparam name="T">The type containing the target method.</typeparam>
    /// <param name="methodName">Name of the method to patch.</param>
    /// <param name="parameterTypes">Parameter types for overload resolution.</param>
    /// <param name="patch">Delegate pointing to the patch method.</param>
    /// <param name="optional">If true, log warning instead of error when method not found.</param>
    public PatchSet Postfix<T>(string methodName, Type[] parameterTypes, Delegate patch, bool optional = false)
    {
        return Postfix(typeof(T), methodName, parameterTypes, patch, optional);
    }

    /// <summary>
    /// Add a Postfix patch to the set using a Type instance.
    /// </summary>
    public PatchSet Postfix(Type targetType, string methodName, Type[] parameterTypes, Delegate patch, bool optional = false)
    {
        _entries.Add(new PatchEntry
        {
            TargetType = targetType,
            MethodName = methodName,
            ParameterTypes = parameterTypes,
            Postfix = patch?.Method,
            Optional = optional
        });
        return this;
    }

    #endregion

    #region Combined Prefix + Postfix

    /// <summary>
    /// Add both Prefix and Postfix patches to the same method.
    /// </summary>
    /// <typeparam name="T">The type containing the target method.</typeparam>
    /// <param name="methodName">Name of the method to patch.</param>
    /// <param name="prefix">Delegate pointing to the prefix patch method.</param>
    /// <param name="postfix">Delegate pointing to the postfix patch method.</param>
    /// <param name="optional">If true, log warning instead of error when method not found.</param>
    public PatchSet PrefixPostfix<T>(string methodName, Delegate prefix, Delegate postfix, bool optional = false)
    {
        return PrefixPostfix(typeof(T), methodName, null, prefix, postfix, optional);
    }

    /// <summary>
    /// Add both Prefix and Postfix patches to the same method with parameter types for overload resolution.
    /// </summary>
    /// <typeparam name="T">The type containing the target method.</typeparam>
    /// <param name="methodName">Name of the method to patch.</param>
    /// <param name="parameterTypes">Parameter types for overload resolution.</param>
    /// <param name="prefix">Delegate pointing to the prefix patch method.</param>
    /// <param name="postfix">Delegate pointing to the postfix patch method.</param>
    /// <param name="optional">If true, log warning instead of error when method not found.</param>
    public PatchSet PrefixPostfix<T>(string methodName, Type[] parameterTypes, Delegate prefix, Delegate postfix, bool optional = false)
    {
        return PrefixPostfix(typeof(T), methodName, parameterTypes, prefix, postfix, optional);
    }

    /// <summary>
    /// Add both Prefix and Postfix patches to the same method using a Type instance.
    /// </summary>
    public PatchSet PrefixPostfix(Type targetType, string methodName, Type[] parameterTypes, Delegate prefix, Delegate postfix, bool optional = false)
    {
        _entries.Add(new PatchEntry
        {
            TargetType = targetType,
            MethodName = methodName,
            ParameterTypes = parameterTypes,
            Prefix = prefix?.Method,
            Postfix = postfix?.Method,
            Optional = optional
        });
        return this;
    }

    #endregion

    #region String-based Type Resolution

    /// <summary>
    /// Add a Prefix patch using a type name string (resolved at apply time).
    /// </summary>
    /// <param name="typeName">Name of the type to patch (simple or fully qualified).</param>
    /// <param name="methodName">Name of the method to patch.</param>
    /// <param name="patch">Delegate pointing to the patch method.</param>
    /// <param name="optional">If true, log warning instead of error when type/method not found.</param>
    public PatchSet Prefix(string typeName, string methodName, Delegate patch, bool optional = false)
    {
        _entries.Add(new PatchEntry
        {
            TypeName = typeName,
            MethodName = methodName,
            Prefix = patch?.Method,
            Optional = optional
        });
        return this;
    }

    /// <summary>
    /// Add a Postfix patch using a type name string (resolved at apply time).
    /// </summary>
    /// <param name="typeName">Name of the type to patch (simple or fully qualified).</param>
    /// <param name="methodName">Name of the method to patch.</param>
    /// <param name="patch">Delegate pointing to the patch method.</param>
    /// <param name="optional">If true, log warning instead of error when type/method not found.</param>
    public PatchSet Postfix(string typeName, string methodName, Delegate patch, bool optional = false)
    {
        _entries.Add(new PatchEntry
        {
            TypeName = typeName,
            MethodName = methodName,
            Postfix = patch?.Method,
            Optional = optional
        });
        return this;
    }

    /// <summary>
    /// Add both Prefix and Postfix patches using a type name string.
    /// </summary>
    public PatchSet PrefixPostfix(string typeName, string methodName, Delegate prefix, Delegate postfix, bool optional = false)
    {
        _entries.Add(new PatchEntry
        {
            TypeName = typeName,
            MethodName = methodName,
            Prefix = prefix?.Method,
            Postfix = postfix?.Method,
            Optional = optional
        });
        return this;
    }

    #endregion

    /// <summary>
    /// Apply all registered patches and return the result.
    /// Never throws - all failures are logged via ModError.
    /// </summary>
    public PatchResult Apply()
    {
        var result = new PatchResult();

        foreach (var entry in _entries)
        {
            ApplyEntry(entry, result);
        }

        // Log summary
        if (result.FailureCount > 0)
        {
            ModError.Warn(_modName, $"PatchSet applied {result.SuccessCount}/{result.TotalCount} patches");
        }
        else if (result.SuccessCount > 0)
        {
            ModError.Info(_modName, $"PatchSet applied {result.SuccessCount} patches successfully");
        }

        return result;
    }

    private void ApplyEntry(PatchEntry entry, PatchResult result)
    {
        var description = entry.GetDescription();

        try
        {
            // Resolve target type
            Type targetType = entry.TargetType ?? ResolveType(entry.TypeName);
            if (targetType == null)
            {
                if (entry.Optional)
                {
                    ModError.Warn(_modName, $"[Optional] Type not found: {entry.TypeName ?? "null"}");
                }
                else
                {
                    ModError.Report(_modName, $"Type not found: {entry.TypeName ?? "null"}");
                    result.RecordFailure(description);
                }
                return;
            }

            // Resolve target method
            MethodInfo targetMethod = ResolveMethod(targetType, entry.MethodName, entry.ParameterTypes);
            if (targetMethod == null)
            {
                if (entry.Optional)
                {
                    ModError.Warn(_modName, $"[Optional] Method not found: {targetType.Name}.{entry.MethodName}");
                }
                else
                {
                    ModError.Report(_modName, $"Method not found: {targetType.Name}.{entry.MethodName}");
                    result.RecordFailure(description);
                }
                return;
            }

            var unmarshallable = GamePatch.FindUnmarshallableParameter(targetMethod);
            if (unmarshallable != null)
            {
                ModError.Report(_modName, $"Cannot patch {targetType.Name}.{entry.MethodName}: {unmarshallable}");
                result.RecordFailure(description);
                return;
            }

            // Apply the patch
            var prefixHm = entry.Prefix != null ? new HarmonyMethod(entry.Prefix) : null;
            var postfixHm = entry.Postfix != null ? new HarmonyMethod(entry.Postfix) : null;

            _harmony.Patch(targetMethod, prefix: prefixHm, postfix: postfixHm);
            result.RecordSuccess();
        }
        catch (Exception ex)
        {
            if (entry.Optional)
            {
                ModError.Warn(_modName, $"[Optional] Patch failed: {description} - {ex.Message}");
            }
            else
            {
                ModError.Report(_modName, $"Patch failed: {description}", ex);
                result.RecordFailure(description);
            }
        }
    }

    private static Type ResolveType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        try
        {
            // First try Assembly-CSharp (most common)
            var gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");

            if (gameAssembly != null)
            {
                var type = gameAssembly.GetTypes()
                    .FirstOrDefault(t => t.Name == typeName || t.FullName == typeName);
                if (type != null)
                    return type;
            }

            // Fallback: search all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(typeName);
                    if (type != null)
                        return type;

                    // Try by simple name
                    type = assembly.GetTypes().FirstOrDefault(t => t.Name == typeName);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Skip assemblies that throw on GetTypes()
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static MethodInfo ResolveMethod(Type targetType, string methodName, Type[] parameterTypes)
    {
        if (targetType == null || string.IsNullOrEmpty(methodName))
            return null;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static;

        try
        {
            MethodInfo method;

            if (parameterTypes != null && parameterTypes.Length > 0)
            {
                // Use specific parameter types for overload resolution
                method = targetType.GetMethod(methodName, flags, null, parameterTypes, null);
            }
            else
            {
                // Try to get the method without parameter specification
                method = targetType.GetMethod(methodName, flags);

                // If not found, search up the hierarchy with DeclaredOnly
                if (method == null)
                {
                    var current = targetType;
                    while (current != null && method == null)
                    {
                        method = current.GetMethod(methodName, flags | BindingFlags.DeclaredOnly);
                        current = current.BaseType;
                    }
                }
            }

            return method;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Internal representation of a pending patch.
    /// </summary>
    private class PatchEntry
    {
        public Type TargetType;
        public string TypeName;
        public string MethodName;
        public Type[] ParameterTypes;
        public MethodInfo Prefix;
        public MethodInfo Postfix;
        public bool Optional;

        public string GetDescription()
        {
            var typeName = TargetType?.Name ?? TypeName ?? "?";
            var patchType = (Prefix != null && Postfix != null) ? "PrefixPostfix"
                          : (Prefix != null) ? "Prefix"
                          : "Postfix";
            var optionalTag = Optional ? " [optional]" : "";
            return $"{patchType} {typeName}.{MethodName}{optionalTag}";
        }
    }
}
