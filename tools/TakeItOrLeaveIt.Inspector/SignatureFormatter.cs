using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;

namespace TakeItOrLeaveIt.Inspector;

/// <summary>Decodes metadata signatures into readable C#-like type names.</summary>
internal sealed class SignatureFormatter : ISignatureTypeProvider<string, object?>
{
    public static readonly SignatureFormatter Instance = new();

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.IntPtr => "IntPtr",
        PrimitiveTypeCode.UIntPtr => "UIntPtr",
        PrimitiveTypeCode.TypedReference => "TypedReference",
        _ => typeCode.ToString(),
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => MetadataNames.TypeName(reader, handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        var ns = reader.GetString(reference.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";

    public string GetByReferenceType(string elementType) => "ref " + elementType;

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
    {
        var tick = genericType.IndexOf('`');
        var baseName = tick >= 0 ? genericType.Substring(0, tick) : genericType;
        return baseName + "<" + string.Join(", ", typeArguments) + ">";
    }

    public string GetGenericTypeParameter(object? genericContext, int index) => "T" + index;

    public string GetGenericMethodParameter(object? genericContext, int index) => "M" + index;

    public string GetFunctionPointerType(MethodSignature<string> signature)
        => "delegate*<" + string.Join(", ", signature.ParameterTypes.Concat(new[] { signature.ReturnType })) + ">";

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;
}

internal static class MetadataNames
{
    public static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var def = reader.GetTypeDefinition(handle);
        var name = reader.GetString(def.Name);
        var declaring = def.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return TypeName(reader, declaring) + "+" + name;
        }

        var ns = reader.GetString(def.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static string Namespace(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var def = reader.GetTypeDefinition(handle);
        var declaring = def.GetDeclaringType();
        return declaring.IsNil ? reader.GetString(def.Namespace) : Namespace(reader, declaring);
    }
}
