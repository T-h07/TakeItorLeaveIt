using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace TakeItOrLeaveIt.Game.Discovery;

/// <summary>
/// Read-only reflection helpers used to reach Schedule I data without compile-time game references.
/// Works for both runtimes: IL2CPP interop types live under the <c>Il2Cpp</c> namespace prefix and expose
/// fields as properties; Mono types use the plain namespace and real fields. Every lookup is cached and
/// every miss returns null (callers fail closed). Nothing here writes to game objects.
/// </summary>
internal static class GameReflection
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Dictionary<string, Type?> TypeCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<(Type, string, bool), Func<object?, object?>?> MemberCache = new();

    /// <summary>
    /// Resolves the first matching type for a game type name, trying the IL2CPP (<c>Il2Cpp</c>-prefixed)
    /// name and then the Mono name, in loaded assemblies first and then in Assembly-CSharp.
    /// </summary>
    public static Type? FindGameType(string monoFullName)
    {
        if (TypeCache.TryGetValue(monoFullName, out var cached))
        {
            return cached;
        }

        Type? found = null;
        foreach (var name in new[] { "Il2Cpp" + monoFullName, monoFullName })
        {
            found = FindLoaded(name) ?? SafeGetType(name + ", Assembly-CSharp");
            if (found is not null)
            {
                break;
            }
        }

        TypeCache[monoFullName] = found;
        return found;
    }

    /// <summary>Reads a static field/property (searching base types, e.g. a generic Singleton&lt;T&gt;).</summary>
    public static bool TryGetStatic(Type type, string member, out object? value)
        => TryRead(type, null, member, isStatic: true, out value);

    /// <summary>Reads an instance field/property of <paramref name="target"/> (most-derived declaration wins).</summary>
    public static bool TryGet(object? target, string member, out object? value)
    {
        value = null;
        return target is not null && TryRead(target.GetType(), target, member, isStatic: false, out value);
    }

    public static bool HasMember(Type type, string member, bool isStatic) => GetReader(type, member, isStatic) is not null;

    /// <summary>
    /// Enumerates a managed list, an IL2CPP interop list (Count + indexer) or any IEnumerable.
    /// Returns null when <paramref name="collection"/> is not a recognised collection.
    /// </summary>
    public static IReadOnlyList<object?>? ToList(object? collection)
    {
        switch (collection)
        {
            case null:
                return null;
            case string:
                return null;
            case IList list:
            {
                var copy = new List<object?>(list.Count);
                foreach (var item in list)
                {
                    copy.Add(item);
                }

                return copy;
            }
        }

        var type = collection.GetType();
        if (TryGet(collection, "Count", out var countObj) && countObj is int count)
        {
            var indexer = FindIndexer(type);
            if (indexer is not null)
            {
                var copy = new List<object?>(count);
                for (var i = 0; i < count; i++)
                {
                    copy.Add(indexer.Invoke(collection, new object[] { i }));
                }

                return copy;
            }
        }

        if (collection is IEnumerable enumerable)
        {
            var copy = new List<object?>();
            foreach (var item in enumerable)
            {
                copy.Add(item);
            }

            return copy;
        }

        return null;
    }

    /// <summary>Count of a collection without copying it, when cheaply available.</summary>
    public static int? CountOf(object? collection)
    {
        if (collection is ICollection c)
        {
            return c.Count;
        }

        return TryGet(collection, "Count", out var value) && value is int n ? n : null;
    }

    private static bool TryRead(Type type, object? target, string member, bool isStatic, out object? value)
    {
        value = null;
        var reader = GetReader(type, member, isStatic);
        if (reader is null)
        {
            return false;
        }

        value = reader(target);
        return true;
    }

    private static Func<object?, object?>? GetReader(Type type, string member, bool isStatic)
    {
        var key = (type, member, isStatic);
        if (MemberCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Func<object?, object?>? reader = null;
        var flags = Declared | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        for (var t = type; t is not null && reader is null; t = t.BaseType)
        {
            var property = t.GetProperty(member, flags);
            if (property is not null && property.GetIndexParameters().Length == 0 && property.GetGetMethod(true) is not null)
            {
                reader = o => property.GetValue(o, null);
                break;
            }

            var field = t.GetField(member, flags);
            if (field is not null)
            {
                reader = o => field.GetValue(o);
            }
        }

        MemberCache[key] = reader;
        return reader;
    }

    private static MethodInfo? FindIndexer(Type type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var getter = t.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(int) }, null);
            if (getter is not null)
            {
                return getter;
            }
        }

        return null;
    }

    private static Type? FindLoaded(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type is not null)
                {
                    return type;
                }
            }
            catch (Exception)
            {
                // Some dynamic/broken assemblies throw on lookup; skip them.
            }
        }

        return null;
    }

    private static Type? SafeGetType(string assemblyQualifiedName)
    {
        try
        {
            return Type.GetType(assemblyQualifiedName, throwOnError: false);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
