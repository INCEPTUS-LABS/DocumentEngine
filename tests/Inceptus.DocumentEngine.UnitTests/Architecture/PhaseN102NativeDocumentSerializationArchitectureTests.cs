using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN102NativeDocumentSerializationArchitectureTests
{
    private const BindingFlags DeclaredPublicMembers =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    [Fact]
    public void NativeSerializerExposesTheApprovedVersionedUtf8DocumentApi()
    {
        var serializer = typeof(NativeDocumentSerializer);

        Assert.Same(typeof(Document).Assembly, serializer.Assembly);
        Assert.Equal("Inceptus.DocumentEngine.Runtime.Documents", serializer.Namespace);
        Assert.True(serializer.IsAbstract && serializer.IsSealed);
        Assert.Empty(serializer.GetConstructors(DeclaredPublicMembers));

        var properties = serializer.GetProperties(DeclaredPublicMembers)
            .OrderBy(static property => property.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [nameof(NativeDocumentSerializer.FormatIdentifier), nameof(NativeDocumentSerializer.FormatVersion)],
            properties.Select(static property => property.Name));
        Assert.Equal(typeof(string), properties[0].PropertyType);
        Assert.Equal(typeof(int), properties[1].PropertyType);
        Assert.All(properties, static property =>
        {
            Assert.True(property.GetMethod!.IsStatic);
            Assert.Null(property.SetMethod);
        });
        Assert.Equal("Inceptus.Document", NativeDocumentSerializer.FormatIdentifier);
        Assert.Equal(1, NativeDocumentSerializer.FormatVersion);

        var methods = serializer.GetMethods(DeclaredPublicMembers)
            .Where(static method => !method.IsSpecialName)
            .OrderBy(static method => method.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                nameof(NativeDocumentSerializer.Export),
                nameof(NativeDocumentSerializer.Export),
                nameof(NativeDocumentSerializer.Import),
            ],
            methods.Select(static method => method.Name));
        Assert.All(methods, static method => Assert.True(method.IsStatic));

        var exports = methods
            .Where(static method => method.Name == nameof(NativeDocumentSerializer.Export))
            .ToArray();
        Assert.Equal(2, exports.Length);
        Assert.All(exports, static export =>
        {
            Assert.Equal(typeof(ImmutableArray<byte>), export.ReturnType);
            Assert.False(Assert.Single(export.GetParameters()).IsOptional);
        });
        Assert.Equal(
            [typeof(Document), typeof(DocumentSnapshot)],
            exports.Select(static export => export.GetParameters()[0].ParameterType)
                .OrderBy(static type => type.Name, StringComparer.Ordinal));

        var import = Assert.Single(
            methods,
            static method => method.Name == nameof(NativeDocumentSerializer.Import));
        Assert.Equal(typeof(DocumentConstructionResult), import.ReturnType);
        Assert.Equal(
            [typeof(ReadOnlyMemory<byte>), typeof(IElementConnectorAnchorPolicyProvider)],
            import.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.False(import.GetParameters()[0].IsOptional);
        Assert.True(import.GetParameters()[1].IsOptional);
        Assert.Empty(serializer.GetEvents(DeclaredPublicMembers));
        Assert.Empty(serializer.GetFields(DeclaredPublicMembers));
    }

    [Fact]
    public void NativeSerializerPublicSurfaceIsNotationNeutralAndContainsNoRuntimeStateOrJsonTypes()
    {
        var serializer = typeof(NativeDocumentSerializer);
        var signatureTypes = PublicSignatureTypes(serializer).ToArray();

        Assert.DoesNotContain(signatureTypes, IsForbiddenSignatureType);
        Assert.DoesNotContain(signatureTypes, type =>
            type.Namespace?.Contains(".EditorState", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains(".History", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains(".Layout", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains(".Routing", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains(".Projection", StringComparison.Ordinal) == true ||
            type.Name.Contains("Scene", StringComparison.OrdinalIgnoreCase));

        var methods = serializer.GetMethods(DeclaredPublicMembers)
            .Where(static method => !method.IsSpecialName)
            .ToArray();
        Assert.DoesNotContain(methods, static method =>
            method.ReturnType == typeof(DocumentId) ||
            method.Name.Contains("NewId", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("GenerateId", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Random", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsForbiddenSignatureType(Type type)
    {
        var namespaceName = type.Namespace ?? string.Empty;
        return namespaceName.StartsWith("System.Text.Json", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Bpmn", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Organizational", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Canvas2D", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) ||
            namespaceName.StartsWith(
                "System.Runtime.InteropServices.JavaScript",
                StringComparison.Ordinal);
    }

    private static IEnumerable<Type> PublicSignatureTypes(Type type)
    {
        foreach (var property in type.GetProperties(DeclaredPublicMembers))
        {
            foreach (var expanded in ExpandType(property.PropertyType))
            {
                yield return expanded;
            }
        }

        foreach (var method in type.GetMethods(DeclaredPublicMembers))
        {
            foreach (var expanded in ExpandType(method.ReturnType))
            {
                yield return expanded;
            }

            foreach (var parameter in method.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var expanded in ExpandType(elementType))
            {
                yield return expanded;
            }
        }

        foreach (var genericArgument in type.GetGenericArguments())
        {
            foreach (var expanded in ExpandType(genericArgument))
            {
                yield return expanded;
            }
        }
    }
}
