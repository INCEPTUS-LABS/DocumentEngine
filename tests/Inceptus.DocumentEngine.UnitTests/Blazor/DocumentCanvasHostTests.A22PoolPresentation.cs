using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(0.3d)]
    public async Task A22InitialParticipantGapMatchesStandalonePoolGapBeforeAnyMoveAndAfterReopen(double zoom)
    {
        var initial = A22PoolSnapshot();
        await using var host = CreatePropertiesUxHost(initial);
        await host.InitializeAsync("a22-initial-spacing", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var prepared = document.CaptureSnapshot();
        var regions = prepared.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
        RectD Saved(string pool) => regions.Single(region => region.ContainerSemanticElementId?.Value == pool).ContentBounds!.Value;
        Assert.Equal(Saved("sales").Bottom, Saved("warehouse").Top);
        Assert.Equal(40d, Saved("supplier").Top - Saved("warehouse").Bottom);
        Assert.Equal(40d, Saved("supplier2").Top - Saved("supplier").Bottom);
        Assert.Equal(initial.Revision, document.Revision);
        Assert.Equal(initial.VisualModel.VisualStates.AsEnumerable(), prepared.VisualModel.VisualStates);
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);

        Assert.True((await session.UpdateViewportAsync(new ViewportSnapshot(zoom, default))).Succeeded);
        await session.WaitForIdleAsync();
        var scene = session.CaptureState().CurrentScene!;
        RectD Displayed(string pool) => scene.SpatialPresentationPlan!.Regions.Single(region => region.ContainerSemanticElementId?.Value == pool).Bounds;
        var groupGap = scene.ViewportTransform.TransformPoint(Displayed("supplier").TopLeft).Y -
            scene.ViewportTransform.TransformPoint(new PointD(Displayed("warehouse").Left, Displayed("warehouse").Bottom)).Y;
        var standaloneGap = scene.ViewportTransform.TransformPoint(Displayed("supplier2").TopLeft).Y -
            scene.ViewportTransform.TransformPoint(new PointD(Displayed("supplier").Left, Displayed("supplier").Bottom)).Y;
        Assert.Equal(40d * zoom, groupGap, 10);
        Assert.Equal(standaloneGap, groupGap, 10);
        Assert.Single(scene.Items, item => item.Geometry.Content == "Company A");
        Assert.Equal(prepared, document.CaptureSnapshot());

        var opened = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(prepared).AsMemory());
        Assert.True(opened.Succeeded);
        await using var reopened = CreatePropertiesUxHost(opened.Document!.CaptureSnapshot());
        await reopened.InitializeAsync("a22-initial-spacing-reopened", "container");
        Assert.Equal(prepared, AttachedDocument(Session(reopened)).CaptureSnapshot());
    }

    [Fact]
    public async Task A22SharedParticipantAndPoolLabelsRemainIndependentThroughNameHistoryAndNativeOpen()
    {
        await using var host = A22PoolHost(new ToolboxSelectionState());
        await host.InitializeAsync("a22-names", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var regions = document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
        Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(document.DocumentId, document.Revision,
            new("company"), BpmnSemanticProperties.Name, "Company renamed"))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Company renamed");
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "sales");
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "warehouse");
        Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(document.DocumentId, document.Revision,
            new("sales"), OrganizationalSemanticProperties.Name, "Sales renamed"))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Company renamed");
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Sales renamed");
        Assert.Equal(regions.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single().Geometry.Regions);
        var bytes = NativeDocumentSerializer.Export(document.CaptureSnapshot());
        var opened = NativeDocumentSerializer.Import(bytes.AsMemory());
        Assert.True(opened.Succeeded);
        Assert.Equal(bytes.ToArray(), NativeDocumentSerializer.Export(opened.Document!).ToArray());
        await host.UndoAsync();
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "sales");
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Company renamed");
        await host.UndoAsync();
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Company A");
        await host.RedoAsync();
        await host.RedoAsync();
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Sales renamed");
    }

    [Fact]
    public async Task A22CompactGroupEnvelopeAndAnchorsFollowSceneMapWithoutPersistingGeometry()
    {
        await using var host = A22ConnectionHost(new ToolboxSelectionState());
        await host.InitializeAsync("a22-compact-group", "container");
        var session = Session(host);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [A22ViewParticipantVisualId]));
        await session.WaitForIdleAsync();
        var before = AttachedDocument(session).CaptureSnapshot();
        var original = A22ViewBody(session.CaptureState().CurrentScene!, A22ViewParticipantId).Bounds;
        var collapsed = session.CaptureState().ModelProfileElementViewState.WithCollapsed(OrganizationalModelProfile.Id, A22ViewPoolId, true);
        Assert.True((await session.UpdateModelProfileElementViewStateAsync(collapsed)).Succeeded);
        await session.WaitForIdleAsync();
        var scene = session.CaptureState().CurrentScene!;
        var body = A22ViewBody(scene, A22ViewParticipantId);
        Assert.True(body.Bounds.Height < original.Height);
        Assert.Equal(original, body.DerivedRegionMapping!.LogicalBounds);
        var anchor = Assert.Single(scene.Items, item => item.Metadata.TryGetValue(
            Inceptus.DocumentEngine.Canvas2D.Interaction.Canvas2DConnectorAnchorMetadata.AnchorId, out var id) &&
            id.TextValue == A22SourceAnchor(A22ViewParticipantVisualId).Value);
        Assert.Equal(scene.SpatialPresentationPlan!.MapLogicalToScene(new PointD(original.Left + original.Width / 2d, original.Bottom)), Center(anchor.Bounds));
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        var hidden = session.CaptureState().ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, false);
        Assert.True((await session.UpdateModelProfileViewStateAsync(hidden)).Succeeded);
        await session.WaitForIdleAsync();
        Assert.False(A22ViewBody(session.CaptureState().CurrentScene!, A22ViewParticipantId).IsVisible);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22ActivityMessageSupportDoesNotBypassOtherPublicationChecks(bool includePools)
    {
        await using var host = A22ConnectionHost(new ToolboxSelectionState());
        await host.InitializeAsync("a22-publish-message", "container");
        var session = Session(host);
        await SaveDefaultPublicationAsync(session);
        var document = AttachedDocument(session);
        Assert.True((await session.ExecuteAsync(new CreateBpmnMessageFlowCommand(document.DocumentId, document.Revision,
            new("a22:publish:message"), new("a22:publish:visual"), A22ViewTaskId, A22ToolPeerTask,
            A22SourceAnchor(A22ViewTaskVisualId), new(A22ToolPeerVisual.Value + ":target")))).IsCommitted);
        await session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        var state = session.CaptureState();
        var captured = await session.CapturePresentationAsync(state.ModelProfileViewState, state.ModelProfileElementViewState);
        Assert.True(captured.Succeeded);
        var result = new Inceptus.Bpmn.Integration.PublishingPackageGenerator().Generate(before, captured.Capture!,
            new Inceptus.Bpmn.Integration.BpmnPublishingOptions { IncludePools = includePools });
        Assert.False(result.Succeeded);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "PUBLISH_MESSAGE_ENDPOINT_UNSUPPORTED");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == Inceptus.DocumentEngine.Contracts.Diagnostics.DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == BpmnPublishedTokenRoleClassifier.CollaborationUnsupported);
        Assert.Null(result.Package);
        Assert.Same(before, document.CaptureSnapshot());
    }
}
