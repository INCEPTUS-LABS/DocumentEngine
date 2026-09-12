using System.IO.Compression;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Publishing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN104StaticPublishIntegrationTests
{
    [Fact]
    public async Task CurrentNestedScopePublishesMinimalReachableStartActivityEndPackage()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        await using var renderer = CreateRenderer();
        var initialized = await renderer.InitializeAsync(
            "phase-n104-nested",
            new Canvas2DSurfaceSize(1200d, 800d, 1d));
        Assert.True(initialized.Succeeded, Diagnostics(initialized.Diagnostics));
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var publication = await session.ExecuteAsync(
            new UpdateDocumentPublicationCommand(
                composition.Document.DocumentId,
                composition.Document.Revision,
                "nested-process",
                "Nested process",
                "N10.4 integration fixture"));
        Assert.True(publication.IsCommitted, Diagnostics(publication.Diagnostics));
        await session.WaitForIdleAsync();
        Assert.True((await session.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId))
            .Succeeded);
        await session.WaitForIdleAsync();
        var before = session.CaptureState();
        var documentBefore = composition.Document.CaptureSnapshot();

        var captureResult = await session.CapturePresentationAsync(
            before.ModelProfileViewState,
            ModelProfileElementViewStateSnapshot.Empty);
        var capture = Assert.IsType<EditingSessionPresentationCapture>(captureResult.Capture);
        var build = new PublishedProcessPackageBuilder(
            new BpmnPublishedTokenRoleClassifier(),
            new BpmnPublishedNodeDataMapper()).Build(capture);

        Assert.True(captureResult.Succeeded, Diagnostics(captureResult.Diagnostics));
        Assert.True(build.Succeeded, Diagnostics(build.Diagnostics));
        var package = Assert.IsType<PublishedProcessPackage>(build.Package);
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId.Value,
            package.Snapshot.Source.ScopeId);
        Assert.Equal(before.DocumentRevision.Value, package.Snapshot.Source.Revision);
        Assert.Equal(3, package.Snapshot.TokenGraph.Nodes.Length);
        Assert.Contains(package.Snapshot.TokenGraph.Nodes,
            node => node.Role == PublishedTokenRole.Start);
        Assert.Contains(package.Snapshot.TokenGraph.Nodes,
            node => node.Role == PublishedTokenRole.ActivityDelay);
        Assert.Contains(package.Snapshot.TokenGraph.Nodes,
            node => node.Role == PublishedTokenRole.End);
        Assert.Equal(
            ["index.html", "process.json", "process.data.js", "inceptus.publish.js", "styles.css"],
            ArchiveEntryNames(package.Archive));
        Assert.All(package.Snapshot.Presentation.Nodes, node =>
            Assert.Contains(capture.ProjectedGraph.Nodes, projected =>
                projected.Source.SemanticElementId.Value == node.Id));

        var after = session.CaptureState();
        Assert.Same(documentBefore, composition.Document.CaptureSnapshot());
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.ActiveScopeId, after.ActiveScopeId);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.EditorState, after.EditorState);
    }

    [Fact]
    public async Task CaptureAfterPinnedEditKeepsDocumentArtifactsSceneAndGenerationCoherent()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        await using var renderer = CreateRenderer();
        var initialized = await renderer.InitializeAsync(
            "phase-n104-coherent",
            new Canvas2DSurfaceSize(1200d, 800d, 1d));
        Assert.True(initialized.Succeeded, Diagnostics(initialized.Diagnostics));
        var attachment = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var visual = composition.Document.VisualModel.VisualStates.Single(item =>
            item.Id == BpmnDemoPipeline.TaskVisualId);
        var moved = await session.ExecuteAsync(new MoveVisualStateCommand(
            composition.Document.DocumentId,
            composition.Document.Revision,
            visual.Id,
            new PointD(visual.Position.X + 37d, visual.Position.Y + 19d),
            VisualPlacementMode.Pinned));
        Assert.True(moved.IsCommitted, Diagnostics(moved.Diagnostics));
        await session.WaitForIdleAsync();
        var before = session.CaptureState();

        var result = await session.CapturePresentationAsync(
            before.ModelProfileViewState,
            before.ModelProfileElementViewState);

        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        var capture = Assert.IsType<EditingSessionPresentationCapture>(result.Capture);
        Assert.Equal(before.DocumentId, capture.Document.DocumentId);
        Assert.Equal(before.DocumentRevision, capture.Document.Revision);
        Assert.Equal(before.DocumentRevision, capture.ProjectedGraph.SourceRevision);
        Assert.Equal(before.DocumentRevision, capture.LayoutResult.SourceRevision);
        Assert.Equal(before.DocumentRevision, capture.RoutingResult.SourceRevision);
        Assert.Equal(before.DocumentRevision, capture.Scene.SourceRevision);
        Assert.Equal(before.ActiveScopeId, capture.ActiveScopeId);
        Assert.Equal(before.Generation, capture.Generation);
        var primary = capture.Scene.Items.Single(item =>
            item.Id == Canvas2DSceneObjectIdentity.ForProjected(
                capture.ProjectedGraph.Nodes.Single(node =>
                    node.Source.SemanticElementId == BpmnDemoPipeline.TaskId).Id,
                "node"));
        Assert.Equal(visual.Position.X + 37d, primary.Bounds.X, 9);
        Assert.Equal(visual.Position.Y + 19d, primary.Bounds.Y, 9);
        var after = session.CaptureState();
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Same(before.EditorState, after.EditorState);
    }

    private static Canvas2DRenderer CreateRenderer() =>
        new(
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "org.dejavu.DejaVuSans",
                        "2.37",
                        "DejaVu Sans",
                        "fonts/DejaVuSans-2.37.ttf",
                        400,
                        TextFontStyle.Normal),
                ],
                defaultFontFamily: "DejaVu Sans"));

    private static string[] ArchiveEntryNames(System.Collections.Immutable.ImmutableArray<byte> zip)
    {
        using var input = new MemoryStream(zip.ToArray(), writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        return archive.Entries.Select(static entry => entry.FullName).ToArray();
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));
}
