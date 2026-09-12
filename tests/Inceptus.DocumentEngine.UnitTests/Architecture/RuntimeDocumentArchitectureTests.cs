using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.EditorState;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class RuntimeDocumentArchitectureTests
{
    private const string ContractsAssemblyName = "Inceptus.DocumentEngine.Contracts";
    private const string RuntimeAssemblyName = "Inceptus.DocumentEngine.Runtime";

    private static readonly Assembly RuntimeAssembly = typeof(Document).Assembly;

    [Fact]
    public void RuntimeExportsExactlyTheApprovedPublicTypesThroughPhaseN102()
    {
        string[] expectedTypeNames =
        [
            "Inceptus.DocumentEngine.Runtime.Commands.CommandProcessor",
            "Inceptus.DocumentEngine.Runtime.Documents.Document",
            "Inceptus.DocumentEngine.Runtime.Documents.DocumentConstructionResult",
            "Inceptus.DocumentEngine.Runtime.Documents.DocumentFactory",
            "Inceptus.DocumentEngine.Runtime.Documents.DocumentReconstructor",
            "Inceptus.DocumentEngine.Runtime.Documents.NativeDocumentSerializer",
            "Inceptus.DocumentEngine.Runtime.EditorState.EditorStateStore",
            "Inceptus.DocumentEngine.Runtime.History.HistoryManager",
            "Inceptus.DocumentEngine.Runtime.Layout.LayoutEngine",
            "Inceptus.DocumentEngine.Runtime.Projection.ProjectionEngine",
            "Inceptus.DocumentEngine.Runtime.Routing.RoutingEngine",
            "Inceptus.DocumentEngine.Runtime.Validation.ModelValidationEngine",
            "Inceptus.DocumentEngine.Runtime.Validation.RoutingModelValidationIssueProvider",
        ];
        var actualTypeNames = RuntimeAssembly
            .GetExportedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal);

        Assert.Equal(expectedTypeNames.Order(StringComparer.Ordinal), actualTypeNames);
        Assert.DoesNotContain(RuntimeAssembly.GetExportedTypes(), type => type.IsInterface);
    }

    [Fact]
    public void RuntimeKeepsDocumentConstructionControlled()
    {
        Assert.True(typeof(Document).IsSealed);
        Assert.Empty(typeof(Document).GetConstructors());
        Assert.True(typeof(DocumentConstructionResult).IsSealed);
        Assert.Empty(typeof(DocumentConstructionResult).GetConstructors());
        Assert.True(typeof(DocumentFactory).IsAbstract && typeof(DocumentFactory).IsSealed);
        Assert.True(typeof(DocumentReconstructor).IsAbstract && typeof(DocumentReconstructor).IsSealed);

        var factoryMethods = typeof(DocumentFactory)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();
        var create = Assert.Single(factoryMethods, method => method.Name == "Create");
        var createEmpty = Assert.Single(factoryMethods, method => method.Name == "CreateEmpty");
        var reconstruct = Assert.Single(typeof(DocumentReconstructor).GetMethods(
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));

        Assert.Equal(typeof(DocumentConstructionResult), create.ReturnType);
        Assert.Equal(
            [typeof(DocumentSnapshot), typeof(IElementConnectorAnchorPolicyProvider)],
            create.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.False(create.GetParameters()[0].IsOptional);
        Assert.True(create.GetParameters()[1].IsOptional);
        Assert.Equal(typeof(DocumentConstructionResult), createEmpty.ReturnType);
        Assert.Equal(
            [
                typeof(DocumentId),
                typeof(PropertyMap),
                typeof(PropertyMap),
                typeof(IElementConnectorAnchorPolicyProvider),
            ],
            createEmpty.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.False(createEmpty.GetParameters()[0].IsOptional);
        Assert.True(createEmpty.GetParameters()[1].IsOptional);
        Assert.True(createEmpty.GetParameters()[2].IsOptional);
        Assert.True(createEmpty.GetParameters()[3].IsOptional);
        Assert.Equal("Reconstruct", reconstruct.Name);
        Assert.Equal(typeof(DocumentConstructionResult), reconstruct.ReturnType);
        Assert.Equal(
            [typeof(DocumentSnapshot), typeof(IElementConnectorAnchorPolicyProvider)],
            reconstruct.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.False(reconstruct.GetParameters()[0].IsOptional);
        Assert.True(reconstruct.GetParameters()[1].IsOptional);
    }

    [Fact]
    public void CommandProcessorExposesOnlyTheApprovedExecutionSurface()
    {
        var constructor = Assert.Single(typeof(CommandProcessor).GetConstructors());
        var constructorParameters = constructor.GetParameters();
        var methods = typeof(CommandProcessor).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var execute = Assert.Single(methods, method =>
            method.Name == nameof(CommandProcessor.ExecuteAsync));
        var captureDiagnostics = Assert.Single(methods, method =>
            method.Name == nameof(CommandProcessor.CaptureDispatchDiagnostics));

        Assert.True(typeof(CommandProcessor).IsSealed);
        Assert.Equal(
            [
                typeof(IEnumerable<CommandHandlerRegistration>),
                typeof(IEnumerable<CommandValidatorRegistration>),
                typeof(IEnumerable<IDocumentChangedSubscriber>),
                typeof(IEnumerable<CommandHistoryPolicyRegistration>),
                typeof(IElementConnectorAnchorPolicyProvider),
            ],
            constructorParameters.Select(parameter => parameter.ParameterType));
        Assert.All(constructorParameters, parameter => Assert.True(parameter.IsOptional));
        Assert.Equal(nameof(CommandProcessor.ExecuteAsync), execute.Name);
        Assert.Equal(typeof(ValueTask<CommandExecutionResult>), execute.ReturnType);
        Assert.Equal(
            [typeof(Document), typeof(ICommand), typeof(CancellationToken)],
            execute.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(execute.GetParameters()[^1].IsOptional);
        Assert.Equal(typeof(ImmutableArray<Diagnostic>), captureDiagnostics.ReturnType);
        Assert.Empty(captureDiagnostics.GetParameters());
        Assert.Empty(typeof(CommandProcessor).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Empty(typeof(CommandProcessor).GetEvents(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void RuntimeDocumentExposesOnlyReadOperationsAndAuthoritativeViews()
    {
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(Document.DocumentId)] = typeof(DocumentId),
            [nameof(Document.Revision)] = typeof(DocumentRevision),
            [nameof(Document.SemanticModel)] = typeof(ISemanticModelView),
            [nameof(Document.VisualModel)] = typeof(IVisualModelView),
            [nameof(Document.Metadata)] = typeof(IDocumentMetadataView),
            [nameof(Document.Publication)] = typeof(DocumentPublicationSnapshot),
        };
        var actualProperties = typeof(Document)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        var methods = typeof(Document)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();

        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(actualProperties.Keys, propertyName =>
            Assert.Null(typeof(Document).GetProperty(propertyName)?.SetMethod));
        Assert.Contains(typeof(IDocumentView), typeof(Document).GetInterfaces());
        var captureSnapshot = Assert.Single(methods);
        Assert.Equal(nameof(Document.CaptureSnapshot), captureSnapshot.Name);
        Assert.Equal(typeof(DocumentSnapshot), captureSnapshot.ReturnType);
        Assert.Empty(captureSnapshot.GetParameters());
        Assert.Empty(typeof(Document).GetEvents(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Empty(typeof(Document).GetFields(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void ConstructionResultHasAnImmutableAndCoherentPublicShape()
    {
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(DocumentConstructionResult.Succeeded)] = typeof(bool),
            [nameof(DocumentConstructionResult.Document)] = typeof(Document),
            [nameof(DocumentConstructionResult.Diagnostics)] = typeof(ImmutableArray<Diagnostic>),
        };
        var actualProperties = typeof(DocumentConstructionResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(
            typeof(DocumentConstructionResult).GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(
            typeof(DocumentConstructionResult).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => !method.IsSpecialName);
        Assert.Empty(typeof(DocumentConstructionResult).GetEvents(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void RuntimeReferencesOnlyContractsAmongProductionAssemblies()
    {
        var productionReferences = RuntimeAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name?.StartsWith("Inceptus.DocumentEngine.", StringComparison.Ordinal) == true)
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([ContractsAssemblyName], productionReferences);
        Assert.Equal(RuntimeAssemblyName, RuntimeAssembly.GetName().Name);
    }

    [Fact]
    public void RuntimePublicSurfaceContainsNoMutationOrIdentifierGenerationCapability()
    {
        string[] forbiddenDocumentMethodPrefixes =
        [
            "Add",
            "Apply",
            "Begin",
            "Clear",
            "Commit",
            "Delete",
            "Insert",
            "Mutate",
            "Remove",
            "Replace",
            "Rollback",
            "Set",
            "Update",
        ];
        var documentMethods = typeof(Document)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();

        Assert.DoesNotContain(documentMethods, method =>
            forbiddenDocumentMethodPrefixes.Any(prefix =>
                method.Name.StartsWith(prefix, StringComparison.Ordinal)));

        var publicStaticMethods = RuntimeAssembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .ToArray();

        Assert.DoesNotContain(publicStaticMethods, method => method.ReturnType == typeof(DocumentId));
        Assert.DoesNotContain(publicStaticMethods, method =>
            method.Name.Contains("NewId", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("GenerateId", StringComparison.OrdinalIgnoreCase) ||
            method.Name.Contains("Random", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(DocumentFactory).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly),
            method => method.GetParameters().Length == 0);
    }

    [Fact]
    public void RuntimePublicSurfaceExposesNoMutableCollectionsDelegatesOrObjectValuedData()
    {
        foreach (var type in RuntimeAssembly.GetExportedTypes())
        {
            var properties = type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly);
            var methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly);
            var publicDataTypes = properties
                .SelectMany(property => ExpandType(property.PropertyType))
                .Concat(methods
                    .Where(method => !method.IsSpecialName)
                    .SelectMany(method => method.GetParameters())
                    .SelectMany(parameter => ExpandType(parameter.ParameterType)))
                .ToArray();
            var returnedCollectionTypes = properties
                .Select(property => property.PropertyType)
                .Concat(methods
                    .Where(method => !method.IsSpecialName)
                    .Select(method => method.ReturnType));

            Assert.All(properties, property => Assert.Null(property.SetMethod));
            Assert.DoesNotContain(returnedCollectionTypes, IsMutableCollectionContract);
            Assert.DoesNotContain(publicDataTypes, dataType => dataType == typeof(object));
            Assert.DoesNotContain(publicDataTypes, dataType => dataType == typeof(Type));
            Assert.DoesNotContain(publicDataTypes, dataType =>
                typeof(Delegate).IsAssignableFrom(dataType));
        }
    }

    [Fact]
    public void RuntimeContainsNoBrowserCanvasOrBpmnApiAndJsonDoesNotLeakThroughPublicSignatures()
    {
        string[] forbiddenAssemblyPrefixes =
        [
            "Microsoft.AspNetCore.Components",
            "Microsoft.JSInterop",
            "System.Runtime.InteropServices.JavaScript",
        ];
        string[] forbiddenApiFragments =
        [
            "Bpmn",
            "Canvas2D",
            "EditingSession",
            "JavaScript",
            "Json",
            "Pipeline",
            "Renderer",
            "Scene",
        ];
        var referencedAssemblies = RuntimeAssembly.GetReferencedAssemblies();

        Assert.DoesNotContain(referencedAssemblies, reference =>
            forbiddenAssemblyPrefixes.Any(prefix =>
                reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true));
        Assert.DoesNotContain(GetPublicSignatureTypes(RuntimeAssembly), IsForbiddenExternalType);

        foreach (var type in RuntimeAssembly.GetExportedTypes())
        {
            Assert.DoesNotContain(forbiddenApiFragments, fragment =>
                type.FullName?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true);

            foreach (var member in type.GetMembers(
                         BindingFlags.Public | BindingFlags.Instance |
                         BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(forbiddenApiFragments, fragment =>
                    member.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void InternalStoresValidatorsAndMaterializersDoNotEscape()
    {
        string[] internalMechanismFragments =
        [
            "Builder",
            "DocumentInvariantValidator",
            "DocumentMaterializer",
            "DocumentSnapshotCloner",
            "DocumentState",
            "Mutable",
            "Store",
        ];

        Assert.DoesNotContain(RuntimeAssembly.GetExportedTypes(), type =>
            type != typeof(EditorStateStore) &&
            internalMechanismFragments.Any(fragment =>
                type.Name.Contains(fragment, StringComparison.Ordinal)));
        Assert.DoesNotContain(GetPublicSignatureTypes(RuntimeAssembly), signatureType =>
            signatureType.Assembly == RuntimeAssembly && !signatureType.IsPublic);
    }

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

        Type[] forbiddenDefinitions =
        [
            typeof(List<>),
            typeof(Dictionary<,>),
            typeof(IList<>),
            typeof(IDictionary<,>),
            typeof(ICollection<>),
        ];

        return forbiddenDefinitions.Contains(type.GetGenericTypeDefinition());
    }

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
            "CanvasRenderingContext2D",
            "ElementReference",
            "HTMLCanvasElement",
            "ImageBitmap",
            "Path2D",
        ];

        return forbiddenNamespacePrefixes.Any(prefix =>
                   type.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true) ||
               forbiddenTypeNames.Contains(type.Name, StringComparer.Ordinal) ||
               type.Namespace?.Contains("Bpmn", StringComparison.OrdinalIgnoreCase) == true ||
               type.Namespace?.Contains("Canvas2D", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var expanded in ExpandType(type))
            {
                yield return expanded;
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
