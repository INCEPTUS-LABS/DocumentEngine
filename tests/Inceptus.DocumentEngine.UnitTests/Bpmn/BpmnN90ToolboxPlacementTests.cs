using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Placement;
using Inceptus.DocumentEngine.Bpmn.Scene;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class BpmnN90ToolboxPlacementTests
{
    private static readonly DocumentId DocumentId = new("bpmn:n9:toolbox-document");
    private static readonly SemanticElementId ActivityId = new("bpmn:n9:activity");
    private static readonly ToolboxItemId TimerBoundaryItemId =
        new("bpmn:toolbox:timer-boundary-event");
    private static readonly RectD ActivityBounds = new(100d, 100d, 120d, 80d);

    [Fact]
    public void N90AddsOneStableTimerBoundaryEventToolAndRegisteredStrategy()
    {
        var previous = BpmnPluginRegistration.N82;
        var current = BpmnPluginRegistration.N90;
        var contribution = Assert.Single(current.ToolboxContributions);
        var eventsGroup = Assert.Single(
            contribution.Groups,
            static group => group.DisplayName == "Events");
        var eventItems = contribution.Items
            .Where(item => item.GroupId == eventsGroup.GroupId)
            .ToArray();

        Assert.Equal(
            [
                "Start Event",
                "Message Catch Event",
                "Message Throw Event",
                "Timer Catch Event",
                "Timer Boundary Event",
                "Signal Catch Event",
                "Signal Throw Event",
                "End Event",
            ],
            eventItems.Select(static item => item.DisplayName));
        Assert.Equal(Enumerable.Range(0, 8), eventItems.Select(static item => item.Order));

        var item = eventItems[4];
        Assert.Equal("bpmn:toolbox:timer-boundary-event", item.ItemId.Value);
        Assert.Equal(BpmnSemanticTypes.TimerBoundaryEvent, item.ElementTypeId);
        Assert.Equal(eventsGroup.GroupId, item.GroupId);
        Assert.Equal("Timer Boundary Event", item.DisplayName);
        Assert.Equal(4, item.Order);
        Assert.Equal("bpmn:timer-boundary-event", item.Icon.IconKey);
        Assert.Equal("▭◎◷", item.Icon.FallbackGlyph);

        Assert.Equal(
            previous.ToolboxPlacementRegistrations.Select(static value => value.ToolboxItemId),
            current.ToolboxPlacementRegistrations
                .Take(previous.ToolboxPlacementRegistrations.Length)
                .Select(static value => value.ToolboxItemId));
        Assert.Equal(
            previous.ToolboxPlacementRegistrations.Length + 1,
            current.ToolboxPlacementRegistrations.Length);
        var registration = TimerBoundaryRegistration();
        Assert.Equal(
            "BpmnTimerBoundaryEventPlacementCandidateProvider",
            registration.CandidateProvider?.GetType().Name);
        Assert.Equal(
            "BpmnTimerBoundaryEventToolboxPlacementCommandFactory",
            registration.CommandFactory.GetType().Name);
    }

    [Fact]
    public void CandidateProviderUsesInclusiveToleranceAndRejectsJustBeyondIt()
    {
        var document = Document([Task(ActivityId)]);
        var target = Target(ActivityId, BpmnSemanticTypes.Task, ActivityBounds);

        var atTolerance = ResolveCandidate(
            document,
            new PointD(ActivityBounds.Right + 18d, 140d),
            [target]);
        var beyondTolerance = ResolveCandidate(
            document,
            new PointD(ActivityBounds.Right + 18.001d, 140d),
            [target]);

        Assert.NotNull(atTolerance);
        Assert.Equal(target, atTolerance.Target);
        Assert.Equal(new RectD(202d, 122d, 36d, 36d), atTolerance.PreviewBounds);
        Assert.Equal(
            (long)BoundaryAttachmentSide.Right,
            atTolerance.Properties[AttachmentSideProperty].IntegerValue);
        Assert.Equal(0.5d, atTolerance.Properties[PositionOnSideProperty].NumberValue);
        Assert.Null(beyondTolerance);
    }

    [Fact]
    public void CandidateProviderResolvesCornerTiesToTopWithStablePreviewProperties()
    {
        var document = Document([Task(ActivityId)]);
        var target = Target(ActivityId, BpmnSemanticTypes.Task, ActivityBounds);

        var candidate = Assert.IsType<ToolboxPlacementCandidate>(ResolveCandidate(
            document,
            ActivityBounds.TopLeft,
            [target]));

        Assert.Equal(
            BpmnTimerBoundaryEventSceneFeedback.AttachmentCandidateKind,
            candidate.FeedbackKind);
        Assert.Equal(new RectD(82d, 82d, 36d, 36d), candidate.PreviewBounds);
        Assert.Equal(
            (long)BoundaryAttachmentSide.Top,
            candidate.Properties[AttachmentSideProperty].IntegerValue);
        Assert.Equal(0d, candidate.Properties[PositionOnSideProperty].NumberValue);
        Assert.True(candidate.Properties[BpmnSemanticProperties.CancelActivity].BooleanValue);
    }

    [Fact]
    public void CandidateProviderUsesOnlyActivitiesInTheRequestedActiveScope()
    {
        var rootActivityId = new SemanticElementId("bpmn:n9:root-activity");
        var childActivityId = new SemanticElementId("bpmn:n9:child-activity");
        var scopeOwnerId = new SemanticElementId("bpmn:n9:scope-owner");
        var childScopeId = new DocumentScopeId("bpmn:n9:child-scope");
        var document = Document(
            [
                Task(rootActivityId),
                Task(childActivityId),
                new SemanticElementSnapshot(
                    scopeOwnerId,
                    BpmnSemanticTypes.SubProcess),
            ],
            nestedScopes:
            [
                new DocumentScopeSnapshot(
                    childScopeId,
                    new DocumentScopeId(DocumentId.Value),
                    scopeOwnerId),
            ],
            memberships:
            [
                new SemanticElementScopeMembershipSnapshot(
                    childActivityId,
                    childScopeId),
            ]);
        var root = Target(
            rootActivityId,
            BpmnSemanticTypes.Task,
            ActivityBounds,
            "a-root");
        var child = Target(
            childActivityId,
            BpmnSemanticTypes.Task,
            ActivityBounds,
            "z-child");

        var candidate = Assert.IsType<ToolboxPlacementCandidate>(ResolveCandidate(
            document,
            new PointD(160d, ActivityBounds.Bottom),
            [root, child],
            childScopeId));

        Assert.Equal(child, candidate.Target);
        Assert.Equal(childActivityId, candidate.Target.SemanticElementId);
        Assert.Equal(
            (long)BoundaryAttachmentSide.Bottom,
            candidate.Properties[AttachmentSideProperty].IntegerValue);
    }

    [Fact]
    public void CandidateProviderAcceptsEveryActivityFamilyOnAPositiveDocumentBoundary()
    {
        foreach (var activityType in BpmnActivitySemanticTypes.All)
        {
            var document = Document(
                [new SemanticElementSnapshot(ActivityId, activityType)]);
            var target = Target(ActivityId, activityType, ActivityBounds);

            var candidate = Assert.IsType<ToolboxPlacementCandidate>(ResolveCandidate(
                document,
                new PointD(ActivityBounds.Right, 140d),
                [target]));

            Assert.Equal(target, candidate.Target);
            Assert.True(DocumentGeometryBoundary.Contains(candidate.PreviewBounds));
            Assert.Equal(
                (long)BoundaryAttachmentSide.Right,
                candidate.Properties[AttachmentSideProperty].IntegerValue);
            Assert.Equal(0.5d, candidate.Properties[PositionOnSideProperty].NumberValue);
        }
    }

    [Fact]
    public void CandidateProviderRejectsMissingNonActivityAndForgedOwners()
    {
        var startId = new SemanticElementId("bpmn:n9:start");
        var missingId = new SemanticElementId("bpmn:n9:missing");
        var document = Document(
            [new SemanticElementSnapshot(startId, BpmnSemanticTypes.StartEvent)]);
        var point = new PointD(ActivityBounds.Right, 140d);

        Assert.Null(ResolveCandidate(
            document,
            point,
            [Target(missingId, BpmnSemanticTypes.Task, ActivityBounds)]));
        Assert.Null(ResolveCandidate(
            document,
            point,
            [Target(startId, BpmnSemanticTypes.StartEvent, ActivityBounds)]));
        Assert.Null(ResolveCandidate(
            document,
            point,
            [Target(startId, BpmnSemanticTypes.Task, ActivityBounds)]));
    }

    [Fact]
    public void FactoryCreatesExactTimerBoundaryCommandFromResolvedCandidate()
    {
        var existingBoundary = BpmnSemanticFactory.CreateTimerBoundaryEvent(
            new SemanticElementId("bpmn:n9:existing-boundary"),
            ActivityId,
            "Existing timer");
        var document = Document([Task(ActivityId), existingBoundary]);
        var target = Target(ActivityId, BpmnSemanticTypes.Task, ActivityBounds);
        var candidate = Assert.IsType<ToolboxPlacementCandidate>(ResolveCandidate(
            document,
            new PointD(160d, ActivityBounds.Bottom),
            [target]));
        var identity = new DocumentCreationIdentity(
            new SemanticElementId("bpmn:n9:created-boundary"),
            new VisualStateId("bpmn:n9:created-boundary:visual"));
        var identities = new RecordingIdentityProvider(identity);
        var registration = TimerBoundaryRegistration();

        var result = registration.CommandFactory.CreatePlan(Request(
            document,
            new PointD(160d, ActivityBounds.Bottom),
            [target],
            identities,
            candidate: candidate));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Diagnostics);
        var plan = Assert.IsType<ToolboxPlacementPlan>(result.Plan);
        Assert.Equal(identity.SemanticElementId, plan.CreatedSemanticElementId);
        Assert.Equal(identity.VisualStateId, plan.CreatedVisualStateId);
        Assert.Equal(1, identities.CallCount);
        var command = Assert.IsType<CreateBpmnTimerBoundaryEventCommand>(plan.Command);
        Assert.Equal(DocumentId, command.TargetDocumentId);
        Assert.Equal(document.Revision, command.ExpectedRevision);
        Assert.Equal(CommandCategory.Document, command.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.SemanticModel |
                AuthoritativeDocumentComponent.VisualModel,
            command.AffectedComponents);
        Assert.Equal(identity.SemanticElementId, command.ElementId);
        Assert.Equal(identity.VisualStateId, command.VisualStateId);
        Assert.Equal(ActivityId, command.AttachedToActivityId);
        Assert.Equal(BoundaryAttachmentSide.Bottom, command.Side);
        Assert.Equal(0.5d, command.PositionOnSide);
        Assert.Equal(ActivityBounds, command.EffectiveOwnerBounds);
        Assert.Equal("Timer Boundary Event 2", command.Name);
        Assert.Null(command.TimerDefinition);
        Assert.True(command.CancelActivity);
        Assert.Equal("Timer Boundary Event created from the Toolbox.", command.Description);
        Assert.Equal(document.SemanticModel.RootScopeId, command.TargetScopeId);
    }

    [Fact]
    public void FactoryRejectsWrongToolMissingCandidateAndMalformedCandidateWithoutIdentityUse()
    {
        var document = Document([Task(ActivityId)]);
        var target = Target(ActivityId, BpmnSemanticTypes.Task, ActivityBounds);
        var identities = new RecordingIdentityProvider(new DocumentCreationIdentity(
            new SemanticElementId("bpmn:n9:unused-boundary"),
            new VisualStateId("bpmn:n9:unused-boundary:visual")));
        var factory = TimerBoundaryRegistration().CommandFactory;

        var wrongItem = factory.CreatePlan(new ToolboxPlacementRequest(
            new ToolboxItemId("bpmn:toolbox:task"),
            document,
            document.Revision,
            new PointD(160d, ActivityBounds.Bottom),
            identities,
            visibleTargets: [target]));
        var missing = factory.CreatePlan(Request(
            document,
            new PointD(160d, ActivityBounds.Bottom),
            [target],
            identities));
        var malformedCandidate = new ToolboxPlacementCandidate(
            target,
            BpmnTimerBoundaryEventSceneFeedback.AttachmentCandidateKind,
            new RectD(142d, 162d, 36d, 36d));
        var malformed = factory.CreatePlan(Request(
            document,
            new PointD(160d, ActivityBounds.Bottom),
            [target],
            identities,
            candidate: malformedCandidate));

        AssertFailure(
            wrongItem,
            BpmnToolboxPlacementDiagnosticCodes.ToolboxItemMismatch);
        AssertFailure(
            missing,
            BpmnToolboxPlacementDiagnosticCodes.AttachmentCandidateRequired);
        AssertFailure(
            malformed,
            BpmnToolboxPlacementDiagnosticCodes.AttachmentCandidateInvalid);
        Assert.Equal(0, identities.CallCount);
    }

    private static ToolboxPlacementCandidate? ResolveCandidate(
        DocumentSnapshot document,
        PointD point,
        IEnumerable<ToolboxPlacementTarget> targets,
        DocumentScopeId? targetScopeId = null) =>
        TimerBoundaryRegistration().CandidateProvider!.ResolveCandidate(Request(
            document,
            point,
            targets,
            new RecordingIdentityProvider(new DocumentCreationIdentity(
                new SemanticElementId("bpmn:n9:preview-identity"),
                new VisualStateId("bpmn:n9:preview-identity:visual"))),
            targetScopeId));

    private static ToolboxPlacementRequest Request(
        DocumentSnapshot document,
        PointD point,
        IEnumerable<ToolboxPlacementTarget> targets,
        IDocumentCreationIdentityProvider identityProvider,
        DocumentScopeId? targetScopeId = null,
        ToolboxPlacementCandidate? candidate = null) =>
        new(
            TimerBoundaryItemId,
            document,
            document.Revision,
            point,
            identityProvider,
            targetScopeId,
            targets,
            candidate);

    private static ToolboxPlacementRegistration TimerBoundaryRegistration() =>
        Assert.Single(
            BpmnPluginRegistration.N90.ToolboxPlacementRegistrations,
            static registration => registration.ToolboxItemId ==
                TimerBoundaryItemId);

    private static ToolboxPlacementTarget Target(
        SemanticElementId semanticElementId,
        SemanticTypeId semanticTypeId,
        RectD bounds,
        string suffix = "activity") =>
        new(
            semanticElementId,
            semanticTypeId,
            new VisualStateId($"bpmn:n9:{suffix}:visual"),
            new ProjectedObjectId($"bpmn:n9:{suffix}:projected"),
            bounds);

    private static SemanticElementSnapshot Task(SemanticElementId id) =>
        BpmnSemanticFactory.CreateTask(id, "TASK", "Task", 1L);

    private static DocumentSnapshot Document(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                elements,
                nestedScopes: nestedScopes,
                scopeMemberships: memberships),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

    private static void AssertFailure(
        ToolboxPlacementPlanResult result,
        string expectedCode)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        Assert.Equal(expectedCode, Assert.Single(result.Diagnostics).Code);
    }

    private sealed class RecordingIdentityProvider(DocumentCreationIdentity identity) :
        IDocumentCreationIdentityProvider
    {
        internal int CallCount { get; private set; }

        public DocumentCreationIdentity CreateIdentity()
        {
            CallCount++;
            return identity;
        }
    }

    private const string AttachmentSideProperty =
        "bpmn:toolbox:boundary-attachment-side";
    private const string PositionOnSideProperty =
        "bpmn:toolbox:boundary-position-on-side";
}
