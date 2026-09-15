using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Deletion;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN314DeletionArchitectureTests
{
    [Fact]
    public void DeletionContractsArePublicGenericAndNotationNeutral()
    {
        Type[] contracts =
        [
            typeof(DiagramDeletionId),
            typeof(DiagramDeletionTargetKind),
            typeof(DiagramDeletionRequest),
            typeof(DiagramDeletionPlan),
            typeof(DiagramDeletionPlanResult),
            typeof(IDiagramDeletionCommandFactory),
            typeof(DiagramDeletionRegistration),
            typeof(DiagramDeletionCatalog),
        ];
        Assert.All(contracts, type =>
        {
            Assert.True(type.IsPublic);
            Assert.StartsWith(
                "Inceptus.DocumentEngine.Contracts.Deletion",
                type.FullName,
                StringComparison.Ordinal);
        });

        var source = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            "Deletion");
        string[] forbidden = ["Bpmn", "BPMN", "SequenceFlow", "Canvas2D"];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PluginOwnsTwoAtomicDeletionCommandsAndN314AloneOptsIn()
    {
        Assert.All(
            new[]
            {
                typeof(DeleteBpmnSequenceFlowCommand),
                typeof(DeleteBpmnFlowNodeCommand),
            },
            type =>
            {
                Assert.True(type.IsPublic);
                Assert.True(typeof(ICommand).IsAssignableFrom(type));
                Assert.True(typeof(ICommandPipelineInvalidation).IsAssignableFrom(type));
            });
        Assert.Empty(BpmnPluginRegistration.N31.DiagramDeletionRegistrations);
        Assert.Single(BpmnPluginRegistration.N314.DiagramDeletionRegistrations);

        var handlers = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnDeletionCommandHandlers.cs");
        var validation = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnDeletionValidation.cs");
        Assert.Contains("BpmnDocumentReplacement.Replace(", handlers,
            StringComparison.Ordinal);
        Assert.Contains("BpmnFlowNodeDeletionPlan.TryCreate(", handlers,
            StringComparison.Ordinal);
        Assert.Contains("RemovedScopeIds", handlers, StringComparison.Ordinal);
        Assert.Contains("RemovedElementIds", handlers, StringComparison.Ordinal);
        Assert.Contains("RemovedRelationshipIds", handlers, StringComparison.Ordinal);
        Assert.Contains("RemovedSemanticIds", handlers, StringComparison.Ordinal);
        Assert.Contains("GetDescendants(childScope.Id)", validation,
            StringComparison.Ordinal);
        Assert.Contains(".ToImmutableHashSet()", validation, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandProcessor", handlers, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveConnectorAnchorCommand", handlers,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteAsync(", handlers, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericHostMenuHasExactActionsAndRevalidatesCurrentState()
    {
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var properties = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasProperties.cs");
        var razor = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        // P1.15 permits only the explicit Event exclusion in BPMN element Properties.
        Assert.Equal(1, properties.Split("!BpmnSemanticTypes.IsEvent(TypeId)", StringSplitOptions.None).Length - 1);
        var genericSource = host + properties.Replace(
            "!BpmnSemanticTypes.IsEvent(TypeId)", string.Empty, StringComparison.Ordinal) + razor;

        Assert.Contains("@Text[\"Context_DeleteElement\"]", razor, StringComparison.Ordinal);
        Assert.Contains("@Text[\"Context_DeleteConnection\"]", razor, StringComparison.Ordinal);
        Assert.Contains("ExecuteDeletionContextActionAsync", host,
            StringComparison.Ordinal);
        Assert.Contains("TryCaptureDocumentSnapshot", host, StringComparison.Ordinal);
        Assert.Contains("SessionGeneration", host, StringComparison.Ordinal);
        Assert.Contains("GetMatchingRegistrations", host, StringComparison.Ordinal);
        Assert.Contains("ExecuteForSceneTargetAsync", host,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Backspace", razor, StringComparison.OrdinalIgnoreCase);
        string[] forbidden =
        [
            "BpmnSemanticTypes",
            "BpmnPluginRegistration",
            "SequenceFlow",
            "DeleteBpmn",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, genericSource, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HistoryAndPipelinePreserveExactRuntimeGeometryWithoutLayoutFallback()
    {
        var history = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "History",
            "BpmnDeletionHistoryPolicy.cs");
        var pipeline = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSessionPipeline.cs");

        Assert.Contains("ForRemovedVisualStates", history, StringComparison.Ordinal);
        Assert.Contains("ForHistoricalRestoration", history, StringComparison.Ordinal);
        Assert.Contains("before.Revision", history, StringComparison.Ordinal);
        Assert.Contains("HistoricalSourceRevision", pipeline, StringComparison.Ordinal);
        Assert.Contains("RequiresExactCarryForward", pipeline, StringComparison.Ordinal);
        Assert.Contains("NodeGeometryPreservationUnavailable", pipeline,
            StringComparison.Ordinal);
        Assert.DoesNotContain("VisualPlacementMode.Pinned", history,
            StringComparison.Ordinal);
        Assert.DoesNotContain("VisualPlacementMode.Pinned", pipeline,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionDeletionDoesNotRemoveOrSynthesizeEndpointAnchors()
    {
        var handlerSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnDeletionCommandHandlers.cs");
        var connectionHandler = Between(
            handlerSource,
            "internal sealed class BpmnSequenceFlowDeletionCommandHandler",
            "internal sealed class BpmnFlowNodeDeletionCommandHandler");

        Assert.Contains("relationship.Id != deletion.RelationshipId", connectionHandler,
            StringComparison.Ordinal);
        Assert.Contains("visual.Id != deletion.ConnectorVisualStateId", connectionHandler,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectorAnchors", connectionHandler,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectorAnchorInsertion", connectionHandler,
            StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveConnectorAnchor", connectionHandler,
            StringComparison.Ordinal);
    }

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate '{startMarker}'.");
        return source[start..end];
    }

    private static string ReadProductionDirectory(string project, string directory) =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", project, directory),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName,
                        "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
