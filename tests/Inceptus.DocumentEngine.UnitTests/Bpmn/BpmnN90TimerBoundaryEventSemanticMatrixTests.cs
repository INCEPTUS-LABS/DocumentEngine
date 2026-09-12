using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN90TimerBoundaryEventSemanticMatrixTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n9:semantic-matrix");
    private static readonly DocumentScopeId RootScopeId = new(DocumentId.Value);
    private static readonly DocumentScopeId ChildScopeId =
        new("bpmn:n9:semantic-matrix:child");
    private static readonly DocumentScopeId OwnedScopeId =
        new("bpmn:n9:semantic-matrix:owned");
    private static readonly SemanticElementId ScopeOwnerId =
        new("bpmn:n9:semantic-matrix:scope-owner");
    private static readonly SemanticElementId ActivityId =
        new("bpmn:n9:semantic-matrix:activity");
    private static readonly SemanticElementId BoundaryId =
        new("bpmn:n9:semantic-matrix:boundary");
    private static readonly VisualStateId ActivityVisualId =
        new("bpmn:n9:semantic-matrix:activity:visual");
    private static readonly VisualStateId BoundaryVisualId =
        new("bpmn:n9:semantic-matrix:boundary:visual");
    private static readonly RectD ActivityBounds = new(100d, 80d, 120d, 80d);
    private static readonly SizeD BoundarySize = new(36d, 36d);

    public static TheoryData<SemanticTypeId> SupportedActivityTypes => new()
    {
        BpmnSemanticTypes.Task,
        BpmnSemanticTypes.UserTask,
        BpmnSemanticTypes.ManualTask,
        BpmnSemanticTypes.ServiceTask,
        BpmnSemanticTypes.SendTask,
        BpmnSemanticTypes.ReceiveTask,
        BpmnSemanticTypes.SubProcess,
    };

    public static TheoryData<SemanticTypeId> InvalidOwnerTypes => new()
    {
        BpmnSemanticTypes.StartEvent,
        BpmnSemanticTypes.MessageCatchEvent,
        BpmnSemanticTypes.MessageThrowEvent,
        BpmnSemanticTypes.TimerCatchEvent,
        BpmnSemanticTypes.SignalCatchEvent,
        BpmnSemanticTypes.SignalThrowEvent,
        BpmnSemanticTypes.EndEvent,
        BpmnSemanticTypes.ExclusiveGateway,
        BpmnSemanticTypes.ParallelGateway,
        BpmnSemanticTypes.InclusiveGateway,
        BpmnSemanticTypes.EventBasedGateway,
        BpmnSemanticTypes.TimerBoundaryEvent,
        BpmnSemanticTypes.MessageBoundaryEvent,
        BpmnSemanticTypes.SignalBoundaryEvent,
    };

    [Theory]
    [MemberData(nameof(SupportedActivityTypes))]
    public async Task CreationAcceptsEveryCurrentActivityTypeWithExactAttachmentAndHistory(
        SemanticTypeId activityType)
    {
        var owner = Activity(activityType, ActivityId);
        IEnumerable<DocumentScopeSnapshot>? nestedScopes =
            activityType == BpmnSemanticTypes.SubProcess
                ? [new DocumentScopeSnapshot(OwnedScopeId, RootScopeId, ActivityId)]
                : null;
        var harness = CreateHarness(Snapshot(
            [owner],
            [NodeVisual(ActivityVisualId, ActivityId, ActivityBounds)],
            nestedScopes));
        var before = harness.Document.CaptureSnapshot();
        var attachment = new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Right,
            0.25d);

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            Command(
                harness.Document.Revision,
                attachment,
                targetScopeId: RootScopeId));

        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        var committed = harness.Document.CaptureSnapshot();
        Assert.Equal(before.Revision.Increment(), committed.Revision);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
        var boundary = Assert.Single(committed.SemanticModel.Elements, element =>
            element.Id == BoundaryId);
        Assert.Equal(BpmnSemanticTypes.TimerBoundaryEvent, boundary.TypeId);
        Assert.Equal(ActivityId, boundary.AttachedToElementId);
        Assert.Equal(
            [
                BpmnSemanticProperties.CancelActivity,
                BpmnSemanticProperties.Description,
                BpmnSemanticProperties.Name,
                BpmnSemanticProperties.TimerDefinition,
            ],
            boundary.Properties.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(RootScopeId, committed.SemanticModel.GetScope(boundary.Id).Id);
        Assert.DoesNotContain(committed.SemanticModel.ScopeMemberships, membership =>
            membership.SemanticElementId == BoundaryId);

        var visual = Assert.Single(committed.VisualModel.VisualStates, candidate =>
            candidate.Id == BoundaryVisualId);
        var expectedBounds = attachment.ResolveBounds(ActivityBounds, BoundarySize);
        Assert.Equal(expectedBounds.TopLeft, visual.Position);
        Assert.Equal(BoundarySize, visual.Size);
        Assert.Equal(VisualPlacementMode.Manual, visual.PlacementMode);
        Assert.Equal(attachment, visual.BoundaryAttachment);
        Assert.Equal(
            new PointD(ActivityBounds.Right, ActivityBounds.Top + 20d),
            new PointD(
                visual.Position.X + (visual.Size.Width / 2d),
                visual.Position.Y + (visual.Size.Height / 2d)));

        var undo = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        Assert.False(harness.Document.SemanticModel.TryGetElement(BoundaryId, out _));
        Assert.False(harness.Document.VisualModel.TryGetVisualState(
            BoundaryVisualId,
            out _));

        var redo = await harness.History.RedoAsync(harness.Processor);
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        AssertAuthoritativeContentEqual(committed, harness.Document.CaptureSnapshot());
    }

    [Theory]
    [MemberData(nameof(InvalidOwnerTypes))]
    public async Task CreationRejectsEveryEventGatewayAndBoundaryOwnerAtomically(
        SemanticTypeId invalidOwnerType)
    {
        var invalidOwner = new SemanticElementSnapshot(
            ActivityId,
            invalidOwnerType,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Invalid owner"))]);
        var harness = CreateHarness(Snapshot(
            [invalidOwner],
            [NodeVisual(ActivityVisualId, ActivityId, ActivityBounds)]));
        var before = harness.Document.CaptureSnapshot();

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            Command(harness.Document.Revision));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code ==
                BpmnCommandDiagnosticCodes.BoundaryEventAttachmentOwnerInvalid);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task CreationRejectsMissingOwnerAndInvalidOwnerVisualCardinalityAtomically()
    {
        var missingOwner = CreateHarness(Snapshot([], []));
        var missingBefore = missingOwner.Document.CaptureSnapshot();
        var missingResult = await missingOwner.History.ExecuteAsync(
            missingOwner.Processor,
            Command(missingOwner.Document.Revision));

        Assert.False(missingResult.IsCommitted);
        Assert.Contains(missingResult.Diagnostics, diagnostic =>
            diagnostic.Code ==
                BpmnCommandDiagnosticCodes.BoundaryEventAttachmentOwnerInvalid);
        Assert.Same(missingBefore, missingOwner.Document.CaptureSnapshot());
        Assert.Equal(0, missingOwner.History.CaptureStatus().EntryCount);

        foreach (var visuals in new[]
                 {
                     Array.Empty<VisualStateSnapshot>(),
                     new[]
                     {
                         NodeVisual(ActivityVisualId, ActivityId, ActivityBounds),
                         NodeVisual(
                             new VisualStateId($"{ActivityVisualId.Value}:duplicate"),
                             ActivityId,
                             new RectD(300d, 80d, 120d, 80d)),
                     },
                 })
        {
            var harness = CreateHarness(Snapshot(
                [Activity(BpmnSemanticTypes.Task, ActivityId)],
                visuals));
            var before = harness.Document.CaptureSnapshot();
            var result = await harness.History.ExecuteAsync(
                harness.Processor,
                Command(harness.Document.Revision));

            Assert.False(result.IsCommitted);
            Assert.Contains(result.Diagnostics, diagnostic =>
                diagnostic.Code ==
                    BpmnCommandDiagnosticCodes.BoundaryEventAttachmentGeometryInvalid);
            Assert.Same(before, harness.Document.CaptureSnapshot());
            Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
        }
    }

    [Fact]
    public async Task ChildScopeCreationDerivesOwnerScopeAndRejectsCrossScopeRequest()
    {
        var initial = ChildActivitySnapshot();
        var accepted = CreateHarness(initial);

        var acceptedResult = await accepted.History.ExecuteAsync(
            accepted.Processor,
            Command(
                accepted.Document.Revision,
                targetScopeId: ChildScopeId));

        Assert.True(acceptedResult.IsCommitted, Diagnostics(acceptedResult.Diagnostics));
        var committed = accepted.Document.CaptureSnapshot();
        Assert.Equal(ChildScopeId, committed.SemanticModel.GetScope(ActivityId).Id);
        Assert.Equal(ChildScopeId, committed.SemanticModel.GetScope(BoundaryId).Id);
        Assert.Contains(committed.SemanticModel.ScopeMemberships, membership =>
            membership.SemanticElementId == BoundaryId &&
            membership.ScopeId == ChildScopeId);
        Assert.Equal(2, committed.SemanticModel.ScopeMemberships.Length);

        var rejected = CreateHarness(initial);
        var rejectedBefore = rejected.Document.CaptureSnapshot();
        var rejectedResult = await rejected.History.ExecuteAsync(
            rejected.Processor,
            Command(
                rejected.Document.Revision,
                targetScopeId: RootScopeId));

        Assert.False(rejectedResult.IsCommitted);
        Assert.Contains(rejectedResult.Diagnostics, diagnostic =>
            diagnostic.Code ==
                BpmnCommandDiagnosticCodes.BoundaryEventAttachmentScopeMismatch);
        Assert.Same(rejectedBefore, rejected.Document.CaptureSnapshot());
        Assert.Equal(0, rejected.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public async Task DerivedNegativeBoundaryGeometryIsRejectedWithoutMutationOrHistory()
    {
        var ownerBounds = new RectD(0d, 0d, 120d, 80d);
        var harness = CreateHarness(Snapshot(
            [Activity(BpmnSemanticTypes.Task, ActivityId)],
            [NodeVisual(ActivityVisualId, ActivityId, ownerBounds)]));
        var before = harness.Document.CaptureSnapshot();

        var result = await harness.History.ExecuteAsync(
            harness.Processor,
            Command(
                harness.Document.Revision,
                new BoundaryAttachmentPlacement(BoundaryAttachmentSide.Top, 0.5d),
                effectiveOwnerBounds: ownerBounds));

        Assert.False(result.IsCommitted);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code ==
                BpmnCommandDiagnosticCodes.BoundaryEventAttachmentGeometryInvalid);
        Assert.Same(before, harness.Document.CaptureSnapshot());
        Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
    }

    [Fact]
    public void PropertiesSchemaIsExactEditableDataAndExcludesStructuralAttachment()
    {
        var schema = Assert.Single(
            BpmnPluginRegistration.N90.PropertiesSchemas,
            candidate => candidate.SemanticTypeId == BpmnSemanticTypes.TimerBoundaryEvent);

        Assert.Equal(
            [
                new ElementPropertyFieldId("name"),
                new ElementPropertyFieldId("timer-definition"),
                new ElementPropertyFieldId("interrupting"),
                new ElementPropertyFieldId("description"),
            ],
            schema.Fields.Select(static field => field.FieldId));
        Assert.Equal(
            ["Name", "Timer definition", "Interrupting", "Description"],
            schema.Fields.Select(static field => field.DisplayName));
        Assert.Equal(
            [
                BpmnSemanticProperties.Name,
                BpmnSemanticProperties.TimerDefinition,
                BpmnSemanticProperties.CancelActivity,
                BpmnSemanticProperties.Description,
            ],
            schema.Fields.Select(static field => field.SemanticPropertyKey));
        Assert.Equal(
            [
                ElementPropertyEditorKind.SingleLineText,
                ElementPropertyEditorKind.MultilineText,
                ElementPropertyEditorKind.Boolean,
                ElementPropertyEditorKind.MultilineText,
            ],
            schema.Fields.Select(static field => field.EditorKind));
        Assert.All(schema.Fields, static field =>
        {
            Assert.True(field.IsEditable);
            Assert.Equal(SemanticPropertyMutationKind.Property, field.MutationKind);
        });
        Assert.Equal([0, 1, 2, 3], schema.Fields.Select(static field => field.Order));
        Assert.DoesNotContain(schema.Fields, field =>
            field.SemanticPropertyKey.Contains("Attached", StringComparison.OrdinalIgnoreCase) ||
            field.DisplayName.Contains("Attached", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BooleanPropertiesDraftProducesTypedApplyAndUndoRedoRestoresExactly()
    {
        var harness = CreateHarness(Snapshot(
            [Activity(BpmnSemanticTypes.UserTask, ActivityId)],
            [NodeVisual(ActivityVisualId, ActivityId, ActivityBounds)]));
        var created = await harness.History.ExecuteAsync(
            harness.Processor,
            Command(harness.Document.Revision));
        Assert.True(created.IsCommitted, Diagnostics(created.Diagnostics));
        var beforeApply = harness.Document.CaptureSnapshot();
        var schemas = new ElementPropertiesSchemaCatalog(
            BpmnPluginRegistration.N90.PropertiesSchemas);
        Assert.True(DocumentCanvasPropertySnapshot.TryCreate(
            beforeApply,
            BoundaryVisualId,
            schemas,
            out var propertySnapshot));
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(
            propertySnapshot);
        Assert.True(authoritative.IsBoundaryAttached);
        Assert.False(authoritative.CanEditBounds);
        Assert.Equal(
            ["Name", "Timer definition", "Interrupting", "Description"],
            authoritative.DataFields.Select(static field =>
                field.Definition.DisplayName));
        var interruptingField = Assert.Single(authoritative.DataFields, field =>
            field.FieldId == new ElementPropertyFieldId("interrupting"));
        Assert.Equal(PropertyValueKind.Boolean, interruptingField.Value?.Kind);
        Assert.True(interruptingField.Value!.BooleanValue);

        var draft = new DocumentCanvasPropertiesDraft(authoritative);
        Assert.True(draft.TryGetDataField(
            new ElementPropertyFieldId("interrupting"),
            out var interruptingDraft));
        var typedDraft = Assert.IsType<DocumentCanvasDataPropertyDraft>(
            interruptingDraft);
        typedDraft.EditorValue = "false";
        Assert.True(draft.TryValidate(out _, out var validationMessages));
        Assert.Empty(validationMessages);
        Assert.True(draft.TryGetDirtyDataField(out var dirtyField));
        Assert.Same(typedDraft, dirtyField);
        Assert.True(typedDraft.TryCreateTargetValue(out var applyTarget));
        Assert.Equal(PropertyValueKind.Boolean, applyTarget?.Kind);
        Assert.False(applyTarget!.BooleanValue);

        var applied = await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                harness.Document.Revision,
                BoundaryId,
                typedDraft.Definition.SemanticPropertyKey,
                applyTarget));

        Assert.True(applied.IsCommitted, Diagnostics(applied.Diagnostics));
        Assert.Equal(2, harness.History.CaptureStatus().EntryCount);
        var afterApply = harness.Document.CaptureSnapshot();
        var appliedBoundary = Assert.Single(afterApply.SemanticModel.Elements, element =>
            element.Id == BoundaryId);
        var appliedValue = appliedBoundary.Properties[
            BpmnSemanticProperties.CancelActivity];
        Assert.Equal(PropertyValueKind.Boolean, appliedValue.Kind);
        Assert.False(appliedValue.BooleanValue);
        Assert.Equal(
            beforeApply.VisualModel.VisualStates.AsEnumerable(),
            afterApply.VisualModel.VisualStates.AsEnumerable());
        Assert.Equal(ActivityId, appliedBoundary.AttachedToElementId);

        var undo = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        AssertAuthoritativeContentEqual(beforeApply, harness.Document.CaptureSnapshot());

        var redo = await harness.History.RedoAsync(harness.Processor);
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        AssertAuthoritativeContentEqual(afterApply, harness.Document.CaptureSnapshot());
    }

    private static CreateBpmnTimerBoundaryEventCommand Command(
        DocumentRevision revision,
        BoundaryAttachmentPlacement? attachment = null,
        RectD? effectiveOwnerBounds = null,
        DocumentScopeId? targetScopeId = null)
    {
        attachment ??= new BoundaryAttachmentPlacement(
            BoundaryAttachmentSide.Bottom,
            0.5d);
        return new CreateBpmnTimerBoundaryEventCommand(
            DocumentId,
            revision,
            BoundaryId,
            BoundaryVisualId,
            ActivityId,
            attachment.Side,
            attachment.PositionOnSide,
            effectiveOwnerBounds ?? ActivityBounds,
            "Review timeout",
            "PT5M",
            cancelActivity: true,
            "Escalate an overdue review.",
            targetScopeId);
    }

    private static SemanticElementSnapshot Activity(
        SemanticTypeId activityType,
        SemanticElementId id) =>
        BpmnTaskSemanticTypes.IsTask(activityType)
            ? BpmnSemanticFactory.CreateTask(
                id,
                activityType,
                "ACTIVITY",
                "Activity",
                1,
                "Activity description.")
            : activityType == BpmnSemanticTypes.SubProcess
                ? BpmnSemanticFactory.CreateSubProcess(
                    id,
                    "ACTIVITY",
                    "Activity",
                    "Activity description.")
                : throw new ArgumentException(
                    $"Semantic type '{activityType}' is not an Activity.",
                    nameof(activityType));

    private static DocumentSnapshot ChildActivitySnapshot() => Snapshot(
        [
            BpmnSemanticFactory.CreateSubProcess(
                ScopeOwnerId,
                "SCOPE_OWNER",
                "Scope owner"),
            Activity(BpmnSemanticTypes.UserTask, ActivityId),
        ],
        [
            NodeVisual(
                new VisualStateId($"{ScopeOwnerId.Value}:visual"),
                ScopeOwnerId,
                new RectD(20d, 20d, 120d, 80d)),
            NodeVisual(ActivityVisualId, ActivityId, ActivityBounds),
        ],
        [new DocumentScopeSnapshot(ChildScopeId, RootScopeId, ScopeOwnerId)],
        [new SemanticElementScopeMembershipSnapshot(ActivityId, ChildScopeId)]);

    private static VisualStateSnapshot NodeVisual(
        VisualStateId visualStateId,
        SemanticElementId semanticElementId,
        RectD bounds) =>
        new(
            visualStateId,
            semanticElementId,
            bounds.TopLeft,
            bounds.Size,
            VisualPlacementMode.Pinned);

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<VisualStateSnapshot> visuals,
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

    private static Harness CreateHarness(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N90;
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
