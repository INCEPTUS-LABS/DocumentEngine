using System.Reflection;
using System.Xml.Linq;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM1BpmnArchitectureTests
{
    [Fact]
    public void GenericProjectsDoNotReferenceBpmnAndBpmnReferencesOnlyContracts()
    {
        var projects = new[]
        {
            "Inceptus.DocumentEngine.Contracts",
            "Inceptus.DocumentEngine.Runtime",
            "Inceptus.DocumentEngine.Canvas2D",
        };
        Assert.All(projects, project => Assert.DoesNotContain(
            ReadProjectReferences(project),
            reference => reference.Contains("Inceptus.DocumentEngine.Bpmn", StringComparison.Ordinal)));

        var bpmnReferences = ReadProjectReferences("Inceptus.DocumentEngine.Bpmn");
        Assert.Single(bpmnReferences);
        Assert.Contains("Inceptus.DocumentEngine.Contracts", bpmnReferences[0], StringComparison.Ordinal);
    }

    [Fact]
    public void M1UsesGenericCommandProjectionAndHistoryContractsWithoutOwningEngines()
    {
        var types = typeof(BpmnPluginRegistration).Assembly.GetTypes();
        var creationCommands = new[]
        {
            typeof(CreateBpmnStartEventCommand),
            typeof(CreateBpmnTaskCommand),
            typeof(CreateBpmnEndEventCommand),
            typeof(CreateBpmnSequenceFlowCommand),
        };

        Assert.All(creationCommands, type => Assert.True(typeof(ICommand).IsAssignableFrom(type)));
        Assert.Contains(types, type => typeof(IProjectionRule).IsAssignableFrom(type) && !type.IsInterface);
        Assert.Contains(types, type => typeof(ICommandHistoryPolicy).IsAssignableFrom(type));
        Assert.Empty(BpmnPluginRegistration.M1.LayoutAlgorithms);
        Assert.Empty(BpmnPluginRegistration.M1.RoutingAlgorithms);
        Assert.Empty(BpmnPluginRegistration.M1.SceneContributors);
        Assert.Empty(BpmnPluginRegistration.M1.ToolboxContributions);
        Assert.Empty(BpmnPluginRegistration.M1.PropertiesSchemas);
        Assert.DoesNotContain(types, type =>
            type.Name.Contains("CommandProcessor", StringComparison.Ordinal) ||
            type.Name.Contains("HistoryManager", StringComparison.Ordinal) ||
            type.Name.Contains("ProjectionEngine", StringComparison.Ordinal) ||
            type.Name.Contains("LayoutEngine", StringComparison.Ordinal) ||
            type.Name.Contains("RoutingEngine", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionBpmnIsSimulationAndTransformationNeutral()
    {
        var source = string.Concat(Directory
            .GetFiles(
                Path.Combine(RepositoryRoot, "src", "Inceptus.DocumentEngine.Bpmn"),
                "*.cs",
                SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));
        string[] forbidden =
        [
            "Simulation",
            "DurationDistribution",
            "QueueCapacity",
            "ResourceCapacity",
            "ArrivalDistribution",
            "SimulationCost",
            "TransformationMapping",
            "TransformationState",
            "WasTransformed",
            "OriginalBpmnElementId",
            "TransformationProvenance",
            "SourceDocumentId",
            "Decision",
        ];

        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenericBlazorCanvasAndJavaScriptContainNoBpmnSemanticBranches()
    {
        var genericSources = string.Concat(
            ReadSources("Inceptus.DocumentEngine.Canvas2D", "*.cs"),
            ReadSources("Inceptus.DocumentEngine.Blazor", "*.razor"),
            ReadSources("Inceptus.DocumentEngine.Blazor", "*.js"),
            ReadSources("Inceptus.DocumentEngine.Canvas2D", "*.js"));

        Assert.DoesNotContain("BPMN.StartEvent", genericSources, StringComparison.Ordinal);
        Assert.DoesNotContain("BPMN.Task", genericSources, StringComparison.Ordinal);
        Assert.DoesNotContain("BPMN.EndEvent", genericSources, StringComparison.Ordinal);
        Assert.DoesNotContain("BPMN.SequenceFlow", genericSources, StringComparison.Ordinal);
    }

    private static string[] ReadProjectReferences(string project)
    {
        var path = Directory.GetFiles(
            Path.Combine(RepositoryRoot, "src", project),
            "*.csproj",
            SearchOption.TopDirectoryOnly).Single();
        return XDocument.Load(path)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
    }

    private static IEnumerable<string> ReadSources(string project, string pattern) =>
        Directory
            .GetFiles(
                Path.Combine(RepositoryRoot, "src", project),
                pattern,
                SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText);

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
