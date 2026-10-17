using System.Text.Json;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Publishing;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed partial class PhaseN104StaticPublishIntegrationTests
{
    [Theory]
    [InlineData("catch")]
    [InlineData("receiver")]
    [InlineData("timer")]
    [InlineData("signal")]
    [InlineData("priority")]
    [InlineData("notification")]
    [InlineData("activity")]
    [InlineData("activity-catch")]
    [InlineData("send-task")]
    public async Task MessagePublicationPreservesCapturedGeometryAndExecutionAcrossAllViewOptions(string scenario)
    {
        var composition = await new BpmnModelerCompositionFactory(initialDocument: MessageSnapshot(scenario)).CreateAsync();
        await using var renderer = CreateRenderer();
        Assert.True((await renderer.InitializeAsync("message-publication", new Canvas2DSurfaceSize(1400, 900, 1))).Succeeded);
        composition = BpmnModelerTestComposition.WithDocument(composition,
            await BpmnModelerTestComposition.PrepareFreshDocumentAsync(composition.Document, composition.Configuration, renderer));
        var attached = await EditingSession.AttachAsync(composition.Document, renderer, composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        await AddMessageAsync(session);
        await BpmnModelerTestComposition.SetRoutingTypeAsync(session, new("message:visual"), ConnectorRoutingType.Manual);
        var before = session.CaptureState();
        var document = composition.Document.CaptureSnapshot();
        Assert.Equal(ConnectorRoutingType.Manual, BpmnModelerTestComposition.SavedRoute(document, new("message:visual")).RoutingType);
        Assert.True(BpmnDocumentSerializer.Deserialize(BpmnDocumentSerializer.Serialize(document).AsMemory()).Succeeded);
        var captured = await session.CapturePresentationAsync(before.ModelProfileViewState, before.ModelProfileElementViewState);
        Assert.True(captured.Succeeded, Diagnostics(captured.Diagnostics));
        var capture = captured.Capture!;
        PublishedProcessSnapshot? first = null;
        foreach (var pools in new[] { false, true })
            foreach (var current in new[] { false, true })
            {
                var generated = new PublishingPackageGenerator().Generate(document, capture, new BpmnPublishingOptions
                {
                    IncludePools = pools,
                    InitialViewMode = current ? BpmnPublishingInitialViewMode.CurrentEditorView : BpmnPublishingInitialViewMode.FitEntireDiagram,
                    EditorView = current ? new(document.DocumentId, document.Revision, before.ActiveScopeId, new RectD(20, 20, 800, 600)) : null,
                });
                Assert.True(generated.Succeeded, Diagnostics(generated.Diagnostics));
                Assert.DoesNotContain(generated.Diagnostics, item => item.Code == "PUBLISH_MESSAGE_ENDPOINT_UNSUPPORTED");
                Assert.DoesNotContain(generated.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
                var package = generated.Package!;
                var snapshot = package.Snapshot;
                first ??= snapshot;
                Assert.Equal(1, snapshot.FormatVersion);
                Assert.Equal(current, snapshot.InitialView is not null);
                Assert.Equal(JsonSerializer.Serialize(first.TokenGraph), JsonSerializer.Serialize(snapshot.TokenGraph));
                Assert.Equal(JsonSerializer.Serialize(first.Execution), JsonSerializer.Serialize(snapshot.Execution));
                Assert.Equal(JsonSerializer.Serialize(first.Presentation.Nodes), JsonSerializer.Serialize(snapshot.Presentation.Nodes));
                Assert.Equal(JsonSerializer.Serialize(first.Presentation.Connectors), JsonSerializer.Serialize(snapshot.Presentation.Connectors));
                var message = Assert.Single(snapshot.Execution!.CommunicationRoutes);
                Assert.Equal("message", message.ConnectorId);
                Assert.True(message.CanDeliver);
                Assert.Equal("sender", Assert.Single(snapshot.Execution.SenderIds));
                Assert.Equal(scenario is "activity" or "activity-catch" or "send-task" ? PublishedTokenRole.ActivityDelay : PublishedTokenRole.PassThrough,
                    snapshot.TokenGraph.Nodes.Single(node => node.Id == "sender").Role);
                Assert.All(snapshot.TokenGraph.Nodes, node =>
                {
                    Assert.DoesNotContain("message", node.IncomingConnectorIds);
                    Assert.DoesNotContain("message", node.OutgoingConnectorIds);
                });
                Assert.Contains(snapshot.TokenGraph.Nodes, node => node.Id == "target");
                Assert.DoesNotContain(snapshot.TokenGraph.Nodes, node => node.Id.StartsWith("pool", StringComparison.Ordinal));
                foreach (var connector in snapshot.Presentation.Connectors)
                {
                    var edge = capture.ProjectedGraph.Edges.Single(item => item.Source.SemanticElementId.Value == connector.Id);
                    var scene = capture.Scene.Items.Single(item => item.Id == Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"));
                    Assert.Equal(Canvas2DConnectorPathMetadata.Resolve(scene).Select(point => new PublishedPoint(point.X, point.Y)), connector.Points);
                }
                Assert.Equal(["index.html", "process.json", "process.data.js", "inceptus.publish.js", "styles.css"], ArchiveEntryNames(package.Archive));
                Assert.Equal(pools, snapshot.Presentation.Items.Any(item => item.Content == "Sender Company"));
                if (scenario is "timer" or "priority")
                    Assert.Equal(3000, Assert.Single(snapshot.Execution.AttachedTriggers, item => item.Kind == PublishedTriggerKind.IdleTimer).DelayMs);
                Assert.All(snapshot.Execution.AttachedTriggers, trigger =>
                {
                    Assert.Equal("target", trigger.OwnerId);
                    Assert.False(trigger.Interrupting);
                    Assert.Contains(snapshot.TokenGraph.Nodes, node => node.Id == trigger.NodeId);
                });
            }
        Assert.Same(document, composition.Document.CaptureSnapshot());
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        Assert.Same(before.EditorState, session.CaptureState().EditorState);
    }

    [Theory]
    [InlineData("", 3000)]
    [InlineData("not a duration", 3000)]
    [InlineData("PT0.125S", 1000)]
    [InlineData("PT2.5S", 2500)]
    [InlineData("PT5M", 5000)]
    public async Task BoundaryTimerPublicationUsesPresentationDelayWithoutChangingDocumentOrHistory(string definition, double expectedDelay)
    {
        var composition = await new BpmnModelerCompositionFactory(initialDocument: MessageSnapshot("timer", definition)).CreateAsync();
        await using var renderer = CreateRenderer();
        Assert.True((await renderer.InitializeAsync("invalid-timer", new Canvas2DSurfaceSize(1400, 900, 1))).Succeeded);
        composition = BpmnModelerTestComposition.WithDocument(composition,
            await BpmnModelerTestComposition.PrepareFreshDocumentAsync(composition.Document, composition.Configuration, renderer));
        var attachment = await EditingSession.AttachAsync(composition.Document, renderer, composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await AddMessageAsync(session);
        var state = session.CaptureState();
        var capture = await session.CapturePresentationAsync(state.ModelProfileViewState, state.ModelProfileElementViewState);
        Assert.True(capture.Succeeded, Diagnostics(capture.Diagnostics));
        var document = composition.Document.CaptureSnapshot();
        var generated = new PublishingPackageGenerator().Generate(document, capture.Capture!);
        Assert.True(generated.Succeeded, Diagnostics(generated.Diagnostics));
        Assert.Empty(generated.Diagnostics);
        Assert.Equal(expectedDelay, Assert.Single(generated.Package!.Snapshot.Execution!.AttachedTriggers).DelayMs);
        Assert.Equal(definition, document.SemanticModel.Elements.Single(element => element.Id.Value == "timer")
            .Properties[BpmnSemanticProperties.TimerDefinition].TextValue);
        Assert.Same(document, composition.Document.CaptureSnapshot());
        Assert.Equal(state.HistoryStatus, session.CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationDiagnosticsStayInAuthoringResultAndNeverEnterStandalonePackage(bool includePools)
    {
        var composition = await new BpmnModelerCompositionFactory(initialDocument: MessageSnapshot("warning")).CreateAsync();
        await using var renderer = CreateRenderer();
        Assert.True((await renderer.InitializeAsync("authoring-diagnostics", new Canvas2DSurfaceSize(1400, 900, 1))).Succeeded);
        composition = BpmnModelerTestComposition.WithDocument(composition,
            await BpmnModelerTestComposition.PrepareFreshDocumentAsync(composition.Document, composition.Configuration, renderer));
        var attachment = await EditingSession.AttachAsync(composition.Document, renderer, composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        await AddMessageAsync(session, "notification");
        var before = session.CaptureState();
        var document = composition.Document.CaptureSnapshot();
        var captured = await session.CapturePresentationAsync(before.ModelProfileViewState, before.ModelProfileElementViewState);
        Assert.True(captured.Succeeded, Diagnostics(captured.Diagnostics));
        var result = new PublishingPackageGenerator().Generate(document, captured.Capture!,
            new BpmnPublishingOptions { IncludePools = includePools });
        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal("PUBLISH_MESSAGE_ENDPOINT_UNSUPPORTED", warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        var package = result.Package!;
        Assert.Empty(package.Snapshot.Execution!.Warnings);
        Assert.False(Assert.Single(package.Snapshot.Execution.CommunicationRoutes).CanDeliver);
        using var input = new MemoryStream(package.Archive.ToArray(), writable: false);
        using var archive = new System.IO.Compression.ZipArchive(input, System.IO.Compression.ZipArchiveMode.Read);
        foreach (var name in new[] { "process.json", "process.data.js", "index.html" })
        {
            using var reader = new StreamReader(archive.GetEntry(name)!.Open());
            var content = await reader.ReadToEndAsync();
            Assert.DoesNotContain(warning.Code, content, StringComparison.Ordinal);
            Assert.DoesNotContain("is visual only", content, StringComparison.Ordinal);
            Assert.DoesNotContain("viewer-status", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Loading process", content, StringComparison.Ordinal);
        }
        Assert.Same(document, composition.Document.CaptureSnapshot());
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
    }

    private static DocumentSnapshot MessageSnapshot(string scenario, string timer = "PT3S")
    {
        var id = new DocumentId("publish:communication:" + scenario);
        var revision = DocumentRevision.Zero;
        var nodes = new List<(string Id, SemanticTypeId Type, PointD Position, string Pool)>
        {
            ("send-start", BpmnSemanticTypes.StartEvent, new(200, 140), "pool-a"),
            ("sender", scenario == "send-task" ? BpmnSemanticTypes.SendTask :
                scenario is "activity" or "activity-catch" ? BpmnSemanticTypes.Task : BpmnSemanticTypes.MessageThrowEvent, new(450, 140), "pool-a"),
            ("send-end", BpmnSemanticTypes.EndEvent, new(800, 140), "pool-a"),
            ("target", scenario is "catch" or "activity-catch" ? BpmnSemanticTypes.MessageCatchEvent : BpmnSemanticTypes.ReceiveTask, new(450, 540), "pool-b"),
            ("target-end", BpmnSemanticTypes.EndEvent, new(800, 540), "pool-b"),
        };
        if (scenario is not ("catch" or "activity-catch")) nodes.Add(("enable-start", BpmnSemanticTypes.StartEvent, new(200, 540), "pool-b"));
        var boundaries = new List<(string Id, SemanticTypeId Type, double Offset)>();
        if (scenario is "timer" or "priority") boundaries.Add(("timer", BpmnSemanticTypes.TimerBoundaryEvent, 0.2));
        if (scenario is "signal" or "priority") boundaries.Add(("signal", BpmnSemanticTypes.SignalBoundaryEvent, 0.8));
        if (scenario is "notification" or "warning") boundaries.Add(("notification", BpmnSemanticTypes.MessageBoundaryEvent, scenario == "warning" ? 0.9 : 0.5));
        foreach (var boundary in boundaries) nodes.Add((boundary.Id + "-end", BpmnSemanticTypes.EndEvent, new(600 + boundary.Offset * 400, 750), "pool-b"));
        var edges = new List<(string Id, string From, string To, SemanticTypeId Type)>
        {
            ("send", "send-start", "sender", BpmnSemanticTypes.SequenceFlow),
            ("local", "sender", "send-end", BpmnSemanticTypes.SequenceFlow),
            ("message", "sender", scenario == "warning" ? "notification" : "target", BpmnSemanticTypes.MessageFlow),
            ("output", "target", "target-end", BpmnSemanticTypes.SequenceFlow),
        };
        if (scenario is not ("catch" or "activity-catch")) edges.Add(("enable", "enable-start", "target", BpmnSemanticTypes.SequenceFlow));
        foreach (var boundary in boundaries) edges.Add((boundary.Id + "-output", boundary.Id, boundary.Id + "-end", BpmnSemanticTypes.SequenceFlow));
        var elements = new List<SemanticElementSnapshot>();
        foreach (var (suffix, name) in new[] { ("a", "Sender Company"), ("b", "Receiver Company") })
        {
            elements.Add(new(new("participant-" + suffix), BpmnSemanticTypes.Participant,
                [new(BpmnSemanticProperties.Name, PropertyValue.FromText(name))], containmentKind: SemanticElementContainmentKind.Document));
            elements.Add(new(new("pool-" + suffix), OrganizationalSemanticTypes.Pool,
                [new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText("participant-" + suffix)),
                 new(OrganizationalSemanticProperties.Name, PropertyValue.FromText("Department " + suffix))]));
        }
        elements.AddRange(nodes.Select(node => new SemanticElementSnapshot(new(node.Id), node.Type,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText(node.Id))])));
        elements.AddRange(boundaries.Select(boundary => new SemanticElementSnapshot(new(boundary.Id), boundary.Type,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText(boundary.Id)),
             new(BpmnSemanticProperties.CancelActivity, PropertyValue.FromBoolean(false)),
             new(BpmnSemanticProperties.TimerDefinition, PropertyValue.FromText(timer))], new("target"))));
        IEnumerable<ConnectorAnchor> Anchors(string key) => edges.Where(edge => edge.From == key)
            .Select(edge => new ConnectorAnchor(new(edge.Id + ":source"), edge.Type == BpmnSemanticTypes.MessageFlow ? ConnectorAnchorSide.Bottom : ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0))
            .Concat(edges.Where(edge => edge.To == key).Select(edge => new ConnectorAnchor(new(edge.Id + ":target"),
                edge.Type == BpmnSemanticTypes.MessageFlow ? ConnectorAnchorSide.Top : ConnectorAnchorSide.Left, ConnectorAnchorRole.Target, 0)));
        var visuals = nodes.Select(node => new VisualStateSnapshot(new(node.Id + ":visual"), new(node.Id), node.Position,
            BpmnActivitySemanticTypes.IsActivity(node.Type) ? new SizeD(160, 100) : new SizeD(40, 40), VisualPlacementMode.Manual,
            connectorAnchors: Anchors(node.Id))).ToList();
        var owner = visuals.Single(visual => visual.SemanticElementId.Value == "target");
        foreach (var boundary in boundaries)
        {
            var placement = new BoundaryAttachmentPlacement(
                scenario == "warning" ? BoundaryAttachmentSide.Right : BoundaryAttachmentSide.Bottom, boundary.Offset);
            var bounds = placement.ResolveBounds(new RectD(owner.Position.X, owner.Position.Y, owner.Size.Width, owner.Size.Height), new SizeD(40, 40));
            visuals.Add(new(new(boundary.Id + ":visual"), new(boundary.Id), new(bounds.X, bounds.Y), new(40, 40), VisualPlacementMode.Manual,
                connectorAnchors: Anchors(boundary.Id), boundaryAttachment: placement));
        }
        visuals.AddRange(edges.Where(edge => edge.Id != "message").Select(edge => new VisualStateSnapshot(new(edge.Id + ":visual"), new(edge.Id), new(0, 0), new(0, 0), VisualPlacementMode.Manual,
            sourceAnchorId: new(edge.Id + ":source"), targetAnchorId: new(edge.Id + ":target"))));
        foreach (var suffix in new[] { "a", "b" })
            visuals.Add(new(new("participant-" + suffix + ":visual"), new("participant-" + suffix), default, default, VisualPlacementMode.Derived));
        return new(new SemanticModelSnapshot(id, revision, elements,
                edges.Where(edge => edge.Id != "message").Select(edge => new SemanticRelationshipSnapshot(new(edge.Id), edge.Type, new(edge.From), new(edge.To))),
                modelProfiles: new([OrganizationalModelProfile.Id]), profileAssignments:
                nodes.Select(node => new ModelProfileElementAssignmentSnapshot(OrganizationalModelProfile.Id, new(node.Id), new(node.Pool)))),
            new VisualModelSnapshot(id, revision, visuals), new DocumentMetadataSnapshot(id, revision),
            new DocumentPublicationSnapshot(scenario, "Communication — " + scenario, "PublishedProcess custom animation acceptance"));
    }

    private static async Task AddMessageAsync(EditingSession session, string target = "target")
    {
        var state = session.CaptureState();
        var result = await session.ExecuteAsync(new CreateBpmnMessageFlowCommand(state.DocumentId, state.DocumentRevision,
            new("message"), new("message:visual"), new("sender"), new(target), new("message:source"), new("message:target")));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync();
    }
}
