using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Bpmn;
using System.Net;
using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData("company-b")]
    [InlineData(null)]
    public async Task A22LaneReassignmentAppendsAtDestinationOrUnassignmentSeparatesPoolAndHistory(string? target)
    {
        var initial = A22PoolSnapshot();
        var semantic = initial.SemanticModel;
        var participantB = new SemanticElementSnapshot(new("company-b"), BpmnSemanticTypes.Participant,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Company B"))], containmentKind: SemanticElementContainmentKind.Document);
        var snapshot = new DocumentSnapshot(new SemanticModelSnapshot(initial.DocumentId, initial.Revision,
            semantic.Elements.Append(participantB), semantic.Relationships, semantic.NestedScopes, semantic.ScopeMemberships,
            semantic.ModelProfiles, semantic.ProfileAssignments), initial.VisualModel, initial.Metadata);
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1920d, 1080d, 1d)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: snapshot));
        await host.InitializeAsync("pool-reassignment", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        Assert.True((await session.ExecuteAsync(new SetBpmnPoolParticipantCommand(document.DocumentId, document.Revision,
            new("supplier"), new("company-b")))).IsCommitted);
        await session.WaitForIdleAsync();
        var before = document.CaptureSnapshot();
        var regions = before.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
        Assert.True((await session.ExecuteAsync(new SetBpmnPoolParticipantCommand(document.DocumentId, document.Revision,
            new("warehouse"), target is null ? null : new(target)))).IsCommitted);
        await session.WaitForIdleAsync();
        var changed = document.CaptureSnapshot();
        var changedRegions = changed.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
        Assert.Equal(before.Revision.Increment(), changed.Revision);
        Assert.True(BpmnEffectiveParticipant.TryResolve(changed, new("warehouse-task"), out var owner, out _));
        Assert.Equal(target is null ? new EffectiveParticipantKey(EffectiveParticipantKind.StandalonePool, new("warehouse"))
            : new EffectiveParticipantKey(EffectiveParticipantKind.Participant, new(target)), owner);
        var oldFollowing = regions.Single(region => region.ContainerSemanticElementId?.Value == "supplier2");
        var following = changedRegions.Single(region => region.ContainerSemanticElementId?.Value == "supplier2");
        Assert.Equal(oldFollowing.ExpandedHeight, following.ExpandedHeight);
        Assert.Equal(oldFollowing.ContentBounds!.Value.Width, following.ContentBounds!.Value.Width);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), changed.VisualModel.VisualStates);
        if (target is not null)
        {
            var lanes = changedRegions.Where(region => region.ContainerSemanticElementId?.Value is "warehouse" or "supplier")
                .OrderBy(region => region.ContentBounds!.Value.Top).ToArray();
            Assert.Equal("supplier", lanes[0].ContainerSemanticElementId!.Value);
            Assert.Equal(regions.Single(region => region.ContainerSemanticElementId?.Value == "sales").ContentBounds!.Value.Bottom + 40d,
                lanes[0].ContentBounds!.Value.Top);
            Assert.Equal(lanes[0].ContentBounds!.Value.Bottom, lanes[1].ContentBounds!.Value.Top);
            Assert.Equal(lanes[1].ContentBounds!.Value.Bottom + 40d, following.ContentBounds.Value.Top);
        }
        else
        {
            Assert.Equal(oldFollowing.ContentBounds.Value.Top + 40d, following.ContentBounds.Value.Top);
            Assert.Equal(regions.Single(region => region.ContainerSemanticElementId?.Value == "sales").ContentBounds!.Value.Bottom + 40d,
                changedRegions.Single(region => region.ContainerSemanticElementId?.Value == "warehouse").ContentBounds!.Value.Top);
        }
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Company A");
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Company B");
        var bytes = NativeDocumentSerializer.Export(changed);
        var opened = NativeDocumentSerializer.Import(bytes.AsMemory());
        Assert.True(opened.Succeeded);
        Assert.Equal(bytes.ToArray(), NativeDocumentSerializer.Export(opened.Document!).ToArray());
        await host.UndoAsync();
        Assert.Equal(regions.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single().Geometry.Regions);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), document.CaptureSnapshot().SemanticModel.Elements);
        await host.RedoAsync();
        Assert.Equal(changedRegions.AsEnumerable(), document.CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single().Geometry.Regions);
        Assert.Equal(changed.SemanticModel.Elements.AsEnumerable(), document.CaptureSnapshot().SemanticModel.Elements);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A22AssignmentHistoryPreservesLaterRegionHeightsAndUnrelatedGeometry(bool newPool)
    {
        await using var host = A22PoolHost(new ToolboxSelectionState());
        await host.InitializeAsync("pool-focused-history", "container");
        var session = Session(host);
        var document = AttachedDocument(session);
        if (newPool)
        {
            Assert.True((await session.ExecuteAsync(new Inceptus.DocumentEngine.Organizational.Commands.CreateOrganizationalPoolCommand(
                document.DocumentId, document.Revision, new("added"), document.CaptureSnapshot().SemanticModel.RootScopeId,
                Inceptus.DocumentEngine.Organizational.Commands.OrganizationalPoolCreationMode.Empty))).IsCommitted);
            await session.WaitForIdleAsync();
        }
        var before = AttachedDocument(session).CaptureSnapshot();
        var command = new SetBpmnPoolParticipantCommand(before.DocumentId, before.Revision, new(newPool ? "added" : "supplier"), new("company"));
        Assert.True((await session.ExecuteAsync(command)).IsCommitted);
        await session.WaitForIdleAsync();
        var committed = AttachedDocument(session).CaptureSnapshot();
        var history = BpmnPluginRegistration.N100.HistoryPolicies.Single(registration => registration.TypeId == command.TypeId)
            .Policy.Prepare(command, before, committed);
        var scope = committed.VisualModel.RoutingScopes!.Value.Single();
        var geometry = scope.Geometry;
        var laterRegions = geometry.Regions.Select(region => region.ContainerSemanticElementId?.Value is "supplier" or "supplier2"
            ? new SpatialRegionGeometrySnapshot(region.Id, region.ProfileId, region.ContainerSemanticElementId,
                region.ExpandedHeight + 40d, region.LocalToScopeTransform,
                new RectD(region.ContentBounds!.Value.X, region.ContentBounds.Value.Y, region.ContentBounds.Value.Width, region.ExpandedHeight + 40d))
            : region).ToArray();
        var later = new DocumentSnapshot(committed.SemanticModel, new VisualModelSnapshot(committed.DocumentId, committed.Revision,
            committed.VisualModel.VisualStates, committed.VisualModel.ProfileElementPresentations,
            [new ScopeRoutingSnapshot(scope.ScopeId, new ScopeGeometrySnapshot(geometry.PolicyId, geometry.PolicyVersion,
                geometry.LayoutAlgorithmId, geometry.Configuration, geometry.TextConfiguration, geometry.Contributors,
                geometry.Nodes, laterRegions, geometry.Captions, geometry.TextMeasurements, geometry.SpatialWidths), scope.Connectors)]), committed.Metadata);
        var handler = BpmnPluginRegistration.N100.CommandHandlers.Single(registration => registration.TypeId == command.TypeId).Handler;
        var undone = await handler.HandleAsync(history.UndoFactory!.Create(later.DocumentId, later.Revision), later, default);
        Assert.True(undone.Succeeded);
        var restored = undone.ProposedDocument!;
        var restoredRegions = restored.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
        var oldSupplier = before.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions.Single(region => region.ContainerSemanticElementId?.Value == "supplier");
        var supplier = restoredRegions.Single(region => region.ContainerSemanticElementId?.Value == "supplier");
        Assert.Equal(oldSupplier.ContentBounds!.Value.Top, supplier.ContentBounds!.Value.Top);
        Assert.Equal(laterRegions.Single(region => region.ContainerSemanticElementId?.Value == "supplier").ExpandedHeight, supplier.ExpandedHeight);
        var restoredFollowing = restoredRegions.Single(region => region.ContainerSemanticElementId?.Value == "supplier2");
        var laterFollowing = laterRegions.Single(region => region.ContainerSemanticElementId?.Value == "supplier2");
        if (newPool)
        {
            Assert.Equal(before.VisualModel.RoutingScopes.Value.Single().Geometry.Regions.Single(region =>
                region.ContainerSemanticElementId?.Value == "supplier2").ContentBounds!.Value.Top, restoredFollowing.ContentBounds!.Value.Top);
            Assert.Equal(laterFollowing.ExpandedHeight, restoredFollowing.ExpandedHeight);
        }
        else Assert.Equal(laterFollowing, restoredFollowing);
        Assert.Equal(later.VisualModel.VisualStates.AsEnumerable(), restored.VisualModel.VisualStates);
        Assert.Equal(scope.Connectors.AsEnumerable(), restored.VisualModel.RoutingScopes.Value.Single().Connectors);
        var redone = await handler.HandleAsync(history.RedoFactory!.Create(restored.DocumentId, restored.Revision), restored, default);
        Assert.True(redone.Succeeded);
        Assert.Equal(laterRegions.AsEnumerable(), redone.ProposedDocument!.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions);
    }

    [Fact]
    public async Task A22PoolPropertiesRendersLocalizedOptionalParticipantSelectorWithFriendlyNames()
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = A22PoolHost(new ToolboxSelectionState());
        await host.InitializeAsync("pool-selector", "container");
        var session = Session(host);
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(semanticSceneSelection: new("sales")));
        await session.WaitForIdleAsync();
        var snapshot = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(new SemanticElementId("sales")));
        var draft = new DocumentCanvasPropertiesDraft(snapshot);
        var activator = new LocalizationComponentActivator(host, draft);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<Microsoft.JSInterop.IJSRuntime>(new PublishDownloadRuntime())
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DocumentCanvas>());
        var markup = WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
        var selector = Regex.Match(markup, "<select[^>]*data-property-field-id=\"participant\"[\\s\\S]*?</select>").Value;
        Assert.Contains("value=\"company\"", selector, StringComparison.Ordinal);
        Assert.Matches("<option value(?:=\"\")?[^>]*>None</option>", selector);
        Assert.Contains(">Company A</option>", selector, StringComparison.Ordinal);
        Assert.DoesNotContain(">company</option>", selector, StringComparison.Ordinal);
        Assert.DoesNotContain("subtype", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A22PoolPropertiesAssignParticipantAndHistoryRestoreOnlyGroupFrames()
    {
        await using var host = A22PoolHost(new ToolboxSelectionState());
        await host.InitializeAsync("pool-properties", "container");
        var session = Session(host);
        var before = AttachedDocument(session).CaptureSnapshot();
        var regions = before.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
        await session.UpdateEditorStateAsync(new EditorStateSnapshot(semanticSceneSelection: new("supplier")));
        await session.WaitForIdleAsync();
        var snapshot = Assert.IsType<DocumentCanvasPropertySnapshot>(await host.OpenPropertiesAsync(new SemanticElementId("supplier")));
        Assert.Equal("Company A", Assert.Single(snapshot.ParticipantOptions).Value);
        var draft = new DocumentCanvasPropertiesDraft(snapshot);
        draft.DataFields.Single(field => field.FieldId.Value == "participant").EditorValue = "company";
        var result = await host.ApplyPropertiesAsync(draft);
        Assert.Equal(DocumentCanvasPropertiesApplyStatus.Committed, result.Status);
        Assert.Equal("company", result.Authoritative!.DataFields.Single(field => field.FieldId.Value == "participant").EditorValue);
        var assigned = AttachedDocument(session).CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), assigned.Revision);
        var afterRegions = assigned.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions;
        var lanes = afterRegions.Where(region => region.ContainerSemanticElementId?.Value is "sales" or "warehouse" or "supplier")
            .OrderBy(region => region.ContentBounds!.Value.Top).ToArray();
        Assert.Equal(lanes[0].ContentBounds!.Value.Bottom, lanes[1].ContentBounds!.Value.Top);
        Assert.Equal(lanes[1].ContentBounds!.Value.Bottom, lanes[2].ContentBounds!.Value.Top);
        Assert.Equal(regions.Single(region => region.ContainerSemanticElementId?.Value == "supplier2"),
            afterRegions.Single(region => region.ContainerSemanticElementId?.Value == "supplier2"));
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), assigned.VisualModel.VisualStates);
        Assert.Single(session.CaptureState().CurrentScene!.Items, item => item.Geometry.Content == "Company A");
        await host.UndoAsync();
        var undone = AttachedDocument(session).CaptureSnapshot();
        Assert.Null(OrganizationalSemantics.GetParticipantId(undone.SemanticModel.Elements.Single(element => element.Id.Value == "supplier")));
        Assert.Equal(regions.AsEnumerable(), undone.VisualModel.RoutingScopes!.Value.Single().Geometry.Regions);
        await host.RedoAsync();
        Assert.Equal(afterRegions.AsEnumerable(), AttachedDocument(session).CaptureSnapshot().VisualModel.RoutingScopes!.Value.Single().Geometry.Regions);
    }

    [Theory]
    [InlineData("sales-task", "BPMN.Task", true)]
    [InlineData("supplier-task", "BPMN.Task", true)]
    [InlineData("sales-task", "BPMN.SendTask", true)]
    [InlineData("sales-task", "BPMN.MessageThrowEvent", true)]
    [InlineData("supplier", "BPMN.Task", true)]
    [InlineData("company", "BPMN.Task", true)]
    [InlineData("sales-task", "BPMN.ReceiveTask", false)]
    [InlineData("sales-task", "BPMN.MessageCatchEvent", false)]
    [InlineData("sales-task", "BPMN.ExclusiveGateway", false)]
    [InlineData("sales-task", "BPMN.TimerCatchEvent", false)]
    [InlineData("sales", "BPMN.Task", false)]
    public async Task A22PoolDiagramFirstClickUsesEffectiveOwnerWithoutTargetOrAnchors(string source, string type, bool valid)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22PoolHost(selection, new(type));
        await host.InitializeAsync("pool-source", "container");
        Assert.True(host.CaptureState().IsInitialized, string.Join("; ", host.CaptureState().HostDiagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message + " " + string.Join(",", diagnostic.Context))));
        var session = Session(host);
        Assert.True(session.CaptureState().CurrentScene is not null, string.Join("; ",
            session.CaptureState().RuntimeDiagnostics.Concat(session.CaptureState().PresentationDiagnostics).Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message + " " + string.Join(",", diagnostic.Context))));
        var before = AttachedDocument(session).CaptureSnapshot();
        var tool = A22FlowTool("Message Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        var scene = session.CaptureState().CurrentScene!;
        var body = A22ViewBody(scene, new(source));
        var point = source is "sales" or "supplier" ? new Inceptus.DocumentEngine.Contracts.Geometry.PointD(body.Bounds.Right - 10, body.Bounds.Bottom - 10) : Center(body.Bounds);
        var pointer = PointerObserver(host);
        await pointer.MoveDocumentPointAsync(scene, point, pointerId: 501, buttons: 0);
        await pointer.DownDocumentPointAsync(session.CaptureState().CurrentScene!, point, pointerId: 501);
        await pointer.UpDocumentPointAsync(session.CaptureState().CurrentScene!, point, pointerId: 501);
        var state = session.CaptureState();
        Assert.Equal(valid, ConnectionToolState.GetPendingSource(state.EditorState) is not null);
        Assert.Equal(tool.CreationId, ConnectionToolState.GetSelection(state.EditorState));
        Assert.Same(before, AttachedDocument(session).CaptureSnapshot());
        Assert.Equal(0, state.HistoryStatus.EntryCount);
        if (valid)
        {
            var preview = Assert.Single(state.CurrentScene!.Items, item => item.Origin.StableSourceKey == ElementConnectionPreview.FeedbackId);
            Assert.Equal(Center(body.Bounds), preview.Geometry.Points[0]);
        }
    }

    [Theory]
    [InlineData("sales-task", "warehouse-task", false)]
    [InlineData("sales-task", "supplier-task", true)]
    [InlineData("supplier", "supplier2", true)]
    [InlineData("company", "supplier", true)]
    public async Task A22PoolDiagramTwoClicksCommitOneCanonicalFlowAndHistory(string source, string target, bool message)
    {
        var selection = new ToolboxSelectionState();
        await using var host = A22PoolHost(selection);
        await host.InitializeAsync("pool-pair", "container");
        Assert.True(host.CaptureState().IsInitialized, string.Join("; ", host.CaptureState().HostDiagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message + " " + string.Join(",", diagnostic.Context))));
        var session = Session(host);
        Assert.True(session.CaptureState().CurrentScene is not null, string.Join("; ",
            session.CaptureState().RuntimeDiagnostics.Concat(session.CaptureState().PresentationDiagnostics).Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message + " " + string.Join(",", diagnostic.Context))));
        var tool = A22FlowTool(message ? "Message Flow" : "Sequence Flow");
        selection.Select(tool.ItemId, tool.CreationId);
        await host.RefreshToolboxPlacementAsync();
        var before = AttachedDocument(session).CaptureSnapshot();
        foreach (var endpoint in new[] { source, target })
        {
            var scene = session.CaptureState().CurrentScene!;
            var body = A22ViewBody(scene, new(endpoint));
            var point = endpoint is "supplier" or "supplier2" ? new Inceptus.DocumentEngine.Contracts.Geometry.PointD(body.Bounds.Right - 10, body.Bounds.Bottom - 10) : Center(body.Bounds);
            await PointerObserver(host).DownDocumentPointAsync(scene, point, pointerId: 502);
            await PointerObserver(host).UpDocumentPointAsync(session.CaptureState().CurrentScene!, point, pointerId: 502);
        }
        var after = AttachedDocument(session).CaptureSnapshot();
        var flow = Assert.Single(after.SemanticModel.Relationships);
        Assert.Equal(message ? BpmnSemanticTypes.MessageFlow : BpmnSemanticTypes.SequenceFlow, flow.TypeId);
        Assert.Single(after.VisualModel.EnumerateConnectorRoutingRecords());
        Assert.Equal(before.Revision.Increment(), after.Revision);
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        var bytes = NativeDocumentSerializer.Export(after);
        var imported = NativeDocumentSerializer.Import(bytes.AsMemory(), new ElementConnectorAnchorPolicyRegistry(BpmnPluginRegistration.N100.ConnectorAnchorPolicies));
        Assert.True(imported.Succeeded, string.Join("; ", imported.Diagnostics.Select(item => item.Message)));
        Assert.Equal(bytes.ToArray(), NativeDocumentSerializer.Export(imported.Document!).ToArray());
        Assert.Empty(after.VisualModel.DocumentRoutingStates);
        Assert.DoesNotContain(after.VisualModel.RoutingScopes!.Value.SelectMany(scope => scope.Geometry.Nodes), node =>
            after.VisualModel.VisualStates.Any(visual => visual.Id == node.VisualStateId && visual.PlacementMode == VisualPlacementMode.Derived));
        await host.UndoAsync();
        Assert.Empty(AttachedDocument(session).CaptureSnapshot().SemanticModel.Relationships);
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), AttachedDocument(session).CaptureSnapshot().VisualModel.VisualStates);
        await host.RedoAsync();
        Assert.Equal(flow, Assert.Single(AttachedDocument(session).CaptureSnapshot().SemanticModel.Relationships));
    }

    private static DocumentCanvasHost A22PoolHost(ToolboxSelectionState selection, SemanticTypeId? sourceType = null) =>
        CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1920d, 1080d, 1d)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: A22PoolSnapshot(sourceType)), toolboxSelection: selection);

    private static DocumentSnapshot A22PoolSnapshot(SemanticTypeId? sourceType = null)
    {
        var id = new DocumentId("a22:pool-diagram");
        var revision = DocumentRevision.Zero;
        var company = new SemanticElementSnapshot(new("company"), BpmnSemanticTypes.Participant,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Company A"))], containmentKind: SemanticElementContainmentKind.Document);
        string[] names = ["sales", "warehouse", "supplier", "supplier2"];
        var pools = names.Select(name =>
            new SemanticElementSnapshot(new(name), OrganizationalSemanticTypes.Pool,
                new[] { new KeyValuePair<string, PropertyValue>(OrganizationalSemanticProperties.Name, PropertyValue.FromText(name)) }
                    .Concat(name is "sales" or "warehouse" ? [new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText("company"))] : []))).ToArray();
        var tasks = pools.Select(pool => new SemanticElementSnapshot(new(pool.Id.Value + "-task"),
            pool.Id.Value == "sales" ? sourceType ?? BpmnSemanticTypes.Task : BpmnSemanticTypes.Task,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText(pool.Id.Value + " task"))])).ToArray();
        var semantic = new SemanticModelSnapshot(id, revision, pools.Concat(tasks).Prepend(company),
            modelProfiles: new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]),
            profileAssignments: tasks.Select(task => new ModelProfileElementAssignmentSnapshot(OrganizationalModelProfile.Id,
                task.Id, new(task.Id.Value.Replace("-task", "", StringComparison.Ordinal)))));
        var visual = new VisualModelSnapshot(id, revision, tasks.Select(task => new VisualStateSnapshot(new(task.Id.Value + "-visual"),
            task.Id, new(80, 35), new(120, 80), VisualPlacementMode.Pinned)),
            pools.Select((pool, index) => new ModelProfileElementPresentationSnapshot(OrganizationalModelProfile.Id, pool.Id, index)));
        return new(semantic, visual, new DocumentMetadataSnapshot(id, revision));
    }
}
