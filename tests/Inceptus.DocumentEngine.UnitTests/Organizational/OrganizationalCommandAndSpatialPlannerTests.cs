using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.ContextMenus;
using Inceptus.DocumentEngine.Organizational.Deletion;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Properties;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Organizational;

public sealed class OrganizationalCommandAndSpatialPlannerTests
{
    private static readonly DocumentId DocumentId =
        new("test:organizational:document");
    private static readonly SemanticElementId TaskAId =
        new("test:organizational:task-a");
    private static readonly SemanticElementId TaskBId =
        new("test:organizational:task-b");
    private static readonly SemanticElementId NestedTaskId =
        new("test:organizational:nested-task");
    private static readonly SemanticElementId FlowId =
        new("test:organizational:flow");
    private static readonly SemanticElementId PoolAId =
        new("test:organizational:pool-a");
    private static readonly SemanticElementId PoolBId =
        new("test:organizational:pool-b");
    private static readonly VisualStateId TaskAVisualId =
        new("test:organizational:task-a:visual");
    private static readonly VisualStateId TaskBVisualId =
        new("test:organizational:task-b:visual");
    private static readonly VisualStateId NestedTaskVisualId =
        new("test:organizational:nested-task:visual");
    private static readonly DocumentScopeId NestedScopeId =
        new("test:organizational:nested-scope");

    [Fact]
    public void PoolProjectionIsExplicitlyConsumedWithoutCreatingAGraphNode()
    {
        var snapshot = Snapshot(
            [Task(TaskAId), Pool(PoolAId)],
            assignments: [Assignment(TaskAId, PoolAId)],
            visuals: [Visual(TaskAVisualId, TaskAId, 10d)],
            presentations: [Presentation(PoolAId, 0)]);
        var bpmn = BpmnPluginRegistration.N100;
        var organizational = Registration();

        var result = new ProjectionEngine(
            bpmn.ProjectionRules.Concat(organizational.ProjectionRules))
            .Project(snapshot);

        Assert.True(result.IsSuccessful, Diagnostics(result.Diagnostics));
        Assert.NotNull(result.Graph);
        Assert.Single(result.Graph.Nodes);
        Assert.Equal(TaskAId, result.Graph.Nodes[0].Source.SemanticElementId);
        Assert.DoesNotContain(
            result.Diagnostics,
            diagnostic => diagnostic.Code ==
                ProjectionDiagnosticCodes.UnsupportedSemanticType);
    }

    [Fact]
    public async Task FirstPoolAdoptsExactScopeAndSecondPoolIsEmptyWithExactUndo()
    {
        var initial = Snapshot(
            [Task(TaskAId), Task(TaskBId), Task(NestedTaskId)],
            [BpmnSemanticFactory.CreateSequenceFlow(FlowId, TaskAId, TaskBId)],
            [new DocumentScopeSnapshot(NestedScopeId)],
            [new SemanticElementScopeMembershipSnapshot(NestedTaskId, NestedScopeId)],
            visuals: [
                Visual(TaskAVisualId, TaskAId, 10d),
                Visual(TaskBVisualId, TaskBId, 180d),
                Visual(NestedTaskVisualId, NestedTaskId, 350d),
            ]);
        var harness = CreateHarness(initial);
        var originalVisuals = harness.Document.VisualModel.VisualStates;

        var first = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateOrganizationalPoolCommand(
                DocumentId,
                harness.Document.Revision,
                PoolAId,
                harness.Document.SemanticModel.RootScopeId,
                OrganizationalPoolCreationMode.AdoptEligibleUnassigned));

        Assert.True(first.IsCommitted, Diagnostics(first.Diagnostics));
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
        Assert.Equal(
            [TaskAId, TaskBId],
            AssignedIds(harness.Document.CaptureSnapshot(), PoolAId));
        Assert.DoesNotContain(
            harness.Document.SemanticModel.ProfileAssignments,
            assignment => assignment.SemanticElementId == NestedTaskId);
        Assert.True(originalVisuals.AsSpan().SequenceEqual(
            harness.Document.VisualModel.VisualStates.AsSpan()));
        Assert.DoesNotContain(
            harness.Document.VisualModel.VisualStates,
            visual => visual.SemanticElementId == PoolAId);
        Assert.Equal(
            0,
            Presentation(harness.Document.CaptureSnapshot(), PoolAId).Order);
        Assert.Contains(
            harness.Document.SemanticModel.Relationships,
            relationship => relationship.Id == FlowId);

