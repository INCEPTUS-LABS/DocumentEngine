using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task A22ReferenceHostSampleHasExplicitParticipantsAndPreparesUsingTheActualHost()
    {
        var sample = await BpmnPoolDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync();
        var initial = sample.CaptureSnapshot();
        Assert.Null(initial.VisualModel.RoutingScopes);
        Assert.Empty(initial.VisualModel.DocumentRoutingStates);
        Assert.Empty(initial.SemanticModel.Relationships);
        Assert.Single(initial.SemanticModel.Elements, element => element.TypeId == BpmnSemanticTypes.Participant);
        await using var host = CreatePropertiesUxHost(initial);
        await host.InitializeAsync("a22-reference-sample", "a22-reference-container");
        Assert.True(host.CaptureState().IsInitialized, string.Join("; ", host.CaptureState().HostDiagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message + " " + string.Join(",", diagnostic.Context))));
        var state = Session(host).CaptureState();
        Assert.Equal(12, state.ProjectedGraph!.Nodes.Length);
        Assert.IsType<ProcessScopePresentationContext>(state.PresentationContext);
        Assert.Equal(0, state.HistoryStatus.EntryCount);
        Assert.Equal(initial.VisualModel.VisualStates.AsEnumerable(), AttachedDocument(Session(host)).VisualModel.VisualStates);
    }

    [Fact]
    public async Task A22MessageFlowPropertiesSetRenameClearAndHistoryPreserveEndpointsAndGeometry()
    {
        await using var host = CreatePropertiesUxHost(A22ViewSnapshot());
        await host.InitializeAsync("a22-message-properties", "a22-message-container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var sourceAnchor = new ConnectorAnchorId("a22:properties:source");
        var targetAnchor = new ConnectorAnchorId("a22:properties:target");
        var targetId = new SemanticElementId("a22:host:peer-task");
        var targetVisualId = new VisualStateId("a22:host:peer-task-visual");
        Assert.True((await session.ExecuteAsync(new AddConnectorAnchorCommand(document.DocumentId,
            document.Revision, A22ViewTaskVisualId, sourceAnchor, ConnectorAnchorSide.Bottom,
            ConnectorAnchorRole.Source, 0))).IsCommitted);
        Assert.True((await session.ExecuteAsync(new AddConnectorAnchorCommand(document.DocumentId,
            document.Revision, targetVisualId, targetAnchor, ConnectorAnchorSide.Top,
            ConnectorAnchorRole.Target, 0))).IsCommitted);
        var relationshipId = new SemanticElementId("a22:properties:message");
        var visualId = new VisualStateId("a22:properties:message-visual");
        var created = await session.ExecuteAsync(new CreateBpmnMessageFlowCommand(document.DocumentId,
            document.Revision, relationshipId, visualId, A22ViewTaskId, targetId, sourceAnchor, targetAnchor));
        Assert.True(created.IsCommitted, string.Join("; ", created.Diagnostics.Select(diagnostic => diagnostic.Message)));
        await session.WaitForIdleAsync();
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [visualId]))).Succeeded);
        await session.WaitForIdleAsync();

        string? previousName = null;
        foreach (var name in new[] { "Order submitted", "Order received", string.Empty })
        {
            var snapshot = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(visualId));
            Assert.Equal(BpmnSemanticTypes.MessageFlow, snapshot.TypeId);
            Assert.False(snapshot.CanEditType);
            Assert.Empty(snapshot.SwitchableTypes);
            Assert.Equal(A22ViewContext, snapshot.PresentationContext);
            var draft = new DocumentCanvasPropertiesDraft(snapshot);
            var field = Assert.Single(draft.DataFields);
            Assert.Equal("name", field.Definition.FieldId.Value);
            Assert.True(field.CanEdit);
            field.EditorValue = name;
            Assert.True(host.UpdatePropertiesFormState(true, visualId, draft.IsDirty));
            var before = document.CaptureSnapshot();
            var history = session.CaptureState().HistoryStatus.EntryCount;
            var applied = await host.ApplyPropertiesAsync(draft);
            Assert.True(applied.Succeeded, applied.Message);
            Assert.Equal(history + 1, session.CaptureState().HistoryStatus.EntryCount);
            Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
            Assert.Equal(before.VisualModel.EnumerateConnectorRoutingRecords(), document.CaptureSnapshot().VisualModel.EnumerateConnectorRoutingRecords());
            AssertName(name.Length == 0 ? null : name);
            Assert.Equal(name.Length == 0 ? null : name, host.GetValidationTargetName(
                ModelValidationTarget.ForSemanticElement(relationshipId)));
            Assert.True(host.UpdatePropertiesFormState(false, null, false));
            Assert.True((await session.UndoAsync()).IsCommitted);
            await session.WaitForIdleAsync();
            AssertName(previousName);
            Assert.True((await session.RedoAsync()).IsCommitted);
            await session.WaitForIdleAsync();
            previousName = name.Length == 0 ? null : name;
            AssertName(previousName);
        }

        void AssertName(string? expected)
        {
            var relationship = document.SemanticModel.Relationships.Single(item => item.Id == relationshipId);
            Assert.Equal(A22ViewTaskId, relationship.SourceId);
            Assert.Equal(targetId, relationship.TargetId);
            Assert.Equal(expected, relationship.Properties.TryGetValue(BpmnSemanticProperties.Name, out var value)
                ? value.TextValue : null);
        }
    }
}
