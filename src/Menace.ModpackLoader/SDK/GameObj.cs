using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Menace.SDK.Internal;

namespace Menace.SDK;

/// <summary>
/// Safe handle for an IL2CPP object. All reads return defaults on failure;
/// all writes return false on failure. Never throws.
/// </summary>
public readonly struct GameObj : IEquatable<GameObj>
{
    public IntPtr Pointer { get; }

    public bool IsNull => Pointer == IntPtr.Zero;

    /// <summary>
    /// Check if the underlying Unity object is still alive (m_CachedPtr != 0).
    /// </summary>
    public bool IsAlive
    {
        get
        {
            if (Pointer == IntPtr.Zero) return false;

            try
            {
                var offset = OffsetCache.ObjectCachedPtrOffset;
                if (offset == 0) return true; // can't verify, assume alive

                var cachedPtr = Marshal.ReadIntPtr(Pointer + (int)offset);
                return cachedPtr != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }
    }

    public GameObj(IntPtr pointer)
    {
        Pointer = pointer;
    }

    public static GameObj Null => default;

    // --- Field reads by name (resolve offset each time unless cached) ---

    public int ReadInt(string fieldName)
    {
        var offset = ResolveFieldOffset(fieldName);
        return offset == 0 ? 0 : ReadInt(offset);
    }

    public float ReadFloat(string fieldName)
    {
        var offset = ResolveFieldOffset(fieldName);
        return offset == 0 ? 0f : ReadFloat(offset);
    }

    public bool ReadBool(string fieldName)
    {
        var offset = ResolveFieldOffset(fieldName);
        if (offset == 0) return false;
        try
        {
            return Marshal.ReadByte(Pointer + (int)offset) != 0;
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj.ReadBool", $"Failed at offset {offset}", ex);
            return false;
        }
    }

    public IntPtr ReadPtr(string fieldName)
    {
        var offset = ResolveFieldOffset(fieldName);
        return offset == 0 ? IntPtr.Zero : ReadPtr(offset);
    }

    public string ReadString(string fieldName)
    {
        var ptr = ReadPtr(fieldName);
        if (ptr == IntPtr.Zero) return null;

        try
        {
            return IL2CPP.Il2CppStringToManaged(ptr);
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj.ReadString", $"Failed to read '{fieldName}'", ex);
            return null;
        }
    }

    public GameObj ReadObj(string fieldName)
    {
        var ptr = ReadPtr(fieldName);
        return new GameObj(ptr);
    }

    // --- Field writes by name ---

    public bool WriteInt(string fieldName, int value)
    {
        var offset = ResolveFieldOffset(fieldName);
        if (offset == 0) return false;
        try
        {
            Marshal.WriteInt32(Pointer + (int)offset, value);
            return true;
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj.WriteInt", $"Failed '{fieldName}'", ex);
            return false;
        }
    }

    public bool WriteFloat(string fieldName, float value)
    {
        var offset = ResolveFieldOffset(fieldName);
        if (offset == 0) return false;
        try
        {
            var intVal = BitConverter.SingleToInt32Bits(value);
            Marshal.WriteInt32(Pointer + (int)offset, intVal);
            return true;
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj.WriteFloat", $"Failed '{fieldName}'", ex);
            return false;
        }
    }

    public bool WritePtr(string fieldName, IntPtr value)
    {
        var offset = ResolveFieldOffset(fieldName);
        if (offset == 0) return false;
        try
        {
            Marshal.WriteIntPtr(Pointer + (int)offset, value);
            return true;
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj.WritePtr", $"Failed '{fieldName}'", ex);
            return false;
        }
    }

    // --- Field reads by pre-cached offset ---

    public int ReadInt(uint offset)
    {
        if (Pointer == IntPtr.Zero || offset == 0) return 0;
        try
        {
            return Marshal.ReadInt32(Pointer + (int)offset);
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj.ReadInt", $"Failed at offset {offset}", ex);
            return 0;
        }
    }

    public float ReadFloat(uint offset)
    {
        if (Pointer == IntPtr.Zero || offset == 0) return 0f;
        try
        {
            var raw = Marshal.ReadInt32(Pointer + (int)offset);
            return BitConverter.Int32BitsToSingle(raw);
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj.ReadFloat", $"Failed at offset {offset}", ex);
            return 0f;
        }
    }

    public IntPtr ReadPtr(uint offset)
    {
        if (Pointer == IntPtr.Zero || offset == 0) return IntPtr.Zero;
        try
        {
            return Marshal.ReadIntPtr(Pointer + (int)offset);
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj.ReadPtr", $"Failed at offset {offset}", ex);
            return IntPtr.Zero;
        }
    }

    // --- Name-first reads ---
    //
    // A field name is resolved through IL2CPP metadata at runtime, so it survives a game update
    // that moves the field. The offset is kept as a fallback for anything the name lookup can't
    // resolve (an obfuscated or renamed field), which is the older, more brittle route.

    /// <summary>
    /// Read a pointer field by name, falling back to a raw offset. The fallback fires only when
    /// the NAME fails to resolve, never when the resolved field's value is null: after a field
    /// move, a legitimately-null field must read as null, not as whatever bytes now sit at the
    /// stale offset (a garbage pointer that crashes on first native use).
    /// </summary>
    public IntPtr ReadPtr(string fieldName, uint fallbackOffset)
    {
        var offset = ResolveFieldOffset(fieldName);
        return offset != 0 ? ReadPtr(offset) : ReadPtr(fallbackOffset);
    }

    /// <summary>Read an int field by name, falling back to a raw offset.</summary>
    public int ReadInt(string fieldName, uint fallbackOffset)
    {
        var offset = ResolveFieldOffset(fieldName);
        return offset != 0 ? ReadInt(offset) : ReadInt(fallbackOffset);
    }

    // --- Type operations ---

    public GameType GetGameType()
    {
        if (Pointer == IntPtr.Zero) return GameType.Invalid;

        try
        {
            var klass = IL2CPP.il2cpp_object_get_class(Pointer);
            return GameType.FromPointer(klass);
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj.GetGameType", "Failed", ex);
            return GameType.Invalid;
        }
    }

    public bool Is(GameType type)
    {
        if (type == null || !type.IsValid || Pointer == IntPtr.Zero)
            return false;
        return type.IsAssignableFrom(Pointer);
    }

    public string GetTypeName()
    {
        return GetGameType().FullName;
    }

    /// <summary>
    /// Convert this GameObj to its managed IL2CPP proxy type.
    /// Returns null if conversion fails.
    /// </summary>
    public object ToManaged()
    {
        if (Pointer == IntPtr.Zero) return null;

        try
        {
            var gameType = GetGameType();
            var managedType = gameType?.ManagedType;
            if (managedType == null)
            {
                ModError.WarnInternal("GameObj.ToManaged", $"No managed type for {gameType?.FullName}");
                return null;
            }

            var ptrCtor = managedType.GetConstructor(new[] { typeof(IntPtr) });
            if (ptrCtor == null)
            {
                ModError.WarnInternal("GameObj.ToManaged", $"No IntPtr constructor on {managedType.Name}");
                return null;
            }

            return ptrCtor.Invoke(new object[] { Pointer });
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj.ToManaged", "Conversion failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Convert this GameObj to a specific managed IL2CPP proxy type.
    /// Returns null if conversion fails or type doesn't match.
    /// </summary>
    public T As<T>() where T : class
    {
        if (Pointer == IntPtr.Zero) return null;

        try
        {
            var ptrCtor = typeof(T).GetConstructor(new[] { typeof(IntPtr) });
            if (ptrCtor == null)
            {
                ModError.WarnInternal("GameObj.As<T>", $"No IntPtr constructor on {typeof(T).Name}");
                return null;
            }

            return (T)ptrCtor.Invoke(new object[] { Pointer });
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj.As<T>", $"Conversion to {typeof(T).Name} failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Get the Unity object name (reads the "name" IL2CPP string field
    /// on UnityEngine.Object-derived objects).
    /// </summary>
    public string GetName()
    {
        if (Pointer == IntPtr.Zero) return null;

        try
        {
            var klass = IL2CPP.il2cpp_object_get_class(Pointer);
            if (klass == IntPtr.Zero) return null;

            // Try m_Name field first (some Unity objects)
            var nameField = OffsetCache.FindField(klass, "m_Name");
            if (nameField != IntPtr.Zero)
            {
                var offset = IL2CPP.il2cpp_field_get_offset(nameField);
                if (offset != 0)
                {
                    var strPtr = Marshal.ReadIntPtr(Pointer + (int)offset);
                    if (strPtr != IntPtr.Zero)
                        return IL2CPP.Il2CppStringToManaged(strPtr);
                }
            }

            // Fallback: use "name" property via managed type (UnityEngine.Object.name)
            var gameType = GetGameType();
            var managedType = gameType?.ManagedType;
            if (managedType != null)
            {
                var nameProp = managedType.GetProperty("name",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (nameProp != null)
                {
                    var ptrCtor = managedType.GetConstructor(new[] { typeof(IntPtr) });
                    if (ptrCtor != null)
                    {
                        var proxy = ptrCtor.Invoke(new object[] { Pointer });
                        var name = nameProp.GetValue(proxy);
                        if (name != null)
                            return name.ToString();
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    // --- Equality ---

    public bool Equals(GameObj other) => Pointer == other.Pointer;
    public override bool Equals(object obj) => obj is GameObj other && Equals(other);
    public override int GetHashCode() => Pointer.GetHashCode();
    public static bool operator ==(GameObj left, GameObj right) => left.Pointer == right.Pointer;
    public static bool operator !=(GameObj left, GameObj right) => left.Pointer != right.Pointer;

    public override string ToString()
    {
        if (Pointer == IntPtr.Zero) return "GameObj.Null";
        var name = GetName();
        var typeName = GetTypeName();
        return name != null
            ? $"{typeName} '{name}' @ 0x{Pointer:X}"
            : $"{typeName} @ 0x{Pointer:X}";
    }

    // --- Internal helpers ---

    private uint ResolveFieldOffset(string fieldName)
    {
        if (Pointer == IntPtr.Zero || string.IsNullOrEmpty(fieldName))
            return 0;

        try
        {
            var klass = IL2CPP.il2cpp_object_get_class(Pointer);
            if (klass == IntPtr.Zero) return 0;
            return OffsetCache.GetOrResolve(klass, fieldName);
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameObj", $"Failed to resolve offset for '{fieldName}'", ex);
            return 0;
        }
    }
}

/// <summary>
/// Utility methods for IL2CPP interop.
/// </summary>
public static class Il2CppUtils
{
    // Offset for m_DefaultTranslation in LocalizedLine/LocalizedMultiLine
    private const int LOC_DEFAULT_TRANSLATION_OFFSET = 0x38;

    /// <summary>
    /// Safely convert a reflection result to a .NET string.
    /// Handles .NET strings, IL2CPP strings, and LocalizedLine objects.
    /// Use this when calling Invoke() or GetValue() on IL2CPP objects
    /// that might return localized strings.
    /// </summary>
    public static string ToManagedString(object value)
    {
        if (value == null) return null;
        if (value is string s) return s;

        // Handle IL2CPP objects
        if (value is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase il2cppObj)
        {
            try
            {
                var ptr = il2cppObj.Pointer;
                if (ptr == IntPtr.Zero) return null;

                // Check if this is a LocalizedLine/LocalizedMultiLine by checking the class name
                var klass = IL2CPP.il2cpp_object_get_class(ptr);
                if (klass != IntPtr.Zero)
                {
                    var classNamePtr = IL2CPP.il2cpp_class_get_name(klass);
                    var className = classNamePtr != IntPtr.Zero
                        ? Marshal.PtrToStringAnsi(classNamePtr)
                        : null;

                    if (className == "LocalizedLine" || className == "LocalizedMultiLine" || className == "BaseLocalizedString")
                    {
                        // Read m_DefaultTranslation string at offset 0x38
                        var strPtr = Marshal.ReadIntPtr(ptr + LOC_DEFAULT_TRANSLATION_OFFSET);
                        if (strPtr != IntPtr.Zero)
                            return IL2CPP.Il2CppStringToManaged(strPtr);
                        return null;
                    }

                    // Check if it's already a string type
                    if (className == "String")
                        return IL2CPP.Il2CppStringToManaged(ptr);
                }

                // Fallback: try to convert directly
                return IL2CPP.Il2CppStringToManaged(ptr);
            }
            catch
            {
                return value.ToString();
            }
        }

        return value.ToString();
    }

    /// <summary>
    /// Create a managed IL2CPP proxy object from a pointer and managed type.
    /// Returns null if the pointer is invalid or construction fails.
    /// </summary>
    public static object GetManagedProxy(IntPtr pointer, Type managedType)
    {
        if (pointer == IntPtr.Zero || managedType == null) return null;

        try
        {
            var ptrCtor = managedType.GetConstructor(new[] { typeof(IntPtr) });
            return ptrCtor?.Invoke(new object[] { pointer });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Create a managed IL2CPP proxy object from a GameObj and managed type.
    /// Returns null if the pointer is invalid or construction fails.
    /// </summary>
    public static object GetManagedProxy(GameObj obj, Type managedType)
    {
        if (obj.IsNull || managedType == null) return null;
        return GetManagedProxy(obj.Pointer, managedType);
    }
}
