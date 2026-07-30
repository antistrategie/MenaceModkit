using System;
using System.Linq;
using System.Reflection;

namespace Menace.SDK;

/// <summary>
/// Binds to game members by shape rather than by exact signature.
///
/// Game updates routinely add a parameter to a method or move a field, which breaks
/// <c>GetMethod(name, exactParameterTypes)</c> and any hardcoded struct offset. Every lookup
/// that goes through here survives that: methods are matched on the parameters that carry
/// meaning and the rest are defaulted, and field reads prefer a property over an offset.
/// </summary>
public static class GameMember
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// Find a method whose leading parameters match <paramref name="leadingParameterTypes"/>,
    /// tolerating any number of extra trailing parameters. Prefers the fewest extras, and only
    /// accepts extras that can be safely defaulted (bools, enums, numbers), so a method whose
    /// added parameter carries real meaning is not called with a made-up value.
    /// Returns null when nothing matches.
    /// </summary>
    public static MethodInfo FindMethod(Type type, string name, params Type[] leadingParameterTypes)
    {
        if (type == null || string.IsNullOrEmpty(name))
            return null;

        var leading = leadingParameterTypes ?? Array.Empty<Type>();

        return type.GetMethods(InstanceFlags | BindingFlags.Static)
            .Where(m => m.Name == name)
            .Select(m => new { Method = m, Params = m.GetParameters() })
            .Where(c => c.Params.Length >= leading.Length)
            .Where(c => LeadingMatches(c.Params, leading))
            .Where(c => c.Params.Skip(leading.Length).All(p => IsDefaultable(p.ParameterType)))
            .OrderBy(c => c.Params.Length)
            .Select(c => c.Method)
            .FirstOrDefault();
    }

    /// <summary>
    /// Find a method by name and exact parameter count. Use when the parameters are positional
    /// and none of them can be defaulted (so <see cref="FindMethod"/> would reject the match).
    /// </summary>
    public static MethodInfo FindMethodWithArity(Type type, string name, int parameterCount)
    {
        if (type == null || string.IsNullOrEmpty(name))
            return null;

        return type.GetMethods(InstanceFlags | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameterCount);
    }

    /// <summary>
    /// Invoke <paramref name="method"/> with <paramref name="leadingArgs"/>, defaulting any
    /// remaining parameters. Throws whatever the target throws, unwrapped by the caller.
    /// </summary>
    public static object Invoke(MethodInfo method, object instance, params object[] leadingArgs)
    {
        if (method == null)
            throw new ArgumentNullException(nameof(method));

        return method.Invoke(instance, BuildArgs(method, leadingArgs));
    }

    /// <summary>
    /// Build a full argument array for <paramref name="method"/> from the arguments that matter,
    /// defaulting the rest (false / zero / null).
    /// </summary>
    public static object[] BuildArgs(MethodInfo method, params object[] leadingArgs)
    {
        var parameters = method.GetParameters();
        var given = leadingArgs ?? Array.Empty<object>();

        // Refuse rather than truncate: silently dropping a caller's explicit argument (say, an
        // addSlotWhenNeeded flag) would change behaviour with no error anywhere.
        if (given.Length > parameters.Length)
            throw new ArgumentException(
                $"{method.DeclaringType?.Name}.{method.Name} takes {parameters.Length} parameter(s) " +
                $"but {given.Length} argument(s) were supplied");

        var args = new object[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            args[i] = i < given.Length ? given[i] : DefaultFor(parameters[i].ParameterType);

        return args;
    }

    /// <summary>
    /// Read a pointer-valued member: the property first, falling back to a raw struct offset for
    /// builds that don't expose one. Offsets shift with every game build, so they are the last
    /// resort rather than the first choice. Returns <see cref="GameObj.Null"/> if both fail.
    /// </summary>
    public static GameObj ReadPointerMember(
        object instance, Type type, string propertyName, string fieldName = null, int fallbackOffset = -1)
    {
        if (instance == null || type == null)
            return GameObj.Null;

        try
        {
            var property = type.GetProperty(propertyName, InstanceFlags);
            var value = property?.GetValue(instance);
            if (value is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase proxy && proxy.Pointer != IntPtr.Zero)
                return new GameObj(proxy.Pointer);
        }
        catch
        {
            // Property exists but threw (unset backing field, for instance) - fall through.
        }

        if (instance is not Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase self)
            return GameObj.Null;

        var obj = new GameObj(self.Pointer);

        // A field name is resolved to its offset by the IL2CPP metadata at runtime, so it keeps
        // working across builds that move the field. GameObj.ReadPtr(name, offset) only uses the
        // raw offset when the NAME fails to resolve: a resolved-but-null field must read as null,
        // not as the stale offset's bytes.
        var ptr = !string.IsNullOrEmpty(fieldName)
            ? (fallbackOffset >= 0 ? obj.ReadPtr(fieldName, (uint)fallbackOffset) : obj.ReadPtr(fieldName))
            : (fallbackOffset >= 0 ? obj.ReadPtr((uint)fallbackOffset) : IntPtr.Zero);

        return ptr == IntPtr.Zero ? GameObj.Null : new GameObj(ptr);
    }

    // --- Harmony __args access ---
    //
    // Harmony matches named patch parameters against the target's own parameter names, and this
    // game names them with a leading underscore ("_actor", "_target"), which silently stops a
    // patch from binding. Reading positionally out of __args avoids that and tolerates a rename.
    // Every read is bounds-checked, so a signature that loses a parameter degrades to a default
    // rather than throwing inside a patch.

    /// <summary>Argument at <paramref name="index"/>, or null when the signature is shorter.</summary>
    public static object Arg(object[] args, int index)
        => args != null && index >= 0 && index < args.Length ? args[index] : null;

    /// <summary>Argument as an int, unboxing enums. Zero when absent or not convertible.</summary>
    public static int ArgInt(object[] args, int index)
    {
        var value = Arg(args, index);
        if (value == null) return 0;
        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Argument as a float. Zero when absent or not convertible.</summary>
    public static float ArgFloat(object[] args, int index)
    {
        var value = Arg(args, index);
        if (value == null) return 0f;
        try
        {
            return Convert.ToSingle(value);
        }
        catch
        {
            return 0f;
        }
    }

    /// <summary>Argument as a bool. False when absent.</summary>
    public static bool ArgBool(object[] args, int index)
        => Arg(args, index) is bool b && b;

    /// <summary>
    /// True when a parameter can be filled with a harmless placeholder. Flags and counts can be
    /// defaulted, object references cannot: passing null where the game expects a real object
    /// would turn a missing-parameter problem into a null dereference inside the game.
    /// </summary>
    private static bool IsDefaultable(Type type)
        => type == typeof(bool) || type.IsEnum || IsNumeric(type);

    private static bool IsNumeric(Type type)
        => type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
           || type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte)
           || type == typeof(float) || type == typeof(double);

    private static object DefaultFor(Type type)
    {
        if (type == typeof(bool)) return false;
        if (type.IsEnum) return Enum.ToObject(type, 0);
        if (type.IsValueType) return Activator.CreateInstance(type);
        return null;
    }

    /// <summary>True when each requested type can be passed to the parameter in that position.</summary>
    private static bool LeadingMatches(ParameterInfo[] parameters, Type[] leading)
    {
        for (var i = 0; i < leading.Length; i++)
        {
            if (leading[i] == null)
                continue; // caller does not care about this position
            if (!parameters[i].ParameterType.IsAssignableFrom(leading[i]))
                return false;
        }

        return true;
    }
}
