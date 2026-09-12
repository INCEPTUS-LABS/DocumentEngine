using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Projection;

public sealed class ProjectionEngineTests
{
    private static readonly DocumentId DocumentId = new("test:projection-document");
    private static readonly DocumentRevision Revision = new(17);
    private static readonly SemanticTypeId NodeTypeId = new("test:node");
    private static readonly SemanticTypeId EdgeTypeId = new("test:edge");
    private static readonly ProjectionRuleId NodeRuleId = new("test:rule:node");
    private static readonly ProjectionRuleId EdgeRuleId = new("test:rule:edge");

    [Fact]
    public void ProjectsOneCoherentSnapshotIntoTheCanonicalNeutralGraph()
    {
        var snapshot = NeutralSnapshot(reverseInputs: true);
        var observedInputs = new List<ProjectionRuleInput>();
        var engine = NeutralEngine(observedInputs);

        var result = engine.Project(snapshot);

        Assert.True(result.IsSuccessful);
        Assert.Equal(DocumentId, result.DocumentId);
        Assert.Equal(Revision, result.SourceRevision);
        Assert.Empty(result.Diagnostics);
        var graph = Assert.IsType<ProjectedGraph>(result.Graph);
        Assert.Equal(DocumentId, graph.DocumentId);
        Assert.Equal(Revision, graph.SourceRevision);
        Assert.Equal(3, graph.NodeCount);
        Assert.Equal(2, graph.EdgeCount);
        Assert.Empty(graph.Groups);
        Assert.Empty(graph.Ports);
        Assert.Empty(graph.Labels);
        Assert.Equal(5, observedInputs.Count);
        Assert.All(observedInputs, input =>
        {
            Assert.Equal(DocumentId, input.DocumentId);
            Assert.Equal(Revision, input.SourceRevision);
            Assert.Same(ProjectionContext.Empty, input.Context);
        });
        Assert.Equal(
            ["test:a", "test:b", "test:c", "test:ab", "test:bc"],
            observedInputs.Select(input => input.SemanticId.Value));

        var ab = graph.Edges.Single(edge => edge.Source.SemanticElementId.Value == "test:ab");
        Assert.Equal("test:a", NodeSource(graph, ab.SourceNodeId).SemanticElementId.Value);
        Assert.Equal("test:b", NodeSource(graph, ab.TargetNodeId).SemanticElementId.Value);
        Assert.Equal(new VisualStateId("test:visual-ab"), ab.Source.VisualStateId);
        Assert.Equal(2, ab.PersistentRoute.Length);
    }

