using System.Text.Json;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed partial class PhaseN104StaticPublishIntegrationTests
{
    [Fact]
    public async Task PublishInitialViewKeepsOnlyTheAuthorsSelectedRegionOfALargeDiagram()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        await using var renderer = CreateRenderer();
        var surface = new Canvas2DSurfaceSize(1200, 800, 1);
        Assert.True((await renderer.InitializeAsync("publish-large-view", surface)).Succeeded);
        var attached = await EditingSession.AttachAsync(composition.Document, renderer, composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        Assert.True(session.TryCaptureDocumentSnapshot(out var document));
        Assert.True((await session.ExecuteAsync(new UpdateDocumentPublicationCommand(document.DocumentId, document.Revision,
            "large-view", "Large diagram", "Selected opening region"))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.True((await session.ResizeAsync(surface)).Succeeded);
        Assert.True((await session.UpdateViewportAsync(new ViewportSnapshot(2.5, new(-40, -20)))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.True(session.TryCaptureDocumentSnapshot(out document));
        var before = session.CaptureState();
        var region = before.EditorState.Viewport.VisibleDocumentRegion!.Value;
        var captured = await session.CapturePresentationAsync(before.ModelProfileViewState, before.ModelProfileElementViewState);
        Assert.True(captured.Succeeded, Diagnostics(captured.Diagnostics));
        var generator = new PublishingPackageGenerator();
        var fit = generator.Generate(document, captured.Capture!);
        var selected = generator.Generate(document, captured.Capture!, new BpmnPublishingOptions
        {
            InitialViewMode = BpmnPublishingInitialViewMode.CurrentEditorView,
            EditorView = new(document.DocumentId, document.Revision, before.ActiveScopeId, region),
        });
        Assert.True(fit.Succeeded, Diagnostics(fit.Diagnostics));
        Assert.True(selected.Succeeded, Diagnostics(selected.Diagnostics));
        Assert.True(fit.Package!.Snapshot.Presentation.Nodes.Length >= 15);
        Assert.True(fit.Package.Snapshot.Presentation.ContentBounds.Width > region.Width * 2);
        Assert.Equal(new PublishedRect(region.X, region.Y, region.Width, region.Height), selected.Package!.Snapshot.InitialView!.VisibleRegion);
        Assert.Equal(JsonSerializer.Serialize(fit.Package.Snapshot.Presentation), JsonSerializer.Serialize(selected.Package.Snapshot.Presentation));
        Assert.Equal(JsonSerializer.Serialize(fit.Package.Snapshot.TokenGraph), JsonSerializer.Serialize(selected.Package.Snapshot.TokenGraph));
        Assert.True(session.TryCaptureDocumentSnapshot(out var after));
        Assert.Same(document, after);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData(0, false, false, "visible")]
    [InlineData(1, false, false, "visible")]
    [InlineData(1, false, true, "visible")]
    [InlineData(3, false, true, "visible")]
    [InlineData(2, true, true, "visible")]
    [InlineData(2, true, true, "hidden")]
    [InlineData(2, true, true, "collapsed-empty")]
    [InlineData(2, true, true, "collapsed-populated")]
    public async Task PublishPoolsUsesOnlyCapturedGraphicsAndPreservesExecutionAndDocument(
        int poolCount, bool shared, bool adopt, string visibility)
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        await using var renderer = CreateRenderer();
        Assert.True((await renderer.InitializeAsync("publish-pools", new Canvas2DSurfaceSize(1200, 800, 1))).Succeeded);
        var attached = await EditingSession.AttachAsync(composition.Document, renderer, composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        Assert.True(session.TryCaptureDocumentSnapshot(out var document));
        async Task Commit(ICommand command)
        {
            var result = await session.ExecuteAsync(command);
            Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
            await session.WaitForIdleAsync();
            Assert.True(session.TryCaptureDocumentSnapshot(out document));
        }
        await Commit(new UpdateDocumentPublicationCommand(document.DocumentId, document.Revision,
            "pool-publication", "Pool publication", "Frozen Pool/Lane graphics"));
        Assert.True((await session.NavigateToScopeAsync(BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        await Commit(new SetModelProfileAvailabilityCommand(document.DocumentId, document.Revision,
            [new ModelProfileAvailabilityChange(OrganizationalModelProfile.Id, true)]));
        var scope = session.CaptureState().ActiveScopeId;
        var pools = Enumerable.Range(0, poolCount).Select(index => new SemanticElementId($"publish:pool:{index}")).ToArray();
        foreach (var pool in pools)
            await Commit(new CreateOrganizationalPoolCommand(document.DocumentId, document.Revision, pool, scope,
                pool == pools[0] ? OrganizationalPoolCreationMode.AdoptEligibleUnassigned : OrganizationalPoolCreationMode.Empty,
                "Pool " + pool.Value));
        if (!adopt && poolCount > 0)
        {
            var unassigned = document.VisualModel.RoutingScopes!.Value.Single(item => item.ScopeId == scope).Geometry.Regions
                .Single(region => region.ContainerSemanticElementId is null && region.ProfileId == OrganizationalModelProfile.Id);
            await Commit(new SetOrganizationalRegionExpandedHeightCommand(document.DocumentId, document.Revision, scope, unassigned.Id, 800));
            var members = document.SemanticModel.Elements.Where(element =>
                element.AttachedToElementId is null && !pools.Contains(element.Id) && document.SemanticModel.GetScope(element.Id).Id == scope &&
                Inceptus.DocumentEngine.Organizational.Semantics.OrganizationalSemantics.GetOwningPoolId(document.SemanticModel, element.Id) == pools[0])
                .Select(element => element.Id).ToArray();
            foreach (var member in members)
                await Commit(new UnassignOrganizationalElementCommand(document.DocumentId, document.Revision, member));
        }
        var participant = new SemanticElementId("publish:participant");
        if (shared)
        {
            var collaboration = new SemanticElementId("publish:collaboration");
            await Commit(new CreateBpmnCollaborationCommand(document.DocumentId, document.Revision, collaboration));
            await Commit(new CreateBpmnParticipantCommand(document.DocumentId, document.Revision, participant, collaboration,
                name: "Shared Participant"));
            foreach (var pool in pools)
                await Commit(new SetBpmnPoolParticipantCommand(document.DocumentId, document.Revision, pool, participant));
        }
        Assert.True((await session.ResizeAsync(new Canvas2DSurfaceSize(1200, 800, 1))).Succeeded);
        Assert.True((await session.UpdateViewportAsync(new ViewportSnapshot(2, new(-500, -200)))).Succeeded);
        await session.WaitForIdleAsync();
        var before = session.CaptureState();
        var snapshot = document;
        var profiles = visibility == "hidden"
            ? before.ModelProfileViewState.WithPreferredVisibility(OrganizationalModelProfile.Id, false)
            : before.ModelProfileViewState;
        var elements = visibility.StartsWith("collapsed", StringComparison.Ordinal)
            ? before.ModelProfileElementViewState.WithCollapsed(OrganizationalModelProfile.Id,
                pools[visibility == "collapsed-empty" ? 1 : 0], true)
            : before.ModelProfileElementViewState;
        var captured = await session.CapturePresentationAsync(profiles, elements);
        Assert.True(captured.Succeeded, Diagnostics(captured.Diagnostics));
        var capture = captured.Capture!;
        var generator = new PublishingPackageGenerator();
        var legacy = generator.Generate(snapshot, capture);
        var disabled = generator.Generate(snapshot, capture, new BpmnPublishingOptions());
        var enabled = generator.Generate(snapshot, capture, new BpmnPublishingOptions { IncludePools = true });
        var region = before.EditorState.Viewport.VisibleDocumentRegion!.Value;
        var editorView = new BpmnPublishingEditorView(snapshot.DocumentId, snapshot.Revision, scope, region);
        var current = generator.Generate(snapshot, capture, new BpmnPublishingOptions
        { InitialViewMode = BpmnPublishingInitialViewMode.CurrentEditorView, EditorView = editorView });
        var currentPools = generator.Generate(snapshot, capture, new BpmnPublishingOptions
        { IncludePools = true, InitialViewMode = BpmnPublishingInitialViewMode.CurrentEditorView, EditorView = editorView });
        Assert.Equal(legacy.Succeeded, disabled.Succeeded);
        if (visibility == "collapsed-populated")
        {
            Assert.False(legacy.Succeeded);
            Assert.False(enabled.Succeeded);
            Assert.False(current.Succeeded);
            Assert.False(currentPools.Succeeded);
            Assert.Equal(legacy.Diagnostics.Select(item => item.Code), enabled.Diagnostics.Select(item => item.Code));
            Assert.Contains(enabled.Diagnostics, item => item.Code == "PUBLISH_TOKEN_NODE_MISSING");
        }
        else
        {
            Assert.True(legacy.Succeeded, Diagnostics(legacy.Diagnostics));
            Assert.True(enabled.Succeeded, Diagnostics(enabled.Diagnostics));
            Assert.Equal(legacy.Package!.Archive.ToArray(), disabled.Package!.Archive.ToArray());
            Assert.True(current.Succeeded, Diagnostics(current.Diagnostics));
            Assert.True(currentPools.Succeeded, Diagnostics(currentPools.Diagnostics));
            Assert.Null(legacy.Package.Snapshot.InitialView);
            Assert.Null(enabled.Package!.Snapshot.InitialView);
            using (var json = JsonDocument.Parse(legacy.Package.ProcessJson.AsMemory()))
                Assert.False(json.RootElement.TryGetProperty("initialView", out _));
            foreach (var (fit, selected) in new[] { (legacy.Package, current.Package!), (enabled.Package, currentPools.Package!) })
            {
                Assert.Equal(new PublishedRect(region.X, region.Y, region.Width, region.Height), selected.Snapshot.InitialView!.VisibleRegion);
                Assert.NotEqual(fit.Snapshot.Presentation.ContentBounds, selected.Snapshot.InitialView.VisibleRegion);
                Assert.Equal(JsonSerializer.Serialize(fit.Snapshot.Presentation), JsonSerializer.Serialize(selected.Snapshot.Presentation));
                Assert.Equal(JsonSerializer.Serialize(fit.Snapshot.TokenGraph), JsonSerializer.Serialize(selected.Snapshot.TokenGraph));
                Assert.Equal(fit.Snapshot.Runtime, selected.Snapshot.Runtime);
                Assert.Equal(ArchiveEntryNames(fit.Archive), ArchiveEntryNames(selected.Archive));
            }
            var normal = legacy.Package.Snapshot;
            var withPools = enabled.Package!.Snapshot;
            Assert.Equal(1, withPools.FormatVersion);
            Assert.Equal(JsonSerializer.Serialize(normal.TokenGraph), JsonSerializer.Serialize(withPools.TokenGraph));
            Assert.Equal(JsonSerializer.Serialize(normal.Presentation.Nodes), JsonSerializer.Serialize(withPools.Presentation.Nodes));
            Assert.Equal(JsonSerializer.Serialize(normal.Presentation.Connectors), JsonSerializer.Serialize(withPools.Presentation.Connectors));
            Assert.Equal(normal.Runtime, withPools.Runtime);
            var owners = pools.Append(participant).ToHashSet();
            var expected = capture.Scene.Items.Where(item => item.IsVisible && item.Origin.SemanticElementId is { } id &&
                owners.Contains(id) && (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
                !Canvas2DSceneExportMetadata.IsExcluded(item)).OrderBy(item => item.Layer).ThenBy(item => item.ZIndex)
                .ThenBy(item => item.Id.Value, StringComparer.Ordinal).ToArray();
            var normalIds = normal.Presentation.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var added = withPools.Presentation.Items.Where(item => !normalIds.Contains(item.Id)).ToArray();
            Assert.Equal(expected.Select(item => item.Id.Value), added.Select(item => item.Id));
            foreach (var (source, published) in expected.Zip(added))
            {
                Assert.Equal(source.Geometry.Content, published.Content);
                Assert.Equal((int)source.Layer, published.Layer);
                Assert.Equal(source.ZIndex, published.ZIndex);
                Assert.Equal(source.Style.Fill, published.Fill);
                Assert.Equal(source.Transform.OffsetX, published.Transform.OffsetX);
                Assert.Equal(source.Transform.OffsetY, published.Transform.OffsetY);
                Assert.Equal(source.Transform.M12, published.Transform.M12);
                Assert.Equal(source.Geometry.Bounds.Width, published.GeometryBounds.Width);
                Assert.Equal(source.Geometry.Bounds.Height, published.GeometryBounds.Height);
                var bounds = withPools.Presentation.ContentBounds;
                Assert.True(bounds.X <= source.Bounds.Left + 0.001 && bounds.Y <= source.Bounds.Top + 0.001);
                Assert.True(bounds.X + bounds.Width >= source.Bounds.Right - 0.001 && bounds.Y + bounds.Height >= source.Bounds.Bottom - 0.001);
            }
            Assert.Equal(withPools.Presentation.Items.OrderBy(item => item.Layer).ThenBy(item => item.ZIndex)
                .ThenBy(item => item.Id, StringComparer.Ordinal), withPools.Presentation.Items);
            Assert.DoesNotContain(withPools.Presentation.Items, item => item.Id.Contains("unassigned-region", StringComparison.Ordinal));
            Assert.DoesNotContain(withPools.TokenGraph.Nodes, node => owners.Any(id => id.Value == node.Id));
            if (poolCount == 0 || visibility == "hidden") Assert.Empty(added);
            else
            {
                foreach (var pool in pools) Assert.Contains(added, item => item.Content == "Pool " + pool.Value);
                if (shared) Assert.Contains(added, item => item.Content == "Shared Participant");
                Assert.All(added.Where(item => item.Fill is not null && item.GeometryKind != (int)Canvas2DSceneGeometryKind.Text),
                    item => Assert.Equal((int)Canvas2DSceneLayer.Background, item.Layer));
            }
            Assert.Equal(ArchiveEntryNames(legacy.Package.Archive), ArchiveEntryNames(enabled.Package.Archive));
        }
        Assert.True(session.TryCaptureDocumentSnapshot(out var after));
        Assert.Same(snapshot, after);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Same(before.EditorState, session.CaptureState().EditorState);
        Assert.Equal(before.ModelProfileViewState, session.CaptureState().ModelProfileViewState);
        Assert.Equal(before.ModelProfileElementViewState, session.CaptureState().ModelProfileElementViewState);
    }
}
