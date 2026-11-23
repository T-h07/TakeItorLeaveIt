using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace TakeItOrLeaveIt.Inspector;

/// <summary>
/// Read-only call-graph report for IL2CPP games, from the files MelonLoader's Il2CppInterop generator leaves in
/// <c>MelonLoader/Il2CppAssemblies</c>:
/// <list type="bullet">
/// <item><c>MethodXrefScanCache.db</c>: for every method, the native methods it calls and the methods that call
/// it, found by scanning the machine code of the installed <c>GameAssembly.dll</c>.</item>
/// <item><c>MethodAddressToToken.db</c>: native method address → generated method (assembly + metadata token).</item>
/// <item>The generated assemblies: <c>[CachedScanResults]</c> on each method gives its ranges in the cache.</item>
/// </list>
/// Nothing is loaded or executed; only metadata and the two data files are read.
/// Usage: <c>--xref &lt;Il2CppAssemblies folder&gt; --types "Ns.Type;Ns.Other" [--methods regex] [--out file]</c>.
/// </summary>
internal static class XrefReport
{
    private const int XrefMagic = 0x43584D55; // UMXC
    private const int TokenMagic = 0x4D544D55; // UMTM

    public static int Run(string dir, string typeList, string? methodFilter, string output)
    {
        var xref = File.ReadAllBytes(Path.Combine(dir, "MethodXrefScanCache.db"));
        var map = File.ReadAllBytes(Path.Combine(dir, "MethodAddressToToken.db"));
        if (BitConverter.ToInt32(xref, 0) != XrefMagic || BitConverter.ToInt32(map, 0) != TokenMagic)
        {
            Console.Error.WriteLine("Unexpected cache file format (magic mismatch).");
            return 1;
        }

        var resolver = new MethodResolver(dir, map);
        var filter = methodFilter is null ? null : new Regex(methodFilter, RegexOptions.IgnoreCase);
        var wanted = typeList.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sb = new StringBuilder();
        sb.AppendLine("# Call graph from the installed game (Il2CppInterop xref scan cache)").AppendLine();
        sb.AppendLine("Callees = native methods this method calls (in machine-code order, de-duplicated). Callers = methods whose code calls it.");
        sb.AppendLine("Inlined calls and calls through virtual dispatch / delegates may be missing.").AppendLine();

        foreach (var typeName in wanted)
        {
            var found = resolver.FindType(typeName);
            if (found is null)
            {
                sb.AppendLine($"## {typeName}").AppendLine().AppendLine("NOT FOUND").AppendLine();
                continue;
            }

            var (reader, handle, asm) = found.Value;
            var def = reader.GetTypeDefinition(handle);
            sb.AppendLine($"## {typeName}  ({asm})").AppendLine();
            var baseType = def.BaseType.IsNil ? null : BaseName(reader, def.BaseType);
            if (baseType is not null)
            {
                sb.AppendLine($"Base: `{baseType}`").AppendLine();
            }

            foreach (var mh in def.GetMethods())
            {
                var m = reader.GetMethodDefinition(mh);
                var name = reader.GetString(m.Name);
                if (filter is not null && !filter.IsMatch(name))
                {
                    continue;
                }

                var scan = ReadScanAttribute(reader, m);
                sb.AppendLine($"### `{MethodResolver.Describe(reader, mh)}`");
                if (scan is null)
                {
                    sb.AppendLine("(no scan data: interop helper, accessor without native body, or not scanned)").AppendLine();
                    continue;
                }

                var (refStart, refEnd, xStart, xEnd) = scan.Value;
                var callees = Entries(xref, xStart, xEnd).Where(e => e.Type == 1).Select(e => resolver.Name(e.Address)).Where(n => n is not null).Distinct().ToList();
                var globals = Entries(xref, xStart, xEnd).Count(e => e.Type == 0);
                var callers = Entries(xref, refStart, refEnd).Select(e => resolver.Name(e.Address) ?? $"<native 0x{e.Address:X}>").Distinct().ToList();
                sb.AppendLine($"- callees ({callees.Count}, plus {globals} global/metadata refs):");
                foreach (var c in callees)
                {
                    sb.AppendLine($"  - {c}");
                }

                sb.AppendLine($"- callers ({callers.Count}):");
                foreach (var c in callers)
                {
                    sb.AppendLine($"  - {c}");
                }

                sb.AppendLine();
            }
        }

        File.WriteAllText(output, sb.ToString());
        Console.WriteLine($"Wrote {output}");
        return 0;
    }

    private static string BaseName(MetadataReader reader, EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => MetadataNames.TypeName(reader, (TypeDefinitionHandle)h),
        HandleKind.TypeReference => SignatureFormatter.Instance.GetTypeFromReference(reader, (TypeReferenceHandle)h, 0),
        HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(SignatureFormatter.Instance, null),
        _ => h.Kind.ToString(),
    };

    private readonly record struct Entry(long Address, long FoundAt, int Type);

