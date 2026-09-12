using System.Collections.Concurrent;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task RejectedContextDiagnosticSurvivesSurfaceRebuildAndClearsOnReplacement()
    {
        var snapshot = CreateContextDiagnosticSnapshot(ulong.MaxValue);
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1000d, 700d, 1d));
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: snapshot),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("diagnostic-lifetime-canvas", "diagnostic-lifetime-standby",
            "diagnostic-lifetime-container");
        var session = Session(host);
        await OpenContextDiagnosticAnchorMenuAsync(host);
        var before = session.CaptureState();
        var original = host.CaptureDocumentSnapshot().Snapshot!;

        var rejected = await host.ExecuteConnectorAnchorContextActionAsync(ConnectorAnchorRole.Source);

        Assert.NotNull(rejected);
        Assert.False(rejected.Succeeded);
        Assert.False(rejected.IsCommitted);
        var diagnostic = Assert.Single(host.CaptureState().InteractionDiagnostics);
        Assert.Same(Assert.Single(rejected.Diagnostics), diagnostic);
        Assert.Equal("CMD_REVISION_PREPARATION_FAILURE", diagnostic.Code);
        Assert.Same(original, host.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);

        // The browser's in-flow alert reduces the canvas height and starts a derived rebuild.
        await surface.RaiseAsync(new Canvas2DSurfaceSize(1000d, 665.6d, 1d));
        await session.WaitForIdleAsync();
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        var rebuilt = session.CaptureState();

        Assert.NotEqual(before.Generation, rebuilt.Generation);
        Assert.NotSame(before.CurrentScene, rebuilt.CurrentScene);
        Assert.Same(diagnostic, Assert.Single(host.CaptureState().InteractionDiagnostics));
        Assert.Same(original, host.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(snapshot, original);
        Assert.Equal(before.DocumentId, rebuilt.DocumentId);
        Assert.Equal(before.DocumentRevision, rebuilt.DocumentRevision);
        Assert.Equal(before.ActiveScopeId, rebuilt.ActiveScopeId);
        Assert.Equal(snapshot.SemanticModel, original.SemanticModel);
        Assert.Equal(snapshot.VisualModel, original.VisualModel);
        Assert.Equal(snapshot.Metadata, original.Metadata);
        Assert.Equal(snapshot.Publication, original.Publication);
        Assert.Equal(before.HistoryStatus, rebuilt.HistoryStatus);
        Assert.Empty(ModelerChanges(log));
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());
        Assert.Empty(log.OfType<BpmnModelerOperationFailedEventArgs>());
        Assert.Empty(host.CaptureState().HostDiagnostics);

        var replacement = CreateContextDiagnosticSnapshot(0);
        Assert.True((await host.LoadDocumentAsync(replacement)).Succeeded);
        Assert.NotSame(session, Session(host));
        Assert.Empty(host.CaptureState().InteractionDiagnostics);
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        await OpenContextDiagnosticAnchorMenuAsync(host);
        Assert.True((await host.ExecuteConnectorAnchorContextActionAsync(
            ConnectorAnchorRole.Source))?.IsCommitted);
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));

        var current = host.CaptureDocumentSnapshot().Snapshot!;
        Assert.Equal(replacement.Revision.Increment(), current.Revision);
        Assert.Equal(replacement.Publication, current.Publication);
        Assert.Equal(1, Session(host).CaptureState().HistoryStatus.EntryCount);
        Assert.Empty(host.CaptureState().InteractionDiagnostics);
        Assert.Collection(ModelerChanges(log),
            change => Assert.Equal(BpmnModelerDocumentChangeKind.DocumentReplacement, change.Kind),
            change => Assert.Equal(BpmnModelerDocumentChangeKind.PersistentMutation, change.Kind));
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());
        Assert.Empty(log.OfType<BpmnModelerOperationFailedEventArgs>());
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("scope")]
    [InlineData("replacement")]
    [InlineData("disposal")]
    [InlineData("later-interaction")]
    public async Task RetainedContextDiagnosticClearsOnAuthoritativeOrInteractionTransition(string transition)
    {
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        var surface = new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d));
        await using var host = CreateHost(new RecordingRenderExecution(), surface,
            compositionFactory: new RejectingContextAnchorCompositionFactory(),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("invalidation-canvas", "invalidation-standby", "invalidation-container");
        var session = Session(host);
        var scene = session.CaptureState().CurrentScene!;
        var body = scene.Items.Single(item => item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        var pointer = PointerObserver(host);
        await pointer.ClickDocumentPointAsync(scene, Center(body.Bounds));
        scene = session.CaptureState().CurrentScene!;
        body = scene.Items.Single(item => item.Origin.VisualStateId == BpmnDemoPipeline.TaskVisualId &&
            Canvas2DNodeBodyMetadata.IsNodeBody(item));
        await pointer.ContextMenuDocumentPointAsync(scene,
            new PointD(body.Bounds.Left + (body.Bounds.Width * 0.5d), body.Bounds.Top));
        Assert.True(host.CaptureState().ContextMenu!.ConnectorAnchorAction!.CanAdd(ConnectorAnchorRole.Source));
        await pointer.LeaveAsync();
        var document = AttachedDocument(session);
        var snapshot = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var rejected = await host.ExecuteConnectorAnchorContextActionAsync(ConnectorAnchorRole.Source);
        Assert.NotNull(rejected);
        Assert.False(rejected.IsCommitted);
        var diagnostics = host.CaptureState().InteractionDiagnostics;
        Assert.Equal(rejected.Diagnostics, diagnostics);
        Assert.Collection(diagnostics,
            diagnostic => Assert.Equal("CMD_VALIDATOR_FAILURE", diagnostic.Code),
            diagnostic => Assert.Equal("TEST_CONTEXT_ANCHOR_REJECTED", diagnostic.Code));

        // The same retained-feedback path is exercised at an ordinary revision so a
        // subsequent persistent commit can prove invalidation, not only replacement.
        await surface.RaiseAsync(new Canvas2DSurfaceSize(1400d, 865.6d, 1d));
        await session.WaitForIdleAsync();
        Assert.Equal(diagnostics, host.CaptureState().InteractionDiagnostics);
        if (transition == "revision")
        {
            var visual = snapshot.VisualModel.VisualStates.Single(item => item.Id == BpmnDemoPipeline.TaskVisualId);
            Assert.True((await session.ExecuteAsync(new MoveVisualStateCommand(snapshot.DocumentId,
                snapshot.Revision, visual.Id, visual.Position + new VectorD(10d, 10d),
                VisualPlacementMode.Pinned))).IsCommitted);
            await session.WaitForIdleAsync();
            Assert.Equal(snapshot.Revision.Increment(), session.CaptureState().DocumentRevision);
            Assert.Equal(history.EntryCount + 1, session.CaptureState().HistoryStatus.EntryCount);
        }
        else if (transition == "scope")
        {
            // Exercise the session event directly; do not rely on the host navigation UI's clear.
            Assert.True((await session.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
            await session.WaitForIdleAsync();
            Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, session.CaptureState().ActiveScopeId);
        }
        else if (transition == "replacement")
        {
            Assert.True((await host.LoadDocumentAsync(CreateContextDiagnosticSnapshot(0))).Succeeded);
            Assert.NotSame(session, Session(host));
            Assert.True(session.CaptureState().IsClosed);
        }
        else if (transition == "disposal")
        {
            await host.DisposeAsync();
        }
        else
        {
            await pointer.LeaveAsync();
        }

        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(host.CaptureState().InteractionDiagnostics);
        Assert.Empty(log.OfType<BpmnModelerOperationFailedEventArgs>());
        Assert.Single(log.OfType<BpmnModelerReadyEventArgs>());
        Assert.Equal(transition is "revision" or "replacement" ? 1 : 0, ModelerChanges(log).Length);
        if (transition is not ("revision" or "replacement"))
        {
            Assert.Same(snapshot, document.CaptureSnapshot());
        }
    }

    private sealed class RejectingContextAnchorCompositionFactory : IDocumentCanvasCompositionFactory
    {
        public async ValueTask<DocumentCanvasComposition> CreateAsync(CancellationToken cancellationToken = default)
        {
            var composition = await BpmnModelerTestComposition.CreateDemoAsync(cancellationToken).ConfigureAwait(false);
            var original = composition.Configuration;
            var configuration = new EditingSessionConfiguration(
                original.ProjectionEngine, original.LayoutEngine, original.LayoutAlgorithmId,
                original.RoutingEngine, original.RoutingAlgorithmId, original.SceneBuilder,
                original.ProjectionContext, original.LayoutContext, original.RoutingContext,
                original.InitialEditorState, original.CommandHandlers,
                original.CommandValidators.Add(new CommandValidatorRegistration(
                    AddConnectorAnchorCommand.KnownTypeId, new CommandValidatorId("test:context-anchor:reject"),
                    new RejectingContextAnchorValidator())),
                original.HistoryPolicies, original.DocumentChangedSubscribers,
                original.ConnectorAnchorPolicyProvider, original.ModelProfileCatalog,
                original.InitialModelProfileViewState);
            return new DocumentCanvasComposition(composition.Document, configuration,
                composition.PropertiesSchemaCatalog, composition.Counters,
                composition.ToolboxPlacementCatalog, composition.AnchorConnectionCreationCatalog,
                composition.DocumentCreationIdentityProvider, composition.EndpointReconnectionCatalog,
                composition.DeletionCatalog, composition.ModelValidationCatalog,
                composition.ScopeNavigationCatalog, composition.BackgroundActionCatalog);
        }
    }

    private sealed class RejectingContextAnchorValidator : ICommandValidator
    {
        public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document) =>
            [new Diagnostic("TEST_CONTEXT_ANCHOR_REJECTED", DiagnosticSeverity.Error,
                "Reject the eligible anchor command to observe its feedback lifetime.")];
    }
}
