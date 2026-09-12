using System.Collections.Immutable;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN102NativeDocumentImportExportIntegrationTests
{
    private static readonly DocumentId CompleteDocumentId =
        new("inceptus:n10.2:integration:complete");
    private static readonly DocumentRevision CompleteRevision = new(42);
    private static readonly DocumentScopeId RootScopeId =
        new(CompleteDocumentId.Value);
    private static readonly DocumentScopeId NestedScopeId =
        new("inceptus:n10.2:scope:nested");
    private static readonly DocumentScopeId PeerRootScopeId =
        new("inceptus:n10.2:scope:peer-root");
    private static readonly SemanticElementId CollaborationId =
        new("inceptus:n10.2:collaboration");
    private static readonly SemanticElementId MainParticipantId =
        new("inceptus:n10.2:participant:main");
    private static readonly SemanticElementId PeerParticipantId =
        new("inceptus:n10.2:participant:peer");
    private static readonly SemanticElementId SourceTaskId =
        new("inceptus:n10.2:process:main:task:source");
    private static readonly SemanticElementId TargetTaskId =
        new("inceptus:n10.2:process:main:task:target");
    private static readonly SemanticElementId UnassignedTaskId =
        new("inceptus:n10.2:process:main:task:unassigned");
    private static readonly SemanticElementId BoundaryEventId =
        new("inceptus:n10.2:process:main:event:boundary");
    private static readonly SemanticElementId SubProcessId =
        new("inceptus:n10.2:process:main:subprocess");
    private static readonly SemanticElementId NestedTaskId =
        new("inceptus:n10.2:process:nested:task");
    private static readonly SemanticElementId PeerTaskId =
        new("inceptus:n10.2:process:peer:task");
    private static readonly SemanticElementId PoolAId =
        new("inceptus:n10.2:organization:pool:a");
    private static readonly SemanticElementId PoolBId =
        new("inceptus:n10.2:organization:pool:b");
    private static readonly SemanticElementId SequenceFlowId =
        new("inceptus:n10.2:process:main:flow");
    private static readonly ConnectorAnchorId SourceAnchorId =
        new("inceptus:n10.2:anchor:source");
    private static readonly ConnectorAnchorId SecondSourceAnchorId =
        new("inceptus:n10.2:anchor:source:second");
    private static readonly ConnectorAnchorId TargetAnchorId =
        new("inceptus:n10.2:anchor:target");

    [Fact]
    public void CompleteBpmnOrganizationalDocumentRoundTripsEveryPersistentAuthorityExactly()
    {
        var source = CreateCompleteDocument();
        var expected = source.CaptureSnapshot();

        var payload = NativeDocumentSerializer.Export(source);
        var imported = NativeDocumentSerializer.Import(
            payload.ToArray(),
            ConnectorAnchorPolicies());

        Assert.Equal("Inceptus.Document", NativeDocumentSerializer.FormatIdentifier);
        Assert.Equal(1, NativeDocumentSerializer.FormatVersion);
        Assert.NotEmpty(payload);
        Assert.True(imported.Succeeded, Diagnostics(imported.Diagnostics));
        Assert.Empty(imported.Diagnostics);
        var reconstructed = Assert.IsType<Document>(imported.Document);
        Assert.NotSame(source, reconstructed);
        var actual = reconstructed.CaptureSnapshot();
        Assert.Equal(expected, actual);
        Assert.Equal(CompleteDocumentId, actual.DocumentId);
        Assert.Equal(CompleteRevision, actual.Revision);

        Assert.Equal(
            expected.SemanticModel.Elements.Select(static element => element.Id),
            actual.SemanticModel.Elements.Select(static element => element.Id));
        Assert.Equal(
            [NestedScopeId, PeerRootScopeId],
            actual.SemanticModel.NestedScopes.Select(static scope => scope.Id));
        Assert.True(expected.SemanticModel.ScopeMemberships.AsSpan().SequenceEqual(
            actual.SemanticModel.ScopeMemberships.AsSpan()));
        Assert.Equal(
            expected.VisualModel.VisualStates.Select(static visual => visual.Id),
            actual.VisualModel.VisualStates.Select(static visual => visual.Id));

        var nestedScope = Assert.Single(
            actual.SemanticModel.NestedScopes,
            static scope => scope.Id == NestedScopeId);
        Assert.Equal(RootScopeId, nestedScope.ParentScopeId);
        Assert.Equal(SubProcessId, nestedScope.OwnerSemanticElementId);
        var peerRoot = Assert.Single(
            actual.SemanticModel.NestedScopes,
            static scope => scope.Id == PeerRootScopeId);
        Assert.Null(peerRoot.ParentScopeId);
        Assert.Null(peerRoot.OwnerSemanticElementId);
        Assert.Equal(NestedScopeId, actual.SemanticModel.GetScope(NestedTaskId).Id);
        Assert.Equal(PeerRootScopeId, actual.SemanticModel.GetScope(PeerTaskId).Id);

        var boundary = Assert.Single(
            actual.SemanticModel.Elements,
            static element => element.Id == BoundaryEventId);
        Assert.Equal(SourceTaskId, boundary.AttachedToElementId);
        var boundaryVisual = Assert.Single(
            actual.VisualModel.VisualStates,
            static visual => visual.SemanticElementId == BoundaryEventId);
        Assert.Equal(
            new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Bottom, 0.75d),
            boundaryVisual.BoundaryAttachment);

        var relationship = Assert.Single(actual.SemanticModel.Relationships);
        Assert.Equal(SequenceFlowId, relationship.Id);
        Assert.Equal(SourceTaskId, relationship.SourceId);
        Assert.Equal(TargetTaskId, relationship.TargetId);
        var connector = Assert.Single(
            actual.VisualModel.VisualStates,
            static visual => visual.SemanticElementId == SequenceFlowId);
        Assert.Equal(SourceAnchorId, connector.SourceAnchorId);
        Assert.Equal(TargetAnchorId, connector.TargetAnchorId);
        Assert.True(connector.Route.AsSpan().SequenceEqual(
        [
            new PointD(260d, 150d),
            new PointD(340d, 150d),
            new PointD(420d, 150d),
        ]));
        Assert.True(ConnectorLabelPlacement.TryRead(connector.Properties, out _));
        var sourceVisual = Assert.Single(
            actual.VisualModel.VisualStates,
            static visual => visual.SemanticElementId == SourceTaskId);
        Assert.Equal(
            [(SourceAnchorId, 0), (SecondSourceAnchorId, 1)],
            sourceVisual.ConnectorAnchors.Select(static anchor => (anchor.Id, anchor.Order)));
        Assert.True(NodeLabelVisualOverride.TryRead(sourceVisual.Properties, out _));

        Assert.Equal(
            OrganizationalModelProfile.Id,
            Assert.Single(actual.SemanticModel.ModelProfiles.AvailableProfileIds));
        Assert.Equal(
            [(SourceTaskId, PoolAId), (TargetTaskId, PoolBId)],
            actual.SemanticModel.ProfileAssignments.Select(static assignment =>
                (assignment.SemanticElementId, assignment.ContainerSemanticElementId)));
        Assert.DoesNotContain(
            actual.SemanticModel.ProfileAssignments,
            static assignment => assignment.SemanticElementId == UnassignedTaskId);
        Assert.Equal(
            [(PoolAId, 1), (PoolBId, 0)],
            actual.VisualModel.ProfileElementPresentations.Select(static presentation =>
                (presentation.SemanticElementId, presentation.Order)));
        Assert.Equal(
            [PoolBId, PoolAId],
            OrganizationalSemantics.GetOrderedPoolsInScope(actual, RootScopeId)
                .Select(static pool => pool.Id));

        Assert.Equal(
            PropertyValueKind.Text,
            actual.Metadata.SystemManagedProperties["native:schema"].Kind);
        Assert.Equal(
            PropertyValueKind.Integer,
            actual.Metadata.SystemManagedProperties["native:sequence"].Kind);
        Assert.Equal(
            PropertyValueKind.Boolean,
            actual.Metadata.ExtensionProperties["native:enabled"].Kind);
        Assert.Equal(
            PropertyValueKind.Number,
            actual.Metadata.ExtensionProperties["native:scale"].Kind);
        Assert.True(payload.AsSpan().SequenceEqual(
            NativeDocumentSerializer.Export(reconstructed).AsSpan()));
        Assert.Same(expected, source.CaptureSnapshot());
    }

    [Fact]
    public async Task NativeIoExcludesTransientSessionStateAndLeavesTheAttachedSessionAndHistoryUntouched()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        var document = composition.Document;
        var attachment = await EditingSession.AttachAsync(
            document,
            await CreateRendererAsync(),
            composition.Configuration);
        Assert.Equal(EditingSessionAttachStatus.Ready, attachment.Status);
        await using var session = Assert.IsType<EditingSession>(attachment.Session);
        var poolId = new SemanticElementId("inceptus:n10.2:session:pool");

        await ExecuteCommittedAsync(
            session,
            new SetModelProfileAvailabilityCommand(
                document.DocumentId,
                document.Revision,
                [new ModelProfileAvailabilityChange(
                    OrganizationalModelProfile.Id,
                    isAvailable: true)]));
        var rootState = session.CaptureState();
        await ExecuteCommittedAsync(
            session,
            new CreateOrganizationalPoolCommand(
                document.DocumentId,
                document.Revision,
                poolId,
                rootState.ActiveScopeId,
                OrganizationalPoolCreationMode.AdoptEligibleUnassigned,
                "Native I/O session pool"));
        var assignmentToUnassign = document.SemanticModel.ProfileAssignments.First(
            static assignment => assignment.ProfileId == OrganizationalModelProfile.Id);
        await ExecuteCommittedAsync(
            session,
            new UnassignOrganizationalElementCommand(
                document.DocumentId,
                document.Revision,
                assignmentToUnassign.SemanticElementId));

        var authoritative = document.CaptureSnapshot();
        var beforeTransientPayload = NativeDocumentSerializer.Export(document);
        var historyAfterPersistentEdits = session.CaptureState().HistoryStatus;
        var selectedVisualId = document.VisualModel.VisualStates.Single(visual =>
            visual.SemanticElementId == assignmentToUnassign.SemanticElementId).Id;
        var viewport = new ViewportSnapshot(1.15d, new VectorD(31d, -17d));
        Assert.True((await session.UpdateModelProfileElementViewStateAsync(
            session.CaptureState().ModelProfileElementViewState.WithCollapsed(
                OrganizationalModelProfile.Id,
                poolId,
                isCollapsed: true))).Succeeded);
        await WaitForReadyAsync(session);
        Assert.True((await session.UpdateModelProfileViewStateAsync(
            session.CaptureState().ModelProfileViewState.WithPreferredVisibility(
                OrganizationalModelProfile.Id,
                isVisible: false))).Succeeded);
        await WaitForReadyAsync(session);
        Assert.True((await session.UpdateEditorStateAsync(new EditorStateSnapshot(
            selection: [selectedVisualId],
            activeToolId: "native:n10.2:transient-tool",
            viewport: viewport))).Succeeded);
        await WaitForReadyAsync(session);

        var transientRootState = session.CaptureState();
        Assert.Equal(selectedVisualId, Assert.Single(transientRootState.EditorState.Selection));
        Assert.Equal(viewport, transientRootState.EditorState.Viewport);
        Assert.False(transientRootState.ModelProfileViewState.IsPreferredVisible(
            OrganizationalModelProfile.Id));
        Assert.True(transientRootState.ModelProfileElementViewState.IsCollapsed(
            OrganizationalModelProfile.Id,
            poolId));
        Assert.Equal(historyAfterPersistentEdits, transientRootState.HistoryStatus);
        Assert.Same(authoritative, document.CaptureSnapshot());
        Assert.True(beforeTransientPayload.AsSpan().SequenceEqual(
            NativeDocumentSerializer.Export(document).AsSpan()));

        Assert.True((await session.NavigateToScopeAsync(
            BpmnDemoPipeline.ProcessOrderScopeId)).Succeeded);
        await WaitForReadyAsync(session);
        var beforeIo = session.CaptureState();
        Assert.Equal(BpmnDemoPipeline.ProcessOrderScopeId, beforeIo.ActiveScopeId);
        Assert.Equal(
            transientRootState.HistoryStatus.EntryCount + 1,
            beforeIo.HistoryStatus.EntryCount);
        Assert.Equal(authoritative.Revision, beforeIo.DocumentRevision);
        Assert.Same(authoritative, document.CaptureSnapshot());
        Assert.True(beforeTransientPayload.AsSpan().SequenceEqual(
            NativeDocumentSerializer.Export(document).AsSpan()));

        var exported = NativeDocumentSerializer.Export(document);
        var imported = NativeDocumentSerializer.Import(
            exported.ToArray(),
            ConnectorAnchorPolicies());

        Assert.True(imported.Succeeded, Diagnostics(imported.Diagnostics));
        Assert.Empty(imported.Diagnostics);
        var importedDocument = Assert.IsType<Document>(imported.Document);
        Assert.NotSame(document, importedDocument);
        Assert.Equal(authoritative, importedDocument.CaptureSnapshot());
        Assert.True(exported.AsSpan().SequenceEqual(
            NativeDocumentSerializer.Export(importedDocument).AsSpan()));
        var afterIo = session.CaptureState();
        Assert.Equal(beforeIo.ActiveScopeId, afterIo.ActiveScopeId);
        Assert.Equal(beforeIo.EditorState, afterIo.EditorState);
        Assert.Equal(beforeIo.ModelProfileViewState, afterIo.ModelProfileViewState);
        Assert.Equal(
            beforeIo.ModelProfileElementViewState,
            afterIo.ModelProfileElementViewState);
        Assert.Equal(beforeIo.HistoryStatus, afterIo.HistoryStatus);
        Assert.Equal(beforeIo.DocumentRevision, afterIo.DocumentRevision);
        Assert.Same(authoritative, document.CaptureSnapshot());
    }

    private static Document CreateCompleteDocument()
    {
        var sourceTask = WithAdditionalProperties(
            BpmnSemanticFactory.CreateTask(
                SourceTaskId,
                "SOURCE",
                "Source task",
                long.MaxValue,
                "Unicode: Zażółć gęślą jaźń — 你好"),
            [
                new("native:boolean", PropertyValue.FromBoolean(true)),
                new("native:number", PropertyValue.FromNumber(123.5d)),
            ]);
        var flow = WithAdditionalProperties(
            BpmnSemanticFactory.CreateSequenceFlow(
                SequenceFlowId,
                SourceTaskId,
                TargetTaskId,
                "Authored route",
                "Persistent relationship properties"),
            [new("native:conditional", PropertyValue.FromBoolean(false))]);
        var semanticModel = new SemanticModelSnapshot(
            CompleteDocumentId,
            CompleteRevision,
            [
                BpmnSemanticFactory.CreateCollaboration(
                    CollaborationId,
                    "Native collaboration",
                    "Document-contained semantic authority"),
                BpmnSemanticFactory.CreateParticipant(
                    MainParticipantId,
                    CollaborationId,
                    RootScopeId,
                    "Main process"),
                BpmnSemanticFactory.CreateParticipant(
                    PeerParticipantId,
                    CollaborationId,
                    PeerRootScopeId,
                    "Peer process"),
                sourceTask,
                BpmnSemanticFactory.CreateTask(
                    TargetTaskId,
                    "TARGET",
                    "Target task",
                    2),
                BpmnSemanticFactory.CreateTask(
                    UnassignedTaskId,
                    "UNASSIGNED",
                    "Unassigned process task",
                    3),
                BpmnSemanticFactory.CreateTimerBoundaryEvent(
                    BoundaryEventId,
                    SourceTaskId,
                    "Timeout",
                    "PT15M",
                    cancelActivity: false,
                    "Persistent structural attachment"),
                BpmnSemanticFactory.CreateSubProcess(
                    SubProcessId,
                    "SUB",
                    "Nested process owner"),
                BpmnSemanticFactory.CreateTask(
                    NestedTaskId,
                    "NESTED",
                    "Nested task",
                    4),
                BpmnSemanticFactory.CreateTask(
                    PeerTaskId,
                    "PEER",
                    "Peer-root task",
                    5),
                OrganizationalSemanticFactory.CreatePool(
                    PoolAId,
                    "Operations",
                    "First persistent pool"),
                OrganizationalSemanticFactory.CreatePool(
                    PoolBId,
                    "Fulfilment",
                    "Second persistent pool"),
            ],
            [flow],
            [
                new DocumentScopeSnapshot(
                    NestedScopeId,
                    RootScopeId,
                    SubProcessId),
                new DocumentScopeSnapshot(PeerRootScopeId),
            ],
            [
                new SemanticElementScopeMembershipSnapshot(NestedTaskId, NestedScopeId),
                new SemanticElementScopeMembershipSnapshot(PeerTaskId, PeerRootScopeId),
            ],
            new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]),
            [
                new ModelProfileElementAssignmentSnapshot(
                    OrganizationalModelProfile.Id,
                    SourceTaskId,
                    PoolAId),
                new ModelProfileElementAssignmentSnapshot(
                    OrganizationalModelProfile.Id,
                    TargetTaskId,
                    PoolBId),
            ]);

        var sourceLabelProperties = NodeLabelVisualOverride.UpdateProperties(
            new PropertyMap(
            [
                new("native:visual-text", PropertyValue.FromText("source visual")),
            ]),
            new NodeLabelVisualOverride(0d, 70d, 110d, 28d));
        var connectorLabelProperties = ConnectorLabelPlacement.UpdateProperties(
            new PropertyMap(
            [
                new("native:visual-integer", PropertyValue.FromInteger(-17)),
            ]),
            new ConnectorLabelPlacement(0.625d, new VectorD(6d, -14d)));
        var boundaryPlacement = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Bottom,
            0.75d);
        var visualModel = new VisualModelSnapshot(
            CompleteDocumentId,
            CompleteRevision,
            [
                new VisualStateSnapshot(
                    new VisualStateId("inceptus:n10.2:visual:source"),
                    SourceTaskId,
                    new PointD(100d, 100d),
                    new SizeD(160d, 100d),
                    VisualPlacementMode.Pinned,
                    properties: sourceLabelProperties,
                    connectorAnchors:
                    [
                        new ConnectorAnchor(
                            SourceAnchorId,
                            ConnectorAnchorSide.Right,
                            ConnectorAnchorRole.Source,
                            0),
                        new ConnectorAnchor(
                            SecondSourceAnchorId,
                            ConnectorAnchorSide.Right,
                            ConnectorAnchorRole.Source,
                            1),
                    ]),
                new VisualStateSnapshot(
                    new VisualStateId("inceptus:n10.2:visual:target"),
                    TargetTaskId,
                    new PointD(420d, 100d),
                    new SizeD(160d, 100d),
                    VisualPlacementMode.Manual,
                    connectorAnchors:
                    [
                        new ConnectorAnchor(
                            TargetAnchorId,
                            ConnectorAnchorSide.Left,
                            ConnectorAnchorRole.Target,
                            0),
                    ]),
                new VisualStateSnapshot(
                    new VisualStateId("inceptus:n10.2:visual:unassigned"),
                    UnassignedTaskId,
                    new PointD(100d, 320d),
                    new SizeD(160d, 100d),
                    VisualPlacementMode.Automatic),
                new VisualStateSnapshot(
                    new VisualStateId("inceptus:n10.2:visual:boundary"),
                    BoundaryEventId,
                    new PointD(202d, 182d),
                    new SizeD(36d, 36d),
                    VisualPlacementMode.Pinned,
                    boundaryAttachment: boundaryPlacement),
                new VisualStateSnapshot(
                    new VisualStateId("inceptus:n10.2:visual:subprocess"),
                    SubProcessId,
                    new PointD(680d, 100d),
                    new SizeD(180d, 120d),
                    VisualPlacementMode.Pinned),
                new VisualStateSnapshot(
                    new VisualStateId("inceptus:n10.2:visual:nested-task"),
                    NestedTaskId,
                    new PointD(80d, 80d),
                    new SizeD(140d, 80d),
                    VisualPlacementMode.Pinned),
                new VisualStateSnapshot(
                    new VisualStateId("inceptus:n10.2:visual:peer-task"),
                    PeerTaskId,
                    new PointD(120d, 120d),
                    new SizeD(140d, 80d),
                    VisualPlacementMode.Manual),
                new VisualStateSnapshot(
                    new VisualStateId("inceptus:n10.2:visual:flow"),
                    SequenceFlowId,
                    default,
                    default,
                    VisualPlacementMode.Manual,
                    [
                        new PointD(260d, 150d),
                        new PointD(340d, 150d),
                        new PointD(420d, 150d),
                    ],
                    connectorLabelProperties,
                    sourceAnchorId: SourceAnchorId,
                    targetAnchorId: TargetAnchorId),
            ],
            [
                new ModelProfileElementPresentationSnapshot(
                    OrganizationalModelProfile.Id,
                    PoolAId,
                    1),
                new ModelProfileElementPresentationSnapshot(
                    OrganizationalModelProfile.Id,
                    PoolBId,
                    0),
            ]);
        var metadata = new DocumentMetadataSnapshot(
            CompleteDocumentId,
            CompleteRevision,
            [
                new("native:schema", PropertyValue.FromText("N10.2/v1")),
                new("native:sequence", PropertyValue.FromInteger(long.MinValue)),
            ],
            [
                new("native:enabled", PropertyValue.FromBoolean(true)),
                new("native:scale", PropertyValue.FromNumber(0.125d)),
            ]);

        return RequireSuccess(DocumentReconstructor.Reconstruct(
            new DocumentSnapshot(semanticModel, visualModel, metadata),
            ConnectorAnchorPolicies()));
    }

    private static SemanticElementSnapshot WithAdditionalProperties(
        SemanticElementSnapshot source,
        IEnumerable<KeyValuePair<string, PropertyValue>> additionalProperties) =>
        new(
            source.Id,
            source.TypeId,
            source.Properties.Concat(additionalProperties),
            source.AttachedToElementId,
            source.ContainmentKind);

    private static SemanticRelationshipSnapshot WithAdditionalProperties(
        SemanticRelationshipSnapshot source,
        IEnumerable<KeyValuePair<string, PropertyValue>> additionalProperties) =>
        new(
            source.Id,
            source.TypeId,
            source.SourceId,
            source.TargetId,
            source.Properties.Concat(additionalProperties));

    private static ElementConnectorAnchorPolicyRegistry ConnectorAnchorPolicies() =>
        new(BpmnPluginRegistration.N100.ConnectorAnchorPolicies);

    private static Document RequireSuccess(DocumentConstructionResult result)
    {
        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<Document>(result.Document);
    }

    private static async ValueTask<Canvas2DRenderer> CreateRendererAsync()
    {
        var renderer = new Canvas2DRenderer(
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "org.dejavu.DejaVuSans",
                        "2.37",
                        "DejaVu Sans",
                        "fonts/DejaVuSans-2.37.ttf"),
                ],
                defaultFontFamily: "DejaVu Sans"));
        var initialization = await renderer.InitializeAsync(
            "phase-n102-native-document",
            new Canvas2DSurfaceSize(1600d, 1000d, 1d));
        Assert.True(initialization.Succeeded, Diagnostics(initialization.Diagnostics));
        return renderer;
    }

    private static async ValueTask ExecuteCommittedAsync(
        EditingSession session,
        ICommand command)
    {
        var result = await session.ExecuteAsync(command);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await WaitForReadyAsync(session);
    }

    private static async ValueTask WaitForReadyAsync(EditingSession session)
    {
        await session.WaitForIdleAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(EditingSessionStatus.Ready, session.CaptureState().Status);
    }

    private static string Diagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}"));
}
