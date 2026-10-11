using System.Net;
using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("BPMN.MessageCatchEvent")]
    [InlineData("BPMN.MessageThrowEvent")]
    [InlineData("BPMN.MessageBoundaryEvent")]
    [InlineData("BPMN.Task")]
    [InlineData("BPMN.ExclusiveGateway")]
    public async Task A21PropertiesUsesExistingTypeFieldWithCompatibleConcreteOptions(string originalType)
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = CreatePropertiesUxHost(CreatePropertiesUxSnapshot(originalType));
        await host.InitializeAsync("a21-properties-canvas", "a21-properties-container");
        await OpenPropertiesUxMenuAsync(host);
        Assert.True(host.CanOpenContextProperties());
        var snapshot = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId));
        var draft = new DocumentCanvasPropertiesDraft(snapshot);
        Assert.True(snapshot.CanEditType);
        Assert.Equal(originalType, draft.TypeIdValue);
        Assert.True(draft.TryParseType(out var selected));
        Assert.Equal(snapshot.TypeId, selected);
        Assert.False(draft.IsDirty);
        Assert.True(draft.IsValid);

        var activator = new LocalizationComponentActivator(host, draft);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<Microsoft.JSInterop.IJSRuntime>(new PublishDownloadRuntime())
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        var markup = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        var form = Regex.Match(markup, "<form[^>]*-properties-form\"[\\s\\S]*?</form>").Value;
        var selector = Regex.Match(form, "<select[^>]*-properties-type\"[\\s\\S]*?</select>").Value;
        Assert.NotEmpty(selector);
        Assert.Contains($"value=\"{originalType}\"", selector, StringComparison.Ordinal);
        Assert.Equal(snapshot.SwitchableTypes.Select(type => type.Value),
            Regex.Matches(selector, "<option value=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        foreach (var option in snapshot.SwitchableTypes)
            Assert.Contains(BpmnElementTypeSwitching.GetDisplayName(option), selector, StringComparison.Ordinal);
        Assert.DoesNotContain("subtype", form, StringComparison.OrdinalIgnoreCase);

        if (BpmnSemanticTypes.IsEvent(snapshot.TypeId))
        {
            Assert.NotEmpty(snapshot.DataFields);
            Assert.Empty(draft.DataFields);
            Assert.False(snapshot.CanEditData);
            Assert.False(snapshot.CanEditBounds);
            Assert.DoesNotContain("data-property-group=\"data\"", form, StringComparison.Ordinal);
            Assert.DoesNotContain("-properties-data-field-", form, StringComparison.Ordinal);
        }
        else
        {
            Assert.NotEmpty(draft.DataFields);
            Assert.True(snapshot.CanEditData);
        }
    }

    [Theory]
    [InlineData("BPMN.Task", "BPMN.UserTask")]
    [InlineData("BPMN.MessageCatchEvent", "BPMN.TimerCatchEvent")]
    [InlineData("BPMN.MessageThrowEvent", "BPMN.SignalThrowEvent")]
    [InlineData("BPMN.MessageBoundaryEvent", "BPMN.TimerBoundaryEvent")]
    [InlineData("BPMN.ExclusiveGateway", "BPMN.ParallelGateway")]
    public async Task A21PropertiesCommitsTypeThroughSessionAndRefreshesAuthoritativeSchema(string originalType, string targetType)
    {
        await using var host = CreatePropertiesUxHost(CreatePropertiesUxSnapshot(originalType));
        await host.InitializeAsync("a21-properties-canvas", "a21-properties-container");
        await OpenPropertiesUxMenuAsync(host);
        var original = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId));
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var draft = new DocumentCanvasPropertiesDraft(original) { TypeIdValue = targetType };
        Assert.True(draft.IsTypeDirty);
        Assert.True(host.UpdatePropertiesFormState(true, PropertiesUxVisualId, draft.IsDirty));

        var result = await host.ApplyPropertiesAsync(draft);

        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        var refreshed = Assert.IsType<DocumentCanvasPropertySnapshot>(result.Authoritative);
        Assert.Equal(new SemanticTypeId(targetType), refreshed.TypeId);
        Assert.Equal(original.SemanticId, refreshed.SemanticId);
        Assert.Equal(original.VisualStateId, refreshed.VisualStateId);
        Assert.Equal(original.Bounds, refreshed.Bounds);
        Assert.Equal(original.Revision.Increment(), refreshed.Revision);
        Assert.Equal(BpmnPluginRegistration.N100.PropertiesSchemas.Single(schema => schema.SemanticTypeId == refreshed.TypeId)
            .Fields.Select(field => field.FieldId), refreshed.DataFields.Select(field => field.FieldId));
        var after = AttachedDocument(session).CaptureSnapshot();
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), after.VisualModel.VisualStates.AsEnumerable());
        var beforeElement = before.SemanticModel.Elements.Single(element => element.Id == original.SemanticId);
        var afterElement = after.SemanticModel.Elements.Single(element => element.Id == original.SemanticId);
        Assert.Equal(beforeElement.Properties, afterElement.Properties);
        Assert.Equal(beforeElement.AttachedToElementId, afterElement.AttachedToElementId);
        Assert.Equal(before.SemanticModel.GetScope(original.SemanticId), after.SemanticModel.GetScope(original.SemanticId));
        Assert.Equal(history.EntryCount + 1, session.CaptureState().HistoryStatus.EntryCount);
        var refreshedDraft = new DocumentCanvasPropertiesDraft(refreshed);
        Assert.Equal(targetType, refreshedDraft.TypeIdValue);
        Assert.False(refreshedDraft.IsDirty);

        await host.UndoAsync();
        Assert.True(host.TryCaptureCurrentPropertiesForm(out var undone));
        Assert.Equal(original.TypeId, undone!.TypeId);
        await host.RedoAsync();
        Assert.True(host.TryCaptureCurrentPropertiesForm(out var redone));
        Assert.Equal(refreshed.TypeId, redone!.TypeId);
    }

    [Theory]
    [InlineData("incompatible")]
    [InlineData("unknown")]
    [InlineData("conflicting-data")]
    [InlineData("stale")]
    public async Task A21PropertiesRejectsInvalidOrStaleTypeDraftAtomically(string scenario)
    {
        await using var host = CreatePropertiesUxHost(CreatePropertiesUxSnapshot("BPMN.Task"));
        await host.InitializeAsync("a21-properties-canvas", "a21-properties-container");
        await OpenPropertiesUxMenuAsync(host);
        var original = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId));
        var draft = new DocumentCanvasPropertiesDraft(original)
        {
            TypeIdValue = scenario switch
            {
                "incompatible" => BpmnSemanticTypes.SubProcess.Value,
                "unknown" => "unknown:type",
                _ => BpmnSemanticTypes.UserTask.Value,
            },
        };
        if (scenario == "conflicting-data")
            draft.DataFields.Single(field => field.FieldId.Value == "name").EditorValue = "Another edit";
        Assert.True(host.UpdatePropertiesFormState(true, PropertiesUxVisualId, draft.IsDirty));
        var session = Session(host);
        if (scenario == "stale")
        {
            Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(original.DocumentId,
                original.Revision, original.SemanticId, BpmnSemanticProperties.Name, "External edit"))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        var before = AttachedDocument(session).CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;

        var result = await host.ApplyPropertiesAsync(draft);

        Assert.Equal(scenario == "stale" ? DocumentCanvasPropertiesApplyStatus.Stale :
            DocumentCanvasPropertiesApplyStatus.ValidationFailed, result.Status);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData("BPMN.StartEvent")]
    [InlineData("BPMN.EndEvent")]
    [InlineData("BPMN.SubProcess")]
    public void A21StandaloneTypesExposeNoAlternatives(string type)
    {
        Assert.True(DocumentCanvasPropertySnapshot.TryCreate(CreatePropertiesUxSnapshot(type), PropertiesUxVisualId,
            new ElementPropertiesSchemaCatalog(BpmnPluginRegistration.N100.PropertiesSchemas), out var snapshot));
        Assert.False(snapshot!.CanEditType);
        Assert.Empty(snapshot.SwitchableTypes);
        var draft = new DocumentCanvasPropertiesDraft(snapshot) { TypeIdValue = BpmnSemanticTypes.Task.Value };
        Assert.False(draft.IsTypeDirty);
        Assert.False(draft.TryParseType(out _));
    }

    [Fact]
    public async Task A21TypeSelectorCallbackAppliesDomainChangeFromComponent()
    {
        await using var host = CreatePropertiesUxHost(CreatePropertiesUxSnapshot("BPMN.Task"));
        await host.InitializeAsync("a21-properties-canvas", "a21-properties-container");
        await OpenPropertiesUxMenuAsync(host);
        var original = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId));
        var draft = new DocumentCanvasPropertiesDraft(original);
        var activator = new LocalizationComponentActivator(host, draft);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<Microsoft.JSInterop.IJSRuntime>(new PublishDownloadRuntime())
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        await renderer.Dispatcher.InvokeAsync(() =>
            typeof(DocumentCanvas).GetMethod("UpdateTypeDraftValue", PropertiesUxFlags)!.Invoke(activator.Canvas.Component,
                [new ChangeEventArgs { Value = BpmnSemanticTypes.UserTask.Value }]));
        Assert.True(draft.IsTypeDirty);
        Assert.True(host.CaptureState().PropertiesFormDirty);
        await renderer.Dispatcher.InvokeAsync(() => InvokePropertiesUxAsync(activator.Canvas.Component, "ApplyPropertiesAsync"));
        var state = host.CaptureState();
        Assert.False(state.PropertiesFormOpen);
        Assert.Equal(1, state.Session!.HistoryStatus.EntryCount);
        Assert.Equal(BpmnSemanticTypes.UserTask,
            AttachedDocument(Session(host)).CaptureSnapshot().SemanticModel.Elements.Single(element =>
                element.Id == PropertiesUxElementId).TypeId);
    }
}
