using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN31SmartTargetAnchorArchitectureTests
{
    [Fact]
    public void AcquisitionContractsArePublicGenericAndNotationNeutral()
    {
        Type[] contracts =
        [
            typeof(TargetAnchorAcquisitionKind),
            typeof(TargetAnchorAcquisitionRejectionReason),
            typeof(TargetAnchorAcquisitionResult),
            typeof(TargetAnchorAcquisitionRequest),
            typeof(ConnectorTargetEdgeResolver),
            typeof(ConnectorTargetAnchorAcquisition),
            typeof(AnchorConnectionTargetEligibilityRequest),
            typeof(IAnchorConnectionTargetEligibility),
            typeof(ConnectorAnchorInsertion),
        ];
        Assert.All(contracts, type =>
        {
            Assert.True(type.IsPublic);
            Assert.StartsWith(
                "Inceptus.DocumentEngine.Contracts.",
                type.FullName,
                StringComparison.Ordinal);
        });

        var source = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            "ConnectionCreation") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Visuals",
                "ConnectorAnchorInsertion.cs");
        string[] forbidden =
        [
            "Bpmn",
            "BPMN",
            "SequenceFlow",
            "StartEvent",
            "EndEvent",
            "Gateway",
            "Canvas2D",
            "RoutingEngine",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PolicyOccupancyAndCanonicalGeometryRemainSingleAuthorities()
    {
        var acquisition = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "ConnectionCreation",
            "ConnectorTargetAnchorAcquisition.cs");
        var insertion = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Visuals",
            "ConnectorAnchorInsertion.cs");
        var addHandler = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Commands",
            "AddConnectorAnchorCommandHandler.cs");

        Assert.Contains("ElementConnectorAnchorPolicyEvaluator.CanAdd(", acquisition,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorOccupancy.IsOccupied(", acquisition,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorGeometryResolver.ResolveInsertionIndex(", acquisition,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorGeometryResolver.ResolvePoint(", acquisition,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorInsertion.Insert(", addHandler,
            StringComparison.Ordinal);
        Assert.Contains("anchor.Order >= insertionIndex", insertion,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnConnectorAnchorPolicies", acquisition,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExactTargetStructurallyPrecedesOptionalNodeBodyAcquisition()
    {
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var targetMethod = Between(
            controller,
            "private bool TryResolveConnectionTarget(",
            "private bool IsTargetAcquisitionCurrent(");
        var exact = targetMethod.IndexOf(
            "TryResolveConnectorAnchorHandle(",
            StringComparison.Ordinal);
        var nodeBody = targetMethod.IndexOf(
            "Canvas2DNodeBodyMetadata.IsNodeBody(hitItem)",
            StringComparison.Ordinal);

        Assert.True(exact >= 0 && nodeBody > exact);
        Assert.Contains("connection.Registration.TargetEligibility", targetMethod,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorTargetAnchorAcquisition.Acquire(", targetMethod,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AddConnectorAnchorCommand", controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain("RoutingEngine", targetMethod,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProposalIsSceneOnlyAndAtomicCommitRemainsOnePluginCommand()
    {
        var gestures = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "Canvas2DSceneBuilder.Gestures.cs");
        var preview = Between(
            gestures,
            "private static bool ComposeAnchorConnectionGesturePreview(",
            "private static Canvas2DSceneStyle ResolveConnectorAnchorStyle(");
        var handlers = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnCreationCommandHandlers.cs");
        var atomicHandler = Between(
            handlers,
            "internal sealed class BpmnSequenceFlowWithTargetAnchorCreationCommandHandler",
            "internal static class BpmnDocumentReplacement");

        Assert.Contains("Canvas2DHitTestPolicy.None", preview, StringComparison.Ordinal);
        Assert.Contains("anchor-connection-proposed-target", preview,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ICommand", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteAsync", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("Routing", preview, StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorInsertion.Insert(", atomicHandler,
            StringComparison.Ordinal);
        Assert.Contains("BpmnDocumentReplacement.Add(", atomicHandler,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AddConnectorAnchorCommand", atomicHandler,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CommandProcessor", atomicHandler,
            StringComparison.Ordinal);
    }

    [Fact]
    public void N31OptsInWithoutChangingN2N3OrEndpointReconnection()
    {
        var n2 = Assert.Single(BpmnPluginRegistration.N2.AnchorConnectionCreationRegistrations);
        var n3 = Assert.Single(BpmnPluginRegistration.N3.AnchorConnectionCreationRegistrations);
        var n31 = Assert.Single(BpmnPluginRegistration.N31.AnchorConnectionCreationRegistrations);

        Assert.Null(n2.TargetEligibility);
        Assert.Null(n3.TargetEligibility);
        Assert.NotNull(n31.TargetEligibility);
        Assert.Equal(n2.CreationId, n3.CreationId);
        Assert.Equal(n3.CreationId, n31.CreationId);
        Assert.Equal(
            BpmnPluginRegistration.N3.ConnectorEndpointReconnectionRegistrations,
            BpmnPluginRegistration.N31.ConnectorEndpointReconnectionRegistrations);
        Assert.DoesNotContain(BpmnPluginRegistration.N3.CommandHandlers, registration =>
            registration.TypeId ==
                CreateBpmnSequenceFlowWithTargetAnchorCommand.KnownTypeId);
        Assert.Contains(BpmnPluginRegistration.N31.CommandHandlers, registration =>
            registration.TypeId ==
                CreateBpmnSequenceFlowWithTargetAnchorCommand.KnownTypeId);

        var endpointController = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.EndpointReconnection.cs");
        Assert.DoesNotContain("ConnectorTargetAnchorAcquisition", endpointController,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TargetAnchorAcquisitionResult", endpointController,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AtomicCommandIsBpmnOwnedAndUsesNormalDocumentCommandBoundary()
    {
        Assert.True(typeof(CreateBpmnSequenceFlowWithTargetAnchorCommand).IsPublic);
        Assert.True(typeof(ICommand).IsAssignableFrom(
            typeof(CreateBpmnSequenceFlowWithTargetAnchorCommand)));
        Assert.Equal(
            AuthoritativeDocumentComponent.SemanticModel |
            AuthoritativeDocumentComponent.VisualModel,
            new CreateBpmnSequenceFlowWithTargetAnchorCommand(
                new("document"),
                default,
                new("flow"),
                new("flow-visual"),
                new("source"),
                new("target"),
                new("source-anchor"),
                new("target-visual"),
                new("target-anchor"),
                ConnectorAnchorSide.Left,
                0).AffectedComponents);
    }

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
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
