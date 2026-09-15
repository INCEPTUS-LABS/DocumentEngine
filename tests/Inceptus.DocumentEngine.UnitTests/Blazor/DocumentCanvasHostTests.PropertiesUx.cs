using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    private static readonly VisualStateId PropertiesUxVisualId = new("p15:visual");
    private static readonly SemanticElementId PropertiesUxElementId = new("p15:element");
    private const BindingFlags PropertiesUxFlags = BindingFlags.NonPublic | BindingFlags.Instance;

    [Theory]
    [InlineData("BPMN.StartEvent")]
    [InlineData("BPMN.EndEvent")]
    [InlineData("BPMN.MessageCatchEvent")]
    [InlineData("BPMN.MessageThrowEvent")]
    [InlineData("BPMN.MessageBoundaryEvent")]
    [InlineData("BPMN.TimerCatchEvent")]
    [InlineData("BPMN.TimerBoundaryEvent")]
    [InlineData("BPMN.SignalCatchEvent")]
    [InlineData("BPMN.SignalThrowEvent")]
    [InlineData("BPMN.SignalBoundaryEvent")]
    [InlineData("zero-data")]
    public async Task PropertiesUxIneligibleTargetsHaveNoMenuActionOrOpeningPath(string type)
    {
        var zeroData = type == "zero-data";
        var snapshot = CreatePropertiesUxSnapshot(zeroData ? "BPMN.Task" : type);
        await using var host = CreatePropertiesUxHost(snapshot);
        await host.InitializeAsync("properties-ux-canvas", "properties-ux-container");
        if (zeroData)
        {
            typeof(DocumentCanvasHost).GetField("_propertiesSchemaCatalog", PropertiesUxFlags)!
                .SetValue(host, new ElementPropertiesSchemaCatalog([]));
        }

        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        await OpenPropertiesUxMenuAsync(host);
        Assert.False(host.CanOpenContextProperties());
        var activator = new PublishComponentActivator(host);
        using var services = PublishComponentServices(activator, new PublishDownloadRuntime());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        var markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.DoesNotContain("-properties-action\"", markup, StringComparison.Ordinal);
        Assert.Null(await host.OpenPropertiesAsync(PropertiesUxVisualId));
        Assert.False(host.TryCaptureSelectedProperties(PropertiesUxVisualId, out _));
        var scene = session.CaptureState().CurrentScene!;
        await PointerObserver(host).DoubleClickDocumentPointAsync(scene,
            PropertiesUxBodyCenter(scene));
        Assert.False(host.CaptureState().PropertiesFormOpen);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);

        // The UI exclusion must not delete Event fields or technical snapshot data.
        Assert.True(DocumentCanvasPropertySnapshot.TryCreate(before, PropertiesUxVisualId,
            new ElementPropertiesSchemaCatalog(zeroData ? [] : BpmnPluginRegistration.N100.PropertiesSchemas),
            out var internalSnapshot));
        Assert.False(internalSnapshot!.IsPropertiesAvailable);
        Assert.Equal(PropertiesUxElementId, internalSnapshot.SemanticId);
        Assert.Equal(PropertiesUxVisualId, internalSnapshot.VisualStateId);
        if (zeroData)
        {
            Assert.Empty(internalSnapshot.DataFields);
            Assert.Equal(BpmnSemanticTypes.Task, internalSnapshot.TypeId);
        }
        else if (type is not ("BPMN.StartEvent" or "BPMN.EndEvent"))
        {
            Assert.NotEmpty(internalSnapshot.DataFields);
        }

        // Existing forms also follow selection; this entry path must obey eligibility.
        var ownerVisual = new VisualStateId("p15:owner-visual");
        typeof(DocumentCanvasHost).GetField("_propertiesSchemaCatalog", PropertiesUxFlags)!
            .SetValue(host, new ElementPropertiesSchemaCatalog(BpmnPluginRegistration.N100.PropertiesSchemas));
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [ownerVisual]));
        await session.WaitForIdleAsync();
        Assert.NotNull(await host.OpenPropertiesAsync(ownerVisual));
        if (zeroData)
        {
            typeof(DocumentCanvasHost).GetField("_propertiesSchemaCatalog", PropertiesUxFlags)!
                .SetValue(host, new ElementPropertiesSchemaCatalog([]));
        }
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [PropertiesUxVisualId]));
        await session.WaitForIdleAsync();
        Assert.False(host.CaptureState().PropertiesFormOpen);
    }

    [Theory]
    [InlineData("BPMN.Task", false)]
    [InlineData("BPMN.UserTask", false)]
    [InlineData("BPMN.ManualTask", false)]
    [InlineData("BPMN.ServiceTask", false)]
    [InlineData("BPMN.SendTask", false)]
    [InlineData("BPMN.ReceiveTask", false)]
    [InlineData("BPMN.ExclusiveGateway", false)]
    [InlineData("BPMN.ParallelGateway", false)]
    [InlineData("BPMN.InclusiveGateway", false)]
    [InlineData("BPMN.EventBasedGateway", false)]
    [InlineData("BPMN.Task", true)]
    public async Task PropertiesUxEligibleFormsRenderDataParametersAndContentBasedSizing(string type, bool compact)
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = CreatePropertiesUxHost(CreatePropertiesUxSnapshot(type));
        await host.InitializeAsync("properties-ux-canvas", "properties-ux-container");
        if (compact)
        {
            var schema = BpmnPluginRegistration.N100.PropertiesSchemas.Single(schema => schema.SemanticTypeId.Value == type);
            typeof(DocumentCanvasHost).GetField("_propertiesSchemaCatalog", PropertiesUxFlags)!.SetValue(host,
                new ElementPropertiesSchemaCatalog([new ElementPropertiesSchema(schema.SemanticTypeId,
                    schema.Fields.Where(field => field.EditorKind != ElementPropertyEditorKind.MultilineText))]));
        }
        await OpenPropertiesUxMenuAsync(host);
        Assert.True(host.CanOpenContextProperties());
        var activator = new PublishComponentActivator(host);
        using var services = PublishComponentServices(activator, new PublishDownloadRuntime());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        Assert.Contains("-properties-action\"", await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString), StringComparison.Ordinal);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await InvokePropertiesUxAsync(activator.Component, "OpenPropertiesAsync");
            await activator.Component.RefreshAsync();
        });
        var markup = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        var form = Regex.Match(markup, "<form[^>]*-properties-form\"[\\s\\S]*?</form>").Value;
        Assert.NotEmpty(form);
        Assert.Contains("data-property-group=\"data\"", form, StringComparison.Ordinal);
        Assert.Contains("data-property-group=\"parameters\"", form, StringComparison.Ordinal);
        Assert.Matches("<legend[^>]*>Parameters</legend>", form);
        Assert.Matches("data-property-group=\"parameters\"[\\s\\S]*value=\"" + type + "\" readonly", form);
        foreach (var removed in new[] { "identity", "visual" })
        {
            Assert.DoesNotContain($"data-property-group=\"{removed}\"", form, StringComparison.Ordinal);
        }
        foreach (var removed in new[] { "semantic-id", "visual-id", "placement", "x", "y", "width", "height", "source", "target" })
        {
            Assert.DoesNotContain($"-properties-{removed}\"", form, StringComparison.Ordinal);
        }
        Assert.Contains("properties-field-compact", form, StringComparison.Ordinal);
        Assert.Contains(compact ? "properties-form-compact" : "properties-form-wide", form, StringComparison.Ordinal);
        Assert.Equal(!compact, form.Contains("properties-field-long-form", StringComparison.Ordinal));
        Assert.Equal(!compact, form.Contains("properties-long-form-editor", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("commit")]
    [InlineData("wrapped-label")]
    [InlineData("no-change")]
    [InlineData("validation")]
    [InlineData("stale")]
    [InlineData("command-failure")]
    [InlineData("cancel")]
    public async Task PropertiesUxComponentApplyAndCancelPreserveDraftDocumentHistoryAndEvents(string outcome)
    {
        using var culture = new ModelerCultureScope("en");
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        await using var host = CreatePropertiesUxHost(CreatePropertiesUxSnapshot("BPMN.ManualTask",
            outcome == "command-failure" ? ulong.MaxValue : 0));
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("properties-ux-canvas", "properties-ux-container");
        await OpenPropertiesUxMenuAsync(host);
        if (outcome == "wrapped-label")
        {
            var scene = Session(host).CaptureState().CurrentScene!;
            var label = scene.Items.First(item => item.Origin.VisualStateId == PropertiesUxVisualId &&
                item.Geometry.Kind == Canvas2DSceneGeometryKind.Text);
            await PointerObserver(host).ContextMenuDocumentPointAsync(scene, Center(label.Bounds));
            Assert.Equal(label.Id, host.CaptureState().ContextMenu!.TargetSceneObjectId);
        }
        var snapshot = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId));
        var draft = new DocumentCanvasPropertiesDraft(snapshot);
        var field = draft.DataFields.Single(field => field.FieldId.Value == "name");
        var savedName = outcome == "wrapped-label" ? "A much longer name that wraps across several canvas label lines" : "Saved name";
        if (outcome != "no-change")
        {
            field.EditorValue = outcome == "validation" ? " " : savedName;
        }
        Assert.True(host.UpdatePropertiesFormState(true, PropertiesUxVisualId, draft.IsDirty));
        var session = Session(host);
        var document = AttachedDocument(session);
        if (outcome == "stale")
        {
            Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(snapshot.DocumentId,
                snapshot.Revision, PropertiesUxElementId, BpmnSemanticProperties.Name, "External edit"))).IsCommitted);
        }
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);
        await session.WaitForIdleAsync();
        await notifications.Pending;
        var eventCount = ModelerChanges(log).Length;
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var activator = new LocalizationComponentActivator(host, draft);
        using var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<Microsoft.JSInterop.IJSRuntime>(new PublishDownloadRuntime())
            .AddSingleton<Microsoft.AspNetCore.Components.IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        // HtmlRenderer does not run OnAfterRender: wire the same live host callback
        // that the browser component subscribes after its canvas is initialized.
        host.StateChanged += typeof(DocumentCanvas).GetMethod("HandleHostStateChangedAsync", PropertiesUxFlags)!
            .CreateDelegate<Func<Task>>(activator.Canvas.Component);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await InvokePropertiesUxAsync(activator.Canvas.Component,
                outcome == "cancel" ? "CancelProperties" : "ApplyPropertiesAsync");
            await activator.Canvas.Component.RefreshAsync();
        });
        await CommandProcessor.WaitForEventDispatchIdleAsync(document);
        await session.WaitForIdleAsync();
        await notifications.Pending;
        var committed = outcome is "commit" or "wrapped-label";
        var closed = committed || outcome is "cancel" or "no-change";
        Assert.Equal(!closed, host.CaptureState().PropertiesFormOpen);
        Assert.Equal(history.EntryCount + (committed ? 1 : 0), session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(eventCount + (committed ? 1 : 0), ModelerChanges(log).Length);
        var markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Equal(!closed, markup.Contains("<form", StringComparison.Ordinal));
        if (committed)
        {
            Assert.Equal(before.Revision.Increment(), document.CaptureSnapshot().Revision);
            Assert.Equal(savedName, document.CaptureSnapshot().SemanticModel.Elements.Single(
                element => element.Id == PropertiesUxElementId).Properties[BpmnSemanticProperties.Name].TextValue);
            if (outcome == "wrapped-label")
            {
                Assert.DoesNotContain(session.CaptureState().CurrentScene!.Items,
                    item => item.Id == snapshot.TargetSceneObjectId);
            }
            foreach (var (original, current) in before.VisualModel.VisualStates.Zip(document.CaptureSnapshot().VisualModel.VisualStates))
            {
                Assert.Equal(original.Id, current.Id);
                Assert.Equal(original.SemanticElementId, current.SemanticElementId);
                Assert.Equal(original.Position, current.Position);
                Assert.Equal(original.Size, current.Size);
                Assert.Equal(original.PlacementMode, current.PlacementMode);
                Assert.Equal(original.Properties, current.Properties);
                Assert.Equal(original.Route.AsEnumerable(), current.Route.AsEnumerable());
            }
        }
        else
        {
            Assert.Same(before, document.CaptureSnapshot());
        }
        if (!closed)
        {
            Assert.Same(draft, typeof(DocumentCanvas).GetField("_propertiesDraft", PropertiesUxFlags)!.GetValue(activator.Canvas.Component));
            Assert.Equal(outcome == "validation" ? " " : "Saved name", field.EditorValue);
            Assert.Contains("role=\"alert\"", markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PropertiesUxSuccessfulAsyncCompletionDoesNotCloseANewerSelectionDraft()
    {
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new ConcurrentQueue<object>();
        using var notifications = new BpmnModelerNotifications(async notification =>
        {
            log.Enqueue(notification);
            if (notification is BpmnModelerDocumentChangedEventArgs)
            {
                committed.TrySetResult();
                await release.Task;
            }
        });
        await using var host = CreatePropertiesUxHost(CreatePropertiesUxSnapshot("BPMN.ManualTask"));
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("properties-ux-canvas", "properties-ux-container");
        await OpenPropertiesUxMenuAsync(host);
        var snapshot = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId));
        var originalDraft = new DocumentCanvasPropertiesDraft(snapshot);
        originalDraft.DataFields.Single(field => field.FieldId.Value == "name").EditorValue = "Committed original";
        var activator = new LocalizationComponentActivator(host, originalDraft);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<Microsoft.JSInterop.IJSRuntime>(new PublishDownloadRuntime())
            .AddSingleton<Microsoft.AspNetCore.Components.IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        var apply = renderer.Dispatcher.InvokeAsync(() => InvokePropertiesUxAsync(activator.Canvas.Component, "ApplyPropertiesAsync"));
        DocumentCanvasPropertiesDraft? replacement = null;
        var ownerVisual = new VisualStateId("p15:owner-visual");
        try
        {
            await committed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(apply.IsCompleted);
            var session = Session(host);
            await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [ownerVisual]));
            await session.WaitForIdleAsync();
            Assert.True(host.TryCaptureCurrentPropertiesForm(out var current));
            replacement = new DocumentCanvasPropertiesDraft(current!);
            replacement.DataFields.Single(field => field.FieldId.Value == "name").EditorValue = "Newer unsaved draft";
            Assert.True(host.UpdatePropertiesFormState(true, ownerVisual, isDirty: true));
            await renderer.Dispatcher.InvokeAsync(() =>
                typeof(DocumentCanvas).GetField("_propertiesDraft", PropertiesUxFlags)!
                    .SetValue(activator.Canvas.Component, replacement));
        }
        finally
        {
            release.TrySetResult();
        }
        await apply.WaitAsync(TimeSpan.FromSeconds(10));
        await renderer.Dispatcher.InvokeAsync(activator.Canvas.Component.RefreshAsync);
        var state = host.CaptureState();
        Assert.True(state.PropertiesFormOpen);
        Assert.Equal(ownerVisual, state.PropertiesTargetVisualStateId);
        Assert.Same(replacement, typeof(DocumentCanvas).GetField("_propertiesDraft", PropertiesUxFlags)!
            .GetValue(activator.Canvas.Component));
        Assert.Contains("Newer unsaved draft", await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString), StringComparison.Ordinal);
        Assert.Equal(1, state.Session!.HistoryStatus.EntryCount);
        Assert.Equal("Committed original", Assert.Single(ModelerChanges(log)).Snapshot.SemanticModel.Elements.Single(
            element => element.Id == PropertiesUxElementId).Properties[BpmnSemanticProperties.Name].TextValue);
    }

    private static async Task InvokePropertiesUxAsync(DocumentCanvas component, string method)
    {
        var result = typeof(DocumentCanvas).GetMethod(method, PropertiesUxFlags)!.Invoke(component, null);
        if (result is Task task)
        {
            await task;
        }
    }

    private static DocumentCanvasHost CreatePropertiesUxHost(DocumentSnapshot snapshot) => CreateHost(
        new RecordingRenderExecution(), new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400d, 900d, 1d)),
        compositionFactory: new BpmnModelerCompositionFactory(initialDocument: snapshot));

    private static async Task OpenPropertiesUxMenuAsync(DocumentCanvasHost host)
    {
        var session = Session(host);
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [PropertiesUxVisualId]))).Succeeded);
        await session.WaitForIdleAsync();
        var scene = session.CaptureState().CurrentScene!;
        await PointerObserver(host).ContextMenuDocumentPointAsync(scene, PropertiesUxBodyCenter(scene));
        Assert.NotNull(host.CaptureState().ContextMenu);
    }

    private static DocumentSnapshot CreatePropertiesUxSnapshot(string type, ulong revisionValue = 0)
    {
        var id = new DocumentId("p15:document");
        var revision = new DocumentRevision(revisionValue);
        var owner = new SemanticElementId("p15:owner");
        var boundary = BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(new SemanticTypeId(type));
        KeyValuePair<string, PropertyValue>[] fields =
        [
            new(BpmnSemanticProperties.Code, PropertyValue.FromText("P15")),
            new(BpmnSemanticProperties.Name, PropertyValue.FromText("Original name")),
            new(BpmnSemanticProperties.ElementNumber, PropertyValue.FromInteger(15)),
            new(BpmnSemanticProperties.Description, PropertyValue.FromText("Authored description\nSecond line")),
        ];
        return new DocumentSnapshot(
            new SemanticModelSnapshot(id, revision,
            [
                new SemanticElementSnapshot(PropertiesUxElementId, new SemanticTypeId(type), fields,
                    attachedToElementId: boundary ? owner : null),
                new SemanticElementSnapshot(owner, BpmnSemanticTypes.Task, fields),
            ]),
            new VisualModelSnapshot(id, revision,
            [
                new VisualStateSnapshot(PropertiesUxVisualId, PropertiesUxElementId,
                    boundary ? new PointD(407d, 182d) : new PointD(100d, 100d),
                    boundary ? new SizeD(36d, 36d) : new SizeD(100d, 80d), VisualPlacementMode.Pinned,
                    boundaryAttachment: boundary ? new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.5d) : null),
                new VisualStateSnapshot(new VisualStateId("p15:owner-visual"), owner,
                    new PointD(350d, 100d), new SizeD(150d, 100d), VisualPlacementMode.Pinned),
            ]), new DocumentMetadataSnapshot(id, revision));
    }

    private static PointD PropertiesUxBodyCenter(Canvas2DScene scene) => Center(scene.Items.Single(item =>
        item.Origin.VisualStateId == PropertiesUxVisualId && Canvas2DNodeBodyMetadata.IsNodeBody(item)).Bounds);
}
