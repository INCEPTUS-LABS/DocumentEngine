using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Profiles;

public sealed class ModelProfilePersistentRecordsTests
{
    private static readonly DocumentId DocumentId = new("test:profile-records:document");
    private static readonly ModelProfileId ProfileId = new("test:profile-records:profile");
    private static readonly SemanticElementId NodeId = new("test:profile-records:node");
    private static readonly SemanticElementId ContainerId = new("test:profile-records:container");
    private static readonly VisualStateId VisualId = new("test:profile-records:visual");
    private static readonly ConnectorAnchorId AnchorId = new("test:profile-records:anchor");
    private static readonly SemanticTypeId ContainerType = new("test:profile-records:container-type");

    [Fact]
    public void LegacySnapshotsHaveEmptyProfileCollectionsAndNoFakeVisual()
    {
        var semantic = new SemanticModelSnapshot(DocumentId, DocumentRevision.Zero);
        var visual = new VisualModelSnapshot(DocumentId, DocumentRevision.Zero);
        Assert.Empty(semantic.ProfileAssignments);
        Assert.Empty(visual.ProfileElementPresentations);
        var populated = Snapshot();
        Assert.Single(populated.VisualModel.ProfileElementPresentations);
        Assert.Single(populated.VisualModel.VisualStates);
        Assert.Equal(1, populated.VisualModel.Count);
        Assert.DoesNotContain(populated.VisualModel.VisualStates,
            static visual => visual.SemanticElementId == ContainerId);
    }

