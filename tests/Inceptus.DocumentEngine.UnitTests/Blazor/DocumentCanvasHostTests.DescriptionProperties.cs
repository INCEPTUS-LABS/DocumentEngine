using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task SmartCreateDescriptionStartsEmptyAndCanBeEditedWithoutCopyingSourceDescription()
    {
        await using var host = A23Host(A24Snapshot());
        await host.InitializeAsync("smart-create-description", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        const string originalDescription = "Existing source instructions.";
        Assert.True((await session.ExecuteAsync(new UpdateBpmnElementDescriptionCommand(
            document.DocumentId, document.Revision, new("source"), originalDescription))).IsCommitted);
        await session.WaitForIdleAsync();
        await A24Select(host, new VisualStateId("v-source"));
        var before = document.CaptureSnapshot();
        Assert.True((await host.SmartCreateAsync(new("bpmn:toolbox:task")))!.IsCommitted);
        var created = document.CaptureSnapshot();
        var selected = Assert.Single(session.CaptureState().EditorState.Selection);
        var visual = created.VisualModel.VisualStates.Single(item => item.Id == selected);
        var element = created.SemanticModel.Elements.Single(item => item.Id == visual.SemanticElementId);
        Assert.False(element.Properties.ContainsKey(BpmnSemanticProperties.Description));
        Assert.Equal(before.SemanticModel.Elements.Single(), created.SemanticModel.Elements.Single(item => item.Id.Value == "source"));
        Assert.Equal(2, session.CaptureState().HistoryStatus.EntryCount);

        var draft = new DocumentCanvasPropertiesDraft(
            Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(selected)));
        var field = draft.DataFields.Single(item => item.FieldId.Value == "description");
        Assert.True(field.CanEdit);
        Assert.Equal(string.Empty, field.EditorValue);
        Assert.False(draft.IsDirty);
        const string description = "New task instructions.\nSecond line.";
        field.EditorValue = description;
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, (await host.ApplyPropertiesAsync(draft)).Status);
        Assert.Equal(description, document.SemanticModel.Elements.Single(item => item.Id == element.Id)
            .Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.Equal(3, session.CaptureState().HistoryStatus.EntryCount);
        Assert.True(host.UpdatePropertiesFormState(false, null, false));

        await host.UndoAsync();
        Assert.Equal(created.SemanticModel.Elements.AsEnumerable(), document.SemanticModel.Elements);
        await host.RedoAsync();
        Assert.Equal(description, document.SemanticModel.Elements.Single(item => item.Id == element.Id)
            .Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.Equal(originalDescription, document.SemanticModel.Elements.Single(item => item.Id.Value == "source")
            .Properties[BpmnSemanticProperties.Description].TextValue);
    }

    [Theory]
    [InlineData("BPMN.Task", null)]
    [InlineData("BPMN.UserTask", null)]
    [InlineData("BPMN.ManualTask", null)]
    [InlineData("BPMN.ServiceTask", null)]
    [InlineData("BPMN.SendTask", null)]
    [InlineData("BPMN.ReceiveTask", null)]
    [InlineData("BPMN.SubProcess", null)]
    [InlineData("BPMN.ExclusiveGateway", null)]
    [InlineData("BPMN.ParallelGateway", null)]
    [InlineData("BPMN.InclusiveGateway", null)]
    [InlineData("BPMN.EventBasedGateway", null)]
    [InlineData("BPMN.Task", "")]
    [InlineData("BPMN.Task", "Original\nDescription")]
    public async Task PropertiesDescriptionFirstApplySavesClosesAndSupportsExactUndoRedo(string type, string? original)
    {
        await using var host = CreatePropertiesUxHost(DescriptionPropertiesSnapshot(type,
            original is null ? null : PropertyValue.FromText(original)));
        await host.InitializeAsync("description-canvas", "description-container");
        await OpenPropertiesUxMenuAsync(host);
        var snapshot = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId));
        var draft = new DocumentCanvasPropertiesDraft(snapshot);
        var field = draft.DataFields.Single(item => item.FieldId.Value == "description");
        Assert.True(field.CanEdit);
        Assert.Equal(original ?? string.Empty, field.EditorValue);
        Assert.False(draft.IsDirty);
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var activator = new LocalizationComponentActivator(host, draft);
        using var services = PublishComponentServices(activator, new PublishDownloadRuntime());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        var markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        var textarea = Regex.Match(markup, "<textarea[^>]*-properties-data-field-description\"[^>]*>").Value;
        Assert.NotEmpty(textarea);
        Assert.DoesNotContain("readonly", textarea, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled", textarea, StringComparison.Ordinal);
        var callback = typeof(DocumentCanvas).GetMethod("HandleHostStateChangedAsync", PropertiesUxFlags)!
            .CreateDelegate<Func<Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionGeneration?, Task>>(activator.Canvas.Component);
        host.StateChanged += callback;
        const string description = "  Pack carefully.\nInclude invoice.\nZażółć — 東京  ";
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            field.EditorValue = description;
            await InvokePropertiesUxAsync(activator.Canvas.Component, "ApplyPropertiesAsync");
        });
        await session.WaitForIdleAsync();
        await callback(null);
        Assert.False(host.CaptureState().PropertiesFormOpen);
        Assert.DoesNotContain("<form", await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString), StringComparison.Ordinal);
        Assert.Equal(before.Revision.Increment(), AttachedDocument(session).Revision);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        AssertDescriptionValue(description);
        host.StateChanged -= callback;

        var reopened = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId));
        Assert.Equal(description, new DocumentCanvasPropertiesDraft(reopened).DataFields.Single(item => item.FieldId.Value == "description").EditorValue);
        Assert.True(host.UpdatePropertiesFormState(false, null, false));
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        AssertDescriptionValue(original);
        Assert.True((await session.RedoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        AssertDescriptionValue(description);

        await OpenPropertiesUxMenuAsync(host);
        var clearDraft = new DocumentCanvasPropertiesDraft(
            Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId)));
        clearDraft.DataFields.Single(item => item.FieldId.Value == "description").EditorValue = string.Empty;
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, (await host.ApplyPropertiesAsync(clearDraft)).Status);
        AssertDescriptionValue(string.Empty);
        Assert.Equal(2, session.CaptureState().HistoryStatus.EntryCount);
        Assert.True((await session.UndoAsync()).IsCommitted);
        await session.WaitForIdleAsync();
        AssertDescriptionValue(description);

        void AssertDescriptionValue(string? expected)
        {
            var saved = AttachedDocument(session).CaptureSnapshot();
            var element = saved.SemanticModel.Elements.Single(item => item.Id == PropertiesUxElementId);
            Assert.Equal(expected is not null, element.Properties.TryGetValue(BpmnSemanticProperties.Description, out var value));
            Assert.Equal(expected, value?.TextValue);
            var initial = before.SemanticModel.Elements.Single(item => item.Id == PropertiesUxElementId);
            Assert.Equal(initial.TypeId, element.TypeId);
            Assert.Equal(initial.AttachedToElementId, element.AttachedToElementId);
            Assert.Equal(initial.ContainmentKind, element.ContainmentKind);
            Assert.Equal(initial.Properties.Where(item => item.Key != BpmnSemanticProperties.Description),
                element.Properties.Where(item => item.Key != BpmnSemanticProperties.Description));
            Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), saved.VisualModel.VisualStates);
            Assert.Equal(before.SemanticModel.ScopeMemberships, saved.SemanticModel.ScopeMemberships);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PropertiesDescriptionCancelOrUnchangedApplyDoesNotCreateMissingProperty(bool cancel)
    {
        await using var host = CreatePropertiesUxHost(DescriptionPropertiesSnapshot("BPMN.Task", null));
        await host.InitializeAsync("description-canvas", "description-container");
        await OpenPropertiesUxMenuAsync(host);
        var draft = new DocumentCanvasPropertiesDraft(
            Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId)));
        var activator = new LocalizationComponentActivator(host, draft);
        using var services = PublishComponentServices(activator, new PublishDownloadRuntime());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            if (cancel) draft.DataFields.Single(item => item.FieldId.Value == "description").EditorValue = "Unsaved";
            await InvokePropertiesUxAsync(activator.Canvas.Component, cancel ? "CancelProperties" : "ApplyPropertiesAsync");
        });
        Assert.False(host.CaptureState().PropertiesFormOpen);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    [Theory]
    [InlineData("wrong-value-kind")]
    [InlineData("event")]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("unchanged")]
    [InlineData("cancelled")]
    public async Task PropertiesDescriptionInvalidCommandIsAtomic(string failure)
    {
        await using var host = CreatePropertiesUxHost(DescriptionPropertiesSnapshot(
            failure == "event" ? "BPMN.StartEvent" : "BPMN.Task",
            failure == "wrong-value-kind" ? PropertyValue.FromBoolean(true) : null));
        await host.InitializeAsync("description-canvas", "description-container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        if (failure == "wrong-value-kind")
        {
            await OpenPropertiesUxMenuAsync(host);
            var form = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(PropertiesUxVisualId));
            Assert.False(form.DataFields.Single(item => item.Definition.FieldId.Value == "description").CanEdit);
        }
        using var cancellation = new CancellationTokenSource();
        if (failure == "cancelled") await cancellation.CancelAsync();
        var command = new UpdateBpmnElementDescriptionCommand(before.DocumentId,
            failure == "stale" ? before.Revision.Increment() : before.Revision,
            failure == "missing" ? new("description:missing") : PropertiesUxElementId,
            failure == "unchanged" ? null : "New description");
        Assert.False((await session.ExecuteAsync(command, cancellation.Token)).IsCommitted);
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, session.CaptureState().HistoryStatus.EntryCount);
    }

    private static DocumentSnapshot DescriptionPropertiesSnapshot(string type, PropertyValue? description)
    {
        var snapshot = CreatePropertiesUxSnapshot(type);
        var semantic = snapshot.SemanticModel;
        return new DocumentSnapshot(new SemanticModelSnapshot(snapshot.DocumentId, snapshot.Revision,
            semantic.Elements.Select(element => element.Id == PropertiesUxElementId
                ? new SemanticElementSnapshot(element.Id, element.TypeId,
                    element.Properties.Where(item => item.Key != BpmnSemanticProperties.Description).Concat(
                        description is null ? [] : [KeyValuePair.Create(BpmnSemanticProperties.Description, description)]),
                    element.AttachedToElementId, element.ContainmentKind) : element),
            semantic.Relationships, semantic.NestedScopes, semantic.ScopeMemberships,
            semantic.ModelProfiles, semantic.ProfileAssignments), snapshot.VisualModel, snapshot.Metadata, snapshot.Publication);
    }
}
