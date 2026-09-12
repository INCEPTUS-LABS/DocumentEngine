using System.Collections;
using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseHCanvas2DSceneArchitectureTests
{
    private static readonly Assembly ContractsAssembly = typeof(ICanvas2DSceneContributor).Assembly;
    private static readonly Assembly RuntimeAssembly = typeof(Document).Assembly;
    private static readonly Assembly CanvasAssembly = typeof(Canvas2DSceneBuilder).Assembly;

    [Fact]
    public void FrameworkOwnsOneConcreteSceneBuilderWithLegacyAndProfileAwareInputs()
    {
        Assert.True(typeof(Canvas2DSceneBuilder).IsPublic);
        Assert.True(typeof(Canvas2DSceneBuilder).IsSealed);
        Assert.False(typeof(Canvas2DSceneBuilder).IsInterface);
        Assert.DoesNotContain(ContractsAssembly.GetExportedTypes(), type =>
            type.Name is "Canvas2DSceneBuilder" or "ICanvas2DSceneBuilder");
        Assert.DoesNotContain(RuntimeAssembly.GetTypes(), type =>
            type.Name.Contains("SceneBuilder", StringComparison.Ordinal));

        var builds = typeof(Canvas2DSceneBuilder).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => method.Name == "Build")
            .ToArray();
        Assert.Equal(2, builds.Length);
        Assert.All(builds, build => Assert.Equal(
            typeof(Canvas2DSceneBuildResult),
            build.ReturnType));
        var legacyBuild = Assert.Single(builds, build => build.GetParameters().Length == 5);
        Assert.Equal(
            [
                typeof(ProjectedGraph),
                typeof(LayoutResult),
                typeof(RoutingResult),
                typeof(VisualModelSnapshot),
                typeof(EditorStateSnapshot),
            ],
            legacyBuild.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.DoesNotContain(legacyBuild.GetParameters(), parameter =>
            parameter.ParameterType == typeof(DocumentSnapshot) ||
            parameter.ParameterType == typeof(SemanticModelSnapshot) ||
            parameter.ParameterType == typeof(DocumentMetadataSnapshot));
        var profileAwareBuild = Assert.Single(
            builds,
            build => build.GetParameters().Length == 8);
        Assert.Equal(
            [
                typeof(DocumentSnapshot),
                typeof(DocumentScopeId),
                typeof(ModelProfileViewStateSnapshot),
                typeof(ProjectedGraph),
                typeof(LayoutResult),
                typeof(RoutingResult),
                typeof(VisualModelSnapshot),
                typeof(EditorStateSnapshot),
            ],
            profileAwareBuild.GetParameters().Select(
                static parameter => parameter.ParameterType));
    }

    [Fact]
    public void FinalSceneIsGeneratedOnlyInsideCanvas2DAssembly()
    {
        Assert.Equal("Inceptus.DocumentEngine.Canvas2D.Scene", typeof(Canvas2DScene).Namespace);
        Assert.True(typeof(Canvas2DScene).IsSealed);
        Assert.Empty(typeof(Canvas2DScene).GetConstructors());
        Assert.DoesNotContain(ContractsAssembly.GetExportedTypes(), type =>
            type.FullName == typeof(Canvas2DScene).FullName);
        Assert.DoesNotContain(RuntimeAssembly.GetTypes(), type => type.Name == nameof(Canvas2DScene));
        Assert.DoesNotContain(typeof(ICanvas2DSceneContributor).GetMethods(), method =>
            method.ReturnType == typeof(Canvas2DScene));
    }

    [Fact]
    public void ContributorExtensionBoundaryLivesInContractsAndExposesImmutableDataOnly()
    {
        Type[] contributionTypes =
        [
            typeof(ICanvas2DSceneContributor),
            typeof(Canvas2DSceneContributionContext),
            typeof(Canvas2DSceneContribution),
            typeof(Canvas2DCanonicalSceneItemVisualOverride),
            typeof(Canvas2DSceneContributionResult),
            typeof(Canvas2DSceneContributorDescriptor),
            typeof(Canvas2DSceneContributorRegistration),
        ];

        Assert.All(contributionTypes, type => Assert.Equal(ContractsAssembly, type.Assembly));
        Assert.DoesNotContain(contributionTypes.SelectMany(GetPublicSignatureTypes), type =>
            type.Assembly == RuntimeAssembly ||
            type == typeof(Document) ||
            type == typeof(DocumentSnapshot) ||
            type == typeof(SemanticModelSnapshot) ||
            type == typeof(DocumentMetadataSnapshot) ||
            type == typeof(IServiceProvider) ||
            typeof(Delegate).IsAssignableFrom(type));
        Assert.DoesNotContain(CanvasAssembly.GetExportedTypes(), type =>
            type.Name.Contains("ContributorRegistry", StringComparison.Ordinal));
    }

    [Fact]
    public void SceneContractsExposeNoBrowserGraphicsDomOrMutableCollectionTypes()
    {
        var phaseHTypes = ContractsAssembly.GetExportedTypes()
            .Where(type => type.Namespace == "Inceptus.DocumentEngine.Contracts.Canvas2D")
            .Concat(CanvasAssembly.GetExportedTypes().Where(type =>
                type.Namespace == "Inceptus.DocumentEngine.Canvas2D.Scene"))
            .ToArray();
        var signatureTypes = phaseHTypes.SelectMany(GetPublicSignatureTypes).ToArray();

        Assert.DoesNotContain(signatureTypes, IsBrowserOrDomType);
        Assert.DoesNotContain(signatureTypes, type => type == typeof(IServiceProvider));
        Assert.DoesNotContain(signatureTypes, type => typeof(Delegate).IsAssignableFrom(type));
        Assert.DoesNotContain(signatureTypes, IsMutableCollectionContract);
        Assert.All(
            phaseHTypes.SelectMany(type => type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void SceneBoundaryIntroducesNoRendererBackendSelectionOrDrawingApi()
    {
        string[] forbiddenTypeFragments =
        [
            "IRenderer",
            "Renderer",
            "RenderingBackend",
            "RendererRegistry",
            "RendererSelector",
        ];
        string[] forbiddenMembers =
        [
            "Draw",
            "Paint",
            "Render",
            "RegisterRenderer",
            "ResolveRenderer",
            "SelectRenderer",
        ];

        var sceneBoundaryTypes = ContractsAssembly.GetExportedTypes()
            .Where(type => type.Namespace == "Inceptus.DocumentEngine.Contracts.Canvas2D")
            .Concat(CanvasAssembly.GetTypes().Where(type =>
                type.Namespace == "Inceptus.DocumentEngine.Canvas2D.Scene"));

        foreach (var type in sceneBoundaryTypes)
        {
            Assert.DoesNotContain(forbiddenTypeFragments, fragment =>
                type.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(type.GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly), member =>
                    forbiddenMembers.Any(name =>
                        member.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void UpstreamModelsAndRuntimeRemainSceneFree()
    {
        Type[] upstreamContractTypes =
        [
            typeof(DocumentSnapshot),
            typeof(SemanticModelSnapshot),
            typeof(VisualModelSnapshot),
            typeof(DocumentMetadataSnapshot),
            typeof(ProjectedGraph),
            typeof(LayoutResult),
            typeof(RoutingResult),
        ];

        Assert.DoesNotContain(upstreamContractTypes.SelectMany(GetPublicSignatureTypes), type =>
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.Canvas2D",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Canvas2D",
                StringComparison.Ordinal) == true);

        var editorStateSignatureTypes = GetPublicSignatureTypes(typeof(EditorStateSnapshot)).ToArray();
        Assert.DoesNotContain(editorStateSignatureTypes, type =>
            (type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.Canvas2D",
                StringComparison.Ordinal) == true ||
             type.Namespace?.StartsWith(
                 "Inceptus.DocumentEngine.Canvas2D",
                 StringComparison.Ordinal) == true));
        Assert.DoesNotContain(RuntimeAssembly.GetTypes(), type =>
            type.Namespace?.Contains("Canvas2D", StringComparison.OrdinalIgnoreCase) == true ||
            type.Name.Contains("Scene", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SceneImplementationDoesNotInvokeUpstreamEnginesCommandsHistoryOrPersistence()
    {
        var sceneTypes = CanvasAssembly.GetTypes()
            .Where(type => type.Namespace == "Inceptus.DocumentEngine.Canvas2D.Scene")
            .ToArray();
        var implementationSignatureTypes = sceneTypes
            .SelectMany(GetDeclaredImplementationSignatureTypes)
            .ToArray();

        Assert.DoesNotContain(implementationSignatureTypes, type =>
            type.Assembly == RuntimeAssembly ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Bpmn", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Blazor", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("System.Text.Json", StringComparison.Ordinal) == true ||
            IsBrowserOrDomType(type) ||
            type.Name.Contains("Renderer", StringComparison.OrdinalIgnoreCase));

        var sceneDirectory = Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sceneDirectory, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
        string[] forbiddenFragments =
        [
            "CommandProcessor",
            "DocumentEngine.Runtime.Commands",
            "HistoryManager",
            "LayoutEngine",
            "ProjectionEngine",
            "RoutingEngine",
            "System.Text.Json",
        ];

        Assert.DoesNotContain(forbiddenFragments, fragment =>
            source.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void CanonicalVisualOverrideIsNotationNeutralAndCannotReplaceInteractionOwnership()
    {
        var overrideType = typeof(Canvas2DCanonicalSceneItemVisualOverride);
        var properties = overrideType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var constructor = Assert.Single(overrideType.GetConstructors());

        Assert.Equal(
            [
                typeof(Inceptus.DocumentEngine.Contracts.Primitives.SceneObjectId),
                typeof(Canvas2DSceneGeometry),
                typeof(Canvas2DSceneStyle),
            ],
            constructor.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.Equal(
            [
                nameof(Canvas2DCanonicalSceneItemVisualOverride.Geometry),
                nameof(Canvas2DCanonicalSceneItemVisualOverride.Style),
                nameof(Canvas2DCanonicalSceneItemVisualOverride.TargetSceneObjectId),
            ],
            properties.Select(static property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(properties, property =>
            property.Name.Contains("Metadata", StringComparison.Ordinal) ||
            property.Name.Contains("Origin", StringComparison.Ordinal) ||
            property.Name.Contains("Identity", StringComparison.Ordinal) ||
            property.Name.Contains("Transform", StringComparison.Ordinal) ||
            property.Name.Contains("Bounds", StringComparison.Ordinal));

        var implementationSource = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(
                        RepositoryRoot,
                        "src",
                        "Inceptus.DocumentEngine.Canvas2D"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
        Assert.DoesNotContain("Bpmn", implementationSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Notation", implementationSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuilderDefensivelyRejectsDuplicateSceneObjectIds()
    {
        var inputs = Inceptus.DocumentEngine.UnitTests.Canvas2D.Canvas2DSceneTestData.Create();
        var item = new Canvas2DSceneItem(
            Canvas2DSceneObjectIdentity.ForProjected(inputs.Graph.Nodes[0].Id, "duplicate"),
            Canvas2DSceneLayer.Content,
            0,
            Canvas2DSceneGeometry.Rectangle(inputs.Layout.Nodes[0].Bounds),
            new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.SemanticElement |
                Canvas2DSceneOriginCategory.ProjectedRuntimeObject,
                inputs.Graph.Nodes[0].Source.SemanticElementId,
                projectedObjectId: inputs.Graph.Nodes[0].Id));
        var items = new List<Canvas2DSceneItem> { item, item };
        var diagnostics = new List<Diagnostic>();
        var validate = typeof(Canvas2DSceneBuilder).GetMethod(
            "ValidateAndOrderSceneItems",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(validate);
        validate.Invoke(
            null,
            [
                inputs.Graph,
                inputs.Routing,
                inputs.VisualModel,
                items,
                diagnostics,
                null,
                null,
            ]);

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == Canvas2DSceneDiagnosticCodes.DuplicateSceneObjectId);
    }

    private static IEnumerable<Type> GetDeclaredImplementationSignatureTypes(Type type)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(Flags))
        {
            foreach (var expanded in ExpandType(field.FieldType))
            {
                yield return expanded;
            }
        }

        foreach (var constructor in type.GetConstructors(Flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }

        foreach (var property in type.GetProperties(Flags))
        {
            foreach (var expanded in ExpandType(property.PropertyType))
            {
                yield return expanded;
            }
        }

        foreach (var method in type.GetMethods(Flags))
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

    private static bool IsBrowserOrDomType(Type type)
    {
        string[] forbiddenNames =
        [
            "CanvasGradient",
            "CanvasPattern",
            "CanvasRenderingContext2D",
            "DOMEvent",
            "HTMLElement",
            "HTMLCanvasElement",
            "ImageBitmap",
            "Path2D",
        ];

        return forbiddenNames.Contains(type.Name, StringComparer.Ordinal) ||
            type.Namespace?.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "System.Runtime.InteropServices.JavaScript",
                StringComparison.Ordinal) == true;
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

        return type.GetGenericTypeDefinition() == typeof(List<>) ||
            type.GetGenericTypeDefinition() == typeof(Dictionary<,>) ||
            type.GetGenericTypeDefinition() == typeof(IList<>) ||
            type.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
            type.GetGenericTypeDefinition() == typeof(ICollection<>);
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        yield return type;
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

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var expanded in ExpandType(element))
            {
                yield return expanded;
            }
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var expanded in ExpandType(argument))
            {
                yield return expanded;
            }
        }
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
