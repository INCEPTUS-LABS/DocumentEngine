using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN91BoundaryEventCreationCoreTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n91:boundary-core");
    private static readonly DocumentScopeId RootScopeId = new(DocumentId.Value);
    private static readonly DocumentScopeId ChildScopeId =
        new("bpmn:n91:boundary-core:child");
    private static readonly DocumentScopeId OwnedScopeId =
        new("bpmn:n91:boundary-core:owned");
    private static readonly SemanticElementId ScopeOwnerId =
        new("bpmn:n91:boundary-core:scope-owner");
    private static readonly SemanticElementId ActivityId =
        new("bpmn:n91:boundary-core:activity");
    private static readonly SemanticElementId BoundaryId =
        new("bpmn:n91:boundary-core:boundary");
    private static readonly VisualStateId ScopeOwnerVisualId =
        new("bpmn:n91:boundary-core:scope-owner:visual");
    private static readonly VisualStateId ActivityVisualId =
        new("bpmn:n91:boundary-core:activity:visual");
    private static readonly VisualStateId BoundaryVisualId =
        new("bpmn:n91:boundary-core:boundary:visual");
    private static readonly RectD PersistentOwnerBounds = new(20d, 30d, 120d, 80d);
    private static readonly RectD EffectiveOwnerBounds = new(180d, 120d, 140d, 90d);
    private static readonly SizeD BoundarySize = new(36d, 36d);
    private static readonly BoundaryAttachmentPlacement Attachment =
        new(BoundaryAttachmentSide.Right, 0.25d);

    public static TheoryData<SemanticTypeId> NewBoundaryTypes => new()
    {
        BpmnSemanticTypes.MessageBoundaryEvent,
        BpmnSemanticTypes.SignalBoundaryEvent,
    };

    public static TheoryData<SemanticTypeId, SemanticTypeId> BoundaryActivityMatrix
    {
        get
        {
            var values = new TheoryData<SemanticTypeId, SemanticTypeId>();
            foreach (var boundaryType in new[]
                     {
                         BpmnSemanticTypes.MessageBoundaryEvent,
                         BpmnSemanticTypes.SignalBoundaryEvent,
                     })
            {
                foreach (var activityType in BpmnActivitySemanticTypes.All)
                {
                    values.Add(boundaryType, activityType);
                }
            }

            return values;
        }
    }

    [Fact]
    public void SemanticFactoriesAndNarrowCommandsExposeCanonicalBoundaryDataOnly()
    {
        Assert.Equal(
            "BPMN.MessageBoundaryEvent",
            BpmnSemanticTypes.MessageBoundaryEvent.Value);
        Assert.Equal(
            "BPMN.SignalBoundaryEvent",
            BpmnSemanticTypes.SignalBoundaryEvent.Value);
        Assert.Equal(
            [
                BpmnSemanticTypes.TimerBoundaryEvent,
                BpmnSemanticTypes.MessageBoundaryEvent,
                BpmnSemanticTypes.SignalBoundaryEvent,
            ],
            BpmnBoundaryEventSemanticTypes.All.AsEnumerable());

        foreach (var typeId in new[]
                 {
                     BpmnSemanticTypes.MessageBoundaryEvent,
                     BpmnSemanticTypes.SignalBoundaryEvent,
                 })
        {
            Assert.True(BpmnSemanticTypes.IsFlowNode(typeId));
            Assert.True(BpmnSemanticTypes.IsEvent(typeId));
            Assert.True(BpmnSemanticTypes.IsCatchEvent(typeId));
            Assert.True(BpmnBoundaryEventSemanticTypes.IsBoundaryEvent(typeId));
            Assert.False(BpmnIntermediateEventSemanticTypes.IsIntermediateEvent(typeId));
            Assert.False(BpmnIntermediateEventSemanticTypes.IsThrowEvent(typeId));
            Assert.False(BpmnActivitySemanticTypes.IsActivity(typeId));
            Assert.False(BpmnTaskSemanticTypes.IsTask(typeId));
            Assert.False(IsGateway(typeId));

            var element = CreateSemantic(typeId, cancelActivity: false);
            Assert.Equal(typeId, element.TypeId);
            Assert.Equal(ActivityId, element.AttachedToElementId);
            Assert.Equal(
                [
                    BpmnSemanticProperties.CancelActivity,
                    BpmnSemanticProperties.Description,
                    BpmnSemanticProperties.Name,
                ],
                element.Properties.Keys.Order(StringComparer.Ordinal));
            Assert.Equal("Boundary catch", element.Properties[
                BpmnSemanticProperties.Name].TextValue);
            Assert.False(element.Properties[
                BpmnSemanticProperties.CancelActivity].BooleanValue);
            Assert.Equal("Boundary description.", element.Properties[
                BpmnSemanticProperties.Description].TextValue);
            Assert.DoesNotContain(element.Properties.Keys, static key =>
                key.Contains("MessageRef", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("SignalRef", StringComparison.OrdinalIgnoreCase));
        }

        var message = new CreateBpmnMessageBoundaryEventCommand(
            DocumentId,
            DocumentRevision.Zero,
            BoundaryId,
            BoundaryVisualId,
            ActivityId,
            Attachment.Side,
            Attachment.PositionOnSide,
            EffectiveOwnerBounds,
            "Message boundary");
        var signal = new CreateBpmnSignalBoundaryEventCommand(
            DocumentId,
            DocumentRevision.Zero,
            BoundaryId,
            BoundaryVisualId,
            ActivityId,
            Attachment.Side,
            Attachment.PositionOnSide,
            EffectiveOwnerBounds,
            "Signal boundary");

        Assert.Equal(
            "bpmn:command/create-message-boundary-event",
            message.TypeId.Value);
        Assert.Equal(
            "bpmn:command/create-signal-boundary-event",
            signal.TypeId.Value);
        Assert.True(message.CancelActivity);
        Assert.True(signal.CancelActivity);
        Assert.True(BpmnSemanticFactory.CreateMessageBoundaryEvent(
            BoundaryId,
            ActivityId,
            "Message boundary").Properties[
                BpmnSemanticProperties.CancelActivity].BooleanValue);
        Assert.True(BpmnSemanticFactory.CreateSignalBoundaryEvent(
            BoundaryId,
            ActivityId,
            "Signal boundary").Properties[
                BpmnSemanticProperties.CancelActivity].BooleanValue);
        Assert.Equal(Attachment, message.BoundaryAttachment);
        Assert.Equal(Attachment, signal.BoundaryAttachment);
        Assert.Equal(EffectiveOwnerBounds, message.EffectiveOwnerBounds);
        Assert.Equal(EffectiveOwnerBounds, signal.EffectiveOwnerBounds);
        Assert.Null(typeof(CreateBpmnMessageBoundaryEventCommand).GetProperty(
            "TimerDefinition"));
        Assert.Null(typeof(CreateBpmnSignalBoundaryEventCommand).GetProperty(
            "TimerDefinition"));
        Assert.DoesNotContain(
            typeof(CreateBpmnMessageBoundaryEventCommand).GetProperties(),
            static property => property.Name.EndsWith("Ref", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(CreateBpmnSignalBoundaryEventCommand).GetProperties(),
            static property => property.Name.EndsWith("Ref", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(BoundaryActivityMatrix))]
    public async Task CreationAcceptsEveryActivityOwner(
        SemanticTypeId boundaryType,
        SemanticTypeId activityType)
    {
        IEnumerable<DocumentScopeSnapshot>? nestedScopes =
            activityType == BpmnSemanticTypes.SubProcess
                ? [new DocumentScopeSnapshot(OwnedScopeId, RootScopeId, ActivityId)]
                : null;
        var harness = CreateHarness(Snapshot(
            [Activity(activityType)],
            [NodeVisual(
                ActivityVisualId,
                ActivityId,
                PersistentOwnerBounds,
                VisualPlacementMode.Pinned)],
            nestedScopes));

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            CreateCommand(
                boundaryType,
                harness.Document.Revision,
                PersistentOwnerBounds,
                targetScopeId: RootScopeId));

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var committed = harness.Document.CaptureSnapshot();
        var boundary = Assert.Single(committed.SemanticModel.Elements, candidate =>
            candidate.Id == BoundaryId);
        Assert.Equal(boundaryType, boundary.TypeId);
        Assert.Equal(ActivityId, boundary.AttachedToElementId);
        Assert.Equal(RootScopeId, committed.SemanticModel.GetScope(BoundaryId).Id);
        Assert.DoesNotContain(committed.SemanticModel.ScopeMemberships, membership =>
            membership.SemanticElementId == BoundaryId);
        var visual = Assert.Single(committed.VisualModel.VisualStates, candidate =>
            candidate.Id == BoundaryVisualId);
        Assert.Equal(BoundarySize, visual.Size);
        Assert.Equal(
            Attachment.ResolveBounds(PersistentOwnerBounds, BoundarySize).TopLeft,
            visual.Position);
    }

    [Theory]
    [MemberData(nameof(NewBoundaryTypes))]
    public async Task CreationUsesEffectiveAutomaticOwnerGeometryAndExactChildScopeHistory(
        SemanticTypeId boundaryType)
    {
        var harness = CreateHarness(ChildActivitySnapshot());
        var before = harness.Document.CaptureSnapshot();

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            CreateCommand(
                boundaryType,
                harness.Document.Revision,
                EffectiveOwnerBounds,
                targetScopeId: ChildScopeId,
                cancelActivity: false));

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var committed = harness.Document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), committed.Revision);
        Assert.Equal(
            before.SemanticModel.ElementCount + 1,
            committed.SemanticModel.ElementCount);
        Assert.Equal(
            before.VisualModel.Count + 1,
            committed.VisualModel.Count);
        Assert.Equal(
            before.SemanticModel.ScopeMemberships.Length + 1,
            committed.SemanticModel.ScopeMemberships.Length);
        var element = Assert.Single(committed.SemanticModel.Elements, candidate =>
            candidate.Id == BoundaryId);
        Assert.Equal(boundaryType, element.TypeId);
        Assert.Equal(ActivityId, element.AttachedToElementId);
        Assert.False(element.Properties[
            BpmnSemanticProperties.CancelActivity].BooleanValue);
        Assert.Equal(ChildScopeId, committed.SemanticModel.GetScope(BoundaryId).Id);
        Assert.Single(committed.SemanticModel.ScopeMemberships, membership =>
            membership.SemanticElementId == BoundaryId &&
            membership.ScopeId == ChildScopeId);

        var visual = Assert.Single(committed.VisualModel.VisualStates, candidate =>
            candidate.Id == BoundaryVisualId);
        var expectedBounds = Attachment.ResolveBounds(EffectiveOwnerBounds, BoundarySize);
        Assert.Equal(expectedBounds.TopLeft, visual.Position);
        Assert.Equal(BoundarySize, visual.Size);
        Assert.Equal(VisualPlacementMode.Manual, visual.PlacementMode);
        Assert.Equal(Attachment, visual.BoundaryAttachment);

        var retainedOwnerVisual = Assert.Single(
            committed.VisualModel.VisualStates,
            candidate => candidate.Id == ActivityVisualId);
        Assert.Equal(PersistentOwnerBounds.TopLeft, retainedOwnerVisual.Position);
        Assert.Equal(PersistentOwnerBounds.Size, retainedOwnerVisual.Size);
        Assert.Equal(VisualPlacementMode.Automatic, retainedOwnerVisual.PlacementMode);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);

        var undo = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        AssertAuthoritativeContentEqual(before, harness.Document.CaptureSnapshot());

        var redo = await harness.History.RedoAsync(harness.Processor);
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        AssertAuthoritativeContentEqual(committed, harness.Document.CaptureSnapshot());
    }

    [Theory]
    [MemberData(nameof(NewBoundaryTypes))]
    public async Task InvalidOwnerScopeTextAndGeometryAreRejectedAtomically(
        SemanticTypeId boundaryType)
    {
        await AssertRejectedAsync(
            Snapshot(
                [BpmnSemanticFactory.CreateStartEvent(ActivityId, "Not an Activity")],
                [NodeVisual(
                    ActivityVisualId,
                    ActivityId,
                    PersistentOwnerBounds,
                    VisualPlacementMode.Pinned)]),
            revision => CreateCommand(
                boundaryType,
                revision,
                PersistentOwnerBounds),
            BpmnCommandDiagnosticCodes.BoundaryEventAttachmentOwnerInvalid);

        await AssertRejectedAsync(
            ChildActivitySnapshot(),
            revision => CreateCommand(
                boundaryType,
                revision,
                EffectiveOwnerBounds,
                targetScopeId: RootScopeId),
            BpmnCommandDiagnosticCodes.BoundaryEventAttachmentScopeMismatch);

        var edgeBounds = new RectD(0d, 0d, 120d, 80d);
        await AssertRejectedAsync(
            RootActivitySnapshot(edgeBounds, VisualPlacementMode.Automatic),
            revision => CreateCommand(
                boundaryType,
                revision,
                edgeBounds,
                attachment: new BoundaryAttachmentPlacement(
                    BoundaryAttachmentSide.Top,
                    0.5d)),
            BpmnCommandDiagnosticCodes.BoundaryEventAttachmentGeometryInvalid);

        await AssertRejectedAsync(
            RootActivitySnapshot(PersistentOwnerBounds, VisualPlacementMode.Pinned),
            revision => CreateCommand(
                boundaryType,
                revision,
                EffectiveOwnerBounds),
            BpmnCommandDiagnosticCodes.BoundaryEventAttachmentGeometryInvalid);

        await AssertRejectedAsync(
            RootActivitySnapshot(PersistentOwnerBounds, VisualPlacementMode.Pinned),
            revision => CreateCommand(
                boundaryType,
                revision,
                PersistentOwnerBounds,
                name: " "),
            BpmnCommandDiagnosticCodes.InvalidBoundaryEvent);
    }

    [Theory]
    [MemberData(nameof(NewBoundaryTypes))]
    public void ImportedSnapshotsUseBoundaryStructuralRulesAndReadableTypeNames(
        SemanticTypeId boundaryType)
    {
        var displayName = boundaryType == BpmnSemanticTypes.MessageBoundaryEvent
            ? "Message Boundary Event"
            : "Signal Boundary Event";
        var expectedReference =
            $"{displayName} \"Boundary catch\" [ID: {BoundaryId.Value}]";

        var missing = ImportedBoundary(boundaryType, attachedToActivityId: null);
        var missingIssue = Assert.Single(
            Validate(Snapshot([missing])),
            issue => issue.Code ==
                BpmnModelValidationCodes.BoundaryEventAttachmentMissing);
        Assert.Equal(BoundaryId, missingIssue.Target.SemanticElementId);
        Assert.Contains(expectedReference, missingIssue.Message, StringComparison.Ordinal);

        var invalidOwner = BpmnSemanticFactory.CreateStartEvent(
            ActivityId,
            "Not an Activity");
        var invalid = ImportedBoundary(boundaryType, ActivityId);
        var invalidOwnerIssue = Assert.Single(
            Validate(Snapshot([invalidOwner, invalid])),
            issue => issue.Code ==
                BpmnModelValidationCodes.BoundaryEventAttachmentOwnerInvalid);
        Assert.Equal(BoundaryId, invalidOwnerIssue.Target.SemanticElementId);
        Assert.Contains(
            expectedReference,
            invalidOwnerIssue.Message,
            StringComparison.Ordinal);

        var scopeOwner = BpmnSemanticFactory.CreateSubProcess(
            ScopeOwnerId,
            "SCOPE_OWNER",
            "Scope owner");
        var owner = Activity(BpmnSemanticTypes.UserTask);
        var scopeMismatch = Snapshot(
            [scopeOwner, owner, ImportedBoundary(boundaryType, ActivityId)],
            nestedScopes:
            [
                new DocumentScopeSnapshot(
                    ChildScopeId,
                    RootScopeId,
                    ScopeOwnerId),
            ],
            memberships:
            [
                new SemanticElementScopeMembershipSnapshot(BoundaryId, ChildScopeId),
            ]);
        var scopeIssue = Assert.Single(
            Validate(scopeMismatch, ChildScopeId),
            issue => issue.Code ==
                BpmnModelValidationCodes.BoundaryEventAttachmentScopeMismatch);
        Assert.Equal(BoundaryId, scopeIssue.Target.SemanticElementId);
        Assert.Contains(expectedReference, scopeIssue.Message, StringComparison.Ordinal);

        var validIssues = Validate(Snapshot(
            [owner, ImportedBoundary(boundaryType, ActivityId)]));
        Assert.DoesNotContain(validIssues, issue =>
            issue.Target.SemanticElementId == BoundaryId &&
            (issue.Code == BpmnModelValidationCodes.FlowNodeIsolated ||
             issue.Code == BpmnModelValidationCodes.NodeUnreachableFromStart));
    }

    private static SemanticElementSnapshot CreateSemantic(
        SemanticTypeId boundaryType,
        bool cancelActivity) =>
        boundaryType == BpmnSemanticTypes.MessageBoundaryEvent
            ? BpmnSemanticFactory.CreateMessageBoundaryEvent(
                BoundaryId,
                ActivityId,
                "Boundary catch",
                cancelActivity,
                "Boundary description.")
            : boundaryType == BpmnSemanticTypes.SignalBoundaryEvent
                ? BpmnSemanticFactory.CreateSignalBoundaryEvent(
                    BoundaryId,
                    ActivityId,
                    "Boundary catch",
                    cancelActivity,
                    "Boundary description.")
                : throw new ArgumentException(
                    $"Semantic type '{boundaryType}' is not an N9.1 Boundary Event.",
                    nameof(boundaryType));

    private static SemanticElementSnapshot ImportedBoundary(
        SemanticTypeId boundaryType,
        SemanticElementId? attachedToActivityId) =>
        new(
            BoundaryId,
            boundaryType,
            [
                new(
                    BpmnSemanticProperties.Name,
                    PropertyValue.FromText("Boundary catch")),
                new(
                    BpmnSemanticProperties.CancelActivity,
                    PropertyValue.FromBoolean(true)),
            ],
            attachedToElementId: attachedToActivityId);

    private static bool IsGateway(SemanticTypeId typeId) =>
        typeId == BpmnSemanticTypes.ExclusiveGateway ||
        typeId == BpmnSemanticTypes.ParallelGateway ||
        typeId == BpmnSemanticTypes.InclusiveGateway ||
        typeId == BpmnSemanticTypes.EventBasedGateway;

    private static ICommand CreateCommand(
        SemanticTypeId boundaryType,
        DocumentRevision revision,
        RectD effectiveOwnerBounds,
        DocumentScopeId? targetScopeId = null,
        bool cancelActivity = true,
        BoundaryAttachmentPlacement? attachment = null,
        string name = "Boundary catch")
    {
        attachment ??= Attachment;
        return boundaryType == BpmnSemanticTypes.MessageBoundaryEvent
            ? new CreateBpmnMessageBoundaryEventCommand(
                DocumentId,
                revision,
                BoundaryId,
                BoundaryVisualId,
                ActivityId,
                attachment.Side,
                attachment.PositionOnSide,
                effectiveOwnerBounds,
                name,
                cancelActivity,
                "Boundary description.",
                targetScopeId)
            : boundaryType == BpmnSemanticTypes.SignalBoundaryEvent
                ? new CreateBpmnSignalBoundaryEventCommand(
                    DocumentId,
                    revision,
                    BoundaryId,
                    BoundaryVisualId,
                    ActivityId,
                    attachment.Side,
                    attachment.PositionOnSide,
                    effectiveOwnerBounds,
                    name,
                    cancelActivity,
                    "Boundary description.",
                    targetScopeId)
                : throw new ArgumentException(
                    $"Semantic type '{boundaryType}' is not an N9.1 Boundary Event.",
                    nameof(boundaryType));
    }

    private static async Task AssertRejectedAsync(
        DocumentSnapshot initial,
        Func<DocumentRevision, ICommand> commandFactory,
        string diagnosticCode)
    {
        var harness = CreateHarness(initial);
        var before = harness.Document.CaptureSnapshot();

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            commandFactory(harness.Document.Revision));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == diagnosticCode);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    private static DocumentSnapshot ChildActivitySnapshot() => Snapshot(
        [
            BpmnSemanticFactory.CreateSubProcess(
                ScopeOwnerId,
                "SCOPE_OWNER",
                "Scope owner"),
            Activity(BpmnSemanticTypes.UserTask),
        ],
        [
            NodeVisual(
                ScopeOwnerVisualId,
                ScopeOwnerId,
                new RectD(10d, 10d, 140d, 100d),
                VisualPlacementMode.Pinned),
            NodeVisual(
                ActivityVisualId,
                ActivityId,
                PersistentOwnerBounds,
                VisualPlacementMode.Automatic),
        ],
        [new DocumentScopeSnapshot(ChildScopeId, RootScopeId, ScopeOwnerId)],
        [new SemanticElementScopeMembershipSnapshot(ActivityId, ChildScopeId)]);

    private static DocumentSnapshot RootActivitySnapshot(
        RectD bounds,
        VisualPlacementMode placementMode) => Snapshot(
        [Activity(BpmnSemanticTypes.UserTask)],
        [NodeVisual(ActivityVisualId, ActivityId, bounds, placementMode)]);

    private static SemanticElementSnapshot Activity(SemanticTypeId activityType) =>
        BpmnTaskSemanticTypes.IsTask(activityType)
            ? BpmnSemanticFactory.CreateTask(
                ActivityId,
                activityType,
                "ACTIVITY",
                "Activity",
                1,
                "Activity description.")
            : activityType == BpmnSemanticTypes.SubProcess
                ? BpmnSemanticFactory.CreateSubProcess(
                    ActivityId,
                    "ACTIVITY",
                    "Activity",
                    "Activity description.")
                : throw new ArgumentException(
                    $"Semantic type '{activityType}' is not a BPMN Activity.",
                    nameof(activityType));

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        RectD bounds,
        VisualPlacementMode placementMode) =>
        new(
            visualStateId,
            semanticElementId,
            bounds.TopLeft,
            bounds.Size,
            placementMode);

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<VisualStateSnapshot>? visuals = null,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                elements,
                relationships: null,
                nestedScopes,
                memberships),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero, visuals),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

    private static System.Collections.Immutable.ImmutableArray<ModelValidationIssue>
        Validate(DocumentSnapshot document, DocumentScopeId? activeScopeId = null) =>
        new BpmnStructuralValidationRule().Validate(
            activeScopeId is null
                ? new ModelValidationContext(document)
                : new ModelValidationContext(document, activeScopeId));

    private static Harness CreateHarness(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N91;
        var anchorPolicies = new ElementConnectorAnchorPolicyRegistry(
            registration.ConnectorAnchorPolicies);
        var creation = DocumentFactory.Create(snapshot, anchorPolicies);
        Assert.True(creation.Succeeded, Diagnostics(creation.Diagnostics));
        var document = Assert.IsType<Document>(creation.Document);
        var processor = new CommandProcessor(
            registration.CommandHandlers,
            registration.CommandValidators,
            historyPolicies: registration.HistoryPolicies,
            connectorAnchorPolicyProvider: anchorPolicies);
        return new Harness(document, processor, new HistoryManager(document));
    }

    private static void AssertAuthoritativeContentEqual(
        DocumentSnapshot expected,
        DocumentSnapshot actual)
    {
        Assert.Equal(
            expected.SemanticModel.Elements.AsEnumerable(),
            actual.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.Relationships.AsEnumerable(),
            actual.SemanticModel.Relationships.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.NestedScopes.AsEnumerable(),
            actual.SemanticModel.NestedScopes.AsEnumerable());
        Assert.Equal(
            expected.SemanticModel.ScopeMemberships.AsEnumerable(),
            actual.SemanticModel.ScopeMemberships.AsEnumerable());
        Assert.Equal(
            expected.VisualModel.VisualStates.AsEnumerable(),
            actual.VisualModel.VisualStates.AsEnumerable());
    }

    private static string Diagnostics(
        IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);
}
