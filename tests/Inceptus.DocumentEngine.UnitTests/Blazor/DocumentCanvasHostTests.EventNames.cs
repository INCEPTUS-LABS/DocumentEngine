using System.Text;
using System.Text.Json;
using Inceptus.DocumentEngine.Bpmn.Appearance;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Export;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("StartEvent", null)]
    [InlineData("EndEvent", null)]
    [InlineData("StartEvent", "")]
    [InlineData("EndEvent", "")]
    [InlineData("StartEvent", "  ")]
    [InlineData("EndEvent", "  ")]
    [InlineData("StartEvent", "Old name")]
    [InlineData("EndEvent", "Old name")]
    [InlineData("MessageCatchEvent", null)]
    [InlineData("TimerCatchEvent", null)]
    [InlineData("SignalCatchEvent", null)]
    [InlineData("MessageThrowEvent", null)]
    [InlineData("SignalThrowEvent", null)]
    [InlineData("MessageBoundaryEvent", null)]
    [InlineData("TimerBoundaryEvent", null)]
    [InlineData("SignalBoundaryEvent", null)]
    public async Task EventNamesPropertiesAddEditClearAndRestoreExactOptionalState(string type, string? original)
    {
        await using var host = CreatePropertiesUxHost(EventNameSnapshot(type, original is null ? null : PropertyValue.FromText(original)));
        await host.InitializeAsync("event-names", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        await A24Select(host, PropertiesUxVisualId);
        Assert.True(host.CaptureState().ElementToolbox!.CanProperties);
        await OpenPropertiesUxMenuAsync(host);
        Assert.True(host.CanOpenContextProperties());
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId));
        Assert.False(properties.CanEditBounds);
        Assert.Equal(type is not ("StartEvent" or "EndEvent"), properties.CanEditType);
        var draft = new DocumentCanvasPropertiesDraft(properties);
        var name = Assert.Single(draft.DataFields);
        Assert.Equal("name", name.FieldId.Value);
        Assert.True(name.CanEdit);
        Assert.Equal(original ?? string.Empty, name.EditorValue);
        Assert.False(draft.IsDirty);
        const string savedName = "Order completed after all requested items have been carefully packed";
        name.EditorValue = savedName;
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, (await host.ApplyPropertiesAsync(draft)).Status);
        await session.WaitForIdleAsync();
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(savedName, EventName(document.CaptureSnapshot()));
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
        Assert.Equal(before.SemanticModel.Elements.Single(e => e.Id == PropertiesUxElementId).Properties
            .Where(p => p.Key != BpmnSemanticProperties.Name), document.SemanticModel.Elements
            .Single(e => e.Id == PropertiesUxElementId).Properties.Where(p => p.Key != BpmnSemanticProperties.Name));
        var scene = session.CaptureState().CurrentScene!;
        var body = scene.Items.Single(i => i.Origin.VisualStateId == PropertiesUxVisualId && i.Layer == Canvas2DSceneLayer.Content);
        var labels = scene.Items.Where(i => i.Origin.VisualStateId == PropertiesUxVisualId && i.Geometry.Kind == Canvas2DSceneGeometryKind.Text).ToArray();
        Assert.NotEmpty(labels);
        Assert.All(labels, label => Assert.True(label.Bounds.Top >= body.Bounds.Bottom));
        Assert.True(labels.Length > 1); // The existing external-label layout wraps long names.
        Assert.True(host.UpdatePropertiesFormState(false, null, false));
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(original, EventName(document.CaptureSnapshot()));
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(savedName, EventName(document.CaptureSnapshot()));
        var bytes = NativeDocumentSerializer.Export(document.CaptureSnapshot());
        var imported = NativeDocumentSerializer.Import(bytes.AsMemory());
        Assert.True(imported.Succeeded);
        Assert.Equal(savedName, EventName(imported.Document!.CaptureSnapshot()));
        Assert.Equal(bytes.ToArray(), NativeDocumentSerializer.Export(imported.Document).ToArray());
        var clear = new DocumentCanvasPropertiesDraft(Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId)));
        Assert.Single(clear.DataFields).EditorValue = original is null ? "   " : string.Empty;
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, (await host.ApplyPropertiesAsync(clear)).Status);
        await session.WaitForIdleAsync();
        Assert.Null(EventName(document.CaptureSnapshot()));
        Assert.DoesNotContain(session.CaptureState().CurrentScene!.Items,
            item => item.Origin.VisualStateId == PropertiesUxVisualId && item.Geometry.Kind == Canvas2DSceneGeometryKind.Text);
        Assert.Equal(2, session.CaptureState().HistoryStatus.EntryCount);
        Assert.True(host.UpdatePropertiesFormState(false, null, false));
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(savedName, EventName(document.CaptureSnapshot()));
    }

    [Theory]
    [InlineData("StartEvent", true)]
    [InlineData("EndEvent", true)]
    [InlineData("StartEvent", false)]
    [InlineData("EndEvent", false)]
    public async Task EventNamesCancelAndUnchangedApplyDoNotCreateName(string type, bool cancel)
    {
        await using var host = CreatePropertiesUxHost(EventNameSnapshot(type, null));
        await host.InitializeAsync("event-no-change", "container");
        await A24Select(host, PropertiesUxVisualId);
        var before = AttachedDocument(Session(host)).CaptureSnapshot();
        var draft = new DocumentCanvasPropertiesDraft(Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId)));
        if (cancel)
        {
            Assert.Single(draft.DataFields).EditorValue = "Discard me";
            Assert.True(host.UpdatePropertiesFormState(false, null, false));
        }
        else Assert.Equal(DocumentCanvasPropertiesApplyStatus.NoChange, (await host.ApplyPropertiesAsync(draft)).Status);
        Assert.Same(before, AttachedDocument(Session(host)).CaptureSnapshot());
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData("wrong-type")]
    [InlineData("unsupported-gateway")]
    [InlineData("unsupported-task")]
    [InlineData("missing")]
    [InlineData("unchanged")]
    [InlineData("cancelled")]
    [InlineData("stale")]
    public async Task EventNamesInvalidCommandsAreAtomic(string failure)
    {
        await using var host = CreatePropertiesUxHost(EventNameSnapshot(
            failure == "unsupported-gateway" ? "ExclusiveGateway" : failure == "unsupported-task" ? "Task" : "StartEvent",
            failure == "wrong-type" ? PropertyValue.FromBoolean(true) :
                failure is "unsupported-task" or "unsupported-gateway" ? PropertyValue.FromText("Non-event") : null));
        await host.InitializeAsync("event-invalid", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        using var cancellation = new CancellationTokenSource();
        if (failure == "cancelled") await cancellation.CancelAsync();
        var command = new UpdateBpmnEventNameCommand(before.DocumentId,
            failure == "stale" ? before.Revision.Increment() : before.Revision,
            failure == "missing" ? new("missing") : PropertiesUxElementId,
            failure == "unchanged" ? null : "New name");
        Assert.False((await session.ExecuteAsync(command, cancellation.Token)).IsCommitted);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData("StartEvent", true)]
    [InlineData("EndEvent", true)]
    [InlineData("MessageCatchEvent", true)]
    [InlineData("TimerCatchEvent", true)]
    [InlineData("zero-data", false)]
    public async Task EventNamesToolboxReflectsPropertiesEligibility(string type, bool eligible)
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = CreatePropertiesUxHost(EventNameSnapshot(type == "zero-data" ? "StartEvent" : type, PropertyValue.FromText("Event")));
        await host.InitializeAsync("event-toolbox", "container");
        if (type == "zero-data")
            typeof(DocumentCanvasHost).GetField("_propertiesSchemaCatalog", PropertiesUxFlags)!
                .SetValue(host, new ElementPropertiesSchemaCatalog([]));
        await A24Select(host, PropertiesUxVisualId);
        var panel = Assert.IsType<ElementToolboxState>(host.CaptureState().ElementToolbox);
        Assert.Equal(eligible, panel.CanProperties);
        Assert.Equal(eligible ? 124 : 86, panel.ActionsWidth);
        await using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ElementToolbox>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ElementToolbox.State)] = panel })));
        var html = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Equal(eligible, html.Contains("aria-label=\"Properties\"", StringComparison.Ordinal));
        Assert.Contains("aria-label=\"Delete\"", html, StringComparison.Ordinal);
        host.SetSmartEditEnabled(false);
        Assert.Null(host.CaptureState().ElementToolbox);
        await OpenPropertiesUxMenuAsync(host);
        Assert.Equal(eligible, host.CanOpenContextProperties());
    }

    [Theory]
    [InlineData("white", "#000000", "#000000")]
    [InlineData("mixed", "#223D2B", "#552D2D")]
    [InlineData("custom", "#123456", "#654321")]
    public async Task EventNamesColoursAndExportsPreserveExecutionAndGeometry(string scheme, string startText, string endText)
    {
        await using var host = A23Host(A23Snapshot(A23Edges("linear")));
        await host.InitializeAsync("event-exports", "container");
        var session = Session(host);
        await SaveDefaultPublicationAsync(session);
        var builder = new PublishedProcessPackageBuilder(new BpmnPublishedTokenRoleClassifier(), new BpmnPublishedNodeDataMapper());
        var before = builder.Build((await host.CapturePresentationAsync())!.Capture!);
        Assert.True(before.Succeeded, PublishDiagnostics(before.Diagnostics));
        var document = AttachedDocument(session);
        var geometry = document.CaptureSnapshot();
        foreach (var (id, name) in new[] { ("start", "Order received"), ("end", "Order completed") })
            Assert.True((await session.ExecuteAsync(new UpdateBpmnEventNameCommand(document.DocumentId, document.Revision, new(id), name))).IsCommitted);
        Assert.True((await session.ExecuteAsync(new SetBpmnDocumentAppearanceCommand(document.DocumentId, document.Revision,
            [new(BpmnAppearanceSettings.SchemeKey, PropertyValue.FromText(scheme == "white" ? "white" : "mixed")),
             new(BpmnAppearanceSettings.Key(scheme == "white" ? "white" : "mixed", "start-event", "text"), PropertyValue.FromText(startText)),
             new(BpmnAppearanceSettings.Key(scheme == "white" ? "white" : "mixed", "end-event", "text"), PropertyValue.FromText(endText))]))).IsCommitted);
        await session.WaitForIdleAsync();
        var renamed = document.CaptureSnapshot();
        Assert.Equal(geometry.VisualModel.VisualStates.AsEnumerable(), renamed.VisualModel.VisualStates);
        foreach (var scope in geometry.VisualModel.RoutingScopes!.Value)
        {
            var other = renamed.VisualModel.RoutingScopes!.Value.Single(item => item.ScopeId == scope.ScopeId);
            // Caption text and its measurements must refresh; node/container geometry must not.
            Assert.Equal(scope.Geometry.Nodes.AsEnumerable(), other.Geometry.Nodes);
            Assert.Equal(scope.Geometry.Regions.AsEnumerable(), other.Geometry.Regions);
            Assert.Equal(scope.Geometry.SpatialWidths.AsEnumerable(), other.Geometry.SpatialWidths);
            Assert.Equal(scope.Connectors.AsEnumerable(), other.Connectors);
        }
        var capture = (await host.CapturePresentationAsync())!.Capture!;
        foreach (var (id, colour) in new[] { ("start", startText), ("end", endText) })
        {
            var labels = capture.Scene.Items.Where(i => i.Origin.SemanticElementId?.Value == id && i.Geometry.Kind == Canvas2DSceneGeometryKind.Text).ToArray();
            Assert.NotEmpty(labels);
            Assert.All(labels, item => Assert.Equal(colour, item.Style.Fill));
        }
        var prepared = GraphicalExportPreparer.Prepare(capture);
        Assert.True(prepared.Succeeded);
        var svg = SvgGraphicalExporter.Export(prepared.Scene!);
        Assert.True(svg.Succeeded);
        var svgText = Encoding.UTF8.GetString(svg.Artifact!.Content.AsSpan());
        Assert.Contains("Order received", svgText, StringComparison.Ordinal);
        Assert.Contains("Order completed", svgText, StringComparison.Ordinal);
        var browser = new AppearanceExportRuntime();
        Assert.True((await new Canvas2DGraphicalExporter(browser).ExportAsync(capture, GraphicalExportFormat.Png)).Succeeded);
        Assert.Contains(browser.Frame!.Items, item => item.Fill == startText);
        Assert.Contains(browser.Frame.Items, item => item.Fill == endText);
        var after = builder.Build(capture);
        Assert.True(after.Succeeded, PublishDiagnostics(after.Diagnostics));
        Assert.Equal(JsonSerializer.Serialize(before.Package!.Snapshot.Execution), JsonSerializer.Serialize(after.Package!.Snapshot.Execution));
        using var oldJson = JsonDocument.Parse(before.Package.ProcessJson.AsMemory());
        using var newJson = JsonDocument.Parse(after.Package.ProcessJson.AsMemory());
        Assert.Equal(oldJson.RootElement.GetProperty("tokenGraph").GetRawText(), newJson.RootElement.GetProperty("tokenGraph").GetRawText());
        Assert.Equal(1, newJson.RootElement.GetProperty("formatVersion").GetInt32());
        Assert.Contains("Order received", Encoding.UTF8.GetString(after.Package.ProcessJson.AsSpan()), StringComparison.Ordinal);
        Assert.Contains("Order completed", Encoding.UTF8.GetString(after.Package.ProcessJson.AsSpan()), StringComparison.Ordinal);
    }

    private static string? EventName(DocumentSnapshot snapshot) => snapshot.SemanticModel.Elements
        .Single(e => e.Id == PropertiesUxElementId).Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) ? name.TextValue : null;

    private static DocumentSnapshot EventNameSnapshot(string type, PropertyValue? name)
    {
        var snapshot = CreatePropertiesUxSnapshot("BPMN." + type);
        var semantic = snapshot.SemanticModel;
        return new(new SemanticModelSnapshot(snapshot.DocumentId, snapshot.Revision,
            semantic.Elements.Select(e => e.Id == PropertiesUxElementId ? new SemanticElementSnapshot(e.Id, e.TypeId,
                e.Properties.Where(p => p.Key != BpmnSemanticProperties.Name).Concat(name is null ? [] :
                    new[] { KeyValuePair.Create(BpmnSemanticProperties.Name, name) }), e.AttachedToElementId, e.ContainmentKind) : e),
            semantic.Relationships, semantic.NestedScopes, semantic.ScopeMemberships, semantic.ModelProfiles, semantic.ProfileAssignments),
            snapshot.VisualModel, snapshot.Metadata, snapshot.Publication);
    }
}