    [Fact]
    public void EquivalentSnapshotsAndRegistrationOrdersProduceEqualGraphs()
    {
        var first = NeutralEngine().Project(NeutralSnapshot(reverseInputs: false));
        var second = NeutralEngine(reverseRegistrations: true)
            .Project(NeutralSnapshot(reverseInputs: true));
        var third = NeutralEngine().Project(NeutralSnapshot(reverseInputs: false));

        Assert.Equal(ProjectionStatus.Succeeded, first.Status);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void MatchingRulesRunInStableRuleIdOrderAndMayContributeCompatibly()
    {
        var calls = new List<string>();
        var element = Element("test:only", NodeTypeId);
        var snapshot = Snapshot([element]);
        var ruleZ = new DelegateRule((input, _) =>
        {
            calls.Add("z");
            return NodeResult(input, new ProjectionRuleId("test:z"), "z");
        });
        var ruleA = new DelegateRule((input, _) =>
        {
            calls.Add("a");
            return NodeResult(input, new ProjectionRuleId("test:a"), "a");
        });
        var wrongTypeRule = new DelegateRule((_, _) =>
            throw new InvalidOperationException("A non-matching rule ran."));
        var engine = new ProjectionEngine(
        [
            Registration("test:z", ProjectionSourceKind.SemanticElement, NodeTypeId, ruleZ),
            Registration("test:other", ProjectionSourceKind.SemanticElement, "test:other", wrongTypeRule),
            Registration("test:a", ProjectionSourceKind.SemanticElement, NodeTypeId, ruleA),
        ]);

        var result = engine.Project(snapshot);

        Assert.Equal(["a", "z"], calls);
        Assert.Equal(2, Assert.IsType<ProjectedGraph>(result.Graph).NodeCount);
    }

    [Fact]
    public void UnsupportedSemanticTypeFailsDeterministicallyWithoutAPartialGraph()
    {
        var snapshot = Snapshot([Element("test:unsupported", "test:unsupported-type")]);
        var engine = new ProjectionEngine();

        var first = engine.Project(snapshot);
        var second = engine.Project(snapshot);

        Assert.Equal(ProjectionStatus.Failed, first.Status);
        Assert.Null(first.Graph);
        Assert.Equal(first, second);
        var diagnostic = Assert.Single(first.Diagnostics);
        Assert.Equal(ProjectionDiagnosticCodes.UnsupportedSemanticType, diagnostic.Code);
        Assert.Equal("test:unsupported", diagnostic.SourceIdentity);
    }

    [Fact]
    public void EmptyDocumentProjectsToAnEmptyGraphWithoutRules()
    {
        var result = new ProjectionEngine().Project(Snapshot([]));

        Assert.True(result.IsSuccessful);
        var graph = Assert.IsType<ProjectedGraph>(result.Graph);
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ProjectsOnlyTheExactRequestedScopeAndPreservesRootOverloadBehavior()
    {
        var rootScopeId = new DocumentScopeId(DocumentId.Value);
        var childScopeId = new DocumentScopeId("test:scope:child");
        var nestedScopeId = new DocumentScopeId("test:scope:nested");
        var rootA = Element("test:root-a", NodeTypeId);
        var rootB = Element("test:root-b", NodeTypeId);
        var childA = Element("test:child-a", NodeTypeId);
        var childB = Element("test:child-b", NodeTypeId);
        var nestedA = Element("test:nested-a", NodeTypeId);
        var nestedB = Element("test:nested-b", NodeTypeId);
        var snapshot = Snapshot(
            [rootA, rootB, childA, childB, nestedA, nestedB],
            [
                Relationship("test:root-edge", rootA.Id, rootB.Id),
                Relationship("test:child-edge", childA.Id, childB.Id),
                Relationship("test:nested-edge", nestedA.Id, nestedB.Id),
            ],
            nestedScopes:
            [
                new DocumentScopeSnapshot(childScopeId, rootScopeId),
                new DocumentScopeSnapshot(nestedScopeId, childScopeId),
            ],
            scopeMemberships:
            [
                new SemanticElementScopeMembershipSnapshot(childA.Id, childScopeId),
                new SemanticElementScopeMembershipSnapshot(childB.Id, childScopeId),
                new SemanticElementScopeMembershipSnapshot(nestedA.Id, nestedScopeId),
                new SemanticElementScopeMembershipSnapshot(nestedB.Id, nestedScopeId),
            ]);
        var engine = NeutralEngine();

        var implicitRoot = Assert.IsType<ProjectedGraph>(engine.Project(snapshot).Graph);
        var explicitRoot = Assert.IsType<ProjectedGraph>(
            engine.Project(snapshot, rootScopeId).Graph);
        var child = Assert.IsType<ProjectedGraph>(
            engine.Project(snapshot, childScopeId).Graph);
        var nested = Assert.IsType<ProjectedGraph>(
            engine.Project(snapshot, nestedScopeId).Graph);

        Assert.Equal(implicitRoot, explicitRoot);
        AssertProjectedSources(implicitRoot, [rootA.Id, rootB.Id], ["test:root-edge"]);
        AssertProjectedSources(child, [childA.Id, childB.Id], ["test:child-edge"]);
        AssertProjectedSources(nested, [nestedA.Id, nestedB.Id], ["test:nested-edge"]);
    }

    [Fact]
    public void ProcessProjectionExcludesDocumentSemanticsAndOtherPeerRoots()
    {
        var peerScopeId = new DocumentScopeId("test:scope:peer");
        var main = Element("test:main", NodeTypeId);
        var peer = Element("test:peer", NodeTypeId);
        var collaboration = new SemanticElementSnapshot(
            new SemanticElementId("test:collaboration"),
            NodeTypeId,
            containmentKind: SemanticElementContainmentKind.Document);
        var snapshot = Snapshot(
            [collaboration, peer, main],
            nestedScopes: [new DocumentScopeSnapshot(peerScopeId)],
            scopeMemberships: [new(peer.Id, peerScopeId)]);
        var engine = NeutralEngine();

        var mainGraph = Assert.IsType<ProjectedGraph>(engine.Project(snapshot).Graph);
        var peerGraph = Assert.IsType<ProjectedGraph>(
            engine.Project(snapshot, peerScopeId).Graph);

        AssertProjectedSources(mainGraph, [main.Id], []);
        AssertProjectedSources(peerGraph, [peer.Id], []);
    }

    [Fact]
    public void MissingRequestedScopeIsRejectedWithoutProjectionRuleExecution()
    {
        var observedInputs = new List<ProjectionRuleInput>();

        var result = NeutralEngine(observedInputs).Project(
            NeutralSnapshot(reverseInputs: false),
            new DocumentScopeId("test:scope:missing"));

        Assert.Equal(ProjectionStatus.Failed, result.Status);
        Assert.Null(result.Graph);
        Assert.Empty(observedInputs);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ProjectionDiagnosticCodes.InvalidInput);
    }

    [Fact]
    public void DuplicateRuleRegistrationIsRejectedBeforeProjection()
    {
        var rule = new DelegateRule((_, _) =>
            ProjectionRuleResult.Success(ProjectionRuleContribution.Empty));
        var first = Registration(
            "test:duplicate",
            ProjectionSourceKind.SemanticElement,
            NodeTypeId,
            rule);
        var second = Registration(
            "test:duplicate",
            ProjectionSourceKind.SemanticRelationship,
            EdgeTypeId,
            rule);

        var exception = Assert.Throws<ArgumentException>(() =>
            new ProjectionEngine([first, second]));

        Assert.Contains(
            ProjectionDiagnosticCodes.DuplicateRuleRegistration,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RuleFault.Throw, ProjectionDiagnosticCodes.RuleFailure)]
    [InlineData(RuleFault.NullResult, ProjectionDiagnosticCodes.InvalidRuleResult)]
    [InlineData(RuleFault.ReportedFailure, "TEST_RULE_ERROR")]
    public void RuleFailureModesProduceDiagnosticsAndNoGraph(
        RuleFault fault,
        string expectedCode)
    {
        var rule = new DelegateRule((_, _) => fault switch
        {
            RuleFault.Throw => throw new InvalidOperationException("test failure"),
            RuleFault.NullResult => null!,
            RuleFault.ReportedFailure => ProjectionRuleResult.Failure(
            [
                new Diagnostic(
                    "TEST_RULE_ERROR",
                    DiagnosticSeverity.Error,
                    "The test rule rejected its input."),
            ]),
            _ => throw new InvalidOperationException("Unknown test fault."),
        });
        var engine = new ProjectionEngine(
        [
            Registration("test:fault", ProjectionSourceKind.SemanticElement, NodeTypeId, rule),
        ]);

        var result = engine.Project(Snapshot([Element("test:only", NodeTypeId)]));

        Assert.Equal(ProjectionStatus.Failed, result.Status);
        Assert.Null(result.Graph);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }

    [Fact]
    public void InvalidSourceTraceabilityAndVisualAssociationAreRejected()
    {
        var element = Element("test:only", NodeTypeId);
        var snapshot = Snapshot(
            [element],
            visualStates: [Visual("test:visual", element.Id)]);
        var wrongRuleTrace = new DelegateRule((input, _) => NodeResult(
            input,
            new ProjectionRuleId("test:not-the-registered-rule"),
            "node"));
        var unknownVisualTrace = new DelegateRule((input, _) =>
        {
            var trace = Trace(
                input,
                new ProjectionRuleId("test:unknown-visual"),
                "node",
                new VisualStateId("test:not-associated"));
            return ProjectionRuleResult.Success(
                new ProjectionRuleContribution(nodes: [new ProjectedNode(trace)]));
        });

        var wrongRuleResult = new ProjectionEngine(
        [
            Registration(
                "test:registered",
                ProjectionSourceKind.SemanticElement,
                NodeTypeId,
                wrongRuleTrace),
        ]).Project(snapshot);
        var unknownVisualResult = new ProjectionEngine(
        [
            Registration(
                "test:unknown-visual",
                ProjectionSourceKind.SemanticElement,
                NodeTypeId,
                unknownVisualTrace),
        ]).Project(snapshot);

        AssertInvalidTraceability(wrongRuleResult);
        AssertInvalidTraceability(unknownVisualResult);
    }

    [Fact]
    public void InvalidTopologyAndDuplicateProjectedIdsAreRejected()
    {
        var element = Element("test:only", NodeTypeId);
        var snapshot = Snapshot([element]);
        var invalidTopologyRule = new DelegateRule((input, _) =>
        {
            var edge = new ProjectedEdge(
                Trace(input, new ProjectionRuleId("test:topology"), "edge"),
                new ProjectedObjectId("test:missing-source"),
                new ProjectedObjectId("test:missing-target"));
            return ProjectionRuleResult.Success(
                new ProjectionRuleContribution(edges: [edge]));
        });
        var duplicateRule = new DelegateRule((input, _) =>
        {
            var node = new ProjectedNode(
                Trace(input, new ProjectionRuleId("test:duplicate-output"), "node"));
            var contribution = new ProjectionRuleContribution(nodes: [node]);
            var nodesField = typeof(ProjectionRuleContribution).GetField(
                "<Nodes>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(nodesField);
            nodesField.SetValue(contribution, ImmutableArray.Create(node, node));
            return ProjectionRuleResult.Success(contribution);
        });

        var topologyResult = new ProjectionEngine(
        [
            Registration(
                "test:topology",
                ProjectionSourceKind.SemanticElement,
                NodeTypeId,
                invalidTopologyRule),
        ]).Project(snapshot);
        var duplicateResult = new ProjectionEngine(
        [
            Registration(
                "test:duplicate-output",
                ProjectionSourceKind.SemanticElement,
                NodeTypeId,
                duplicateRule),
        ]).Project(snapshot);

        Assert.Equal(ProjectionStatus.Failed, topologyResult.Status);
        Assert.Null(topologyResult.Graph);
        Assert.Contains(
            topologyResult.Diagnostics,
            diagnostic => diagnostic.Code == ProjectionDiagnosticCodes.InvalidGraph);
        Assert.Equal(ProjectionStatus.Failed, duplicateResult.Status);
        Assert.Null(duplicateResult.Graph);
        Assert.Contains(
            duplicateResult.Diagnostics,
            diagnostic => diagnostic.Code == ProjectionDiagnosticCodes.DuplicateProjectedObjectId);
    }

    [Fact]
    public void CancellationBeforeAndDuringProjectionReturnsNoUsableGraph()
    {
        var snapshot = Snapshot([Element("test:only", NodeTypeId)]);
        using var beforeCancellation = new CancellationTokenSource();
        beforeCancellation.Cancel();
        var before = NeutralEngine().Project(
            snapshot,
            cancellationToken: beforeCancellation.Token);

        using var duringCancellation = new CancellationTokenSource();
        var cancellingRule = new DelegateRule((_, cancellationToken) =>
        {
            duringCancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ProjectionRuleResult.Success(ProjectionRuleContribution.Empty);
        });
        var during = new ProjectionEngine(
        [
            Registration(
                "test:cancelling",
                ProjectionSourceKind.SemanticElement,
                NodeTypeId,
                cancellingRule),
        ]).Project(snapshot, cancellationToken: duringCancellation.Token);

        AssertCancelled(before);
        AssertCancelled(during);
    }

    [Fact]
    public void ProjectionLeavesItsImmutableInputUnchangedAfterSuccessAndFailure()
    {
        var snapshot = NeutralSnapshot(reverseInputs: false);
        var equalBefore = NeutralSnapshot(reverseInputs: true);
        var success = NeutralEngine().Project(snapshot);
        var failure = new ProjectionEngine().Project(snapshot);

        Assert.Equal(equalBefore, snapshot);
        Assert.True(success.IsSuccessful);
        Assert.Equal(ProjectionStatus.Failed, failure.Status);
        Assert.Equal(equalBefore, snapshot);
        Assert.Equal(Revision, snapshot.Revision);
    }

    private static ProjectionEngine NeutralEngine(
        List<ProjectionRuleInput>? observedInputs = null,
        bool reverseRegistrations = false)
    {
        var nodeRule = new DelegateRule((input, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            observedInputs?.Add(input);
            var visual = input.VisualStates.FirstOrDefault();
            var placement = visual is null
                ? null
                : new ProjectedPlacementHint(
                    visual.Position,
                    visual.Size,
                    visual.PlacementMode);
            var elementInput = Assert.IsType<ElementProjectionRuleInput>(input);
            var node = new ProjectedNode(
                Trace(input, NodeRuleId, "node", visual?.Id),
                placement,
                semanticProperties: elementInput.Element.Properties);
            return ProjectionRuleResult.Success(
                new ProjectionRuleContribution(nodes: [node]));
        });
        var edgeRule = new DelegateRule((input, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            observedInputs?.Add(input);
            var relationshipInput = Assert.IsType<RelationshipProjectionRuleInput>(input);
            var visual = input.VisualStates.FirstOrDefault();
            var edge = new ProjectedEdge(
                Trace(input, EdgeRuleId, "edge", visual?.Id),
                NodeId(input.DocumentId, relationshipInput.SourceElement.Id),
                NodeId(input.DocumentId, relationshipInput.TargetElement.Id),
                persistentRoute: visual?.Route,
                semanticProperties: relationshipInput.Relationship.Properties);
            return ProjectionRuleResult.Success(
                new ProjectionRuleContribution(edges: [edge]));
        });
        ProjectionRuleRegistration[] registrations =
        [
            new(NodeRuleId, ProjectionSourceKind.SemanticElement, NodeTypeId, nodeRule),
            new(EdgeRuleId, ProjectionSourceKind.SemanticRelationship, EdgeTypeId, edgeRule),
        ];
        if (reverseRegistrations)
        {
            Array.Reverse(registrations);
        }

        return new ProjectionEngine(registrations);
    }

    private static DocumentSnapshot NeutralSnapshot(bool reverseInputs)
    {
        SemanticElementSnapshot[] elements =
        [
            Element("test:a", NodeTypeId),
            Element("test:b", NodeTypeId),
            Element("test:c", NodeTypeId),
        ];
        SemanticRelationshipSnapshot[] relationships =
        [
            Relationship("test:ab", elements[0].Id, elements[1].Id),
            Relationship("test:bc", elements[1].Id, elements[2].Id),
        ];
        VisualStateSnapshot[] visualStates =
        [
            Visual("test:visual-a", elements[0].Id),
            Visual(
                "test:visual-ab",
                relationships[0].Id,
                [new PointD(1d, 2d), new PointD(3d, 4d)]),
        ];
        if (reverseInputs)
        {
            Array.Reverse(elements);
            Array.Reverse(relationships);
            Array.Reverse(visualStates);
        }

        return Snapshot(elements, relationships, visualStates);
    }

    private static DocumentSnapshot Snapshot(
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot>? relationships = null,
        IEnumerable<VisualStateSnapshot>? visualStates = null,
        IEnumerable<DocumentScopeSnapshot>? nestedScopes = null,
        IEnumerable<SemanticElementScopeMembershipSnapshot>? scopeMemberships = null) =>
        new(
            new SemanticModelSnapshot(
                DocumentId,
                Revision,
                elements,
                relationships,
                nestedScopes,
                scopeMemberships),
            new VisualModelSnapshot(DocumentId, Revision, visualStates),
            new DocumentMetadataSnapshot(DocumentId, Revision));

    private static void AssertProjectedSources(
        ProjectedGraph graph,
        IEnumerable<SemanticElementId> expectedNodeIds,
        IEnumerable<string> expectedEdgeIds)
    {
        Assert.Equal(
            expectedNodeIds.Select(static id => id.Value).Order(),
            graph.Nodes
                .Select(static node => node.Source.SemanticElementId.Value)
                .Order());
        Assert.Equal(
            expectedEdgeIds.Order(),
            graph.Edges
                .Select(static edge => edge.Source.SemanticElementId.Value)
                .Order());
    }

    private static SemanticElementSnapshot Element(string id, SemanticTypeId typeId) =>
        new(new SemanticElementId(id), typeId);

    private static SemanticElementSnapshot Element(string id, string typeId) =>
        Element(id, new SemanticTypeId(typeId));

    private static SemanticRelationshipSnapshot Relationship(
        string id,
        SemanticElementId sourceId,
        SemanticElementId targetId) =>
        new(new SemanticElementId(id), EdgeTypeId, sourceId, targetId);

    private static VisualStateSnapshot Visual(
        string id,
        SemanticElementId semanticId,
        IEnumerable<PointD>? route = null) =>
        new(
            new VisualStateId(id),
            semanticId,
            new PointD(10d, 20d),
            new SizeD(100d, 60d),
            VisualPlacementMode.Manual,
            route);

    private static ProjectionRuleRegistration Registration(
        string ruleId,
        ProjectionSourceKind sourceKind,
        SemanticTypeId typeId,
        IProjectionRule rule) =>
        new(new ProjectionRuleId(ruleId), sourceKind, typeId, rule);

    private static ProjectionRuleRegistration Registration(
        string ruleId,
        ProjectionSourceKind sourceKind,
        string typeId,
        IProjectionRule rule) =>
        Registration(ruleId, sourceKind, new SemanticTypeId(typeId), rule);

    private static ProjectionRuleResult NodeResult(
        ProjectionRuleInput input,
        ProjectionRuleId ruleId,
        string localKey) =>
        ProjectionRuleResult.Success(
            new ProjectionRuleContribution(
                nodes: [new ProjectedNode(Trace(input, ruleId, localKey))]));

    private static ProjectionSourceTrace Trace(
        ProjectionRuleInput input,
        ProjectionRuleId ruleId,
        string localKey,
        VisualStateId? visualStateId = null) =>
        new(
            input.DocumentId,
            ruleId,
            input.SourceKind,
            input.SemanticId,
            input.SemanticTypeId,
            localKey,
            visualStateId);

    private static ProjectedObjectId NodeId(
        DocumentId documentId,
        SemanticElementId semanticElementId) =>
        ProjectedObjectIdentity.Create(
            documentId,
            NodeRuleId,
            ProjectionSourceKind.SemanticElement,
            semanticElementId,
            ProjectedObjectKind.Node,
            "node");

    private static ProjectionSourceTrace NodeSource(
        ProjectedGraph graph,
        ProjectedObjectId nodeId) =>
        graph.Nodes.Single(node => node.Id == nodeId).Source;

    private static void AssertInvalidTraceability(ProjectionResult result)
    {
        Assert.Equal(ProjectionStatus.Failed, result.Status);
        Assert.Null(result.Graph);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ProjectionDiagnosticCodes.InvalidSourceTraceability);
    }

    private static void AssertCancelled(ProjectionResult result)
    {
        Assert.Equal(ProjectionStatus.Cancelled, result.Status);
        Assert.Null(result.Graph);
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Code == ProjectionDiagnosticCodes.Cancelled);
    }

    public enum RuleFault
    {
        Throw,
        NullResult,
        ReportedFailure,
    }

    private sealed class DelegateRule(
        Func<ProjectionRuleInput, CancellationToken, ProjectionRuleResult> project) : IProjectionRule
    {
        public ProjectionRuleResult Project(
            ProjectionRuleInput input,
            CancellationToken cancellationToken) =>
            project(input, cancellationToken);
    }
}
