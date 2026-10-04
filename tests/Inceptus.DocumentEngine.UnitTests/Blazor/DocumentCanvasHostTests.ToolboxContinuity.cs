using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("BPMN.Task")]
    [InlineData("BPMN.UserTask")]
    [InlineData("BPMN.ServiceTask")]
    public async Task ToolboxRegionRejectionUsesStableDiagnosticSlotAndRetainsStableSceneContent(string type)
    {
        await using var test = await PanComponentFixture.CreateAsync();
        var session = Session(test.Host);
        await test.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            await EnableOrganizationalProfileAsync(session);
            await AddOrganizationalPoolFromMenuAsync(test.Host);
            await AddOrganizationalPoolFromMenuAsync(test.Host);
            Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot())).Succeeded);
        });
        await test.DrainAsync();
        var before = session.CaptureState();
        var snapshot = test.Host.CaptureDocumentSnapshot().Snapshot;
        var scene = before.CurrentScene!;
        var region = scene.SpatialPresentationPlan!.Regions.Last();
        var inside = Center(region.Bounds);
        var insideBody = new RectD(inside.X - 60d, inside.Y - 40d, 120d, 80d);
        Assert.True(region.Bounds.Contains(insideBody));
        Assert.True(DocumentGeometryBoundary.Contains(region.MapSceneToLocal(insideBody)));
        Assert.DoesNotContain(scene.Items, candidate =>
            candidate.SpatialRegion?.Id == region.Id && candidate.Origin.ProjectedObjectId is not null);
        var outside = new PointD(0d, 0d);
        Assert.DoesNotContain(scene.SpatialPresentationPlan.Regions,
            candidate => candidate.Bounds.Contains(outside));
        var item = new ToolboxCatalog(BpmnPluginRegistration.N100.ToolboxContributions)
            .Items.Single(candidate => candidate.ElementTypeId.Value == type);
        Assert.True(test.Selection.Select(item.ItemId));
        await test.Host.RefreshToolboxPlacementAsync();
        await test.DrainAsync();
        var initialMarkup = await test.MarkupAsync();
        Assert.Contains("class=\"canvas-diagnostics\"", initialMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("role=\"alert\"", DiagnosticSlot(initialMarkup), StringComparison.Ordinal);
        var uploads = test.Execution.FullUploadCount;

        for (var repeat = 0; repeat < 2; repeat++)
        {
            await test.Renderer.Dispatcher.InvokeAsync(() =>
                PointerObserver(test.Host).MoveDocumentPointAsync(scene, outside));
            await test.DrainAsync();
            Assert.Single(test.Host.CaptureState().InteractionDiagnostics,
                item => item.Code == "TOOLBOX_PLACEMENT_OUTSIDE_REGION");
            Assert.Contains(test.Host.CaptureState().InteractionDiagnostics,
                item => item.Code == "CMD_VISUAL_STATE_GEOMETRY_INVALID");
            var rejectedMarkup = await test.MarkupAsync();
            Assert.Contains("role=\"alert\"", DiagnosticSlot(rejectedMarkup), StringComparison.Ordinal);
            Assert.Contains(test.Host.CaptureState().InteractionDiagnostics[0].Message,
                DiagnosticSlot(rejectedMarkup), StringComparison.Ordinal);
            Assert.Equal(CanvasElements(initialMarkup), CanvasElements(rejectedMarkup));
            Assert.True(scene.RenderContent == session.CaptureState().CurrentScene!.RenderContent);
            Assert.False(Assert.Single(session.CaptureState().EditorState.TemporaryFeedback).PlacementPreview!.IsAllowed);

            await test.Renderer.Dispatcher.InvokeAsync(() =>
                PointerObserver(test.Host).MoveDocumentPointAsync(scene, inside));
            await test.DrainAsync();
            Assert.Empty(test.Host.CaptureState().InteractionDiagnostics);
            var readyMarkup = await test.MarkupAsync();
            Assert.DoesNotContain("role=\"alert\"", DiagnosticSlot(readyMarkup), StringComparison.Ordinal);
            Assert.Equal(CanvasElements(initialMarkup), CanvasElements(readyMarkup));
        }

        Assert.Same(snapshot, test.Host.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(before.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.True(scene.RenderContent == session.CaptureState().CurrentScene!.RenderContent);
        Assert.Equal(uploads, test.Execution.FullUploadCount);
        Assert.Equal(item.ItemId, test.Selection.SelectedItemId);
    }

    private static string DiagnosticSlot(string markup)
    {
        var match = Regex.Match(markup, "<div class=\"canvas-diagnostics\"[^>]*>(.*?)</div>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private static string[] CanvasElements(string markup) =>
        Regex.Matches(markup, "<canvas\\b[^>]*>", RegexOptions.CultureInvariant)
            .Select(match => match.Value).ToArray();
}
