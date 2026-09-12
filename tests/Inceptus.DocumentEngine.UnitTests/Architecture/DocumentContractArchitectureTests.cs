using System.Collections;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class DocumentContractArchitectureTests
{
    private static readonly Assembly ContractsAssembly = typeof(DocumentSnapshot).Assembly;

    private static readonly string[] BoundaryNamespaces =
    [
        "Inceptus.DocumentEngine.Contracts.Documents",
        "Inceptus.DocumentEngine.Contracts.Metadata",
        "Inceptus.DocumentEngine.Contracts.Properties",
        "Inceptus.DocumentEngine.Contracts.Semantics",
        "Inceptus.DocumentEngine.Contracts.Visuals",
    ];

    [Fact]
    public void DocumentSnapshotHasExactlyTheApprovedAuthoritativeShape()
    {
        var actualProperties = typeof(DocumentSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(DocumentSnapshot.DocumentId)] = typeof(DocumentId),
            [nameof(DocumentSnapshot.Revision)] = typeof(DocumentRevision),
            [nameof(DocumentSnapshot.SemanticModel)] = typeof(SemanticModelSnapshot),
            [nameof(DocumentSnapshot.VisualModel)] = typeof(VisualModelSnapshot),
            [nameof(DocumentSnapshot.Metadata)] = typeof(DocumentMetadataSnapshot),
            [nameof(DocumentSnapshot.Publication)] = typeof(DocumentPublicationSnapshot),
        };

        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Contains(typeof(IDocumentView), typeof(DocumentSnapshot).GetInterfaces());
    }

    [Fact]
    public void ComponentSnapshotsDeclareWholeDocumentIdentityAndRevision()
    {
        Type[] componentTypes =
        [
            typeof(SemanticModelSnapshot),
            typeof(VisualModelSnapshot),
            typeof(DocumentMetadataSnapshot),
        ];

        foreach (var componentType in componentTypes)
        {
            Assert.Equal(
                typeof(DocumentId),
                componentType.GetProperty(nameof(ISemanticModelView.DocumentId))?.PropertyType);
            Assert.Equal(
                typeof(DocumentRevision),
                componentType.GetProperty(nameof(ISemanticModelView.Revision))?.PropertyType);
        }
    }

    [Fact]
    public void PublicDocumentBoundaryPropertiesHaveNoSetters()
    {
        foreach (var type in GetBoundaryTypes())
        {
            foreach (var property in type.GetProperties(
                         BindingFlags.Public | BindingFlags.Instance |
                         BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.Null(property.SetMethod);
            }
        }
    }

    [Fact]
    public void ReadOnlyViewsAndSnapshotsExposeNoMutationOperations()
    {
        string[] forbiddenPrefixes =
        [
            "Add",
            "Begin",
            "Clear",
            "Commit",
            "Create",
            "Delete",
            "Insert",
            "Mutate",
            "Remove",
            "Replace",
            "Rollback",
            "Set",
            "Update",
        ];

        foreach (var type in GetBoundaryTypes())
        {
            var methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName);

            foreach (var method in methods)
            {
                Assert.DoesNotContain(
                    forbiddenPrefixes,
                    prefix => method.Name.StartsWith(prefix, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void PublicDataCollectionsUseNoMutableCollectionContracts()
    {
        foreach (var type in GetBoundaryTypes())
        {
            var returnedTypes = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(property => property.PropertyType)
                .Concat(type
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(method => !method.IsSpecialName)
                    .Select(method => method.ReturnType));

            Assert.DoesNotContain(returnedTypes, IsMutableCollectionContract);
        }
    }

    [Fact]
    public void PublicContractConstructionUsesNoObjectValuedOrJsonData()
    {
        Type[] forbiddenDataTypes =
        [
            typeof(object),
            typeof(Type),
        ];

        foreach (var type in ContractsAssembly.GetExportedTypes())
        {
            var dataTypes = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(property => ExpandType(property.PropertyType))
                .Concat(type
                    .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .SelectMany(constructor => constructor.GetParameters())
                    .SelectMany(parameter => ExpandType(parameter.ParameterType)))
                .ToArray();

            Assert.DoesNotContain(dataTypes, candidate => forbiddenDataTypes.Contains(candidate));
            Assert.DoesNotContain(dataTypes, IsJsonType);
        }

        var propertyValueType = typeof(PropertyMap)
            .GetInterfaces()
            .Single(type =>
                type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            .GetGenericArguments()[1];

        Assert.Equal(typeof(PropertyValue), propertyValueType);
    }

    [Fact]
    public void ContractAssemblyContainsNoBrowserCanvasBpmnOrJsonLeak()
    {
        var referencedAssemblyNames = ContractsAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();
        var signatureTypes = GetPublicSignatureTypes(ContractsAssembly).ToArray();

        Assert.DoesNotContain("System.Text.Json", referencedAssemblyNames);
        Assert.DoesNotContain(
            referencedAssemblyNames,
            name => name.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal));
        Assert.DoesNotContain(signatureTypes, IsForbiddenBrowserBpmnOrJsonType);

        string[] forbiddenOwnNameFragments =
        [
            "Bpmn",
            "EndEvent",
            "HTMLCanvas",
            "Json",
            "SequenceFlow",
            "StartEvent",
        ];

        foreach (var type in ContractsAssembly.GetExportedTypes())
        {
            Assert.DoesNotContain(
                forbiddenOwnNameFragments,
                fragment => type.FullName?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true);

            foreach (var member in type.GetMembers(
                         BindingFlags.Public | BindingFlags.Instance |
                         BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(
                    forbiddenOwnNameFragments,
                    fragment => member.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void SemanticTypesUseStableIdentifiersRatherThanClrTypes()
    {
        Assert.Equal(
            typeof(SemanticTypeId),
            typeof(SemanticElementSnapshot).GetProperty(nameof(SemanticElementSnapshot.TypeId))?.PropertyType);
        Assert.Equal(
            typeof(SemanticTypeId),
            typeof(SemanticRelationshipSnapshot).GetProperty(nameof(SemanticRelationshipSnapshot.TypeId))?.PropertyType);

        var constructorDataTypes = new[]
        {
            typeof(SemanticElementSnapshot),
            typeof(SemanticRelationshipSnapshot),
        }.SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .SelectMany(parameter => ExpandType(parameter.ParameterType));

        Assert.DoesNotContain(typeof(Type), constructorDataTypes);
    }

    [Fact]
    public void StringIdentifiersExposeNoDefaultOrImplicitGenerationApi()
    {
        var identifierTypes = ContractsAssembly
            .GetExportedTypes()
            .Where(type =>
                type.Namespace == "Inceptus.DocumentEngine.Contracts.Primitives" &&
                type.Name.EndsWith("Id", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(identifierTypes);

        foreach (var identifierType in identifierTypes)
        {
            var constructor = Assert.Single(identifierType.GetConstructors());
            var parameter = Assert.Single(constructor.GetParameters());
            Assert.Equal(typeof(string), parameter.ParameterType);

            var generatedValues = identifierType
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && method.ReturnType == identifierType)
                .ToArray();
            var generatedProperties = identifierType
                .GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(property => property.PropertyType == identifierType)
                .ToArray();

            Assert.Empty(generatedValues);
            Assert.Empty(generatedProperties);
            Assert.DoesNotContain(
                identifierType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly),
                method => method.Name is "op_Implicit" or "op_Explicit");
        }
    }

    private static Type[] GetBoundaryTypes() =>
        ContractsAssembly
            .GetExportedTypes()
            .Where(type => BoundaryNamespaces.Contains(type.Namespace, StringComparer.Ordinal))
            .ToArray();

    private static bool IsMutableCollectionContract(Type type)
    {
        if (type.IsArray || type == typeof(IList) || type == typeof(IDictionary) ||
            type == typeof(ICollection))
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        Type[] forbiddenDefinitions =
        [
            typeof(List<>),
            typeof(Dictionary<,>),
            typeof(IList<>),
            typeof(IDictionary<,>),
            typeof(ICollection<>),
        ];

        return forbiddenDefinitions.Contains(definition);
    }

    private static bool IsJsonType(Type type) =>
        type.Namespace?.StartsWith("System.Text.Json", StringComparison.Ordinal) == true ||
        type.Name.Contains("Json", StringComparison.Ordinal);

    private static bool IsForbiddenExternalType(Type type)
    {
        string[] forbiddenNamespacePrefixes =
        [
            "Microsoft.AspNetCore.Components",
            "Microsoft.JSInterop",
            "System.Runtime.InteropServices.JavaScript",
            "System.Text.Json",
        ];
        string[] forbiddenTypeNames =
        [
            "CanvasGradient",
            "CanvasPattern",
            "CanvasRenderingContext2D",
            "DOMEvent",
            "DOMNode",
            "ElementReference",
            "HTMLElement",
            "HTMLCanvasElement",
            "ImageBitmap",
            "Path2D",
            "SVGElement",
            "WebGLRenderingContext",
        ];

        return forbiddenNamespacePrefixes.Any(prefix =>
                   type.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true) ||
               forbiddenTypeNames.Contains(type.Name, StringComparer.Ordinal) ||
               type.Namespace?.Contains("Bpmn", StringComparison.OrdinalIgnoreCase) == true ||
               type.Namespace?.Contains("Canvas2D", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsForbiddenBrowserBpmnOrJsonType(Type type)
    {
        string[] forbiddenNamespacePrefixes =
        [
            "Microsoft.AspNetCore.Components",
            "Microsoft.JSInterop",
            "System.Runtime.InteropServices.JavaScript",
            "System.Text.Json",
        ];
        string[] forbiddenTypeNames =
        [
            "CanvasGradient",
            "CanvasPattern",
            "CanvasRenderingContext2D",
            "DOMEvent",
            "DOMNode",
            "ElementReference",
            "HTMLElement",
            "HTMLCanvasElement",
            "ImageBitmap",
            "Path2D",
            "SVGElement",
            "WebGLRenderingContext",
        ];

        return forbiddenNamespacePrefixes.Any(prefix =>
                   type.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true) ||
               forbiddenTypeNames.Contains(type.Name, StringComparer.Ordinal) ||
               type.Namespace?.Contains("Bpmn", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var expanded in ExpandType(type))
            {
                yield return expanded;
            }

            foreach (var constructor in type.GetConstructors())
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    foreach (var expanded in ExpandType(parameter.ParameterType))
                    {
                        yield return expanded;
                    }
                }
            }

            foreach (var property in type.GetProperties(
                         BindingFlags.Public | BindingFlags.Instance |
                         BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var expanded in ExpandType(property.PropertyType))
                {
                    yield return expanded;
                }
            }

            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance |
                         BindingFlags.Static | BindingFlags.DeclaredOnly))
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
