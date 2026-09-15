using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class SolutionArchitectureTests
{
    private const string Contracts = "Inceptus.DocumentEngine.Contracts";
    private const string Runtime = "Inceptus.DocumentEngine.Runtime";
    private const string Canvas2D = "Inceptus.DocumentEngine.Canvas2D";
    private const string Bpmn = "Inceptus.DocumentEngine.Bpmn";
    private const string BpmnBlazor = "Inceptus.DocumentEngine.Bpmn.Blazor";
    private const string Organizational = "Inceptus.DocumentEngine.Organizational";
    private const string Blazor = "Inceptus.DocumentEngine.Blazor";

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedProductionGraph =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Contracts] = [],
            [Runtime] = [Contracts],
            [Canvas2D] = [Contracts, Runtime],
            [Bpmn] = [Contracts],
            [Organizational] = [Contracts],
            [BpmnBlazor] = [Bpmn, Canvas2D, Contracts, Organizational, Runtime],
            [Blazor] = [BpmnBlazor, Canvas2D, Contracts, Runtime],
        };

    private static readonly string[] ProductionAssemblyNames =
        [Contracts, Runtime, Canvas2D, Bpmn, Organizational, BpmnBlazor, Blazor];

    [Fact]
    public void ProductionProjectReferencesMatchTheApprovedGraphExactly()
    {
        var projects = GetProductionProjectPaths();

        foreach (var (projectName, expectedReferences) in ExpectedProductionGraph)
        {
            var actualReferences = ReadProjectReferences(projects[projectName])
                .Select(path => Path.GetFileNameWithoutExtension(path)
                    ?? throw new InvalidOperationException("A project reference has no file name."))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
        }
    }

    [Fact]
    public void ProductionProjectSetContainsExactlyTheSevenApprovedProjects()
    {
        var actualProjects = GetProductionProjectPaths().Keys.Order(StringComparer.Ordinal);
        var expectedProjects = ExpectedProductionGraph.Keys.Order(StringComparer.Ordinal);

        Assert.Equal(expectedProjects, actualProjects);
    }

    [Fact]
    public void ProductionProjectGraphIsAcyclicAndAllReferencesResolve()
    {
        var projects = GetProductionProjectPaths();
        var graph = projects.ToDictionary(
            pair => pair.Key,
            pair => ReadProjectReferences(pair.Value)
                .Select(path => Path.GetFileNameWithoutExtension(path)
                    ?? throw new InvalidOperationException("A project reference has no file name."))
                .ToArray(),
            StringComparer.Ordinal);

        foreach (var references in graph.Values)
        {
            foreach (var reference in references)
            {
                Assert.True(graph.ContainsKey(reference), $"Unknown production project reference: {reference}.");
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in graph.Keys)
        {
            Assert.False(HasCycle(project, graph, visited, active), $"A cycle includes {project}.");
        }
    }

    [Fact]
    public void BuildConfigurationIsCentralAndTargetsNet10()
    {
        var buildProperties = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        var packageProperties = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Packages.props"));

        Assert.Equal("net10.0", ReadElementValue(buildProperties, "TargetFramework"));
        Assert.Equal("enable", ReadElementValue(buildProperties, "Nullable"));
        Assert.Equal("enable", ReadElementValue(buildProperties, "ImplicitUsings"));
        Assert.Equal("true", ReadElementValue(buildProperties, "TreatWarningsAsErrors"));
        Assert.Equal("true", ReadElementValue(buildProperties, "Deterministic"));
        Assert.Equal("true", ReadElementValue(buildProperties, "DeterministicSourcePaths"));
        Assert.Equal("$(MSBuildThisFileDirectory)=/_/", ReadElementValue(buildProperties, "PathMap"));
        Assert.Equal("true", ReadElementValue(buildProperties, "EnableNETAnalyzers"));
        Assert.Equal("14.0", ReadElementValue(buildProperties, "LangVersion"));
        Assert.Equal("true", ReadElementValue(packageProperties, "ManagePackageVersionsCentrally"));
    }

    [Fact]
    public void SharedPackageFamilyMetadataIsExplicit()
    {
        var buildProperties = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));

        Assert.Equal("0.1.5", ReadElementValue(buildProperties, "Version"));
        Assert.Equal("Robert Prokopczuk", ReadElementValue(buildProperties, "Authors"));

        var releaseTargets = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.targets"));
        Assert.Equal("MIT", ReadElementValue(releaseTargets, "PackageLicenseExpression"));
        Assert.Equal("Copyright (c) 2026 Inceptus Robert Prokopczuk",
            ReadElementValue(releaseTargets, "Copyright"));
        Assert.Equal("https://inceptus.online/bpmn/", ReadElementValue(releaseTargets, "PackageProjectUrl"));
        Assert.Equal("https://github.com/INCEPTUS-LABS/DocumentEngine",
            ReadElementValue(releaseTargets, "RepositoryUrl"));
        Assert.Equal("git", ReadElementValue(releaseTargets, "RepositoryType"));
        Assert.Equal("true", ReadElementValue(releaseTargets, "PublishRepositoryUrl"));
        Assert.Equal("portable", ReadElementValue(releaseTargets, "DebugType"));
        Assert.Equal("true", ReadElementValue(releaseTargets, "IncludeSymbols"));
        Assert.Equal("snupkg", ReadElementValue(releaseTargets, "SymbolPackageFormat"));
        Assert.Equal("README.md", ReadElementValue(releaseTargets, "PackageReadmeFile"));
        Assert.DoesNotContain(releaseTargets.Descendants(), element =>
            element.Name.LocalName is "RepositoryCommit" or "SourceRevisionId" or "PackageLicenseFile");
        Assert.All(releaseTargets.Root!.Elements(), group =>
            Assert.Equal("'$(IsPackable)' == 'true'", (string?)group.Attribute("Condition")));

        string[] releaseDocuments = ["README.md", "LICENSE", "RELEASE-NOTES.md", "THIRD-PARTY-NOTICES.md"];
        var packedFiles = releaseTargets.Descendants("None").ToArray();
        Assert.Equal(releaseDocuments.Order(StringComparer.Ordinal),
            packedFiles.Select(file => ((string)file.Attribute("Include")!)
                .Replace("$(MSBuildThisFileDirectory)", string.Empty, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
        Assert.All(packedFiles, file =>
        {
            Assert.Equal("true", (string?)file.Attribute("Pack"));
            Assert.Equal("/", (string?)file.Attribute("PackagePath"));
        });
        Assert.All(releaseDocuments, file => Assert.True(File.Exists(Path.Combine(RepositoryRoot, file))));
        Assert.Contains("Copyright (c) 2026 Inceptus Robert Prokopczuk",
            File.ReadAllText(Path.Combine(RepositoryRoot, "LICENSE")), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyCanvas2DRuntimeFriendDependencyHasAMatchingPackageConstraint()
    {
        var constraints = GetProductionProjectPaths().SelectMany(pair =>
            XDocument.Load(pair.Value).Descendants("_ProjectReferencesWithVersions")
                .Select(element => (Project: pair.Key, Element: element))).ToArray();
        var constraint = Assert.Single(constraints);
        Assert.Equal(Canvas2D, constraint.Project);
        Assert.Equal("@(_ProjectReferencesWithVersions)", (string?)constraint.Element.Attribute("Update"));
        Assert.Equal("'%(_ProjectReferencesWithVersions.Filename)' == 'Inceptus.DocumentEngine.Runtime'",
            (string?)constraint.Element.Attribute("Condition"));
        Assert.Equal("[$(PackageVersion)]", constraint.Element.Element("ProjectVersion")?.Value);
        Assert.Equal("_GetProjectReferenceVersions",
            (string?)constraint.Element.Ancestors("Target").Single().Attribute("AfterTargets"));
    }

    [Fact]
    public void ReusablePackageProjectsDeclareExplicitFoundationMetadata()
    {
        var expectedMetadata = new Dictionary<string, (string Description, string Tags)>(StringComparer.Ordinal)
        {
            [Contracts] =
            (
                "Immutable contracts and extension APIs for the Inceptus Document Engine.",
                "inceptus;document-engine;contracts"
            ),
            [Runtime] =
            (
                "Headless document, command, history, projection, layout, routing, validation, and native persistence runtime for the Inceptus Document Engine.",
                "inceptus;document-engine;runtime;headless"
            ),
            [Bpmn] =
            (
                "BPMN notation semantics, commands, validation, and plugin contributions for the Inceptus Document Engine.",
                "inceptus;document-engine;bpmn"
            ),
            [Organizational] =
            (
                "Organizational Profile semantics and plugin contributions for the Inceptus Document Engine.",
                "inceptus;document-engine;organizational"
            ),
            [Canvas2D] =
            (
                "Canvas2D browser editing session, interaction, scene, and renderer integration for the Inceptus Document Engine.",
                "inceptus;document-engine;canvas2d;browser"
            ),
            [BpmnBlazor] =
            (
                "BPMN modeler components and browser presentation integration for the Inceptus Document Engine.",
                "inceptus;document-engine;bpmn;blazor;modeler"
            ),
        };

        foreach (var (projectName, expected) in expectedMetadata)
        {
            var project = XDocument.Load(FindProject(projectName));

            Assert.Equal(projectName, ReadElementValue(project, "PackageId"));
            Assert.Equal(projectName, ReadElementValue(project, "AssemblyName"));
            Assert.Equal(projectName, ReadElementValue(project, "RootNamespace"));
            Assert.Equal("true", ReadElementValue(project, "IsPackable"));
            Assert.Equal(expected.Description, ReadElementValue(project, "Description"));
            Assert.Equal(expected.Tags, ReadElementValue(project, "PackageTags"));
            Assert.DoesNotContain(
                project.Descendants(),
                element => element.Name.LocalName is
                    "Version" or "VersionPrefix" or "VersionSuffix" or "PackageVersion" or "Authors");
        }
    }

    [Fact]
    public void ApplicationAndTestProjectsRemainExplicitlyNonPackable()
    {
        string[] nonPackableProjects =
        [
            Blazor,
            "Inceptus.DocumentEngine.UnitTests",
            "Inceptus.DocumentEngine.IntegrationTests",
        ];

        foreach (var projectName in nonPackableProjects)
        {
            var project = XDocument.Load(FindProject(projectName));

            Assert.Equal("false", ReadElementValue(project, "IsPackable"));
        }
    }

    [Fact]
    public void SdkAndCentralPackageVersionsArePinnedExactly()
    {
        using var globalConfiguration = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot, "global.json")));
        var sdk = globalConfiguration.RootElement.GetProperty("sdk");

        Assert.Equal("10.0.401", sdk.GetProperty("version").GetString());
        Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());

        var packageDocument = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Packages.props"));
        var actualVersions = packageDocument
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageVersion")
            .ToDictionary(
                element => (string?)element.Attribute("Include")
                    ?? throw new InvalidOperationException("A central package has no name."),
                element => (string?)element.Attribute("Version")
                    ?? throw new InvalidOperationException("A central package has no version."),
                StringComparer.Ordinal);
        var expectedVersions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Microsoft.AspNetCore.Components.Web"] = "10.0.5",
            ["Microsoft.AspNetCore.Components.WebAssembly"] = "10.0.5",
            ["Microsoft.AspNetCore.Components.WebAssembly.DevServer"] = "10.0.5",
            ["Microsoft.JSInterop"] = "10.0.5",
            ["Microsoft.Extensions.Localization"] = "10.0.5",
            ["Microsoft.NET.Test.Sdk"] = "18.0.1",
            ["xunit"] = "2.9.3",
            ["xunit.runner.visualstudio"] = "3.1.5",
        };

        Assert.Equal(
            expectedVersions.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualVersions.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void ProjectsDeclareOnlyTheExpectedDirectPackages()
    {
        var expectedPackages = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Contracts] = [],
            [Runtime] = [],
            [Canvas2D] = ["Microsoft.JSInterop"],
            [Bpmn] = [],
            [BpmnBlazor] =
            [
                "Microsoft.AspNetCore.Components.Web",
                "Microsoft.Extensions.Localization",
                "Microsoft.JSInterop",
            ],
            [Organizational] = [],
            [Blazor] =
            [
                "Microsoft.AspNetCore.Components.WebAssembly",
                "Microsoft.AspNetCore.Components.WebAssembly.DevServer",
            ],
            ["Inceptus.DocumentEngine.UnitTests"] =
            [
                "Microsoft.NET.Test.Sdk",
                "xunit",
                "xunit.runner.visualstudio",
            ],
            ["Inceptus.DocumentEngine.IntegrationTests"] =
            [
                "Microsoft.NET.Test.Sdk",
                "xunit",
                "xunit.runner.visualstudio",
            ],
        };

        foreach (var (projectName, expected) in expectedPackages)
        {
            var projectPath = FindProject(projectName);
            var document = XDocument.Load(projectPath);
            var packageReferences = document
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .ToArray();
            var actual = packageReferences
                .Select(element => (string?)element.Attribute("Include"))
                .Where(value => value is not null)
                .Cast<string>()
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
            Assert.All(packageReferences, reference => Assert.Null(reference.Attribute("Version")));
        }
    }

    [Fact]
    public void CompiledAssembliesDoNotContainForbiddenProductionDependencies()
    {
        var forbiddenReferences = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Contracts] = [Runtime, Canvas2D, Bpmn, Organizational, BpmnBlazor, Blazor],
            [Runtime] = [Canvas2D, Bpmn, Organizational, BpmnBlazor, Blazor],
            [Canvas2D] = [Bpmn, Organizational, BpmnBlazor, Blazor],
            [Bpmn] = [Runtime, Canvas2D, Organizational, BpmnBlazor, Blazor],
            [Organizational] = [Runtime, Canvas2D, Bpmn, BpmnBlazor, Blazor],
            [BpmnBlazor] = [Blazor],
        };

        foreach (var (assemblyName, forbidden) in forbiddenReferences)
        {
            var references = LoadProductionAssembly(assemblyName)
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            foreach (var forbiddenReference in forbidden)
            {
                Assert.DoesNotContain(forbiddenReference, references);
            }
        }
    }

    [Fact]
    public void UpstreamAssembliesReferenceNoBrowserOrJavaScriptInteropAssemblies()
    {
        string[] forbiddenPrefixes =
        [
            "Microsoft.AspNetCore.Components",
            "Microsoft.JSInterop",
            "System.Runtime.InteropServices.JavaScript",
        ];

        foreach (var assemblyName in new[] { Contracts, Runtime, Bpmn, Organizational })
        {
            var references = LoadProductionAssembly(assemblyName).GetReferencedAssemblies();

            Assert.DoesNotContain(
                references,
                reference => forbiddenPrefixes.Any(prefix =>
                    reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true));
        }
    }

    [Fact]
    public void UpstreamPublicSignaturesContainNoBrowserGraphicsOrDomTypes()
    {
        foreach (var assemblyName in new[] { Contracts, Runtime, Bpmn, Organizational })
        {
            var forbiddenTypes = GetPublicSignatureTypes(LoadProductionAssembly(assemblyName))
                .Where(IsBrowserOrDomType)
                .Select(type => type.FullName ?? type.Name)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.Empty(forbiddenTypes);
        }
    }

    [Fact]
    public void NoGenericRendererBackendRegistryOrSelectionApiExists()
    {
        const string ApprovedRenderer =
            "Inceptus.DocumentEngine.Canvas2D.Rendering.Canvas2DRenderer";
        string[] forbiddenTypeNameFragments =
        [
            "RenderingBackend",
            "RenderBackend",
            "RendererRegistry",
            "RendererSelector",
            "RendererSelection",
        ];
        string[] forbiddenMemberNames =
        [
            "GetRenderer",
            "RegisterRenderer",
            "RegisterRenderingBackend",
            "ResolveRenderer",
            "ResolveRenderingBackend",
            "SelectRenderer",
            "SelectRenderingBackend",
            "TryGetRenderer",
        ];

        var rendererTypes = new List<Type>();
        foreach (var assemblyName in ProductionAssemblyNames)
        {
            foreach (var type in LoadProductionAssembly(assemblyName).GetTypes())
            {
                if (type.Name.EndsWith("Renderer", StringComparison.Ordinal))
                {
                    rendererTypes.Add(type);
                }

                Assert.DoesNotContain(
                    forbiddenTypeNameFragments,
                    fragment => type.Name.Contains(fragment, StringComparison.Ordinal));

                var declaredMemberNames = type
                    .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(member => member.Name)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var memberName in forbiddenMemberNames)
                {
                    Assert.DoesNotContain(memberName, declaredMemberNames);
                }
            }
        }

        var renderer = Assert.Single(rendererTypes);
        Assert.Equal(ApprovedRenderer, renderer.FullName);
        Assert.True(renderer.IsClass);
        Assert.True(renderer.IsSealed);
        Assert.False(renderer.IsAbstract);
    }

    [Fact]
    public void PhaseATypesExposeNoPluginImplementableFrameworkEngine()
    {
        var contractEngineTypes = LoadProductionAssembly(Contracts)
            .GetExportedTypes()
            .Where(type => type.Name.Contains("Engine", StringComparison.Ordinal))
            .ToArray();
        var bpmnEngineImplementations = LoadProductionAssembly(Bpmn)
            .GetTypes()
            .Where(type =>
                type.BaseType?.Name.Contains("Engine", StringComparison.Ordinal) == true ||
                type.GetInterfaces().Any(@interface =>
                    @interface.Name.Contains("Engine", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(contractEngineTypes);
        Assert.Empty(bpmnEngineImplementations);
    }

    [Fact]
    public void ContractsExportExactlyTheApprovedPhaseHTypes()
    {
        string[] expectedTypeNames =
        [
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DCanonicalSceneItemVisualOverride",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DHitTestMode",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DHitTestPolicy",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneConfiguration",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneContribution",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneContributionContext",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneContributionResult",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneContributionStage",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneContributorDescriptor",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneContributorId",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneContributorRegistration",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneDiagnosticCodes",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneGeometry",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneGeometryKind",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneItem",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneLayer",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneObjectIdentity",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneOriginCategory",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneOriginTrace",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DScenePresentationContext",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DConnectorPresentationMapping",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DConnectorPresentationRoute",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DConnectorPresentationRoutingRequest",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialEditKind",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialEditRequest",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialEditPlanResult",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialEditPlannerCatalog",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialEditPlannerRegistration",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialPresentationPlan",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialRegion",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialRegionId",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSpatialVisualPlacement",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSceneStyle",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DSemanticSceneInteractionMetadata",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DTextAlignment",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.Canvas2DTextBaseline",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.ICanvas2DSceneContributor",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.ICanvas2DConnectorPresentationRouter",
            "Inceptus.DocumentEngine.Contracts.Canvas2D.ICanvas2DSpatialEditPlanner",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.AnchorConnectionCreationCatalog",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.AnchorConnectionCreationId",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.AnchorConnectionCreationPlan",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.AnchorConnectionCreationPlanResult",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.AnchorConnectionCreationRegistration",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.AnchorConnectionCreationRequest",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.AnchorConnectionCreationSourceRequest",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.AnchorConnectionTargetEligibilityRequest",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.ConnectorTargetAnchorAcquisition",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.ConnectorTargetEdgeResolver",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.IAnchorConnectionCreationCommandFactory",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.IAnchorConnectionTargetEligibility",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.TargetAnchorAcquisitionKind",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.TargetAnchorAcquisitionRejectionReason",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.TargetAnchorAcquisitionRequest",
            "Inceptus.DocumentEngine.Contracts.ConnectionCreation.TargetAnchorAcquisitionResult",
            "Inceptus.DocumentEngine.Contracts.Commands.AddConnectorAnchorCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.CompoundDocumentCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.AuthoritativeDocumentComponent",
            "Inceptus.DocumentEngine.Contracts.Commands.CommandCategory",
            "Inceptus.DocumentEngine.Contracts.Commands.CommandExecutionDiagnosticCodes",
            "Inceptus.DocumentEngine.Contracts.Commands.CommandExecutionResult",
            "Inceptus.DocumentEngine.Contracts.Commands.CommandExecutionStatus",
            "Inceptus.DocumentEngine.Contracts.Commands.CommandHandlerRegistration",
            "Inceptus.DocumentEngine.Contracts.Commands.CommandHandlerResult",
            "Inceptus.DocumentEngine.Contracts.Commands.CommandPipelineInvalidation",
            "Inceptus.DocumentEngine.Contracts.Commands.CommandValidationDiagnosticCodes",
            "Inceptus.DocumentEngine.Contracts.Commands.CommandValidationResult",
            "Inceptus.DocumentEngine.Contracts.Commands.CommandValidatorRegistration",
            "Inceptus.DocumentEngine.Contracts.Commands.CreateTopLevelDocumentScopeCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.DocumentChangedEvent",
            "Inceptus.DocumentEngine.Contracts.Commands.ICommand",
            "Inceptus.DocumentEngine.Contracts.Commands.ICommandPipelineInvalidation",
            "Inceptus.DocumentEngine.Contracts.Commands.ICommandHandler",
            "Inceptus.DocumentEngine.Contracts.Commands.ICommandEnvelopeValidator",
            "Inceptus.DocumentEngine.Contracts.Commands.ICommandValidator",
            "Inceptus.DocumentEngine.Contracts.Commands.IDocumentChangedSubscriber",
            "Inceptus.DocumentEngine.Contracts.Commands.MoveLabelCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.MoveVisualStateCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.MoveVisualStatesCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.ModelProfileAvailabilityChange",
            "Inceptus.DocumentEngine.Contracts.Commands.NodeGeometryPipelineImpact",
            "Inceptus.DocumentEngine.Contracts.Commands.PipelineInvalidation",
            "Inceptus.DocumentEngine.Contracts.Commands.RemoveConnectorAnchorCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.ResizeVisualStateCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.SetModelProfileAvailabilityCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.UpdateConnectionRouteCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.UpdateBoundaryAttachmentCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.UpdateDocumentPublicationCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.UpdateNodeLabelVisualOverrideCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.UpdateSemanticElementNameCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.UpdateSemanticElementPropertyCommand",
            "Inceptus.DocumentEngine.Contracts.Commands.VisualStateMove",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.CanvasBackgroundActionCatalog",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.CanvasBackgroundActionApplicabilityRequest",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.CanvasBackgroundActionDefinition",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.CanvasBackgroundActionId",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.CanvasBackgroundActionPlan",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.CanvasBackgroundActionRequest",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.SemanticSceneCommandActionCatalog",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.SemanticSceneCommandActionDefinition",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.SemanticSceneCommandActionId",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.SemanticSceneCommandActionPlan",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.SemanticSceneCommandActionRequest",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.SemanticSceneViewActionCatalog",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.SemanticSceneViewActionDefinition",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.SemanticSceneViewActionId",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.SemanticSceneViewActionPlan",
            "Inceptus.DocumentEngine.Contracts.ContextMenus.SemanticSceneViewActionRequest",
            "Inceptus.DocumentEngine.Contracts.Deletion.DiagramDeletionCatalog",
            "Inceptus.DocumentEngine.Contracts.Deletion.DiagramDeletionId",
            "Inceptus.DocumentEngine.Contracts.Deletion.DiagramDeletionPlan",
            "Inceptus.DocumentEngine.Contracts.Deletion.DiagramDeletionPlanResult",
            "Inceptus.DocumentEngine.Contracts.Deletion.DiagramDeletionRegistration",
            "Inceptus.DocumentEngine.Contracts.Deletion.DiagramDeletionRequest",
            "Inceptus.DocumentEngine.Contracts.Deletion.DiagramDeletionTargetKind",
            "Inceptus.DocumentEngine.Contracts.Deletion.IDiagramDeletionCommandFactory",
            "Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic",
            "Inceptus.DocumentEngine.Contracts.Diagnostics.DiagnosticSeverity",
            "Inceptus.DocumentEngine.Contracts.Documents.DocumentSnapshot",
            "Inceptus.DocumentEngine.Contracts.Documents.DocumentPublicationSnapshot",
            "Inceptus.DocumentEngine.Contracts.Documents.IDocumentView",
            "Inceptus.DocumentEngine.Contracts.EditorState.EditorFeedbackPresentationMode",
            "Inceptus.DocumentEngine.Contracts.EditorState.EditorFeedbackSnapshot",
            "Inceptus.DocumentEngine.Contracts.EditorState.EditorGestureSnapshot",
            "Inceptus.DocumentEngine.Contracts.EditorState.EditorStateSnapshot",
            "Inceptus.DocumentEngine.Contracts.EditorState.ViewportSnapshot",
            "Inceptus.DocumentEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionCatalog",
            "Inceptus.DocumentEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionId",
            "Inceptus.DocumentEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionPlan",
            "Inceptus.DocumentEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionPlanResult",
            "Inceptus.DocumentEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionRegistration",
            "Inceptus.DocumentEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionRequest",
            "Inceptus.DocumentEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionStartRequest",
            "Inceptus.DocumentEngine.Contracts.EndpointReconnection.IConnectorEndpointReconnectionCommandFactory",
            "Inceptus.DocumentEngine.Contracts.Geometry.DocumentGeometryBoundary",
            "Inceptus.DocumentEngine.Contracts.Geometry.Matrix2D",
            "Inceptus.DocumentEngine.Contracts.Geometry.PointD",
            "Inceptus.DocumentEngine.Contracts.Geometry.RectD",
            "Inceptus.DocumentEngine.Contracts.Geometry.SizeD",
            "Inceptus.DocumentEngine.Contracts.Geometry.VectorD",
            "Inceptus.DocumentEngine.Contracts.History.CommandHistoryPolicyRegistration",
            "Inceptus.DocumentEngine.Contracts.History.CommandHistoryPreparationResult",
            "Inceptus.DocumentEngine.Contracts.History.HistoryDiagnosticCodes",
            "Inceptus.DocumentEngine.Contracts.History.HistoryOperationResult",
            "Inceptus.DocumentEngine.Contracts.History.HistoryOperationStatus",
            "Inceptus.DocumentEngine.Contracts.History.HistoryRecordingBehavior",
            "Inceptus.DocumentEngine.Contracts.History.HistoryStatus",
            "Inceptus.DocumentEngine.Contracts.History.ICommandHistoryPolicy",
            "Inceptus.DocumentEngine.Contracts.History.IHistoryCommandFactory",
            "Inceptus.DocumentEngine.Contracts.Layout.ILayoutAlgorithm",
            "Inceptus.DocumentEngine.Contracts.Layout.LayoutAlgorithmRegistration",
            "Inceptus.DocumentEngine.Contracts.Layout.LayoutAlgorithmResult",
            "Inceptus.DocumentEngine.Contracts.Layout.LayoutComputation",
            "Inceptus.DocumentEngine.Contracts.Layout.LayoutContext",
            "Inceptus.DocumentEngine.Contracts.Layout.LayoutDiagnosticCodes",
            "Inceptus.DocumentEngine.Contracts.Layout.LayoutExecutionResult",
            "Inceptus.DocumentEngine.Contracts.Layout.LayoutExecutionStatus",
            "Inceptus.DocumentEngine.Contracts.Layout.LayoutGroupGeometry",
            "Inceptus.DocumentEngine.Contracts.Layout.LayoutNodeGeometry",
            "Inceptus.DocumentEngine.Contracts.Layout.LayoutResult",
            "Inceptus.DocumentEngine.Contracts.Metadata.DocumentMetadataSnapshot",
            "Inceptus.DocumentEngine.Contracts.Metadata.IDocumentMetadataView",
            "Inceptus.DocumentEngine.Contracts.Primitives.AlgorithmId",
            "Inceptus.DocumentEngine.Contracts.Primitives.CommandTypeId",
            "Inceptus.DocumentEngine.Contracts.Primitives.CommandValidatorId",
            "Inceptus.DocumentEngine.Contracts.Primitives.ConnectorAnchorId",
            "Inceptus.DocumentEngine.Contracts.Primitives.DocumentId",
            "Inceptus.DocumentEngine.Contracts.Primitives.DocumentScopeId",
            "Inceptus.DocumentEngine.Contracts.Primitives.DocumentRevision",
            "Inceptus.DocumentEngine.Contracts.Primitives.ElementPropertyFieldId",
            "Inceptus.DocumentEngine.Contracts.Primitives.ModelProfileId",
            "Inceptus.DocumentEngine.Contracts.Primitives.ProjectedObjectId",
            "Inceptus.DocumentEngine.Contracts.Primitives.PredefinedConnectorAnchorDefinitionId",
            "Inceptus.DocumentEngine.Contracts.Primitives.ProjectionRuleId",
            "Inceptus.DocumentEngine.Contracts.Primitives.SceneObjectId",
            "Inceptus.DocumentEngine.Contracts.Primitives.SemanticElementId",
            "Inceptus.DocumentEngine.Contracts.Primitives.SemanticTypeId",
            "Inceptus.DocumentEngine.Contracts.Primitives.ToolboxGroupId",
            "Inceptus.DocumentEngine.Contracts.Primitives.ToolboxItemId",
            "Inceptus.DocumentEngine.Contracts.Primitives.ToolboxSectionId",
            "Inceptus.DocumentEngine.Contracts.Primitives.VisualStateId",
            "Inceptus.DocumentEngine.Contracts.Profiles.ModelProfileCatalog",
            "Inceptus.DocumentEngine.Contracts.Profiles.ModelProfileDefinition",
            "Inceptus.DocumentEngine.Contracts.Profiles.ModelProfileElementViewStateKey",
            "Inceptus.DocumentEngine.Contracts.Profiles.ModelProfileElementAssignmentSnapshot",
            "Inceptus.DocumentEngine.Contracts.Profiles.ModelProfileElementPresentationSnapshot",
            "Inceptus.DocumentEngine.Contracts.Profiles.ModelProfileElementViewStateSnapshot",
            "Inceptus.DocumentEngine.Contracts.Profiles.ModelProfileStateSnapshot",
            "Inceptus.DocumentEngine.Contracts.Profiles.ModelProfileViewStateSnapshot",
            "Inceptus.DocumentEngine.Contracts.Properties.ElementPropertiesSchema",
            "Inceptus.DocumentEngine.Contracts.Properties.ElementPropertiesSchemaCatalog",
            "Inceptus.DocumentEngine.Contracts.Properties.ElementPropertyEditorKind",
            "Inceptus.DocumentEngine.Contracts.Properties.ElementPropertyFieldDefinition",
            "Inceptus.DocumentEngine.Contracts.Properties.PropertyMap",
            "Inceptus.DocumentEngine.Contracts.Properties.PropertyValue",
            "Inceptus.DocumentEngine.Contracts.Properties.PropertyValueKind",
            "Inceptus.DocumentEngine.Contracts.Properties.SemanticPropertyMutationKind",
            "Inceptus.DocumentEngine.Contracts.Projection.ElementProjectionRuleInput",
            "Inceptus.DocumentEngine.Contracts.Projection.IProjectedObject",
            "Inceptus.DocumentEngine.Contracts.Projection.IProjectionRule",
            "Inceptus.DocumentEngine.Contracts.Projection.NodeLabelPlacement",
            "Inceptus.DocumentEngine.Contracts.Projection.NodeLabelPlacementKind",
            "Inceptus.DocumentEngine.Contracts.Projection.NodeLabelInteractionPolicy",
            "Inceptus.DocumentEngine.Contracts.Projection.NodeGeometryInteractionPolicy",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedEdge",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedBoundaryAttachment",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedGraph",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedGroup",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedLabel",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedNode",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedObjectIdentity",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedObjectKind",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedPlacementHint",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedPort",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedConnectorAnchor",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectedConnectorAnchorMetadata",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectionContext",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectionDiagnosticCodes",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectionResult",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectionRuleContribution",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectionRuleInput",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectionRuleRegistration",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectionRuleResult",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectionSourceKind",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectionSourceTrace",
            "Inceptus.DocumentEngine.Contracts.Projection.ProjectionStatus",
            "Inceptus.DocumentEngine.Contracts.Projection.RelationshipProjectionRuleInput",
            "Inceptus.DocumentEngine.Contracts.Publishing.IPublishedNodeDataMapper",
            "Inceptus.DocumentEngine.Contracts.Publishing.IPublishedTokenRoleClassifier",
            "Inceptus.DocumentEngine.Contracts.Publishing.PublishedNodeData",
            "Inceptus.DocumentEngine.Contracts.Publishing.PublishedTokenRole",
            "Inceptus.DocumentEngine.Contracts.Publishing.PublishedTokenRoleClassification",
            "Inceptus.DocumentEngine.Contracts.Publishing.PublishedTokenRoleClassificationRequest",
            "Inceptus.DocumentEngine.Contracts.Routing.IRoutingAlgorithm",
            "Inceptus.DocumentEngine.Contracts.Routing.RoutedConnectorGeometry",
            "Inceptus.DocumentEngine.Contracts.Routing.RoutingAlgorithmRegistration",
            "Inceptus.DocumentEngine.Contracts.Routing.RoutingAlgorithmResult",
            "Inceptus.DocumentEngine.Contracts.Routing.RoutingComputation",
            "Inceptus.DocumentEngine.Contracts.Routing.RoutingContext",
            "Inceptus.DocumentEngine.Contracts.Routing.RoutingDiagnosticCodes",
            "Inceptus.DocumentEngine.Contracts.Routing.RoutingExecutionResult",
            "Inceptus.DocumentEngine.Contracts.Routing.RoutingExecutionStatus",
            "Inceptus.DocumentEngine.Contracts.Routing.RoutingResult",
            "Inceptus.DocumentEngine.Contracts.ScopeNavigation.IScopeNavigationContribution",
            "Inceptus.DocumentEngine.Contracts.ScopeNavigation.ScopeNavigationCatalog",
            "Inceptus.DocumentEngine.Contracts.ScopeNavigation.ScopeNavigationRegistration",
            "Inceptus.DocumentEngine.Contracts.Semantics.ISemanticModelView",
            "Inceptus.DocumentEngine.Contracts.Semantics.DocumentScopeSnapshot",
            "Inceptus.DocumentEngine.Contracts.Semantics.SemanticElementSnapshot",
            "Inceptus.DocumentEngine.Contracts.Semantics.SemanticElementScopeMembershipSnapshot",
            "Inceptus.DocumentEngine.Contracts.Semantics.SemanticElementContainmentKind",
            "Inceptus.DocumentEngine.Contracts.Semantics.SemanticModelSnapshot",
            "Inceptus.DocumentEngine.Contracts.Semantics.SemanticRelationshipSnapshot",
            "Inceptus.DocumentEngine.Contracts.Text.ITextMetricsService",
            "Inceptus.DocumentEngine.Contracts.Text.TextDirection",
            "Inceptus.DocumentEngine.Contracts.Text.TextFontStyle",
            "Inceptus.DocumentEngine.Contracts.Text.TextMeasurementRequest",
            "Inceptus.DocumentEngine.Contracts.Text.TextMeasurementResult",
            "Inceptus.DocumentEngine.Contracts.Text.TextMeasurementStatus",
            "Inceptus.DocumentEngine.Contracts.Text.TextMetrics",
            "Inceptus.DocumentEngine.Contracts.Text.TextMetricsDiagnosticCodes",
            "Inceptus.DocumentEngine.Contracts.Text.TextWritingMode",
            "Inceptus.DocumentEngine.Contracts.Toolbox.IToolboxPlacementCommandFactory",
            "Inceptus.DocumentEngine.Contracts.Toolbox.IToolboxPlacementCandidateProvider",
            "Inceptus.DocumentEngine.Contracts.Creation.DocumentCreationIdentity",
            "Inceptus.DocumentEngine.Contracts.Creation.IDocumentCreationIdentityProvider",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxCatalog",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxContribution",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxGroupDefinition",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxIconDescriptor",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxItemDefinition",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxSectionDefinition",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxPlacementCatalog",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxPlacementCandidate",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxPlacementPlan",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxPlacementPlanResult",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxPlacementRegistration",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxPlacementRequest",
            "Inceptus.DocumentEngine.Contracts.Toolbox.ToolboxPlacementTarget",
            "Inceptus.DocumentEngine.Contracts.Validation.IModelValidationRule",
            "Inceptus.DocumentEngine.Contracts.Validation.ModelValidationCatalog",
            "Inceptus.DocumentEngine.Contracts.Validation.ModelValidationContext",
            "Inceptus.DocumentEngine.Contracts.Validation.ModelValidationIssue",
            "Inceptus.DocumentEngine.Contracts.Validation.ModelValidationIssueId",
            "Inceptus.DocumentEngine.Contracts.Validation.ModelValidationRuleId",
            "Inceptus.DocumentEngine.Contracts.Validation.ModelValidationSeverity",
            "Inceptus.DocumentEngine.Contracts.Validation.ModelValidationTarget",
            "Inceptus.DocumentEngine.Contracts.Validation.ValidationSnapshot",
            "Inceptus.DocumentEngine.Contracts.Visuals.ConnectorAnchor",
            "Inceptus.DocumentEngine.Contracts.Visuals.BoundaryAttachmentPlacement",
            "Inceptus.DocumentEngine.Contracts.Visuals.BoundaryAttachmentSide",
            "Inceptus.DocumentEngine.Contracts.Visuals.ConnectorAnchorGeometryResolver",
            "Inceptus.DocumentEngine.Contracts.Visuals.ConnectorAnchorInsertion",
            "Inceptus.DocumentEngine.Contracts.Visuals.ConnectorAnchorOccupancy",
            "Inceptus.DocumentEngine.Contracts.Visuals.ConnectorAnchorPolicyMode",
            "Inceptus.DocumentEngine.Contracts.Visuals.ConnectorAnchorReferenceIdentity",
            "Inceptus.DocumentEngine.Contracts.Visuals.ConnectorAnchorRole",
            "Inceptus.DocumentEngine.Contracts.Visuals.ConnectorAnchorRoleCapability",
            "Inceptus.DocumentEngine.Contracts.Visuals.ConnectorAnchorSide",
            "Inceptus.DocumentEngine.Contracts.Visuals.ConnectorEndpointKind",
            "Inceptus.DocumentEngine.Contracts.Visuals.EdgeConnectorAnchorPolicy",
            "Inceptus.DocumentEngine.Contracts.Visuals.ElementConnectorAnchorPolicy",
            "Inceptus.DocumentEngine.Contracts.Visuals.ElementConnectorAnchorPolicyEvaluator",
            "Inceptus.DocumentEngine.Contracts.Visuals.ElementConnectorAnchorPolicyRegistration",
            "Inceptus.DocumentEngine.Contracts.Visuals.ElementConnectorAnchorPolicyRegistry",
            "Inceptus.DocumentEngine.Contracts.Visuals.ElementConnectorAnchorResolver",
            "Inceptus.DocumentEngine.Contracts.Visuals.IElementConnectorAnchorPolicyProvider",
            "Inceptus.DocumentEngine.Contracts.Visuals.IVisualModelView",
            "Inceptus.DocumentEngine.Contracts.Visuals.NodeLabelVisualOverride",
            "Inceptus.DocumentEngine.Contracts.Visuals.PredefinedConnectorAnchorDefinition",
            "Inceptus.DocumentEngine.Contracts.Visuals.ResolvedConnectorAnchor",
            "Inceptus.DocumentEngine.Contracts.Visuals.ResolvedConnectorAnchorKind",
            "Inceptus.DocumentEngine.Contracts.Visuals.VisualModelSnapshot",
            "Inceptus.DocumentEngine.Contracts.Visuals.VisualPlacementMode",
            "Inceptus.DocumentEngine.Contracts.Visuals.ConnectorLabelPlacement",
            "Inceptus.DocumentEngine.Contracts.Visuals.VisualStateSnapshot",
        ];
        var actualTypeNames = LoadProductionAssembly(Contracts)
            .GetExportedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal);

        Assert.Equal(expectedTypeNames.Order(StringComparer.Ordinal), actualTypeNames);
    }

    [Fact]
    public void Canvas2DExportsExactlyTheApprovedPhaseN104SessionSceneRenderingInteractionAndPublishingTypes()
    {
        string[] expectedTypeNames =
        [
            "Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSession",
            "Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionAttachResult",
            "Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionAttachStatus",
            "Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionConfiguration",
            "Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionDiagnosticCodes",
            "Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionGeneration",
            "Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionOperationResult",
            "Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionOperationStatus",
            "Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionState",
            "Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionStateChangedEventArgs",
            "Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionStatus",
            "Inceptus.DocumentEngine.Canvas2D.HitTesting.Canvas2DSceneHitTestConfiguration",
            "Inceptus.DocumentEngine.Canvas2D.HitTesting.Canvas2DSceneHitTestResult",
            "Inceptus.DocumentEngine.Canvas2D.HitTesting.Canvas2DSceneHitTestService",
            "Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DConnectorAnchorContextAction",
            "Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DConnectorAnchorContextActionKind",
            "Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DConnectorRouteContextAction",
            "Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DConnectorRouteContextActionKind",
            "Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DInteractionController",
            "Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DInteractionDiagnosticCodes",
            "Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DInteractionResult",
            "Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DInteractionStatus",
            "Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DNodeLabelContextAction",
            "Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DPointerInput",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.EditingSessionPresentationCapture",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.EditingSessionPresentationCaptureResult",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.EditingSessionPresentationCaptureStatus",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedMatrix",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedPoint",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedPublication",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedPresentation",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedPresentationConnector",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedPresentationItem",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedPresentationNode",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedProcessPackage",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedProcessPackageBuildResult",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedProcessPackageBuilder",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedProcessSnapshot",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedRect",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedRuntimeConfiguration",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedSource",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedTokenGraph",
            "Inceptus.DocumentEngine.Canvas2D.Publishing.PublishedTokenNode",
            "Inceptus.DocumentEngine.Canvas2D.Rendering.Canvas2DFontResource",
            "Inceptus.DocumentEngine.Canvas2D.Rendering.Canvas2DRenderer",
            "Inceptus.DocumentEngine.Canvas2D.Rendering.Canvas2DRendererConfiguration",
            "Inceptus.DocumentEngine.Canvas2D.Rendering.Canvas2DRendererDiagnosticCodes",
            "Inceptus.DocumentEngine.Canvas2D.Rendering.Canvas2DRendererOperationStatus",
            "Inceptus.DocumentEngine.Canvas2D.Rendering.Canvas2DRendererResult",
            "Inceptus.DocumentEngine.Canvas2D.Rendering.Canvas2DSurfaceSize",
            "Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DScene",
            "Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DSceneBuilder",
            "Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DSceneBuildResult",
            "Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DSceneBuildStatus",
            "Inceptus.DocumentEngine.Canvas2D.Scene.Canvas2DSpatialLookup",
        ];
        var actualTypeNames = LoadProductionAssembly(Canvas2D)
            .GetExportedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal);

        Assert.Equal(expectedTypeNames.Order(StringComparer.Ordinal), actualTypeNames);
    }

    [Fact]
    public void BpmnBlazorExportsTheApprovedFacadeWhileKeepingItsCompositionAndHostInternal()
    {
        string[] requiredTypeNames =
        [
            "Inceptus.DocumentEngine.Bpmn.Blazor.IBpmnModelerStartupDocumentProvider",
            "Inceptus.DocumentEngine.Bpmn.Blazor.Components.DocumentCanvas",
            "Inceptus.DocumentEngine.Bpmn.Blazor.Components.InceptusBpmnModeler",
            "Inceptus.DocumentEngine.Bpmn.Blazor.Components.ToolboxPanel",
            "Inceptus.DocumentEngine.Bpmn.Blazor.BpmnModelerDocumentChangeKind",
            "Inceptus.DocumentEngine.Bpmn.Blazor.BpmnModelerOperation",
            "Inceptus.DocumentEngine.Bpmn.Blazor.BpmnModelerReadyEventArgs",
            "Inceptus.DocumentEngine.Bpmn.Blazor.BpmnModelerDocumentChangedEventArgs",
            "Inceptus.DocumentEngine.Bpmn.Blazor.BpmnModelerOperationFailedEventArgs",
            "Inceptus.DocumentEngine.Bpmn.Blazor.BpmnModelerOperationStatus",
            "Inceptus.DocumentEngine.Bpmn.Blazor.BpmnModelerDocumentResult",
            "Inceptus.DocumentEngine.Bpmn.Blazor.BpmnModelerFileArtifact",
            "Inceptus.DocumentEngine.Bpmn.Blazor.BpmnModelerFileResult",
            "Microsoft.Extensions.DependencyInjection.BpmnModelerServiceCollectionExtensions",
        ];
        var assembly = LoadProductionAssembly(BpmnBlazor);
        var actualTypeNames = assembly
            .GetExportedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(requiredTypeNames, typeName => Assert.Contains(typeName, actualTypeNames));

        string[] internalTypeNames =
        [
            "Inceptus.DocumentEngine.Bpmn.Blazor.Composition.BpmnModelerComposition",
            "Inceptus.DocumentEngine.Bpmn.Blazor.Composition.BpmnModelerCompositionFactory",
            "Inceptus.DocumentEngine.Bpmn.Blazor.Presentation.DocumentCanvasHost",
            "Inceptus.DocumentEngine.Bpmn.Blazor.Presentation.BpmnModelerFacade",
            "Inceptus.DocumentEngine.Bpmn.Blazor.Presentation.BpmnModelerDocumentNotificationSource",
            "Inceptus.DocumentEngine.Bpmn.Blazor.Presentation.BpmnModelerNotifications",
        ];
        Assert.All(internalTypeNames, typeName =>
            Assert.False(assembly.GetType(typeName, throwOnError: true)!.IsVisible));
    }

    [Fact]
    public void DemoBlazorExportsOnlyItsApplicationRoot()
    {
        var actualTypeNames = LoadProductionAssembly(Blazor)
            .GetExportedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Cast<string>();

        Assert.Equal(["Inceptus.DocumentEngine.Blazor.App"], actualTypeNames);
    }

    [Fact]
    public void ContractsExposeOnlyTwoDimensionalGeometry()
    {
        string[] forbiddenFullNames =
        [
            "System.Numerics.Matrix4x4",
            "System.Numerics.Plane",
            "System.Numerics.Quaternion",
            "System.Numerics.Vector3",
            "System.Windows.Media.Media3D.Matrix3D",
            "System.Windows.Media.Media3D.Point3D",
            "System.Windows.Media.Media3D.Transform3D",
            "System.Windows.Media.Media3D.Vector3D",
        ];

        var contractsAssembly = typeof(PointD).Assembly;
        string[] expectedGeometryTypes =
        [
            typeof(DocumentGeometryBoundary).FullName!,
            typeof(Matrix2D).FullName!,
            typeof(PointD).FullName!,
            typeof(RectD).FullName!,
            typeof(SizeD).FullName!,
            typeof(VectorD).FullName!,
        ];
        var actualGeometryTypes = contractsAssembly
            .GetExportedTypes()
            .Where(type => type.Namespace == "Inceptus.DocumentEngine.Contracts.Geometry")
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal);
        var forbiddenNamedTypes = contractsAssembly
            .GetExportedTypes()
            .Where(type => type.Name.EndsWith("3D", StringComparison.Ordinal) ||
                type.Name.Contains("ThreeD", StringComparison.Ordinal))
            .ToArray();
        var forbiddenSignatureTypes = GetPublicSignatureTypes(contractsAssembly)
            .Where(type => forbiddenFullNames.Contains(type.FullName, StringComparer.Ordinal))
            .ToArray();

        Assert.Equal(expectedGeometryTypes.Order(StringComparer.Ordinal), actualGeometryTypes);
        Assert.Empty(forbiddenNamedTypes);
        Assert.Empty(forbiddenSignatureTypes);
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

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }

    private static Dictionary<string, string> GetProductionProjectPaths() =>
        Directory
            .GetFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path),
                path => path,
                StringComparer.Ordinal);

    private static string FindProject(string projectName)
    {
        var searchRoot = projectName.EndsWith("Tests", StringComparison.Ordinal)
            ? Path.Combine(RepositoryRoot, "tests")
            : Path.Combine(RepositoryRoot, "src");
        var matches = Directory.GetFiles(searchRoot, $"{projectName}.csproj", SearchOption.AllDirectories);

        return Assert.Single(matches);
    }

    private static IEnumerable<string> ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException("The project path has no directory.");

        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include is not null)
            .Cast<string>()
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include)));
    }

    private static bool HasCycle(
        string project,
        IReadOnlyDictionary<string, string[]> graph,
        ISet<string> visited,
        ISet<string> active)
    {
        if (active.Contains(project))
        {
            return true;
        }

        if (!visited.Add(project))
        {
            return false;
        }

        active.Add(project);

        foreach (var dependency in graph[project])
        {
            if (HasCycle(dependency, graph, visited, active))
            {
                return true;
            }
        }

        active.Remove(project);
        return false;
    }

    private static string? ReadElementValue(XContainer document, string name) =>
        document.Descendants().Single(element => element.Name.LocalName == name).Value;

    private static Assembly LoadProductionAssembly(string name) =>
        name == Contracts ? typeof(DocumentId).Assembly : Assembly.Load(name);

    private static IEnumerable<Type> GetPublicSignatureTypes(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var signatureType in ExpandType(type))
            {
                yield return signatureType;
            }

            if (type.BaseType is not null)
            {
                foreach (var signatureType in ExpandType(type.BaseType))
                {
                    yield return signatureType;
                }
            }

            foreach (var implementedInterface in type.GetInterfaces())
            {
                foreach (var signatureType in ExpandType(implementedInterface))
                {
                    yield return signatureType;
                }
            }

            foreach (var member in type.GetMembers(
                         BindingFlags.Public | BindingFlags.Instance |
                         BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var memberType in GetMemberTypes(member))
                {
                    foreach (var signatureType in ExpandType(memberType))
                    {
                        yield return signatureType;
                    }
                }
            }
        }
    }

    private static IEnumerable<Type> GetMemberTypes(MemberInfo member)
    {
        switch (member)
        {
            case ConstructorInfo constructor:
                return constructor.GetParameters().Select(parameter => parameter.ParameterType);
            case MethodInfo method:
                return new[] { method.ReturnType }
                    .Concat(method.GetParameters().Select(parameter => parameter.ParameterType))
                    .Concat(method.GetGenericArguments().SelectMany(argument => argument.GetGenericParameterConstraints()));
            case PropertyInfo property:
                return new[] { property.PropertyType }
                    .Concat(property.GetIndexParameters().Select(parameter => parameter.ParameterType));
            case FieldInfo field:
                return [field.FieldType];
            case EventInfo @event when @event.EventHandlerType is not null:
                return [@event.EventHandlerType];
            case Type nestedType:
                return [nestedType];
            default:
                return [];
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

    private static bool IsBrowserOrDomType(Type type)
    {
        string[] forbiddenNamespacePrefixes =
        [
            "Microsoft.AspNetCore.Components",
            "Microsoft.JSInterop",
            "System.Runtime.InteropServices.JavaScript",
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
            "MouseEvent",
            "Path2D",
            "PointerEvent",
            "SVGElement",
            "WebGLRenderingContext",
            "WheelEvent",
        ];

        return forbiddenNamespacePrefixes.Any(prefix =>
                   type.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true) ||
               forbiddenTypeNames.Contains(type.Name, StringComparer.Ordinal);
    }
}