    // FileHeader: int magic, int version, long initRva (16 bytes); MethodData: long, long, int (+4 padding) = 24 bytes.
    private static IEnumerable<Entry> Entries(byte[] xref, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var o = 16 + (i * 24);
            if (o + 20 > xref.Length)
            {
                yield break;
            }

            yield return new Entry(BitConverter.ToInt64(xref, o), BitConverter.ToInt64(xref, o + 8), BitConverter.ToInt32(xref, o + 16));
        }
    }

    private static (int RefStart, int RefEnd, int XStart, int XEnd)? ReadScanAttribute(MetadataReader reader, MethodDefinition m)
    {
        foreach (var ah in m.GetCustomAttributes())
        {
            var a = reader.GetCustomAttribute(ah);
            if (AttributeName(reader, a) != "CachedScanResultsAttribute")
            {
                continue;
            }

            var value = a.DecodeValue(AttributeTypes.Instance);
            int Get(string n) => value.NamedArguments.Where(x => x.Name == n).Select(x => Convert.ToInt32(x.Value)).FirstOrDefault();
            return (Get("RefRangeStart"), Get("RefRangeEnd"), Get("XrefRangeStart"), Get("XrefRangeEnd"));
        }

        return null;
    }

    private static string AttributeName(MetadataReader reader, CustomAttribute a)
    {
        if (a.Constructor.Kind == HandleKind.MemberReference)
        {
            var parent = reader.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent;
            if (parent.Kind == HandleKind.TypeReference)
            {
                return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name);
            }
        }
        else if (a.Constructor.Kind == HandleKind.MethodDefinition)
        {
            var t = reader.GetMethodDefinition((MethodDefinitionHandle)a.Constructor).GetDeclaringType();
            return reader.GetString(reader.GetTypeDefinition(t).Name);
        }

        return string.Empty;
    }

    private sealed class AttributeTypes : ICustomAttributeTypeProvider<string>
    {
        public static readonly AttributeTypes Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSystemType() => "System.Type";

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => MetadataNames.TypeName(reader, handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => type == "System.Type";
    }

    /// <summary>Native address → "Type.Method(sig)" through MethodAddressToToken.db and the generated assemblies.</summary>
    private sealed class MethodResolver
    {
        private readonly string _dir;
        private readonly List<string> _assemblies = new();
        private readonly long[] _pointers;
        private readonly int[] _values;
        private readonly Dictionary<string, MetadataReader?> _readers = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PEReader> _keepAlive = new();

        public MethodResolver(string dir, byte[] map)
        {
            _dir = dir;
            var numAssemblies = BitConverter.ToInt32(map, 8);
            var numMethods = BitConverter.ToInt32(map, 12);
            var dataOffset = BitConverter.ToInt32(map, 16);
            using (var br = new BinaryReader(new MemoryStream(map, 20, map.Length - 20), Encoding.UTF8))
            {
                for (var i = 0; i < numAssemblies; i++)
                {
                    _assemblies.Add(br.ReadString());
                }
            }

            _pointers = new long[numMethods];
            _values = new int[numMethods * 2];
            Buffer.BlockCopy(map, dataOffset, _pointers, 0, numMethods * 8);
            Buffer.BlockCopy(map, dataOffset + (numMethods * 8), _values, 0, numMethods * 8);
        }

        public (MetadataReader Reader, TypeDefinitionHandle Handle, string Assembly)? FindType(string fullName)
        {
            foreach (var file in new[] { "Assembly-CSharp", "Il2CppScheduleOne.Core" }.Concat(Directory.GetFiles(_dir, "*.dll").Select(Path.GetFileNameWithoutExtension)).Distinct())
            {
                var reader = Reader(file!);
                if (reader is null)
                {
                    continue;
                }

                foreach (var th in reader.TypeDefinitions)
                {
                    if (MetadataNames.TypeName(reader, th) == fullName)
                    {
                        return (reader, th, file!);
                    }
                }
            }

            return null;
        }

        public string? Name(long address)
        {
            var i = Array.BinarySearch(_pointers, address);
            if (i < 0)
            {
                return null;
            }

            var token = _values[i * 2];
            var asmIndex = _values[(i * 2) + 1];
            if (asmIndex < 0 || asmIndex >= _assemblies.Count)
            {
                return null;
            }

            var reader = Reader(_assemblies[asmIndex]);
            if (reader is null)
            {
                return $"<{_assemblies[asmIndex]} 0x{token:X8}>";
            }

            var handle = MetadataTokens.MethodDefinitionHandle(token & 0x00FFFFFF);
            return Describe(reader, handle);
        }

        public static string Describe(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var m = reader.GetMethodDefinition(handle);
            var type = MetadataNames.TypeName(reader, m.GetDeclaringType());
            string sig;
            try
            {
                var s = m.DecodeSignature(SignatureFormatter.Instance, null);
                var names = m.GetParameters().Select(p => reader.GetParameter(p)).Where(p => p.SequenceNumber > 0).OrderBy(p => p.SequenceNumber).Select(p => reader.GetString(p.Name)).ToList();
                var parts = s.ParameterTypes.Select((t, i) => i < names.Count ? $"{Short(t)} {names[i]}" : Short(t));
                var stat = (m.Attributes & MethodAttributes.Static) != 0 ? "static " : string.Empty;
                sig = $"{stat}{Short(s.ReturnType)} {type}.{reader.GetString(m.Name)}({string.Join(", ", parts)})";
            }
            catch (BadImageFormatException)
            {
                sig = $"{type}.{reader.GetString(m.Name)}(?)";
            }

            return sig;
        }

        private static string Short(string t) => t.Replace("Il2CppSystem.Collections.Generic.", string.Empty).Replace("Il2CppScheduleOne.", string.Empty);

        private MetadataReader? Reader(string assembly)
        {
            var key = assembly.Split(',')[0].Trim();
            key = key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? key[..^4] : key;
            if (_readers.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var path = Path.Combine(_dir, key + ".dll");
            MetadataReader? reader = null;
            if (File.Exists(path))
            {
                var pe = new PEReader(File.OpenRead(path));
                _keepAlive.Add(pe);
                reader = pe.GetMetadataReader();
            }

            _readers[key] = reader;
            return reader;
        }
    }
}
