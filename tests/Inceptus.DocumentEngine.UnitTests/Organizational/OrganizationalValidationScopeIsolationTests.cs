using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Organizational.Validation;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.UnitTests.Organizational;

public sealed class OrganizationalValidationScopeIsolationTests
{
    private static readonly DocumentId DocumentId = new("test:organizational:validation");
    private static readonly DocumentScopeId MainScopeId = new(DocumentId.Value);
    private static readonly DocumentScopeId PeerScopeId = new("test:organizational:peer");
    private static readonly DocumentScopeId NestedScopeId = new("test:organizational:nested");
    private static readonly DocumentScopeId EmptyScopeId = new("test:organizational:empty");
    private static readonly SemanticElementId OwnerId = new("test:organizational:owner");
    private static readonly string[] ExpectedIssueCodes =
    [
        "ORGANIZATIONAL_ASSIGNMENT_INVALID",
        "ORGANIZATIONAL_POOL_SHAPE_INVALID",
        "ORGANIZATIONAL_POOL_VISUAL_STATE_INVALID",
        "ORGANIZATIONAL_PRESENTATION_ORDER_DUPLICATE",
        "ORGANIZATIONAL_PRESENTATION_TARGET_INVALID",
    ];

    [Theory]
    [InlineData("main", 6)]
    [InlineData("peer", 6)]
    [InlineData("nested", 6)]
    [InlineData("empty", 0)]
    public void N5IssuesContainOnlyExactActiveScopePoolAssignmentAndOrderFindings(
        string activeScope,
        int expectedCount)
    {
        var document = MalformedScopes();
        var scopeId = activeScope switch
        {
            "main" => MainScopeId,
            "peer" => PeerScopeId,
            "nested" => NestedScopeId,
            _ => EmptyScopeId,
        };
        Assert.True(DocumentReconstructor.Reconstruct(document).Succeeded);

        var validation = Validate(document, scopeId);

        Assert.Equal(scopeId, validation.ScopeId);
        Assert.Equal(document.Revision, validation.SourceRevision);
        Assert.Equal(expectedCount, validation.Issues.Length);
        Assert.All(validation.Issues, issue =>
        {
            Assert.NotNull(issue.Target.SemanticElementId);
            Assert.Equal(scopeId, document.SemanticModel.GetScope(
                issue.Target.SemanticElementId).Id);
        });
        if (expectedCount != 0)
        {
            Assert.Equal(
                ExpectedIssueCodes,
                validation.Issues.Select(static issue => issue.Code)
                    .Distinct().Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void CrossScopeAssignmentIsReportedOnlyInSourceScopeAndStillRejectedGlobally()
    {
        var task = BpmnSemanticFactory.CreateTask(new("test:task"), "T", "Task", 1);
        var pool = OrganizationalSemanticFactory.CreatePool(new("test:pool"));
        var document = Snapshot(
            [task, pool],
            memberships: [new(pool.Id, PeerScopeId)],
            assignments: [new(OrganizationalModelProfile.Id, task.Id, pool.Id)]);

        var issue = Assert.Single(Validate(document, MainScopeId).Issues);
        Assert.Equal("ORGANIZATIONAL_ASSIGNMENT_INVALID", issue.Code);
        Assert.Equal(task.Id, issue.Target.SemanticElementId);
        Assert.Empty(Validate(document, PeerScopeId).Issues);
        var reconstruction = DocumentReconstructor.Reconstruct(document);
        Assert.False(reconstruction.Succeeded);
        Assert.Contains(reconstruction.Diagnostics, static diagnostic =>
            diagnostic.Code == DocumentInvariantValidator.ProfileAssignmentScopeMismatchCode);
        Assert.Single(document.SemanticModel.ProfileAssignments);
    }

    [Fact]
    public void MissingTargetsAreNotAssignedToMainAndRemainDocumentInvariantFailures()
    {
        var pool = OrganizationalSemanticFactory.CreatePool(new("test:pool"));
        var missing = new SemanticElementId("test:missing");
        var document = Snapshot(
            [pool],
            memberships: [new(pool.Id, PeerScopeId)],
            assignments: [new(OrganizationalModelProfile.Id, missing, pool.Id)],
            presentations: [new(OrganizationalModelProfile.Id, missing, 0)]);

        Assert.Empty(Validate(document, MainScopeId).Issues);
        Assert.Empty(Validate(document, PeerScopeId).Issues);
        var reconstruction = DocumentReconstructor.Reconstruct(document);
        Assert.False(reconstruction.Succeeded);
        Assert.Contains(reconstruction.Diagnostics, static diagnostic =>
            diagnostic.Code == DocumentInvariantValidator.ProfileAssignmentReferenceMissingCode);
        Assert.Contains(reconstruction.Diagnostics, static diagnostic =>
            diagnostic.Code == DocumentInvariantValidator.ProfilePresentationReferenceMissingCode);
        Assert.Single(document.SemanticModel.ProfileAssignments);
        Assert.Single(document.VisualModel.ProfileElementPresentations);
    }

    [Fact]
    public void DocumentContainedMalformedPoolIsNotPresentedAsMainProcessIssue()
    {
        var pool = new SemanticElementSnapshot(
            new("test:document-pool"),
            OrganizationalSemanticTypes.Pool,
            containmentKind: SemanticElementContainmentKind.Document);
        var document = Snapshot(
            [pool],
            presentations: [new(OrganizationalModelProfile.Id, pool.Id, 0)]);

        Assert.False(OrganizationalSemantics.IsPool(pool));
        Assert.False(document.SemanticModel.TryGetScope(pool.Id, out _));
        Assert.Empty(Validate(document, MainScopeId).Issues);
        Assert.Empty(Validate(document, PeerScopeId).Issues);
        Assert.Same(pool, Assert.Single(document.SemanticModel.Elements));
    }

    private static ValidationSnapshot Validate(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId) =>
        new ModelValidationEngine(new ModelValidationCatalog(
        [
            new OrganizationalStructuralValidationRule(
                new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode)),
        ])).Validate(new ModelValidationContext(document, activeScopeId));

    private static DocumentSnapshot MalformedScopes()
    {
        var elements = new List<SemanticElementSnapshot>
        {
            BpmnSemanticFactory.CreateSubProcess(OwnerId, "SP", "SubProcess"),
        };
        var memberships = new List<SemanticElementScopeMembershipSnapshot>
        {
            new(OwnerId, PeerScopeId),
        };
        var assignments = new List<ModelProfileElementAssignmentSnapshot>();
        var presentations = new List<ModelProfileElementPresentationSnapshot>();
        var visuals = new List<VisualStateSnapshot>();
        foreach (var scopeId in new[] { MainScopeId, PeerScopeId, NestedScopeId })
        {
            var malformed = new SemanticElementSnapshot(
                new($"{scopeId}:malformed-pool"), OrganizationalSemanticTypes.Pool);
            var first = OrganizationalSemanticFactory.CreatePool(new($"{scopeId}:pool-a"));
            var second = OrganizationalSemanticFactory.CreatePool(new($"{scopeId}:pool-b"));
            var ineligible = new SemanticElementSnapshot(
                new($"{scopeId}:ineligible"), new("test:ineligible"));
            SemanticElementSnapshot[] scopeElements = [malformed, first, second, ineligible];
            elements.AddRange(scopeElements);
            if (scopeId != MainScopeId)
            {
                memberships.AddRange(scopeElements.Select(element =>
                    new SemanticElementScopeMembershipSnapshot(element.Id, scopeId)));
            }

            assignments.Add(new(OrganizationalModelProfile.Id, ineligible.Id, first.Id));
            presentations.Add(new(OrganizationalModelProfile.Id, first.Id, 0));
            presentations.Add(new(OrganizationalModelProfile.Id, second.Id, 0));
            presentations.Add(new(OrganizationalModelProfile.Id, ineligible.Id, 1));
            visuals.Add(new(
                new($"{scopeId}:pool-visual"),
                first.Id,
                new PointD(10, 10),
                new SizeD(100, 60),
                VisualPlacementMode.Manual));
        }

        return Snapshot(elements, memberships, assignments, presentations, visuals,
            [new(NestedScopeId, PeerScopeId, OwnerId)]);
    }

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? memberships = null,
        IEnumerable<ModelProfileElementAssignmentSnapshot>? assignments = null,
        IEnumerable<ModelProfileElementPresentationSnapshot>? presentations = null,
        IEnumerable<VisualStateSnapshot>? visuals = null,
        IEnumerable<DocumentScopeSnapshot>? extraScopes = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                new DocumentRevision(0),
                elements,
                nestedScopes: new[]
                {
                    new DocumentScopeSnapshot(PeerScopeId),
                    new DocumentScopeSnapshot(EmptyScopeId),
                }.Concat(extraScopes ?? []),
                scopeMemberships: memberships,
                profileAssignments: assignments),
            new VisualModelSnapshot(
                DocumentId,
                new DocumentRevision(0),
                visuals,
                presentations),
            new DocumentMetadataSnapshot(DocumentId, new DocumentRevision(0)));
}
