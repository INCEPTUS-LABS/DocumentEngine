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
    public async Task ToolboxRegionRejectionUsesStableDiagnosticSlotWithoutChangingScene(string type)
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
            var diagnostic = Assert.Single(test.Host.CaptureState().InteractionDiagnostics);
            Assert.Equal("TOOLBOX_PLACEMENT_UNAVAILABLE", diagnostic.Code);
            var rejectedMarkup = await test.MarkupAsync();
            Assert.Contains("role=\"alert\"", DiagnosticSlot(rejectedMarkup), StringComparison.Ordinal);
            Assert.Contains(diagnostic.Message, DiagnosticSlot(rejectedMarkup), StringComparison.Ordinal);
            Assert.Equal(CanvasElements(initialMarkup), CanvasElements(rejectedMarkup));
            Assert.Same(scene, session.CaptureState().CurrentScene);
            Assert.Equal(before.Generation, session.CaptureState().Generation);
            Assert.Same(before.EditorState, session.CaptureState().EditorState);

            await test.Renderer.Dispatcher.InvokeAsync(() =>
                PointerObserver(test.Host).MoveDocumentPointAsync(scene, Center(region.Bounds)));
            await test.DrainAsync();
            Assert.Empty(test.Host.CaptureState().InteractionDiagnostics);
            var readyMarkup = await test.MarkupAsync();
            Assert.DoesNotContain("role=\"alert\"", DiagnosticSlot(readyMarkup), StringComparison.Ordinal);
            Assert.Equal(CanvasElements(initialMarkup), CanvasElements(readyMarkup));
        }

        Assert.Same(snapshot, test.Host.CaptureDocumentSnapshot().Snapshot);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Equal(before.DocumentRevision, session.CaptureState().DocumentRevision);
        Assert.Same(scene, session.CaptureState().CurrentScene);
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
