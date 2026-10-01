using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class PhaseA11SequenceFlowNameCommandTests
{
    [Theory]
    [InlineData("wrong-type")]
    [InlineData("element")]
    [InlineData("missing")]
    [InlineData("unchanged")]
    [InlineData("cancelled")]
    public async Task InvalidNameCommandsLeaveDocumentAndHistoryUntouched(string failure)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var snapshot = composition.Document.CaptureSnapshot();
        var id = BpmnDemoPipeline.ThirdSequenceFlowId;
        if (failure == "wrong-type")
        {
            var semantic = snapshot.SemanticModel;
            snapshot = new DocumentSnapshot(new SemanticModelSnapshot(snapshot.DocumentId, snapshot.Revision,
                semantic.Elements, semantic.Relationships.Select(flow => flow.Id == id
                    ? new SemanticRelationshipSnapshot(flow.Id, flow.TypeId, flow.SourceId, flow.TargetId,
                        [new(BpmnSemanticProperties.Name, PropertyValue.FromBoolean(true))]) : flow),
                semantic.NestedScopes, semantic.ScopeMemberships, semantic.ModelProfiles, semantic.ProfileAssignments),
                snapshot.VisualModel, snapshot.Metadata, snapshot.Publication);
        }
        var registration = BpmnPluginRegistration.N100;
        var policy = new ElementConnectorAnchorPolicyRegistry(registration.ConnectorAnchorPolicies);
        var construction = DocumentReconstructor.Reconstruct(snapshot, policy);
        Assert.True(construction.Succeeded, string.Join("; ", construction.Diagnostics.Select(item => item.Message)));
        var document = construction.Document!;
        var before = document.CaptureSnapshot();
        var history = new HistoryManager(document);
        var processor = new CommandProcessor(registration.CommandHandlers, registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies, connectorAnchorPolicyProvider: policy);
        var target = failure switch
        {
            "element" => BpmnDemoPipeline.ExclusiveGatewayId,
            "missing" => new Inceptus.DocumentEngine.Contracts.Primitives.SemanticElementId("a11:missing"),
            _ => id,
        };
        using var cancellation = new CancellationTokenSource();
        if (failure == "cancelled")
        {
            await cancellation.CancelAsync();
        }
        var result = await history.ExecuteAsync(processor,
            new UpdateBpmnSequenceFlowNameCommand(document.DocumentId, document.Revision, target,
                failure == "unchanged" ? null : "Approved"), cancellation.Token);
        Assert.False(result.IsCommitted);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(0, history.CaptureStatus().EntryCount);
    }
}
