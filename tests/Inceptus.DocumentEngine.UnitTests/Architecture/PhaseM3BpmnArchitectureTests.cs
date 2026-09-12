using System.Reflection;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM3BpmnArchitectureTests
{
    [Fact]
    public void M3KeepsSceneAndToolboxRegistrationsSeparateFromRuntimeRegistries()
    {
        var m2 = BpmnPluginRegistration.M2;
        var m3 = BpmnPluginRegistration.M3;

        Assert.Empty(m2.SceneContributors);
        Assert.Empty(m2.ToolboxContributions);
        Assert.Empty(m2.PropertiesSchemas);
        Assert.All(m3.SceneContributors, registration =>
        {
            Assert.IsType<Canvas2DSceneContributorRegistration>(registration);
            Assert.IsAssignableFrom<ICanvas2DSceneContributor>(registration.Contributor);
        });
        Assert.All(m3.ToolboxContributions, contribution =>
            Assert.IsType<ToolboxContribution>(contribution));
        Assert.Empty(m3.PropertiesSchemas);
        Assert.Equal(m2.CommandHandlers.AsEnumerable(), m3.CommandHandlers.AsEnumerable());
        Assert.Equal(m2.LayoutAlgorithms.AsEnumerable(), m3.LayoutAlgorithms.AsEnumerable());
        Assert.Equal(m2.RoutingAlgorithms.AsEnumerable(), m3.RoutingAlgorithms.AsEnumerable());
    }

    [Fact]
    public void BpmnScenePolicyConsumesOnlyImmutableGenericContracts()
    {
        var contributor = Assert.Single(BpmnPluginRegistration.M3.SceneContributors).Contributor;
        var method = Assert.Single(contributor.GetType().GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        Assert.Equal(nameof(ICanvas2DSceneContributor.Contribute), method.Name);
        Assert.Equal(typeof(Canvas2DSceneContributionResult), method.ReturnType);
        Assert.Equal(
            [typeof(Canvas2DSceneContributionContext)],
            method.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.Same(typeof(ICanvas2DSceneContributor).Assembly, method.ReturnType.Assembly);
        Assert.Same(
            typeof(ICanvas2DSceneContributor).Assembly,
            method.GetParameters().Single().ParameterType.Assembly);
    }

    [Fact]
    public void BpmnScenePolicyContainsNoMutableSubsystemOrDuplicatedGenericMechanism()
    {
        var sceneSource = ReadProductionFiles(
            "Inceptus.DocumentEngine.Bpmn",
            "Scene",
            "*.cs");
        string[] forbidden =
        [
            "CommandProcessor",
            "DocumentSnapshot",
            "EditingSession",
            "HistoryManager",
            "LayoutEngine",
            "ProjectionEngine",
            "RoutingEngine",
            "Canvas2DRenderer",
            "Canvas2DSceneBuilder",
            "TextMetrics",
            "TextLayout",
            "ArrowGeometry",
            "HitTestService",
            "JavaScript",
            "JSInterop",
            ".Compute(",
            ".Route(",
        ];

        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, sceneSource, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ToolboxPolicyIsDataOnlyAndDoesNotOfferSequenceFlowOrPlacementBehavior()
    {
        var toolboxSource = ReadProductionFiles(
            "Inceptus.DocumentEngine.Bpmn",
            "Toolbox",
            "*.cs");
        var contribution = Assert.Single(BpmnPluginRegistration.M3.ToolboxContributions);

        Assert.DoesNotContain(contribution.Items, static item =>
            item.DisplayName.Contains("Sequence Flow", StringComparison.Ordinal));
        string[] forbidden =
        [
            "ICommand",
            "CreateBpmn",
            "EditingSession",
            "History",
            "Placement",
            "RenderFragment",
            "Razor",
            "JavaScript",
            "Delegate",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, toolboxSource, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenericCanvas2DProductionRemainsIndependentOfBpmn()
    {
        var canvasProject = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Canvas2D",
            "Inceptus.DocumentEngine.Canvas2D.csproj"));
        var canvasSources = ReadProductionFiles(
            "Inceptus.DocumentEngine.Canvas2D",
            string.Empty,
            "*.cs");

        Assert.DoesNotContain("Inceptus.DocumentEngine.Bpmn", canvasProject, StringComparison.Ordinal);
        Assert.DoesNotContain("BPMN.", canvasSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bpmn", canvasSources, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadProductionFiles(
        string project,
        string relativeDirectory,
        string pattern)
    {
        var directory = Path.Combine(RepositoryRoot, "src", project, relativeDirectory);
        return string.Concat(Directory
            .GetFiles(directory, pattern, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));
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
