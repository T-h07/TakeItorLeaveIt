using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using TakeItOrLeaveIt.Core.Environment;

namespace TakeItOrLeaveIt.Inspector;

/// <summary>
/// Usage: TakeItOrLeaveIt.Inspector --game "&lt;Schedule I folder&gt;" [--out &lt;folder&gt;]
/// Read-only. Produces environment.md, candidates.md, keywords.md and types.md.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var game = GetArg(args, "--game");
        var output = GetArg(args, "--out") ?? Path.Combine(Directory.GetCurrentDirectory(), "inspection-output");
        if (game is null || !Directory.Exists(game))
        {
            Console.Error.WriteLine("Usage: TakeItOrLeaveIt.Inspector --game \"<folder containing Schedule I.exe>\" [--out <folder>]");
            return 2;
        }

        Directory.CreateDirectory(output);

        var detection = GameRuntimeDetector.Detect(game, PhysicalFileProbe.Instance);
        var env = new StringBuilder();
        env.AppendLine("# Schedule I environment report").AppendLine();
        env.AppendLine($"Generated (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}  ").AppendLine($"Detected runtime: **{detection.Runtime}**").AppendLine();
        env.AppendLine("## Runtime evidence");
        foreach (var e in detection.Evidence)
        {
            env.AppendLine($"- {e}");
        }

        AppendFileFacts(env, game);

        var assemblies = LocateAssemblies(game, detection.Runtime, env);
        File.WriteAllText(Path.Combine(output, "environment.md"), env.ToString());

        if (assemblies.Count == 0)
        {
            Console.Error.WriteLine("No game assemblies found to inspect. For IL2CPP, launch the game once with MelonLoader so it generates MelonLoader/Il2CppAssemblies.");
            Console.WriteLine($"Wrote {Path.Combine(output, "environment.md")}");
            return 1;
        }

        var types = new List<TypeInfo>();
        foreach (var path in assemblies)
        {
            types.AddRange(ReadTypes(path));
        }

        WriteCandidates(Path.Combine(output, "candidates.md"), types);
        WriteKeywords(Path.Combine(output, "keywords.md"), types);
        WriteTypes(Path.Combine(output, "types.md"), types);

        Console.WriteLine($"Runtime: {detection.Runtime}. Inspected {types.Count} types from {assemblies.Count} assemblies.");
        Console.WriteLine($"Reports written to: {output}");
        return 0;
    }

    private static string? GetArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void AppendFileFacts(StringBuilder env, string game)
    {
        env.AppendLine().AppendLine("## Files");
        void Version(string label, string path)
        {
            if (!File.Exists(path))
            {
                env.AppendLine($"- {label}: not found");
                return;
            }

            var info = FileVersionInfo.GetVersionInfo(path);
            env.AppendLine($"- {label}: file {info.FileVersion ?? "?"}, product {info.ProductVersion ?? "?"}");
        }

        Version("Schedule I.exe", Path.Combine(game, "Schedule I.exe"));
        Version("UnityPlayer.dll", Path.Combine(game, "UnityPlayer.dll"));
        Version("MelonLoader (net6)", Path.Combine(game, "MelonLoader", "net6", "MelonLoader.dll"));
        Version("MelonLoader (net35)", Path.Combine(game, "MelonLoader", "net35", "MelonLoader.dll"));
        Version("version.dll (MelonLoader proxy)", Path.Combine(game, "version.dll"));

        var appInfo = Path.Combine(game, "Schedule I_Data", "app.info");
        if (File.Exists(appInfo))
        {
            env.AppendLine($"- app.info (company / product): {string.Join(" / ", File.ReadAllLines(appInfo).Take(2))}");
        }

        // MelonLoader writes game name/developer/version and Unity version near the top of its log.
        var log = Path.Combine(game, "MelonLoader", "Latest.log");
        if (File.Exists(log))
        {
            env.AppendLine().AppendLine("## MelonLoader/Latest.log (header lines)");
            foreach (var line in ReadShared(log).Take(80).Where(l =>
                         l.Contains("Version", StringComparison.OrdinalIgnoreCase) ||
                         l.Contains("Game", StringComparison.OrdinalIgnoreCase) ||
                         l.Contains("Runtime", StringComparison.OrdinalIgnoreCase) ||
                         l.Contains("TakeItOrLeaveIt", StringComparison.Ordinal)))
            {
                env.AppendLine($"    {line}");
            }
        }

        var mods = Path.Combine(game, "Mods");
        if (Directory.Exists(mods))
        {
            env.AppendLine().AppendLine("## Installed mods (file names only)");
            foreach (var f in Directory.GetFiles(mods).Select(Path.GetFileName).OrderBy(n => n))
            {
                env.AppendLine($"- {f}");
            }
        }
    }

    private static IEnumerable<string> ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static List<string> LocateAssemblies(string game, GameRuntime runtime, StringBuilder env)
    {
        var candidates = new List<string>();
        var managed = Path.Combine(game, "Schedule I_Data", "Managed");
        var il2cpp = Path.Combine(game, "MelonLoader", "Il2CppAssemblies");

        if (runtime != GameRuntime.Il2Cpp)
        {
            candidates.Add(Path.Combine(managed, "Assembly-CSharp.dll"));
            candidates.Add(Path.Combine(managed, "ScheduleOne.Core.dll"));
        }

        if (runtime != GameRuntime.Mono)
        {
            candidates.Add(Path.Combine(il2cpp, "Assembly-CSharp.dll"));
            candidates.Add(Path.Combine(il2cpp, "Il2CppScheduleOne.Core.dll"));
        }

        env.AppendLine().AppendLine("## Assemblies inspected");
        var found = new List<string>();
        foreach (var c in candidates)
        {
            var exists = File.Exists(c);
            env.AppendLine($"- {Path.GetRelativePath(game, c)}: {(exists ? "inspected" : "not found")}");
            if (exists)
            {
                found.Add(c);
            }
        }

        return found;
    }

    private sealed record MemberInfo(string Kind, string Name, string Signature);

    private sealed record TypeInfo(string Assembly, string FullName, string Namespace, string Kind, string? BaseType, List<MemberInfo> Members)
    {
        /// <summary>Full name with an IL2CPP "Il2Cpp" namespace prefix removed, for runtime-neutral matching.</summary>
        public string NeutralName => FullName.StartsWith("Il2Cpp", StringComparison.Ordinal) ? FullName.Substring(6) : FullName;

        public string NeutralNamespace => Namespace.StartsWith("Il2Cpp", StringComparison.Ordinal) ? Namespace.Substring(6) : Namespace;
    }

    private static IEnumerable<TypeInfo> ReadTypes(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var asm = Path.GetFileName(path);
        var result = new List<TypeInfo>();

        foreach (var handle in reader.TypeDefinitions)
        {
            var def = reader.GetTypeDefinition(handle);
            var fullName = MetadataNames.TypeName(reader, handle);
            var ns = MetadataNames.Namespace(reader, handle);
            if (fullName.Contains("<", StringComparison.Ordinal))
            {
                continue; // compiler-generated
            }

            string? baseType = null;
            if (!def.BaseType.IsNil)
            {
                baseType = def.BaseType.Kind switch
                {
                    HandleKind.TypeDefinition => MetadataNames.TypeName(reader, (TypeDefinitionHandle)def.BaseType),
                    HandleKind.TypeReference => SignatureFormatter.Instance.GetTypeFromReference(reader, (TypeReferenceHandle)def.BaseType, 0),
                    HandleKind.TypeSpecification => SignatureFormatter.Instance.GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)def.BaseType, 0),
                    _ => null,
                };
            }

            var kind = (def.Attributes & TypeAttributes.Interface) != 0 ? "interface"
                : baseType == "System.Enum" ? "enum"
                : baseType == "System.ValueType" ? "struct"
                : "class";

            var members = new List<MemberInfo>();
            foreach (var fh in def.GetFields())
            {
                var f = reader.GetFieldDefinition(fh);
                var name = reader.GetString(f.Name);
                if (name.Contains('<'))
                {
                    continue;
                }

                string type;
                try
                {
                    type = f.DecodeSignature(SignatureFormatter.Instance, null);
                }
                catch (BadImageFormatException)
                {
                    type = "?";
                }

                var stat = (f.Attributes & FieldAttributes.Static) != 0 ? "static " : string.Empty;
                members.Add(new MemberInfo("field", name, $"{Access(f.Attributes & FieldAttributes.FieldAccessMask)} {stat}{type} {name}"));
            }

            foreach (var ph in def.GetProperties())
            {
                var p = reader.GetPropertyDefinition(ph);
                var name = reader.GetString(p.Name);
                string type;
                try
                {
                    type = p.DecodeSignature(SignatureFormatter.Instance, null).ReturnType;
                }
                catch (BadImageFormatException)
                {
                    type = "?";
                }

                var accessors = p.GetAccessors();
                var acc = (accessors.Getter.IsNil ? string.Empty : "get; ") + (accessors.Setter.IsNil ? string.Empty : "set; ");
                members.Add(new MemberInfo("property", name, $"{type} {name} {{ {acc}}}"));
            }

            foreach (var mh in def.GetMethods())
            {
                var m = reader.GetMethodDefinition(mh);
                var name = reader.GetString(m.Name);
                if (name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal) || name.Contains('<'))
                {
                    continue;
                }

                string sig;
                try
                {
                    var s = m.DecodeSignature(SignatureFormatter.Instance, null);
                    var paramNames = m.GetParameters().Select(h => reader.GetParameter(h)).Where(p => p.SequenceNumber > 0)
                        .OrderBy(p => p.SequenceNumber).Select(p => reader.GetString(p.Name)).ToList();
                    var ps = s.ParameterTypes.Select((t, i) => i < paramNames.Count ? $"{t} {paramNames[i]}" : t);
                    var stat = (m.Attributes & MethodAttributes.Static) != 0 ? "static " : string.Empty;
                    var virt = (m.Attributes & MethodAttributes.Virtual) != 0 ? "virtual " : string.Empty;
                    sig = $"{Access(m.Attributes & MethodAttributes.MemberAccessMask)} {stat}{virt}{s.ReturnType} {name}({string.Join(", ", ps)})";
                }
                catch (BadImageFormatException)
                {
                    sig = $"{name}(?)";
                }

                members.Add(new MemberInfo("method", name, sig));
            }

            result.Add(new TypeInfo(asm, fullName, ns, kind, baseType, members));
        }

        return result;
    }

    private static string Access(FieldAttributes a) => a switch
    {
        FieldAttributes.Public => "public",
        FieldAttributes.Private => "private",
        FieldAttributes.Family => "protected",
        FieldAttributes.Assembly => "internal",
        _ => "protected internal",
    };

    private static string Access(MethodAttributes a) => a switch
    {
        MethodAttributes.Public => "public",
        MethodAttributes.Private => "private",
        MethodAttributes.Family => "protected",
        MethodAttributes.Assembly => "internal",
        _ => "protected internal",
    };

    private static void WriteCandidates(string path, List<TypeInfo> types)
    {
        var byName = types.GroupBy(t => t.NeutralName).ToDictionary(g => g.Key, g => g.First());
        var sb = new StringBuilder();
        sb.AppendLine("# PATCH_MAP candidate verification").AppendLine();
        sb.AppendLine("| ID | Type | Member | Result | Assembly | Signature(s) |").AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var (id, type, member) in PatchCandidates.All)
        {
            if (!byName.TryGetValue(type, out var t))
            {
                sb.AppendLine($"| {id} | {type} | {member} | TYPE MISSING | | |");
                continue;
            }

            var matches = t.Members.Where(m => m.Name == member).Select(m => m.Signature.Replace("|", "\\|")).ToList();
            sb.AppendLine($"| {id} | {t.FullName} | {member} | {(matches.Count > 0 ? "FOUND" : "MEMBER MISSING")} | {t.Assembly} | {string.Join("<br>", matches)} |");
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteKeywords(string path, List<TypeInfo> types)
    {
        var relevant = types.Where(t => PatchCandidates.Namespaces.Any(ns => t.NeutralNamespace == ns || t.NeutralNamespace.StartsWith(ns + ".", StringComparison.Ordinal))).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# Keyword hits in relevant namespaces").AppendLine();
        foreach (var kw in PatchCandidates.DiscoveryKeywords)
        {
            var hits = relevant.SelectMany(t => t.Members.Where(m => m.Kind == "method" && m.Name.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    .Select(m => $"{t.FullName} :: {m.Signature}"))
                .Distinct().OrderBy(s => s).ToList();
            sb.AppendLine($"## {kw} ({hits.Count})");
            foreach (var h in hits)
            {
                sb.AppendLine($"- `{h}`");
            }

            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteTypes(string path, List<TypeInfo> types)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Type dump (relevant namespaces)").AppendLine();
        foreach (var t in types.Where(t => PatchCandidates.Namespaces.Any(ns => t.NeutralNamespace == ns || t.NeutralNamespace.StartsWith(ns + ".", StringComparison.Ordinal)))
                     .OrderBy(t => t.FullName))
        {
            sb.AppendLine($"## {t.Kind} {t.FullName}").AppendLine($"Assembly: {t.Assembly}; base: {t.BaseType ?? "-"}").AppendLine("```");
            foreach (var m in t.Members.OrderBy(m => m.Kind).ThenBy(m => m.Name))
            {
                sb.AppendLine(m.Signature);
            }

            sb.AppendLine("```").AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }
}
