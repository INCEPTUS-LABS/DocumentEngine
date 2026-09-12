using System.Reflection;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM321BpmnConnectorAnchorArchitectureTests
{
    [Fact]
    public void CurrentBpmnSequenceFlowContractStructurallyRequiresGenericAnchorIdentities()
    {
        var constructor = Assert.Single(
            typeof(CreateBpmnSequenceFlowCommand).GetConstructors(BindingFlags.Public |
                BindingFlags.Instance));
        var parameters = constructor.GetParameters().ToDictionary(
            parameter => parameter.Name!,
            StringComparer.Ordinal);

        Assert.Equal(typeof(ConnectorAnchorId), parameters["sourceAnchorId"].ParameterType);
        Assert.Equal(typeof(ConnectorAnchorId), parameters["targetAnchorId"].ParameterType);
        Assert.Equal(
            typeof(ConnectorAnchorId),
            typeof(CreateBpmnSequenceFlowCommand).GetProperty("SourceAnchorId")!.PropertyType);
        Assert.Equal(
            typeof(ConnectorAnchorId),
            typeof(CreateBpmnSequenceFlowCommand).GetProperty("TargetAnchorId")!.PropertyType);
    }

    [Fact]
    public void SequenceFlowHandlerPersistsBindingsWithoutCreatingOrSelectingAnchors()
    {
        var handlerSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnCreationCommandHandlers.cs"));

        Assert.Contains("sourceAnchorId: flow.SourceAnchorId", handlerSource,
            StringComparison.Ordinal);
        Assert.Contains("targetAnchorId: flow.TargetAnchorId", handlerSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AddConnectorAnchorCommand", handlerSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new ConnectorAnchor(", handlerSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("nearest", handlerSource,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BpmnUsesCanonicalGenericAnchorModelWithoutFrameworkNotationBranches()
    {
        var bpmnSource = string.Concat(ReadSources(
            Path.Combine("src", "Inceptus.DocumentEngine.Bpmn"),
            "*.cs"));
        string[] duplicatePersistentModelTokens =
        [
            "class BpmnAnchor",
            "record BpmnAnchor",
            "BpmnAnchorId",
            "BpmnPortId",
            "BpmnConnectionPoint",
            "BpmnAttachmentPoint",
        ];
        Assert.All(duplicatePersistentModelTokens, token => Assert.DoesNotContain(
            token,
            bpmnSource,
            StringComparison.Ordinal));

        var genericSource = string.Concat(
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Contracts"), "*.cs"),
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Runtime"), "*.cs"),
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Canvas2D"), "*.cs"),
            ReadSources(
                Path.Combine("src", "Inceptus.DocumentEngine.Bpmn.Blazor", "Components"),
                "*.razor"),
            ReadSources(
                Path.Combine("src", "Inceptus.DocumentEngine.Bpmn.Blazor", "Presentation"),
                "*.cs"));
        Assert.DoesNotContain("BPMN.SequenceFlow", genericSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(CreateBpmnSequenceFlowCommand),
            genericSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "BpmnConnectorAnchorPolicies",
            genericSource,
            StringComparison.Ordinal);
    }

    private static IEnumerable<string> ReadSources(string relativeDirectory, string pattern) =>
        Directory.Exists(Path.Combine(RepositoryRoot, relativeDirectory))
            ? Directory
                .GetFiles(
                    Path.Combine(RepositoryRoot, relativeDirectory),
                    pattern,
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText)
            : [];

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
