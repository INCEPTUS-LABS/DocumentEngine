using System.Collections.Concurrent;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Runtime.Commands;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task ConnectorPropertiesNameAndRoutingApplyTogether()
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        await using var host = CreatePropertiesUxHost(composition.Document.CaptureSnapshot());
        await host.InitializeAsync("combined-connector-properties", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var visualId = BpmnDemoPipeline.SixthSequenceFlowVisualId;
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [visualId]))).Succeeded);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(visualId));
        var draft = new DocumentCanvasPropertiesDraft(properties) { RoutingTypeValue = "manual" };
        SetDataValue(draft, NameFieldId, "Approved path");
        Assert.True(host.UpdatePropertiesFormState(true, visualId, draft.IsDirty));

        var result = await host.ApplyPropertiesAsync(draft);

        Assert.True(result.Status == DocumentCanvasPropertiesApplyStatus.Committed,
            $"Combined Apply returned {result.Status}: {result.Message}");
        Assert.Equal("Approved path", document.CaptureSnapshot().SemanticModel.Relationships.Single(
            relationship => relationship.Id == properties.SemanticId).Properties[BpmnSemanticProperties.Name].TextValue);
        Assert.Equal(ConnectorRoutingType.Manual, result.Authoritative!.RoutingType);
    }

    [Theory]
    [InlineData(ConnectorRoutingType.Automatic, ConnectorRoutingType.Manual)]
    [InlineData(ConnectorRoutingType.Automatic, ConnectorRoutingType.Straight)]
    [InlineData(ConnectorRoutingType.Manual, ConnectorRoutingType.Automatic)]
    [InlineData(ConnectorRoutingType.Manual, ConnectorRoutingType.Straight)]
    [InlineData(ConnectorRoutingType.Straight, ConnectorRoutingType.Manual)]
    [InlineData(ConnectorRoutingType.Straight, ConnectorRoutingType.Automatic)]
    public async Task ConnectorPropertiesCombinedTransitionsCommitAndReplayOnce(
        ConnectorRoutingType initial, ConnectorRoutingType target)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        await using var host = CreatePropertiesUxHost(composition.Document.CaptureSnapshot());
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("combined-transitions", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var id = BpmnDemoPipeline.SixthSequenceFlowVisualId;
        var snapshot = document.CaptureSnapshot();
        if (ConnectorPropertiesRoute(snapshot).RoutingType != initial)
        {
            Assert.True((await session.ExecuteAsync(new SetConnectorRoutingTypeCommand(snapshot.DocumentId,
                snapshot.Revision, id, initial))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        if (initial == ConnectorRoutingType.Manual)
        {
            snapshot = document.CaptureSnapshot();
            var path = ConnectorPropertiesRoute(snapshot).Path;
            Assert.True((await session.ExecuteAsync(new UpdateConnectionRouteCommand(snapshot.DocumentId,
                snapshot.Revision, id, [path[0], new PointD(470, 180), new PointD(490, 220), path[^1]]))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [id]))).Succeeded);
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(id));
        var draft = new DocumentCanvasPropertiesDraft(properties) { RoutingTypeValue = target.ToString().ToLowerInvariant() };
        SetDataValue(draft, NameFieldId, "Approved path");
        Assert.True(host.UpdatePropertiesFormState(true, id, draft.IsDirty));
        await DrainAsync();
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var eventCount = ModelerChanges(log).Length;

        var applied = await host.ApplyPropertiesAsync(draft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, applied.Status);
        await DrainAsync();
        var committed = document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), committed.Revision);
        Assert.Equal(history.EntryCount + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(eventCount + 1, ModelerChanges(log).Length);
        Assert.Equal(target, ConnectorPropertiesRoute(committed).RoutingType);
        Assert.Equal("Approved path", committed.SemanticModel.Relationships.Single(
            relationship => relationship.Id == properties.SemanticId).Properties[BpmnSemanticProperties.Name].TextValue);
        AssertConnectorPropertiesIdentities(before, committed);

        Assert.True(host.UpdatePropertiesFormState(false, null, false));
        await host.UndoAsync();
        await DrainAsync();
        var undone = document.CaptureSnapshot();
        Assert.Equal(committed.Revision.Increment(), undone.Revision);
        Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), undone.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(initial, ConnectorPropertiesRoute(undone).RoutingType);
        Assert.Equal(ConnectorPropertiesRoute(before).Path.AsEnumerable(), ConnectorPropertiesRoute(undone).Path.AsEnumerable());
        if (initial == ConnectorRoutingType.Manual)
            Assert.Equal(ConnectorPropertiesRoute(before), ConnectorPropertiesRoute(undone));
        Assert.Equal(history.EntryCount + 1, session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(eventCount + 2, ModelerChanges(log).Length);
        AssertConnectorPropertiesIdentities(before, undone);

        await host.RedoAsync();
        await DrainAsync();
        var redone = document.CaptureSnapshot();
        Assert.Equal(undone.Revision.Increment(), redone.Revision);
        Assert.Equal(committed.SemanticModel.Relationships.AsEnumerable(), redone.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(ConnectorPropertiesRoute(committed), ConnectorPropertiesRoute(redone));
        Assert.Equal(eventCount + 3, ModelerChanges(log).Length);
        AssertConnectorPropertiesIdentities(before, redone);

        async Task DrainAsync()
        {
            await CommandProcessor.WaitForEventDispatchIdleAsync(document).WaitAsync(TimeSpan.FromSeconds(10));
            await session.WaitForIdleAsync();
            await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory]
    [InlineData("name")]
    [InlineData("routing")]
    [InlineData("unchanged")]
    [InlineData("invalid-routing")]
    [InlineData("stale")]
    public async Task ConnectorPropertiesSingleNoOpAndRejectedApplyRetainExistingSemantics(string edit)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        await using var host = CreatePropertiesUxHost(composition.Document.CaptureSnapshot());
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("connector-single-and-rejected", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var id = BpmnDemoPipeline.SixthSequenceFlowVisualId;
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [id]));
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(id));
        var draft = new DocumentCanvasPropertiesDraft(properties);
        if (edit is "name" or "invalid-routing" or "stale") SetDataValue(draft, NameFieldId, "Approved path");
        if (edit is "routing" or "stale") draft.RoutingTypeValue = "manual";
        if (edit == "invalid-routing") draft.RoutingTypeValue = "unsupported";
        host.UpdatePropertiesFormState(true, id, draft.IsDirty);
        if (edit == "stale")
        {
            Assert.True((await session.ExecuteAsync(new UpdateBpmnSequenceFlowNameCommand(properties.DocumentId,
                properties.Revision, properties.SemanticId, "External name"))).IsCommitted);
        }
        await DrainAsync();
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var events = ModelerChanges(log).Length;
        var result = await host.ApplyPropertiesAsync(draft);
        await DrainAsync();
        var committed = edit is "name" or "routing";
        Assert.Equal(edit switch
        {
            "unchanged" => DocumentCanvasPropertiesApplyStatus.NoChange,
            "invalid-routing" => DocumentCanvasPropertiesApplyStatus.ValidationFailed,
            "stale" => DocumentCanvasPropertiesApplyStatus.Stale,
            _ => DocumentCanvasPropertiesApplyStatus.Committed,
        }, result.Status);
        Assert.Equal(history.EntryCount + (committed ? 1 : 0), session.CaptureState().HistoryStatus.EntryCount);
        Assert.Equal(events + (committed ? 1 : 0), ModelerChanges(log).Length);
        var after = document.CaptureSnapshot();
        if (!committed)
        {
            Assert.Same(before, after);
            Assert.True(host.CaptureState().PropertiesFormOpen);
        }
        else
        {
            Assert.Equal(before.Revision.Increment(), after.Revision);
            if (edit == "name") Assert.Equal(ConnectorPropertiesRoute(before), ConnectorPropertiesRoute(after));
            else Assert.Equal(before.SemanticModel.Relationships.AsEnumerable(), after.SemanticModel.Relationships.AsEnumerable());
        }

        async Task DrainAsync()
        {
            await CommandProcessor.WaitForEventDispatchIdleAsync(document).WaitAsync(TimeSpan.FromSeconds(10));
            await session.WaitForIdleAsync();
            await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConnectorPropertiesCompoundRejectsEitherInvalidChildAtomically(bool invalidName)
    {
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        var log = new ConcurrentQueue<object>();
        using var notifications = RecordModelerNotifications(log);
        await using var host = CreatePropertiesUxHost(composition.Document.CaptureSnapshot());
        host.ModelerNotifications = notifications;
        await host.InitializeAsync("invalid-connector-compound", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        var before = document.CaptureSnapshot();
        var history = session.CaptureState().HistoryStatus;
        var id = BpmnDemoPipeline.SixthSequenceFlowVisualId;
        var semanticId = before.VisualModel.VisualStates.Single(item => item.Id == id).SemanticElementId;
        var command = new CompoundDocumentCommand(before.DocumentId, before.Revision,
        [
            new UpdateBpmnSequenceFlowNameCommand(before.DocumentId, before.Revision,
                invalidName ? BpmnDemoPipeline.TaskId : semanticId, "Approved path"),
            new SetConnectorRoutingTypeCommand(before.DocumentId, before.Revision,
                invalidName ? id : BpmnDemoPipeline.TaskVisualId, ConnectorRoutingType.Manual),
        ]);
        var result = await session.ExecuteAsync(command);
        await CommandProcessor.WaitForEventDispatchIdleAsync(document).WaitAsync(TimeSpan.FromSeconds(10));
        await session.WaitForIdleAsync();
        await notifications.Pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.IsCommitted);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Same(before, document.CaptureSnapshot());
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.Empty(ModelerChanges(log));
        Assert.True((await session.ExecuteAsync(new UpdateBpmnSequenceFlowNameCommand(before.DocumentId,
            before.Revision, semanticId, "Still usable"))).IsCommitted);
    }

    [Fact]
    public async Task ConnectorPropertiesComponentAppliesBothClosesFormAndKeepsTypeReadonly()
    {
        using var culture = new ModelerCultureScope("en");
        var composition = await BpmnModelerTestComposition.CreateDemoAsync();
        await using var host = CreatePropertiesUxHost(composition.Document.CaptureSnapshot());
        await host.InitializeAsync("connector-properties-component", "container");
        var id = BpmnDemoPipeline.SixthSequenceFlowVisualId;
        var session = Session(host);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [id]));
        var properties = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(id));
        var draft = new DocumentCanvasPropertiesDraft(properties) { RoutingTypeValue = "manual" };
        SetDataValue(draft, NameFieldId, "Approved path");
        host.UpdatePropertiesFormState(true, id, draft.IsDirty);
        var activator = new LocalizationComponentActivator(host, draft);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<Microsoft.JSInterop.IJSRuntime>(new PublishDownloadRuntime())
            .AddSingleton<Microsoft.AspNetCore.Components.IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        host.StateChanged += typeof(DocumentCanvas).GetMethod("HandleHostStateChangedAsync", PropertiesUxFlags)!
            .CreateDelegate<Func<Inceptus.DocumentEngine.Canvas2D.EditingSession.EditingSessionGeneration?, Task>>(activator.Canvas.Component);
        var markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Matches("value=\"BPMN.SequenceFlow\" readonly", markup);
        Assert.DoesNotContain("Apply Name and Routing type separately", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Apply one Data field", markup, StringComparison.Ordinal);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await InvokePropertiesUxAsync(activator.Canvas.Component, "ApplyPropertiesAsync");
            await activator.Canvas.Component.RefreshAsync();
        });
        Assert.False(host.CaptureState().PropertiesFormOpen);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        var after = AttachedDocument(session).CaptureSnapshot();
        Assert.Equal(ConnectorRoutingType.Manual, ConnectorPropertiesRoute(after).RoutingType);
        Assert.Equal("Approved path", after.SemanticModel.Relationships.Single(
            item => item.Id == properties.SemanticId).Properties[BpmnSemanticProperties.Name].TextValue);
    }

    private static ConnectorRoutingRecord ConnectorPropertiesRoute(DocumentSnapshot snapshot) =>
        snapshot.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors).Single(
            route => route.VisualStateId == BpmnDemoPipeline.SixthSequenceFlowVisualId);

    private static void AssertConnectorPropertiesIdentities(DocumentSnapshot before, DocumentSnapshot after)
    {
        var id = BpmnDemoPipeline.SixthSequenceFlowVisualId;
        Assert.Equal(before.VisualModel.VisualStates.Select(item => item.Id), after.VisualModel.VisualStates.Select(item => item.Id));
        foreach (var (oldVisual, newVisual) in before.VisualModel.VisualStates.Zip(after.VisualModel.VisualStates))
        {
            if (oldVisual.Id != id) Assert.Equal(oldVisual, newVisual);
            Assert.Equal(oldVisual.SemanticElementId, newVisual.SemanticElementId);
            Assert.Equal(oldVisual.SourceAnchorId, newVisual.SourceAnchorId);
            Assert.Equal(oldVisual.TargetAnchorId, newVisual.TargetAnchorId);
            Assert.Equal(oldVisual.ConnectorAnchors.Select(anchor => (anchor.Id, anchor.Role, anchor.Side, anchor.Order)),
                newVisual.ConnectorAnchors.Select(anchor => (anchor.Id, anchor.Role, anchor.Side, anchor.Order)));
        }
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), after.SemanticModel.Elements.AsEnumerable());
        foreach (var (oldRelationship, newRelationship) in before.SemanticModel.Relationships.Zip(after.SemanticModel.Relationships))
        {
            Assert.Equal(oldRelationship.Id, newRelationship.Id);
            Assert.Equal(oldRelationship.TypeId, newRelationship.TypeId);
            Assert.Equal(oldRelationship.SourceId, newRelationship.SourceId);
            Assert.Equal(oldRelationship.TargetId, newRelationship.TargetId);
        }
        var oldRoutes = before.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors).ToArray();
        var newRoutes = after.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Connectors).ToArray();
        // The routing authority keeps unchanged survivors first and appends a connector
        // whose path changed. Properties must preserve all identities and survivor order.
        Assert.Equal(oldRoutes.Select(route => route.VisualStateId).OrderBy(item => item.Value),
            newRoutes.Select(route => route.VisualStateId).OrderBy(item => item.Value));
        Assert.Equal(oldRoutes.Where(route => route.VisualStateId != id), newRoutes.Where(route => route.VisualStateId != id));
    }
}
