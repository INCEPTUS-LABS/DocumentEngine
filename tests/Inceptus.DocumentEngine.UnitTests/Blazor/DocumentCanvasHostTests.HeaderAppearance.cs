using System.Text;
using System.Text.Json;
using Inceptus.DocumentEngine.Bpmn.Appearance;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Export;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false, "white", false)]
    [InlineData(true, "white", false)]
    [InlineData(false, "mixed", false)]
    [InlineData(true, "mixed", false)]
    [InlineData(false, "mixed", true)]
    [InlineData(true, "mixed", true)]
    public async Task HeaderAppearanceOnlyChangesPaintAcrossExpandedAndCompactViews(bool compact, string scheme, bool nested)
    {
        var seed = A22PoolSnapshot();
        var scope = new DocumentScopeId("headers:child");
        if (nested)
        {
            var owner = BpmnSemanticFactory.CreateSubProcess(new("headers:owner"), "SUB", "Nested headers");
            seed = new(new SemanticModelSnapshot(seed.DocumentId, seed.Revision, seed.SemanticModel.Elements.Append(owner),
                nestedScopes: [new(scope, seed.SemanticModel.RootScopeId, owner.Id)],
                scopeMemberships: seed.SemanticModel.Elements.Where(element => element.ContainmentKind != SemanticElementContainmentKind.Document)
                    .Select(element => new SemanticElementScopeMembershipSnapshot(element.Id, scope)),
                modelProfiles: seed.SemanticModel.ModelProfiles, profileAssignments: seed.SemanticModel.ProfileAssignments),
                new VisualModelSnapshot(seed.DocumentId, seed.Revision, seed.VisualModel.VisualStates.Append(
                    new VisualStateSnapshot(new("headers:owner:visual"), owner.Id, new(0, 0), new(140, 90), VisualPlacementMode.Pinned)),
                    seed.VisualModel.ProfileElementPresentations), seed.Metadata);
        }
        await using var host = CreatePropertiesUxHost(seed);
        await host.InitializeAsync("header-paint", "container");
        Assert.True(host.CaptureState().IsInitialized, PublishDiagnostics(host.CaptureState().HostDiagnostics));
        var session = Session(host);
        if (nested)
        {
            Assert.True((await session.NavigateToScopeAsync(scope)).Succeeded);
            await session.WaitForIdleAsync();
        }
        if (compact)
        {
            var view = session.CaptureState().ModelProfileElementViewState;
            foreach (var id in new[] { "sales", "warehouse", "supplier" })
                view = view.WithCollapsed(OrganizationalModelProfile.Id, new(id), true);
            Assert.True((await session.UpdateModelProfileElementViewStateAsync(view)).Succeeded);
            await session.WaitForIdleAsync();
        }
        var before = AttachedDocument(session).CaptureSnapshot();
        var oldItems = session.CaptureState().CurrentScene!.Items;
        var colours = new[] { ("pool-header", "#112233", "#223344", "#334455"), ("lane-header", "#445566", "#556677", "#667788"),
            ("participant-header", "#778899", "#8899AA", "#99AABB") };
        var changes = colours.SelectMany(value => new[] {
            new KeyValuePair<string, PropertyValue?>(BpmnAppearanceSettings.Key(scheme, value.Item1, "background"), PropertyValue.FromText(value.Item2)),
            new KeyValuePair<string, PropertyValue?>(BpmnAppearanceSettings.Key(scheme, value.Item1, "border"), PropertyValue.FromText(value.Item3)),
            new KeyValuePair<string, PropertyValue?>(BpmnAppearanceSettings.Key(scheme, value.Item1, "text"), PropertyValue.FromText(value.Item4)) })
            .Append(new(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText(scheme)));
        Assert.True((await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(before.DocumentId, before.Revision, changes))).IsCommitted);
        await session.WaitForIdleAsync();
        var scene = session.CaptureState().CurrentScene!;
        Assert.Equal("#112233", HeaderItem(scene, "supplier", "pool-header").Style.Fill);
        Assert.Equal("#223344", HeaderItem(scene, "supplier", "pool-header").Style.Stroke);
        Assert.Equal("#334455", HeaderItem(scene, "supplier", "pool-name").Style.Fill);
        Assert.Equal("#445566", HeaderItem(scene, "sales", "pool-header").Style.Fill);
        Assert.Equal("#556677", HeaderItem(scene, "sales", "pool-header").Style.Stroke);
        Assert.Equal("#667788", HeaderItem(scene, "sales", "pool-name").Style.Fill);
        Assert.Contains(scene.Items, item => item.Origin.SemanticElementId?.Value == "company" && item.Style.Fill == "#778899" && item.Style.Stroke == "#8899AA");
        Assert.Contains(scene.Items, item => item.Geometry.Content == "Company A" && item.Style.Fill == "#99AABB");
        foreach (var item in oldItems)
        {
            var current = Assert.Single(scene.Items, value => value.Id == item.Id);
            Assert.Equal(item.Geometry, current.Geometry);
            Assert.Equal(item.Bounds, current.Bounds);
            Assert.Equal(item.Transform, current.Transform);
            Assert.Equal(item.Clip, current.Clip);
            Assert.Equal(item.HitTestPolicy, current.HitTestPolicy);
            if (item.Origin.StableSourceKey?.EndsWith(":pool-background", StringComparison.Ordinal) == true)
                Assert.Equal(item.Style, current.Style);
        }
        A23PresentationEqual(before, AttachedDocument(session).CaptureSnapshot());
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(semanticSceneSelection: new("sales"),
            hoveredObjectId: HeaderItem(scene, "sales", "pool-header").Id));
        var selected = session.CaptureState().CurrentScene!;
        Assert.Equal("#445566", HeaderItem(selected, "sales", "pool-header").Style.Fill);
        Assert.Equal("#2563eb", HeaderItem(selected, "sales", "pool-header").Style.Stroke);
        Assert.Equal("#667788", HeaderItem(selected, "sales", "pool-name").Style.Fill);
        Assert.Contains(selected.Items, item => item.Style.Stroke == "#f59e0b"); // Existing semantic-header hover path is retained.
        await session.UpdateEditorStateAsync(EditorStateSnapshot.Empty);
        await host.UndoAsync();
        Assert.Equal(before.Metadata.ExtensionProperties, AttachedDocument(session).Metadata.ExtensionProperties);
        Assert.Equal("#eef2f7", HeaderItem(session.CaptureState().CurrentScene!, "sales", "pool-header").Style.Fill);
        await host.RedoAsync();
        var saved = AttachedDocument(session).CaptureSnapshot();
        var imported = NativeDocumentSerializer.Import(NativeDocumentSerializer.Export(saved).AsMemory());
        Assert.True(imported.Succeeded);
        await using var reopened = CreatePropertiesUxHost(imported.Document!.CaptureSnapshot());
        await reopened.InitializeAsync("header-reopened", "container");
        if (nested)
        {
            Assert.True((await Session(reopened).NavigateToScopeAsync(scope)).Succeeded);
            await Session(reopened).WaitForIdleAsync();
        }
        Assert.Equal("#112233", HeaderItem(Session(reopened).CaptureState().CurrentScene!, "supplier", "pool-header").Style.Fill);
    }

    [Theory]
    [InlineData("white", false, "#dbeafe")]
    [InlineData("white", true, "#123456")]
    [InlineData("mixed", false, "#E7F0EF")]
    [InlineData("mixed", true, "#123456")]
    public async Task HeaderAppearanceSelectionPreservesLegacyWhiteOrConfiguredBackground(string scheme, bool custom, string expected)
    {
        await using var host = A22PoolHost(new ToolboxSelectionState());
        await host.InitializeAsync("header-selection", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var changes = new List<KeyValuePair<string, PropertyValue?>> { new(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText(scheme)) };
        if (custom) changes.Add(new(BpmnAppearanceSettings.Key(scheme, "lane-header", "background"), PropertyValue.FromText("#123456")));
        Assert.True((await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(document.DocumentId, document.Revision, changes))).Succeeded);
        await session.WaitForIdleAsync();
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(semanticSceneSelection: new("sales")));
        Assert.Equal(expected, HeaderItem(session.CaptureState().CurrentScene!, "sales", "pool-header").Style.Fill);
        var key = BpmnAppearanceSettings.Key(scheme, "lane-header", "background");
        Assert.True((await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(document.DocumentId, document.Revision, [new(key, null)]))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.Equal(scheme == "white" ? "#dbeafe" : "#E7F0EF", HeaderItem(session.CaptureState().CurrentScene!, "sales", "pool-header").Style.Fill);
    }

    [Fact]
    public async Task HeaderAppearanceChildEditsCommitOnlyThroughParentAndRejectStaleDraft()
    {
        await using var host = A22PoolHost(new ToolboxSelectionState());
        await host.InitializeAsync("header-draft", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var snapshot = Assert.IsType<DocumentCanvasModelViewPropertiesSnapshot>(await host.OpenModelViewPropertiesAsync(true));
        var draft = new DocumentCanvasModelViewPropertiesDraft(snapshot);
        draft.BeginColourEditing();
        draft.ColourEditor!.SelectScheme("mixed");
        draft.ColourEditor.Set("participant-header", "border", "#AABBCC");
        Assert.False((await host.ApplyModelViewPropertiesAsync(draft)).Succeeded);
        Assert.Equal(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
        Assert.True(draft.AcceptColourEditing());
        Assert.True((await host.ApplyModelViewPropertiesAsync(draft)).Succeeded);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        var after = AttachedDocument(session).CaptureSnapshot();
        await host.UndoAsync();
        Assert.Equal(before.Metadata.ExtensionProperties, AttachedDocument(session).Metadata.ExtensionProperties);
        await host.RedoAsync();
        Assert.Equal(after.Metadata.ExtensionProperties, AttachedDocument(session).Metadata.ExtensionProperties);
        snapshot = Assert.IsType<DocumentCanvasModelViewPropertiesSnapshot>(await host.OpenModelViewPropertiesAsync(true));
        draft = new(snapshot);
        draft.BeginColourEditing();
        draft.ColourEditor!.Set("pool-header", "text", "#445566");
        Assert.True(draft.AcceptColourEditing());
        Assert.True((await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(after.DocumentId, AttachedDocument(session).Revision,
            [new(BpmnAppearanceSettings.Key("mixed", "task", "text"), PropertyValue.FromText("#778899"))]))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(DocumentCanvasModelViewPropertiesApplyStatus.Stale, (await host.ApplyModelViewPropertiesAsync(draft)).Status);
    }

    private static Canvas2DSceneItem HeaderItem(Canvas2DScene scene, string semantic, string key) =>
        Assert.Single(scene.Items, item => item.Origin.StableSourceKey == semantic + ":" + key);

    [Fact]
    public async Task HeaderAppearanceFollowsOwnershipAndProfileVisibilityWithoutIndividualOverrides()
    {
        await using var host = A22PoolHost(new ToolboxSelectionState());
        await host.InitializeAsync("header-ownership", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        Assert.True((await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(document.DocumentId, document.Revision,
            [new(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText("mixed"))]))).IsCommitted);
        await session.WaitForIdleAsync();
        var metadata = document.Metadata.ExtensionProperties;
        Assert.Equal("#E9EEF5", HeaderItem(session.CaptureState().CurrentScene!, "supplier", "pool-header").Style.Fill);
        Assert.True((await session.ExecuteAsync(new SetBpmnPoolParticipantCommand(document.DocumentId, document.Revision,
            new("supplier"), new("company")))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal("#E7F0EF", HeaderItem(session.CaptureState().CurrentScene!, "supplier", "pool-header").Style.Fill);
        Assert.Equal(metadata, document.Metadata.ExtensionProperties);
        var beforeHide = document.CaptureSnapshot();
        var view = session.CaptureState().ModelProfileViewState;
        Assert.True((await session.UpdateModelProfileViewStateAsync(view.WithPreferredVisibility(OrganizationalModelProfile.Id, false))).Succeeded);
        await session.WaitForIdleAsync();
        Assert.DoesNotContain(session.CaptureState().CurrentScene!.Items, item => item.Origin.StableSourceKey == "supplier:pool-header" && item.IsVisible);
        Assert.True((await session.UpdateModelProfileViewStateAsync(view)).Succeeded);
        await session.WaitForIdleAsync();
        Assert.Equal("#E7F0EF", HeaderItem(session.CaptureState().CurrentScene!, "supplier", "pool-header").Style.Fill);
        Assert.Equal(beforeHide, document.CaptureSnapshot());
        await host.UndoAsync();
        Assert.Equal("#E9EEF5", HeaderItem(session.CaptureState().CurrentScene!, "supplier", "pool-header").Style.Fill);
    }

    [Fact]
    public async Task HeaderAppearanceExportsResolvedPaintWithoutChangingPublishedExecution()
    {
        var pools = A22PoolSnapshot();
        var flow = A23Snapshot([("start1", "a"), ("a", "end1"), ("start2", "b"), ("b", "end2")]);
        var id = pools.DocumentId;
        var seed = new DocumentSnapshot(new SemanticModelSnapshot(id, pools.Revision,
            pools.SemanticModel.Elements.Where(element => element.TypeId == OrganizationalSemanticTypes.Pool || element.TypeId == BpmnSemanticTypes.Participant)
                .Concat(flow.SemanticModel.Elements), flow.SemanticModel.Relationships,
            modelProfiles: pools.SemanticModel.ModelProfiles,
            profileAssignments: flow.SemanticModel.Elements.Select(element => new ModelProfileElementAssignmentSnapshot(
                OrganizationalModelProfile.Id, element.Id, new(element.Id.Value is "start1" or "a" or "end1" ? "sales" : "supplier")))),
            new VisualModelSnapshot(id, pools.Revision, flow.VisualModel.VisualStates, pools.VisualModel.ProfileElementPresentations),
            new DocumentMetadataSnapshot(id, pools.Revision));
        await using var host = CreatePropertiesUxHost(seed);
        await host.InitializeAsync("header-export", "container");
        var session = Session(host);
        await SaveDefaultPublicationAsync(session);
        var before = (await host.CapturePresentationAsync())!.Capture!;
        var generator = new Inceptus.Bpmn.Integration.PublishingPackageGenerator();
        var options = new Inceptus.Bpmn.Integration.BpmnPublishingOptions { IncludePools = true };
        var oldPackage = generator.Generate(before.Document, before, options);
        Assert.True(oldPackage.Succeeded, PublishDiagnostics(oldPackage.Diagnostics));
        Assert.Contains(before.Scene.Items, item => item.Origin.SemanticElementId?.Value == "company" &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Rectangle && item.Style.Fill is null);
        var document = AttachedDocument(session);
        var changes = new List<KeyValuePair<string, PropertyValue?>> { new(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText("mixed")) };
        foreach (var family in new[] { "pool-header", "lane-header", "participant-header" })
            changes.Add(new(BpmnAppearanceSettings.Key("mixed", family, "text"), PropertyValue.FromText("#123456")));
        Assert.True((await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(id, document.Revision, changes))).IsCommitted);
        await session.WaitForIdleAsync();
        var capture = (await host.CapturePresentationAsync())!.Capture!;
        var prepared = GraphicalExportPreparer.Prepare(capture);
        Assert.True(prepared.Succeeded);
        var svg = SvgGraphicalExporter.Export(prepared.Scene!);
        Assert.True(svg.Succeeded);
        var svgText = Encoding.UTF8.GetString(svg.Artifact!.Content.AsSpan());
        var runtime = new AppearanceExportRuntime();
        Assert.True((await new Canvas2DGraphicalExporter(runtime).ExportAsync(capture, GraphicalExportFormat.Png)).Succeeded);
        var published = generator.Generate(capture.Document, capture, options);
        Assert.True(published.Succeeded, PublishDiagnostics(published.Diagnostics));
        var jsonText = Encoding.UTF8.GetString(published.Package!.ProcessJson.AsSpan());
        foreach (var colour in new[] { "#E9EEF5", "#E7F0EF", "#ECE9F4", "#123456" })
        {
            Assert.Contains(colour, svgText, StringComparison.Ordinal);
            Assert.Contains(runtime.Frame!.Items, item => item.Fill == colour);
            Assert.Contains(colour, jsonText, StringComparison.Ordinal);
        }
        using var oldJson = JsonDocument.Parse(oldPackage.Package!.ProcessJson.AsMemory());
        using var json = JsonDocument.Parse(published.Package.ProcessJson.AsMemory());
        Assert.Equal(1, json.RootElement.GetProperty("formatVersion").GetInt32());
        foreach (var field in new[] { "tokenGraph", "runtime" })
            Assert.Equal(oldJson.RootElement.GetProperty(field).GetRawText(), json.RootElement.GetProperty(field).GetRawText());
        Assert.Equal(JsonSerializer.Serialize(oldPackage.Package.Snapshot.Execution), JsonSerializer.Serialize(published.Package.Snapshot.Execution));
        Assert.DoesNotContain("bpmn:appearance", jsonText, StringComparison.Ordinal);
        A23PresentationEqual(before.Document, capture.Document);
    }
}