    [Fact]
    public void RecordsAreImmutableCopiedOrderedAndPartOfEquality()
    {
        var secondProfile = new ModelProfileId("test:z-profile");
        var assignments = new[] { Assignment(secondProfile), Assignment() };
        var presentations = new[] { Presentation(secondProfile, 3), Presentation() };
        var snapshot = Snapshot(assignments: assignments, presentations: presentations);
        assignments[0] = new ModelProfileElementAssignmentSnapshot(secondProfile, ContainerId, NodeId);
        presentations[0] = Presentation(secondProfile, 12);

        Assert.Equal(new[] { ProfileId, secondProfile },
            snapshot.SemanticModel.ProfileAssignments.Select(static item => item.ProfileId));
        Assert.Equal(new[] { ProfileId, secondProfile },
            snapshot.VisualModel.ProfileElementPresentations.Select(static item => item.ProfileId));
        Assert.Equal(3, snapshot.VisualModel.ProfileElementPresentations[1].Order);
        var equal = Snapshot(
            assignments: [Assignment(), Assignment(secondProfile)],
            presentations: [Presentation(), Presentation(secondProfile, 3)]);
        Assert.Equal(snapshot, equal);
        Assert.Equal(snapshot.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(snapshot, Snapshot());
        Assert.NotEqual(Snapshot(), Snapshot(assignments: []));
        Assert.NotEqual(Snapshot(), Snapshot(presentations: [Presentation(order: 2)]));
    }

    [Fact]
    public void DuplicateKeysAndInvalidRecordShapesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Snapshot(assignments: [Assignment(), Assignment()]));
        Assert.Throws<ArgumentException>(() => Snapshot(
            presentations: [Presentation(), Presentation(order: 5)]));
        Assert.Throws<ArgumentException>(() => Snapshot(assignments: [null!]));
        Assert.Throws<ArgumentException>(() => Snapshot(presentations: [null!]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Presentation(order: -1));
        Assert.Throws<ArgumentNullException>(() =>
            new ModelProfileElementAssignmentSnapshot(null!, NodeId, ContainerId));
        Assert.Throws<ArgumentNullException>(() =>
            new ModelProfileElementAssignmentSnapshot(ProfileId, null!, ContainerId));
        Assert.Throws<ArgumentNullException>(() =>
            new ModelProfileElementAssignmentSnapshot(ProfileId, NodeId, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ModelProfileElementPresentationSnapshot(null!, ContainerId, 0));
        Assert.Throws<ArgumentNullException>(() =>
            new ModelProfileElementPresentationSnapshot(ProfileId, null!, 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingAssignmentEndpointIsRejectedWithoutSanitizing(bool missingSource)
    {
        var missing = new SemanticElementId("test:missing");
        var snapshot = Snapshot(assignments:
        [
            new ModelProfileElementAssignmentSnapshot(ProfileId,
                missingSource ? missing : NodeId,
                missingSource ? ContainerId : missing),
        ]);
        var result = DocumentReconstructor.Reconstruct(snapshot);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code ==
            DocumentInvariantValidator.ProfileAssignmentReferenceMissingCode);
        Assert.Single(snapshot.SemanticModel.ProfileAssignments);
    }

    [Fact]
    public void MissingPresentationElementIsRejected()
    {
        var result = DocumentReconstructor.Reconstruct(Snapshot(presentations:
        [
            new ModelProfileElementPresentationSnapshot(ProfileId,
                new SemanticElementId("test:missing"), 0),
        ]));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code ==
            DocumentInvariantValidator.ProfilePresentationReferenceMissingCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DocumentContainedAssignmentEndpointIsRejected(bool documentSource)
    {
        var source = new SemanticElementSnapshot(NodeId, ContainerType,
            containmentKind: documentSource
                ? SemanticElementContainmentKind.Document : SemanticElementContainmentKind.Scope);
        var container = new SemanticElementSnapshot(ContainerId, ContainerType,
            containmentKind: documentSource
                ? SemanticElementContainmentKind.Scope : SemanticElementContainmentKind.Document);
        var result = DocumentReconstructor.Reconstruct(Snapshot(elements: [source, container], visuals: []));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code ==
            DocumentInvariantValidator.ProfileAssignmentContainmentInvalidCode);
    }

    [Fact]
    public void SelfAssignmentIsRejected()
    {
        var result = DocumentReconstructor.Reconstruct(Snapshot(assignments:
        [
            new ModelProfileElementAssignmentSnapshot(ProfileId, NodeId, NodeId),
        ]));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code ==
            DocumentInvariantValidator.ProfileAssignmentContainmentInvalidCode);
    }

    [Fact]
    public void AssignmentUsesExactScopeAndLegacyRootRemainsImplicit()
    {
        var peer = new DocumentScopeId("test:peer");
        var mismatch = Snapshot(scopes: [new DocumentScopeSnapshot(peer)],
            memberships: [new SemanticElementScopeMembershipSnapshot(ContainerId, peer)]);
        var rejected = DocumentReconstructor.Reconstruct(mismatch);
        Assert.False(rejected.Succeeded);
        Assert.Contains(rejected.Diagnostics, static diagnostic => diagnostic.Code ==
            DocumentInvariantValidator.ProfileAssignmentScopeMismatchCode);
        var matching = Snapshot(scopes: [new DocumentScopeSnapshot(peer)], memberships:
        [
            new SemanticElementScopeMembershipSnapshot(ContainerId, peer),
            new SemanticElementScopeMembershipSnapshot(NodeId, peer),
        ]);
        Assert.True(DocumentReconstructor.Reconstruct(matching).Succeeded);
        Assert.True(DocumentReconstructor.Reconstruct(Snapshot()).Succeeded);
        Assert.Equal(new DocumentScopeId(DocumentId.Value), Snapshot().SemanticModel.GetScope(NodeId).Id);
    }

    [Fact]
    public void ReconstructionPreservesBothCollectionsAndTheirAuthoritativeComponents()
    {
        var before = Snapshot();
        var result = DocumentReconstructor.Reconstruct(before);
        Assert.True(result.Succeeded);
        var after = Assert.IsType<Document>(result.Document).CaptureSnapshot();
        Assert.NotSame(before, after);
        Assert.Equal(before, after);
        AssertProfilesEqual(before, after);
        Assert.Empty(after.Metadata.ExtensionProperties);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("multi-move")]
    [InlineData("resize")]
    [InlineData("name")]
    [InlineData("property")]
    [InlineData("label")]
    [InlineData("anchor")]
    [InlineData("availability")]
    [InlineData("scope")]
    public async Task OrdinaryRuntimeMutationsAndUndoRedoRetainBothCollections(string operation)
    {
        var before = Snapshot();
        var harness = CreateHarness(before);
        ICommand command = operation switch
        {
            "move" => new MoveVisualStateCommand(DocumentId, before.Revision, VisualId, new PointD(80, 90)),
            "multi-move" => new MoveVisualStatesCommand(DocumentId, before.Revision,
                [new VisualStateMove(VisualId, new PointD(80, 90))]),
            "resize" => new ResizeVisualStateCommand(DocumentId, before.Revision, VisualId,
                new RectD(30, 40, 180, 100)),
            "name" => new UpdateSemanticElementNameCommand(DocumentId, before.Revision, NodeId,
                BpmnSemanticProperties.Name, "Changed"),
            "property" => new UpdateSemanticElementPropertyCommand(DocumentId, before.Revision, NodeId,
                BpmnSemanticProperties.Description, PropertyValue.FromText("Changed description")),
            "label" => new UpdateNodeLabelVisualOverrideCommand(DocumentId, before.Revision, VisualId,
                new NodeLabelVisualOverride(0, 0, 100, 40)),
            "anchor" => new AddConnectorAnchorCommand(DocumentId, before.Revision, VisualId, AnchorId,
                ConnectorAnchorSide.Right, ConnectorAnchorRole.Source, 0),
            "availability" => new SetModelProfileAvailabilityCommand(DocumentId, before.Revision,
                [new ModelProfileAvailabilityChange(ProfileId, false)]),
            "scope" => new CreateTopLevelDocumentScopeCommand(DocumentId, before.Revision,
                new DocumentScopeId("test:new-peer")),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        var result = await harness.History.ExecuteAsync(harness.Processor, command);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        Assert.Equal(before.Revision.Increment(), harness.Document.Revision);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
        AssertProfilesEqual(before, harness.Document.CaptureSnapshot());
        var undo = await harness.History.UndoAsync(harness.Processor);
        Assert.True(undo.IsCommitted, Diagnostics(undo.Diagnostics));
        AssertProfilesEqual(before, harness.Document.CaptureSnapshot());
        var redo = await harness.History.RedoAsync(harness.Processor);
        Assert.True(redo.IsCommitted, Diagnostics(redo.Diagnostics));
        AssertProfilesEqual(before, harness.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task BpmnCreationAndSnapshotRestoreRetainExistingProfileRecords()
    {
        var before = Snapshot();
        var harness = CreateHarness(before);
        var createdId = new SemanticElementId("test:new-task");
        var creation = new CreateBpmnTaskCommand(DocumentId, before.Revision,
            createdId, new VisualStateId("test:new-task:visual"),
            new PointD(300, 40), new SizeD(120, 80), "NEW", "New", 2);
        var result = await harness.History.ExecuteAsync(harness.Processor, creation);
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        AssertProfilesEqual(before, harness.Document.CaptureSnapshot());
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        AssertProfilesEqual(before, harness.Document.CaptureSnapshot());
        Assert.False(harness.Document.SemanticModel.TryGetElement(createdId, out _));
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        AssertProfilesEqual(before, harness.Document.CaptureSnapshot());
        Assert.True(harness.Document.SemanticModel.TryGetElement(createdId, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BpmnDeletionExplicitlyCleansReferencesAndUndoRestoresThem(bool recursive)
    {
        var childScope = new DocumentScopeId("test:child-scope");
        var childNode = new SemanticElementId("test:child-node");
        var childContainer = new SemanticElementId("test:child-container");
        var before = recursive ? Snapshot(
            elements:
            [
                BpmnSemanticFactory.CreateSubProcess(NodeId, "SUB", "Sub", "Description"),
                new SemanticElementSnapshot(ContainerId, ContainerType),
                BpmnSemanticFactory.CreateTask(childNode, "CHILD", "Child", 2),
                new SemanticElementSnapshot(childContainer, ContainerType),
            ],
            scopes: [new DocumentScopeSnapshot(childScope, new DocumentScopeId(DocumentId.Value), NodeId)],
            memberships:
            [
                new SemanticElementScopeMembershipSnapshot(childNode, childScope),
                new SemanticElementScopeMembershipSnapshot(childContainer, childScope),
            ],
            assignments:
            [
                Assignment(),
                new ModelProfileElementAssignmentSnapshot(ProfileId, childNode, childContainer),
            ],
            presentations: [Presentation(), new ModelProfileElementPresentationSnapshot(ProfileId, childContainer, 0)])
            : Snapshot();
        var harness = CreateHarness(before);
        var result = await harness.History.ExecuteAsync(harness.Processor,
            new DeleteBpmnFlowNodeCommand(DocumentId, before.Revision, NodeId, VisualId));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        Assert.Empty(harness.Document.SemanticModel.ProfileAssignments);
        Assert.Equal(ContainerId,
            Assert.Single(harness.Document.VisualModel.ProfileElementPresentations).SemanticElementId);
        Assert.True(harness.Document.SemanticModel.TryGetElement(ContainerId, out _));
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        AssertProfilesEqual(before, harness.Document.CaptureSnapshot());
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), harness.Document.SemanticModel.Elements.AsEnumerable());
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.Empty(harness.Document.SemanticModel.ProfileAssignments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompoundMoveAndSemanticEditCommitOrFailAsOneAtomicOperation(bool failSecondChild)
    {
        var before = Snapshot();
        var harness = CreateHarness(before);
        var command = new CompoundDocumentCommand(DocumentId, before.Revision,
        [
            new MoveVisualStateCommand(DocumentId, before.Revision, VisualId, new PointD(90, 100)),
            new UpdateSemanticElementNameCommand(DocumentId, before.Revision,
                failSecondChild ? new SemanticElementId("test:missing") : NodeId,
                BpmnSemanticProperties.Name, "Compound name"),
        ]);
        var result = await harness.History.ExecuteAsync(harness.Processor, command);
        Assert.Equal(!failSecondChild, result.IsCommitted);
        AssertProfilesEqual(before, harness.Document.CaptureSnapshot());
        if (failSecondChild)
        {
            Assert.Equal(before, harness.Document.CaptureSnapshot());
            Assert.Equal(0, harness.History.CaptureStatus().EntryCount);
            return;
        }

        Assert.Equal(before.Revision.Increment(), harness.Document.Revision);
        Assert.Equal(1, harness.History.CaptureStatus().EntryCount);
        var committed = harness.Document.CaptureSnapshot();
        Assert.True((await harness.History.UndoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(before.SemanticModel.Elements.AsEnumerable(), harness.Document.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(before.VisualModel.VisualStates.AsEnumerable(), harness.Document.VisualModel.VisualStates.AsEnumerable());
        Assert.True((await harness.History.RedoAsync(harness.Processor)).IsCommitted);
        Assert.Equal(committed.SemanticModel.Elements.AsEnumerable(), harness.Document.SemanticModel.Elements.AsEnumerable());
        Assert.Equal(committed.VisualModel.VisualStates.AsEnumerable(), harness.Document.VisualModel.VisualStates.AsEnumerable());
        AssertProfilesEqual(before, harness.Document.CaptureSnapshot());
    }

    private static ModelProfileElementAssignmentSnapshot Assignment(ModelProfileId? profileId = null) =>
        new(profileId ?? ProfileId, NodeId, ContainerId);

    private static ModelProfileElementPresentationSnapshot Presentation(ModelProfileId? profileId = null, int order = 0) =>
        new(profileId ?? ProfileId, ContainerId, order);

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot>? elements = null,
        IEnumerable<VisualStateSnapshot>? visuals = null,
        IEnumerable<DocumentScopeSnapshot>? scopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null,
        IEnumerable<ModelProfileElementAssignmentSnapshot>? assignments = null,
        IEnumerable<ModelProfileElementPresentationSnapshot>? presentations = null) =>
        new(
            new SemanticModelSnapshot(DocumentId, DocumentRevision.Zero,
                elements ??
                [
                    BpmnSemanticFactory.CreateTask(NodeId, "NODE", "Node", 1, "Description"),
                    new SemanticElementSnapshot(ContainerId, ContainerType),
                ],
                nestedScopes: scopes, scopeMemberships: memberships,
                modelProfiles: new ModelProfileStateSnapshot([ProfileId]),
                profileAssignments: assignments ?? [Assignment()]),
            new VisualModelSnapshot(DocumentId, DocumentRevision.Zero,
                visuals ??
                [
                    new VisualStateSnapshot(VisualId, NodeId, new PointD(30, 40),
                        new SizeD(120, 80), VisualPlacementMode.Manual),
                ],
                presentations ?? [Presentation()]),
            new DocumentMetadataSnapshot(DocumentId, DocumentRevision.Zero));

    private static Harness CreateHarness(DocumentSnapshot snapshot)
    {
        var registration = BpmnPluginRegistration.N100;
        var provider = new ElementConnectorAnchorPolicyRegistry(registration.ConnectorAnchorPolicies);
        var construction = DocumentReconstructor.Reconstruct(snapshot, provider);
        Assert.True(construction.Succeeded, Diagnostics(construction.Diagnostics));
        var document = Assert.IsType<Document>(construction.Document);
        return new Harness(document,
            new CommandProcessor(registration.CommandHandlers, registration.CommandValidators,
                historyPolicies: registration.HistoryPolicies, connectorAnchorPolicyProvider: provider),
            new HistoryManager(document));
    }

    private static void AssertProfilesEqual(DocumentSnapshot expected, DocumentSnapshot actual)
    {
        Assert.Equal(expected.SemanticModel.ProfileAssignments.AsEnumerable(), actual.SemanticModel.ProfileAssignments.AsEnumerable());
        Assert.Equal(expected.VisualModel.ProfileElementPresentations.AsEnumerable(), actual.VisualModel.ProfileElementPresentations.AsEnumerable());
    }

    private static string Diagnostics(IEnumerable<Inceptus.DocumentEngine.Contracts.Diagnostics.Diagnostic> diagnostics) =>
        string.Join(" | ", diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed record Harness(Document Document, CommandProcessor Processor, HistoryManager History);
}
