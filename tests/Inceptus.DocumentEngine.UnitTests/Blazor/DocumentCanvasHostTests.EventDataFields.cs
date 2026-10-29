using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Toolbox;
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
    [InlineData("StartEvent")]
    [InlineData("EndEvent")]
    [InlineData("MessageCatchEvent")]
    [InlineData("TimerCatchEvent")]
    [InlineData("SignalCatchEvent")]
    [InlineData("MessageThrowEvent")]
    [InlineData("SignalThrowEvent")]
    [InlineData("MessageBoundaryEvent")]
    [InlineData("TimerBoundaryEvent")]
    [InlineData("SignalBoundaryEvent")]
    public async Task EventDataFieldsEveryToolboxEventIsCreatedUnnamedAndCanBeUndone(string type)
    {
        await using var host = CreatePropertiesUxHost(EventDataSnapshot(type));
        await host.InitializeAsync("event-creation", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var catalog = new ToolboxCatalog(BpmnPluginRegistration.N91.ToolboxContributions);
        var item = Assert.Single(catalog.Items, item => item.ElementTypeId.Value == "BPMN." + type);
        var registration = Assert.Single(BpmnPluginRegistration.N91.ToolboxPlacementRegistrations,
            registration => registration.ToolboxItemId == item.ItemId);
        var target = new ToolboxPlacementTarget(new("p15:owner"), BpmnSemanticTypes.Task,
            new("p15:owner-visual"), new("event-creation:owner"), new RectD(350d, 100d, 150d, 100d));
        var point = new PointD(500d, 150d);
        var preview = registration.PreviewProvider!.Evaluate(new ToolboxPlacementPreviewRequest(
            item.ItemId, before, point, visibleTargets: [target]));
        Assert.True(preview.IsAllowed);
        Assert.Null(preview.Label);
        var request = new ToolboxPlacementRequest(item.ItemId, before, before.Revision, point,
            GuidDocumentCreationIdentityProvider.Instance, visibleTargets: [target]);
        var candidate = registration.CandidateProvider?.ResolveCandidate(request);
        var result = registration.CommandFactory.CreatePlan(new ToolboxPlacementRequest(item.ItemId, before,
            before.Revision, point, GuidDocumentCreationIdentityProvider.Instance, visibleTargets: [target], candidate: candidate));
        Assert.True(result.Succeeded);
        Assert.True((await session.ExecuteAsync(result.Plan!.Command)).IsCommitted);
        await session.WaitForIdleAsync();
        var created = Assert.Single(document.SemanticModel.Elements.Where(element =>
            !before.SemanticModel.Elements.Any(old => old.Id == element.Id)));
        Assert.Equal(item.ElementTypeId, created.TypeId);
        Assert.True(!created.Properties.TryGetValue(BpmnSemanticProperties.Name, out var name) ||
            string.IsNullOrEmpty(name.TextValue));
        Assert.Equal("Original name", EventName(document.CaptureSnapshot()));
        var visual = Assert.Single(document.VisualModel.VisualStates, state => state.SemanticElementId == created.Id);
        Assert.DoesNotContain(session.CaptureState().CurrentScene!.Items, item =>
            item.Origin.VisualStateId == visual.Id && item.Geometry.Kind == Canvas2DSceneGeometryKind.Text);
        await A24Select(host, visual.Id);
        var draft = new DocumentCanvasPropertiesDraft(Assert.IsType<DocumentCanvasPropertySnapshot>(
            await host.OpenPropertiesAsync(visual.Id)));
        Assert.Equal(string.Empty, Assert.Single(draft.DataFields).EditorValue);
        Assert.True(draft.IsValid);
        Assert.True(host.UpdatePropertiesFormState(false, null, false));
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.DoesNotContain(document.SemanticModel.Elements, element => element.Id == created.Id);
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Contains(document.SemanticModel.Elements, element => element == created);
    }

    [Theory]
    [InlineData("MessageCatchEvent")]
    [InlineData("TimerCatchEvent")]
    [InlineData("SignalCatchEvent")]
    [InlineData("MessageThrowEvent")]
    [InlineData("SignalThrowEvent")]
    [InlineData("MessageBoundaryEvent")]
    [InlineData("TimerBoundaryEvent")]
    [InlineData("SignalBoundaryEvent")]
    public async Task EventDataFieldsNameEditingPreservesOtherDataAttachmentAndTypeSwitching(string type)
    {
        await using var host = CreatePropertiesUxHost(EventDataSnapshot(type));
        await host.InitializeAsync("event-data", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        await A24Select(host, PropertiesUxVisualId);
        Assert.True(host.CaptureState().ElementToolbox!.CanProperties);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId));
        Assert.True(properties.CanEditType);
        Assert.False(properties.CanEditBounds);
        Assert.False(properties.HasLongFormData);
        Assert.True(properties.DataFields.Length > 1); // Internal schema values remain intact.
        var draft = new DocumentCanvasPropertiesDraft(properties);
        var name = Assert.Single(draft.DataFields);
        Assert.Equal("name", name.FieldId.Value);
        Assert.True(name.CanEdit);
        Assert.Equal("Original name", name.EditorValue);
        name.EditorValue = "Renamed event";
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, (await host.ApplyPropertiesAsync(draft)).Status);
        await session.WaitForIdleAsync();
        Assert.Equal("Renamed event", EventName(document.CaptureSnapshot()));
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), document.VisualModel.VisualStates);
        var oldElement = before.SemanticModel.Elements.Single(e => e.Id == PropertiesUxElementId);
        var renamed = document.SemanticModel.Elements.Single(e => e.Id == PropertiesUxElementId);
        Assert.Equal(oldElement.AttachedToElementId, renamed.AttachedToElementId);
        Assert.Equal(oldElement.TypeId, renamed.TypeId);
        Assert.Equal(oldElement.Properties.Where(p => p.Key != BpmnSemanticProperties.Name),
            renamed.Properties.Where(p => p.Key != BpmnSemanticProperties.Name));
        Assert.Contains(session.CaptureState().CurrentScene!.Items, item =>
            item.Origin.VisualStateId == PropertiesUxVisualId && item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
            item.Geometry.Content == "Renamed event");
        host.UpdatePropertiesFormState(false, null, false);
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal("Original name", EventName(document.CaptureSnapshot()));
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal("Renamed event", EventName(document.CaptureSnapshot()));
        var native = NativeDocumentSerializer.Export(document.CaptureSnapshot());
        var imported = NativeDocumentSerializer.Import(native.AsMemory());
        Assert.True(imported.Succeeded);
        Assert.Equal(native.ToArray(), NativeDocumentSerializer.Export(imported.Document!).ToArray());

        var cancelled = new DocumentCanvasPropertiesDraft(Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId)));
        Assert.Single(cancelled.DataFields).EditorValue = "Discarded";
        host.UpdatePropertiesFormState(false, null, false);
        Assert.Equal("Renamed event", EventName(document.CaptureSnapshot()));
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);

        var switchDraft = new DocumentCanvasPropertiesDraft(Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId)));
        var targetType = switchDraft.Authoritative.SwitchableTypes.First(candidate => candidate != renamed.TypeId);
        switchDraft.TypeIdValue = targetType.Value;
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, (await host.ApplyPropertiesAsync(switchDraft)).Status);
        await session.WaitForIdleAsync();
        var switched = document.SemanticModel.Elements.Single(e => e.Id == PropertiesUxElementId);
        Assert.Equal(targetType, switched.TypeId);
        Assert.Equal(renamed.Properties, switched.Properties);
        Assert.Equal(renamed.AttachedToElementId, switched.AttachedToElementId);
        Assert.True(host.TryCaptureCurrentPropertiesForm(out var switchedProperties));
        Assert.Single(new DocumentCanvasPropertiesDraft(switchedProperties!).DataFields);
    }

    [Theory]
    [InlineData("MessageCatchEvent")]
    [InlineData("TimerCatchEvent")]
    [InlineData("SignalCatchEvent")]
    [InlineData("MessageThrowEvent")]
    [InlineData("SignalThrowEvent")]
    [InlineData("MessageBoundaryEvent")]
    [InlineData("TimerBoundaryEvent")]
    [InlineData("SignalBoundaryEvent")]
    public async Task EventDataFieldsAllEventNamesCanBeCleared(string type)
    {
        await using var host = CreatePropertiesUxHost(EventDataSnapshot(type));
        await host.InitializeAsync("event-clear", "container");
        await A24Select(host, PropertiesUxVisualId);
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var draft = new DocumentCanvasPropertiesDraft(Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId)));
        Assert.Single(draft.DataFields).EditorValue = "   ";
        Assert.True(draft.IsValid);
        var result = await host.ApplyPropertiesAsync(draft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        await session.WaitForIdleAsync();
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), AttachedDocument(session).VisualModel.VisualStates);
        Assert.Null(EventName(AttachedDocument(session).CaptureSnapshot()));
        Assert.DoesNotContain(session.CaptureState().CurrentScene!.Items, item =>
            item.Origin.VisualStateId == PropertiesUxVisualId && item.Geometry.Kind == Canvas2DSceneGeometryKind.Text);
        host.UpdatePropertiesFormState(false, null, false);
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal("Original name", EventName(AttachedDocument(session).CaptureSnapshot()));
    }

    [Theory]
    [InlineData("MessageCatchEvent", "en")]
    [InlineData("MessageThrowEvent", "en")]
    [InlineData("TimerBoundaryEvent", "en")]
    [InlineData("MessageCatchEvent", "pl")]
    [InlineData("MessageThrowEvent", "pl")]
    [InlineData("TimerBoundaryEvent", "pl")]
    public async Task EventDataFieldsActualFormShowsOnlyNameAndExistingTypeSelector(string type, string cultureName)
    {
        using var culture = new ModelerCultureScope(cultureName);
        await using var host = CreatePropertiesUxHost(EventDataSnapshot(type));
        await host.InitializeAsync("event-markup", "container");
        await A24Select(host, PropertiesUxVisualId);
        var draft = new DocumentCanvasPropertiesDraft(Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId)));
        var activator = new LocalizationComponentActivator(host, draft);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<Microsoft.JSInterop.IJSRuntime>(new PublishDownloadRuntime())
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        var html = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Contains("-properties-data-field-name\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Original name\"", html, StringComparison.Ordinal);
        Assert.Contains("properties-form-compact", html, StringComparison.Ordinal);
        Assert.DoesNotContain("-properties-data-field-description\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("-properties-data-field-timer-definition\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("-properties-data-field-interrupting\"", html, StringComparison.Ordinal);
    }

    private static DocumentSnapshot EventDataSnapshot(string type)
    {
        var snapshot = CreatePropertiesUxSnapshot("BPMN." + type);
        var semantic = snapshot.SemanticModel;
        return new(new SemanticModelSnapshot(snapshot.DocumentId, snapshot.Revision,
            semantic.Elements.Select(e => e.Id == PropertiesUxElementId ? new SemanticElementSnapshot(e.Id, e.TypeId,
                e.Properties.Append(KeyValuePair.Create(BpmnSemanticProperties.TimerDefinition, PropertyValue.FromText("PT5S")))
                    .Append(KeyValuePair.Create(BpmnSemanticProperties.CancelActivity, PropertyValue.FromBoolean(false))),
                e.AttachedToElementId, e.ContainmentKind) : e), semantic.Relationships,
            semantic.NestedScopes, semantic.ScopeMemberships, semantic.ModelProfiles, semantic.ProfileAssignments),
            snapshot.VisualModel, snapshot.Metadata, snapshot.Publication);
    }
}
