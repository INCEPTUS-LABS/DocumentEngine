using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM0ConnectorAnchorPolicyArchitectureTests
{
    [Fact]
    public void PolicyIsTypeConfigurationAndNotSemanticOrVisualInstanceState()
    {
        Assert.Equal(
            "Inceptus.DocumentEngine.Contracts.Visuals",
            typeof(ElementConnectorAnchorPolicy).Namespace);
        Assert.Equal(
            "Inceptus.DocumentEngine.Contracts.Visuals",
            typeof(IElementConnectorAnchorPolicyProvider).Namespace);
        Assert.DoesNotContain(
            typeof(SemanticElementSnapshot).GetProperties(),
            property => property.PropertyType == typeof(ElementConnectorAnchorPolicy) ||
                property.Name.Contains("AnchorPolicy", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(VisualStateSnapshot).GetProperties(),
            property => property.PropertyType == typeof(ElementConnectorAnchorPolicy) ||
                property.PropertyType == typeof(PredefinedConnectorAnchorDefinition) ||
                property.Name.Contains("AnchorPolicy", StringComparison.Ordinal));
        Assert.Equal(
            typeof(Inceptus.DocumentEngine.Contracts.Primitives.SemanticTypeId),
            typeof(ElementConnectorAnchorPolicyRegistration)
                .GetProperty(nameof(ElementConnectorAnchorPolicyRegistration.ElementTypeId))!
                .PropertyType);
    }

    [Fact]
    public void CommandAndDocumentLayersIndependentlyEvaluateTheRegisteredPolicy()
    {
        var addHandler = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Commands",
            "AddConnectorAnchorCommandHandler.cs");
        var removeHandler = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Commands",
            "RemoveConnectorAnchorCommandHandler.cs");
        var invariant = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Documents",
            "DocumentInvariantValidator.cs");

        Assert.Contains("ElementConnectorAnchorPolicyEvaluator.CanAdd", addHandler,
            StringComparison.Ordinal);
        Assert.Contains("ElementConnectorAnchorPolicyEvaluator.CanRemove", removeHandler,
            StringComparison.Ordinal);
        Assert.Contains("ElementConnectorAnchorResolver.Resolve", invariant,
            StringComparison.Ordinal);
        Assert.Contains("VisualConnectorAnchorPolicyInvalidCode", invariant,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PredefinedDefinitionsRemainTypeLevelAndResolveToStableReferences()
    {
        var definitionProperties = typeof(PredefinedConnectorAnchorDefinition)
            .GetProperties()
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal);
        var dynamicProperties = typeof(ConnectorAnchor)
            .GetProperties()
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["Id", "Order", "RoleCapability", "Side"], definitionProperties);
        Assert.Equal(["Id", "Order", "Role", "Side"], dynamicProperties);
        Assert.DoesNotContain(
            typeof(VisualStateSnapshot).GetProperties(),
            property => property.Name.Contains("Predefined", StringComparison.Ordinal));
        Assert.NotNull(typeof(ConnectorAnchorReferenceIdentity).GetMethod(
            nameof(ConnectorAnchorReferenceIdentity.ForPredefined)));
    }

    [Fact]
    public void FrameworkPolicyContractsContainNoBpmnRendererOrBrowserDependency()
    {
        var policy = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Visuals",
            "ElementConnectorAnchorPolicy.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Visuals",
                "ElementConnectorAnchorPolicyRegistry.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Visuals",
                "ResolvedConnectorAnchor.cs");

        Assert.DoesNotContain("Bpmn", policy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Renderer", policy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JavaScript", policy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Canvas", policy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RendererJavaScriptAndPresentationContainNoPolicyDecisionBranches()
    {
        var renderer = ReadProductionFiles(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering",
            "*.cs");
        var javascript = ReadProductionFiles(
            "Inceptus.DocumentEngine.Canvas2D",
            "wwwroot",
            "*.js") +
            ReadProductionFiles(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "wwwroot",
                "*.js");
        var razor = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var forbiddenPolicyTokens = new[]
        {
            nameof(ConnectorAnchorPolicyMode),
            nameof(ConnectorAnchorPolicyMode.DynamicUnlimited),
            nameof(ConnectorAnchorPolicyMode.DynamicSingle),
            nameof(ConnectorAnchorRoleCapability),
            nameof(PredefinedConnectorAnchorDefinition),
        };

        foreach (var token in forbiddenPolicyTokens)
        {
            Assert.DoesNotContain(token, renderer, StringComparison.Ordinal);
            Assert.DoesNotContain(token, javascript, StringComparison.Ordinal);
            Assert.DoesNotContain(token, razor, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FrameworkSourceContainsNoBpmnAnchorPolicyOrInstancePersistence()
    {
        var frameworkProjects = new[]
        {
            "Inceptus.DocumentEngine.Contracts",
            "Inceptus.DocumentEngine.Runtime",
            "Inceptus.DocumentEngine.Canvas2D",
        };
        var frameworkFiles = frameworkProjects.SelectMany(project =>
            Directory.GetFiles(
                    Path.Combine(RepositoryRoot, "src", project),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(static path =>
                    !path.Contains(
                        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains(
                        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal));
        var blazorRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor");
        var genericBlazorFiles = new[]
        {
            Path.Combine(blazorRoot, "Components"),
            Path.Combine(blazorRoot, "Presentation"),
            Path.Combine(blazorRoot, "wwwroot"),
        }.SelectMany(directory => Directory.GetFiles(
            directory,
            "*",
            SearchOption.AllDirectories))
            .Where(static path =>
                (Path.GetExtension(path) is ".cs" or ".razor" or ".js") &&
                !ApprovedN104Changes.IsPublishingApplicationCompositionPath(path))
            .Order(StringComparer.Ordinal);
        var framework = string.Concat(
            frameworkFiles.Concat(genericBlazorFiles).Select(File.ReadAllText));

        // P1.15 permits only the explicit Event exclusion in BPMN element Properties.
        Assert.Equal(1, framework.Split("!BpmnSemanticTypes.IsEvent(TypeId)", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("BpmnSemanticTypes", framework.Replace(
            "!BpmnSemanticTypes.IsEvent(TypeId)", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", framework, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmn", framework, StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(ElementConnectorAnchorPolicy),
            typeof(SemanticElementSnapshot).GetProperties()
                .Select(static property => property.PropertyType));
        Assert.DoesNotContain(
            typeof(ElementConnectorAnchorPolicy),
            typeof(VisualStateSnapshot).GetProperties()
                .Select(static property => property.PropertyType));
        Assert.DoesNotContain(
            typeof(PredefinedConnectorAnchorDefinition),
            typeof(VisualStateSnapshot).GetProperties()
                .Select(static property => property.PropertyType));
    }

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static string ReadProductionFiles(
        string project,
        string directory,
        string searchPattern)
    {
        var root = Path.Combine(RepositoryRoot, "src", project, directory);
        return Directory.Exists(root)
            ? string.Concat(
                Directory.GetFiles(root, searchPattern, SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                    .Select(File.ReadAllText))
            : string.Empty;
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
}