        var beforeSecond = harness.Document.CaptureSnapshot();
        var second = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateOrganizationalPoolCommand(
                DocumentId,
                harness.Document.Revision,
                PoolBId,
                harness.Document.SemanticModel.RootScopeId,
                OrganizationalPoolCreationMode.Empty));

        Assert.True(second.IsCommitted, Diagnostics(second.Diagnostics));
        Assert.Equal(2, harness.History.CaptureStatus().EntryCount);
        Assert.Empty(AssignedIds(harness.Document.CaptureSnapshot(), PoolBId));
        Assert.Equal(0, Presentation(harness.Document.CaptureSnapshot(), PoolAId).Order);
        Assert.Equal(1, Presentation(harness.Document.CaptureSnapshot(), PoolBId).Order);
        Assert.All(
            harness.Document.SemanticModel.ProfileAssignments,
            assignment => Assert.Equal(PoolAId, assignment.ContainerSemanticElementId));

        var invalidAdoption = await harness.History.ExecuteAsync(
            harness.Processor,
            new CreateOrganizationalPoolCommand(
                DocumentId,
                harness.Document.Revision,
                new SemanticElementId("test:organizational:pool-invalid"),
                harness.Document.SemanticModel.RootScopeId,
                OrganizationalPoolCreationMode.AdoptEligibleUnassigned));
        Assert.False(invalidAdoption.IsCommitted);
        Assert.Equal(2, harness.History.CaptureStatus().EntryCount);

        var undo = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        AssertEquivalentIgnoringRevision(beforeSecond, harness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task AssignmentUnassignmentAndReorderAreScopeSafeAndExactlyUndoable()
    {
        var initial = Snapshot(
            [Task(TaskAId), Task(TaskBId), Pool(PoolAId), Pool(PoolBId)],
            assignments: [Assignment(TaskAId, PoolAId)],
            visuals: [Visual(TaskAVisualId, TaskAId, 10d), Visual(TaskBVisualId, TaskBId, 180d)],
            presentations: [Presentation(PoolAId, 0), Presentation(PoolBId, 1)]);
        var harness = CreateHarness(initial);

        var assigned = await harness.History.ExecuteAsync(
            harness.Processor,
            new AssignOrganizationalElementCommand(
                DocumentId,
                harness.Document.Revision,
                TaskAId,
                PoolBId));
        Assert.True(assigned.IsCommitted, Diagnostics(assigned.Diagnostics));
        Assert.Equal(PoolBId, AssignedPool(harness.Document.CaptureSnapshot(), TaskAId));

        var undoAssignment = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undoAssignment.IsCommitted, Diagnostics(undoAssignment.Diagnostics));
        AssertEquivalentIgnoringRevision(initial, harness.Document.CaptureSnapshot());

        var unassigned = await harness.History.ExecuteAsync(
            harness.Processor,
            new UnassignOrganizationalElementCommand(
                DocumentId,
                harness.Document.Revision,
                TaskAId));
        Assert.True(unassigned.IsCommitted, Diagnostics(unassigned.Diagnostics));
        Assert.Null(AssignedPool(harness.Document.CaptureSnapshot(), TaskAId));
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);

        var beforeMove = harness.Document.CaptureSnapshot();
        var moved = await harness.History.ExecuteAsync(
            harness.Processor,
            new MoveOrganizationalPoolCommand(
                DocumentId,
                harness.Document.Revision,
                PoolBId,
                OrganizationalPoolMoveDirection.Up));
        Assert.True(moved.IsCommitted, Diagnostics(moved.Diagnostics));
        Assert.True(beforeMove.SemanticModel.Elements.AsSpan().SequenceEqual(
            harness.Document.SemanticModel.Elements.AsSpan()));
        Assert.True(beforeMove.VisualModel.VisualStates.AsSpan().SequenceEqual(
            harness.Document.VisualModel.VisualStates.AsSpan()));
        Assert.Equal(0, Presentation(harness.Document.CaptureSnapshot(), PoolBId).Order);
        Assert.Equal(1, Presentation(harness.Document.CaptureSnapshot(), PoolAId).Order);
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        AssertEquivalentIgnoringRevision(beforeMove, harness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task DeletePoolClearsReferencesAndOrderButPreservesProcessAndRestoresExactly()
    {
        var initial = Snapshot(
            [Task(TaskAId), Task(TaskBId), Pool(PoolAId), Pool(PoolBId)],
            [BpmnSemanticFactory.CreateSequenceFlow(FlowId, TaskAId, TaskBId)],
            assignments: [Assignment(TaskAId, PoolAId), Assignment(TaskBId, PoolAId)],
            visuals: [Visual(TaskAVisualId, TaskAId, 10d), Visual(TaskBVisualId, TaskBId, 180d)],
            presentations: [Presentation(PoolAId, 0), Presentation(PoolBId, 1)]);
        var harness = CreateHarness(initial);
        var registration = Registration();
        var request = new DiagramDeletionRequest(
            harness.Document.CaptureSnapshot(),
            harness.Document.Revision,
            DiagramDeletionTargetKind.Element,
            PoolAId,
            visualStateId: null);
        var deletionRegistration = Assert.Single(
            new DiagramDeletionCatalog(registration.DiagramDeletionRegistrations)
                .GetMatchingRegistrations(request));
        Assert.Equal(
            OrganizationalPoolDeletionContribution.DeletionId,
            deletionRegistration.DeletionId);
        var plan = deletionRegistration.CommandFactory.CreatePlan(request);
        var command = Assert.IsType<DeleteOrganizationalPoolCommand>(plan.Plan!.Command);

        var deleted = await harness.History.ExecuteAsync(harness.Processor, command);

        Assert.True(deleted.IsCommitted, Diagnostics(deleted.Diagnostics));
        Assert.False(harness.Document.SemanticModel.TryGetElement(PoolAId, out _));
        Assert.True(harness.Document.SemanticModel.TryGetElement(TaskAId, out _));
        Assert.True(harness.Document.SemanticModel.TryGetElement(TaskBId, out _));
        Assert.Contains(
            harness.Document.SemanticModel.Relationships,
            relationship => relationship.Id == FlowId);
        Assert.True(initial.VisualModel.VisualStates.AsSpan().SequenceEqual(
            harness.Document.VisualModel.VisualStates.AsSpan()));
        Assert.Empty(harness.Document.SemanticModel.ProfileAssignments);
        Assert.DoesNotContain(
            harness.Document.VisualModel.ProfileElementPresentations,
            presentation => presentation.SemanticElementId == PoolAId);
        Assert.Equal(1, Presentation(harness.Document.CaptureSnapshot(), PoolBId).Order);

        var undo = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        AssertEquivalentIgnoringRevision(initial, harness.Document.CaptureSnapshot());
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.False(harness.Document.SemanticModel.TryGetElement(PoolAId, out _));
    }

    [Fact]
    public async Task GenericPoolPropertiesPreserveAssignmentAndPresentationAuthorities()
    {
        var initial = Snapshot(
            [Task(TaskAId), Pool(PoolAId)],
            assignments: [Assignment(TaskAId, PoolAId)],
            visuals: [Visual(TaskAVisualId, TaskAId, 10d)],
            presentations: [Presentation(PoolAId, 0)]);
        var harness = CreateHarness(initial);
        Assert.Equal(
            ["Name", "Description"],
            OrganizationalPoolPropertiesSchema.Definition.Fields.Select(
                static field => field.DisplayName));

        var renamed = await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateSemanticElementNameCommand(
                DocumentId,
                harness.Document.Revision,
                PoolAId,
                OrganizationalSemanticProperties.Name,
                "Operations"));
        Assert.True(renamed.IsCommitted, Diagnostics(renamed.Diagnostics));
        var described = await harness.History.ExecuteAsync(
            harness.Processor,
            new UpdateSemanticElementPropertyCommand(
                DocumentId,
                harness.Document.Revision,
                PoolAId,
                OrganizationalSemanticProperties.Description,
                PropertyValue.FromText("Primary operating pool")));
        Assert.True(described.IsCommitted, Diagnostics(described.Diagnostics));
        Assert.Equal(
            PoolAId,
            AssignedPool(harness.Document.CaptureSnapshot(), TaskAId));
        Assert.Equal(0, Presentation(harness.Document.CaptureSnapshot(), PoolAId).Order);
        Assert.Equal(
            "Operations",
            harness.Document.SemanticModel.Elements.Single(element =>
                element.Id == PoolAId).Properties[
                    OrganizationalSemanticProperties.Name].TextValue);
    }

    [Fact]
    public async Task SpatialCreationAndMoveComposeAssignmentWithBaseEditInOneHistoryEntry()
    {
        var initial = Snapshot(
            [Task(TaskAId), Pool(PoolAId), Pool(PoolBId)],
            assignments: [Assignment(TaskAId, PoolAId)],
            visuals: [Visual(TaskAVisualId, TaskAId, 10d)],
            presentations: [Presentation(PoolAId, 0), Presentation(PoolBId, 1)]);
        var harness = CreateHarness(initial);
        var planner = Assert.Single(Registration().SpatialEditPlannerRegistrations).Planner;
        var poolBRegion = Region(PoolBId);
        var movePlan = planner.Plan(new Canvas2DSpatialEditRequest(
            harness.Document.CaptureSnapshot(),
            harness.Document.SemanticModel.RootScopeId,
            Canvas2DSpatialEditKind.Move,
            new MoveVisualStateCommand(
                DocumentId,
                harness.Document.Revision,
                TaskAVisualId,
                new PointD(250d, 80d)),
            TaskAId,
            poolBRegion));
        var moveCompound = Assert.IsType<CompoundDocumentCommand>(movePlan.Command);
        Assert.IsType<AssignOrganizationalElementCommand>(moveCompound.Commands[1]);

        var moved = await harness.History.ExecuteAsync(
            harness.Processor,
            moveCompound);
        Assert.True(moved.IsCommitted, Diagnostics(moved.Diagnostics));
        Assert.Equal(new PointD(250d, 80d), harness.Document.VisualModel.VisualStates
            .Single(visual => visual.Id == TaskAVisualId).Position);
        Assert.Equal(PoolBId, AssignedPool(harness.Document.CaptureSnapshot(), TaskAId));
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        AssertEquivalentIgnoringRevision(initial, harness.Document.CaptureSnapshot());

        var newTaskId = new SemanticElementId("test:organizational:created-task");
        var newVisualId = new VisualStateId("test:organizational:created-task:visual");
        var creationPlan = planner.Plan(new Canvas2DSpatialEditRequest(
            harness.Document.CaptureSnapshot(),
            harness.Document.SemanticModel.RootScopeId,
            Canvas2DSpatialEditKind.Creation,
            new CreateBpmnTaskCommand(
                DocumentId,
                harness.Document.Revision,
                newTaskId,
                newVisualId,
                new PointD(30d, 30d),
                new SizeD(120d, 80d),
                "NEW_TASK",
                "New task",
                20),
            newTaskId,
            poolBRegion));
        var creationCompound = Assert.IsType<CompoundDocumentCommand>(creationPlan.Command);
        var conditionalAssignment = Assert.IsType<AssignOrganizationalElementCommand>(
            creationCompound.Commands[1]);
        Assert.True(conditionalAssignment.OnlyIfEligible);

        var created = await harness.History.ExecuteAsync(
            harness.Processor,
            creationCompound);
        Assert.True(created.IsCommitted, Diagnostics(created.Diagnostics));
        Assert.Equal(PoolBId, AssignedPool(harness.Document.CaptureSnapshot(), newTaskId));
        Assert.True(harness.Document.VisualModel.TryGetVisualState(newVisualId, out _));
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.False(harness.Document.SemanticModel.TryGetElement(newTaskId, out _));
        Assert.False(harness.Document.VisualModel.TryGetVisualState(newVisualId, out _));
    }

    [Fact]
    public void SpatialMoveRejectsMissingAndOffScopeSources()
    {
        var initial = Snapshot(
            [Task(TaskAId), Task(NestedTaskId), Pool(PoolAId)],
            nestedScopes: [new DocumentScopeSnapshot(NestedScopeId)],
            memberships: [
                new SemanticElementScopeMembershipSnapshot(NestedTaskId, NestedScopeId),
            ],
            visuals: [
                Visual(TaskAVisualId, TaskAId, 10d),
                Visual(NestedTaskVisualId, NestedTaskId, 180d),
            ],
            presentations: [Presentation(PoolAId, 0)]);
        var harness = CreateHarness(initial);
        var planner = Assert.Single(Registration().SpatialEditPlannerRegistrations).Planner;
        var unassignedRegion = Region(poolId: null);
        var missing = planner.Plan(new Canvas2DSpatialEditRequest(
            harness.Document.CaptureSnapshot(),
            harness.Document.SemanticModel.RootScopeId,
            Canvas2DSpatialEditKind.Move,
            new MoveVisualStateCommand(
                DocumentId,
                harness.Document.Revision,
                TaskAVisualId,
                new PointD(20d, 20d)),
            new SemanticElementId("test:organizational:missing"),
            unassignedRegion));
        Assert.False(missing.Succeeded);

        var offScope = planner.Plan(new Canvas2DSpatialEditRequest(
            harness.Document.CaptureSnapshot(),
            harness.Document.SemanticModel.RootScopeId,
            Canvas2DSpatialEditKind.Move,
            new MoveVisualStateCommand(
                DocumentId,
                harness.Document.Revision,
                NestedTaskVisualId,
                new PointD(220d, 20d)),
            NestedTaskId,
            unassignedRegion));
        Assert.False(offScope.Succeeded);
        Assert.All(
            missing.Diagnostics.Concat(offScope.Diagnostics),
            diagnostic => Assert.Equal(
                "ORGANIZATIONAL_SPATIAL_EDIT_INVALID",
                diagnostic.Code));
    }

    [Fact]
    public void BackgroundActionPlansExplicitFirstAdoptionThenEmptyCreation()
    {
        var initial = Snapshot([Task(TaskAId)], visuals: [Visual(TaskAVisualId, TaskAId, 10d)]);
        var provider = new StubIdentityProvider(PoolAId);
        var request = new Inceptus.DocumentEngine.Contracts.ContextMenus.CanvasBackgroundActionRequest(
            initial,
            initial.SemanticModel.RootScopeId,
            provider);
        var first = Assert.IsType<CreateOrganizationalPoolCommand>(
            OrganizationalCanvasBackgroundActions.AddPool.CreatePlan(request).Command);
        Assert.Equal(OrganizationalPoolCreationMode.AdoptEligibleUnassigned, first.CreationMode);
        Assert.Equal(initial.SemanticModel.RootScopeId, first.TargetScopeId);
        Assert.Equal(PoolAId, first.PoolId);

        var withPool = Snapshot(
            [Task(TaskAId), Pool(PoolAId)],
            assignments: [Assignment(TaskAId, PoolAId)],
            visuals: [Visual(TaskAVisualId, TaskAId, 10d)],
            presentations: [Presentation(PoolAId, 0)]);
        var second = Assert.IsType<CreateOrganizationalPoolCommand>(
            OrganizationalCanvasBackgroundActions.AddPool.CreatePlan(
                new Inceptus.DocumentEngine.Contracts.ContextMenus.CanvasBackgroundActionRequest(
                    withPool,
                    withPool.SemanticModel.RootScopeId,
                    new StubIdentityProvider(PoolBId))).Command);
        Assert.Equal(OrganizationalPoolCreationMode.Empty, second.CreationMode);
    }

    private static Harness CreateHarness(DocumentSnapshot snapshot)
    {
        var construction = DocumentFactory.Create(snapshot);
        Assert.True(construction.Succeeded, Diagnostics(construction.Diagnostics));
        var document = construction.Document!;
        var bpmn = BpmnPluginRegistration.N100;
        var organizational = Registration();
        return new Harness(
            document,
            new CommandProcessor(
                bpmn.CommandHandlers.Concat(organizational.CommandHandlers),
                bpmn.CommandValidators.Concat(organizational.CommandValidators),
                historyPolicies:
                    bpmn.HistoryPolicies.Concat(organizational.HistoryPolicies)),
            new HistoryManager(document));
    }

    private static OrganizationalPluginRegistration Registration() =>
        OrganizationalPluginRegistration.Create(
            new OrganizationalElementEligibilityPolicy(
                BpmnSemanticTypes.IsFlowNode));

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot>? relationships = null,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null,
        IEnumerable<ModelProfileElementAssignmentSnapshot>? assignments = null,
        IEnumerable<VisualStateSnapshot>? visuals = null,
        IEnumerable<ModelProfileElementPresentationSnapshot>? presentations = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                elements,
                relationships,
                nestedScopes,
                memberships,
                new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]),
                assignments),
            new VisualModelSnapshot(
                DocumentId,
                DocumentRevision.Zero,
                visuals,
                presentations),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

    private static SemanticElementSnapshot Task(SemanticElementId id) =>
        BpmnSemanticFactory.CreateTask(id, id.Value, id.Value, elementNumber: 1);

    private static SemanticElementSnapshot Pool(SemanticElementId id) =>
        OrganizationalSemanticFactory.CreatePool(id, id.Value);

    private static VisualStateSnapshot Visual(
        VisualStateId visualId,
        SemanticElementId semanticId,
        double x) =>
        new(
            visualId,
            semanticId,
            new PointD(x, 10d),
            new SizeD(120d, 80d),
            VisualPlacementMode.Manual);

    private static ModelProfileElementAssignmentSnapshot Assignment(
        SemanticElementId semanticId,
        SemanticElementId poolId) =>
        new(OrganizationalModelProfile.Id, semanticId, poolId);

    private static ModelProfileElementPresentationSnapshot Presentation(
        SemanticElementId poolId,
        int order) =>
        new(OrganizationalModelProfile.Id, poolId, order);

    private static ModelProfileElementPresentationSnapshot Presentation(
        DocumentSnapshot document,
        SemanticElementId poolId) =>
        document.VisualModel.ProfileElementPresentations.Single(candidate =>
            candidate.ProfileId == OrganizationalModelProfile.Id &&
            candidate.SemanticElementId == poolId);

    private static SemanticElementId[] AssignedIds(
        DocumentSnapshot document,
        SemanticElementId poolId) =>
        document.SemanticModel.ProfileAssignments
            .Where(assignment =>
                assignment.ProfileId == OrganizationalModelProfile.Id &&
                assignment.ContainerSemanticElementId == poolId)
            .Select(static assignment => assignment.SemanticElementId)
            .OrderBy(static id => id.Value, StringComparer.Ordinal)
            .ToArray();

    private static SemanticElementId? AssignedPool(
        DocumentSnapshot document,
        SemanticElementId semanticId) =>
        document.SemanticModel.ProfileAssignments.SingleOrDefault(assignment =>
            assignment.ProfileId == OrganizationalModelProfile.Id &&
            assignment.SemanticElementId == semanticId)?.ContainerSemanticElementId;

    private static Canvas2DSpatialRegion Region(SemanticElementId? poolId) =>
        new(
            new Canvas2DSpatialRegionId(
                poolId is null
                    ? "test:organizational:region:unassigned"
                    : $"test:organizational:region:{poolId.Value}"),
            OrganizationalModelProfile.Id,
            poolId,
            Matrix2D.Identity,
            new RectD(0d, 0d, 800d, 600d));

    private static void AssertEquivalentIgnoringRevision(
        DocumentSnapshot expected,
        DocumentSnapshot actual)
    {
        Assert.Equal(expected.DocumentId, actual.DocumentId);
        Assert.True(expected.SemanticModel.Elements.AsSpan().SequenceEqual(
            actual.SemanticModel.Elements.AsSpan()));
        Assert.True(expected.SemanticModel.Relationships.AsSpan().SequenceEqual(
            actual.SemanticModel.Relationships.AsSpan()));
        Assert.True(expected.SemanticModel.NestedScopes.AsSpan().SequenceEqual(
            actual.SemanticModel.NestedScopes.AsSpan()));
        Assert.True(expected.SemanticModel.ScopeMemberships.AsSpan().SequenceEqual(
            actual.SemanticModel.ScopeMemberships.AsSpan()));
        Assert.Equal(expected.SemanticModel.ModelProfiles, actual.SemanticModel.ModelProfiles);
        Assert.True(expected.SemanticModel.ProfileAssignments.AsSpan().SequenceEqual(
            actual.SemanticModel.ProfileAssignments.AsSpan()));
        Assert.True(expected.VisualModel.VisualStates.AsSpan().SequenceEqual(
            actual.VisualModel.VisualStates.AsSpan()));
        Assert.True(expected.VisualModel.ProfileElementPresentations.AsSpan().SequenceEqual(
            actual.VisualModel.ProfileElementPresentations.AsSpan()));
    }

    private static string Diagnostics(
        IEnumerable<Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed record Harness(
        Document Document,
        CommandProcessor Processor,
        HistoryManager History);

    private sealed class StubIdentityProvider :
        Inceptus.DocumentEngine.Contracts.Creation.IDocumentCreationIdentityProvider
    {
        private readonly SemanticElementId _semanticElementId;

        internal StubIdentityProvider(SemanticElementId semanticElementId) =>
            _semanticElementId = semanticElementId;

        public Inceptus.DocumentEngine.Contracts.Creation.DocumentCreationIdentity
            CreateIdentity() =>
            new(
                _semanticElementId,
                new VisualStateId($"{_semanticElementId.Value}:unused-visual"));

        public DocumentScopeId CreateDocumentScopeId() =>
            new($"{_semanticElementId.Value}:unused-scope");
    }
}
