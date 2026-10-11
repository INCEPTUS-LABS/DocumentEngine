using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Presentation;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed partial class PhaseA22MessageFlowDomainTests
{
    [Theory]
    [InlineData("a", "b")]
    [InlineData("pa", "pb")]
    [InlineData("pa", "b")]
    [InlineData("a", "pb")]
    public async Task ProjectionAndCanonicalSceneUseExactEndpointPortsAndProcessOwnedMessageRoute(string source, string target)
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.CreateFlow(source, target);
        var snapshot = fixture.Snapshot;
        var engine = fixture.Configuration.ProjectionEngine;
        var context = new ProcessScopePresentationContext(snapshot.SemanticModel.RootScopeId);
        var resolved = engine.ResolvePresentation(snapshot, context);
        Assert.True(resolved.Succeeded, Diagnostics(resolved.Diagnostics));
        var plan = resolved.Plan!;
        var projected = engine.Project(snapshot, plan);
        Assert.True(projected.IsSuccessful, Diagnostics(projected.Diagnostics));
        var graph = projected.Graph!;
        var edge = Assert.Single(graph.Edges, edge => edge.Source.SemanticTypeId == BpmnSemanticTypes.MessageFlow);
        Assert.Equal(Id("message"), edge.Source.SemanticElementId);
        Assert.Equal(Visual("message"), edge.Source.VisualStateId);
        Assert.Equal(Id(source), graph.Nodes.Single(node => node.Id == edge.SourceNodeId).Source.SemanticElementId);
        Assert.Equal(Id(target), graph.Nodes.Single(node => node.Id == edge.TargetNodeId).Source.SemanticElementId);
        Assert.Equal(Id(source), graph.Ports.Single(port => port.Id == edge.SourcePortId).Source.SemanticElementId);
        Assert.Equal(Id(target), graph.Ports.Single(port => port.Id == edge.TargetPortId).Source.SemanticElementId);
        var style = ConnectorVisualMetadata.Resolve(edge.ProjectedProperties);
        Assert.Equal(ConnectorEndpointDecoration.OpenCircle, style.SourceDecoration);
        Assert.Equal(ConnectorEndpointDecoration.OpenArrow, style.TargetDecoration);
        Assert.Equal<double>([6d, 4d], style.DashPattern);
        Assert.Contains(engine.Project(snapshot, snapshot.SemanticModel.RootScopeId).Graph!.Edges,
            item => item.Source.SemanticTypeId == BpmnSemanticTypes.MessageFlow);
        var scope = snapshot.VisualModel.RoutingScopes!.Value.Single(record => record.ScopeId == context.ScopeId);
        var layout = ConnectorRoutingStatePreparer.RestoreLocalLayout(graph, scope.Geometry);
        var routing = ConnectorRoutingStatePreparer.RestoreRouting(graph, layout, scope, BpmnAlgorithmIds.DefaultRouting);
        Assert.Equal(ConnectorRoutingOutcome.Path, fixture.MessageRoute.Outcome);
        var result = new Canvas2DSceneBuilder().Build(graph, layout, routing, snapshot.VisualModel, EditorStateSnapshot.Empty);
        Assert.True(result.Succeeded, Diagnostics(result.Diagnostics));
        var scene = result.Scene!;
        var line = scene.Items.Single(item => item.Id == Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector"));
        Assert.Equal(fixture.MessageRoute.Path.AsEnumerable(), Canvas2DConnectorPathMetadata.Resolve(line));
        Assert.Equal(style.DashPattern.AsEnumerable(), line.Style.DashPattern);
        Assert.Contains(scene.Items, item => item.Origin.VisualStateId == Visual("message") &&
            item.Geometry.Kind == Canvas2DSceneGeometryKind.Ellipse);
        var arrow = Assert.Single(scene.Items, item => item.Origin.VisualStateId == Visual("message") &&
            item.Metadata.ContainsKey(Canvas2DConnectorArrowMetadata.TargetArrow));
        Assert.True(arrow.Geometry.IsClosed);
        Assert.Equal("#ffffff", arrow.Style.Fill);
        Assert.Equal(snapshot, fixture.Snapshot);
    }

    [Theory]
    [InlineData(ConnectorEndpointKind.Source, "pa")]
    [InlineData(ConnectorEndpointKind.Target, "pb")]
    [InlineData(ConnectorEndpointKind.Target, "catch")]
    public async Task ReconnectionFactoryUsesTypedMembershipAndProducesNormalDomainCommand(ConnectorEndpointKind endpoint, string candidate)
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.CreateFlow();
        var snapshot = fixture.Snapshot;
        var context = new ProcessScopePresentationContext(snapshot.SemanticModel.RootScopeId);
        var engine = fixture.Configuration.ProjectionEngine;
        var plan = engine.ResolvePresentation(snapshot, context).Plan!;
        var registration = BpmnPluginRegistration.N100.ConnectorEndpointReconnectionRegistrations.Single(item =>
            item.ReconnectionId.Value == "bpmn:endpoint-reconnection/message-flow");
        var original = endpoint == ConnectorEndpointKind.Source ? "a" : "b";
        var role = endpoint == ConnectorEndpointKind.Source ? "source" : "target";
        var request = new ConnectorEndpointReconnectionRequest(context, plan, snapshot, snapshot.Revision,
            Id("message"), Visual("message"), endpoint, Id(original), Anchor(original, role),
            Id(candidate), Visual(candidate), Anchor(candidate, role));
        Assert.True(registration.CommandFactory.CanStart(new ConnectorEndpointReconnectionStartRequest(context, plan,
            snapshot, snapshot.Revision, Id("message"), Visual("message"), endpoint, Id(original), Anchor(original, role))));
        Assert.True(registration.CandidateEligibility!.IsEligible(request));
        var planned = registration.CommandFactory.CreatePlan(request);
        Assert.True(planned.Succeeded, Diagnostics(planned.Diagnostics));
        var command = Assert.IsType<ReconnectBpmnMessageFlowEndpointCommand>(planned.Plan!.Command);
        Assert.Equal(Id(candidate), command.NewSemanticElementId);
        Assert.Equal(Visual("message"), command.ConnectorVisualStateId);
        Assert.Same(snapshot, fixture.Snapshot);
        await fixture.Commit(command, history: true);
        Assert.Equal(Id(candidate), endpoint == ConnectorEndpointKind.Source ? fixture.Flow.SourceId : fixture.Flow.TargetId);
        Assert.Equal(snapshot.VisualModel.VisualStates.Where(item => item.Id != Visual("message")),
            fixture.Snapshot.VisualModel.VisualStates.Where(item => item.Id != Visual("message")));
    }

    [Theory]
    [InlineData(ConnectorEndpointKind.Source, "b")]
    [InlineData(ConnectorEndpointKind.Source, "gateway")]
    [InlineData(ConnectorEndpointKind.Target, "a2")]
    [InlineData(ConnectorEndpointKind.Target, "throw")]
    public async Task ReconnectionCandidateAuthorityRejectsDirectionAndParticipantViolations(ConnectorEndpointKind endpoint, string candidate)
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.CreateFlow();
        var snapshot = fixture.Snapshot;
        var context = new ProcessScopePresentationContext(snapshot.SemanticModel.RootScopeId);
        var plan = fixture.Configuration.ProjectionEngine
            .ResolvePresentation(snapshot, context).Plan!;
        var registration = BpmnPluginRegistration.N100.ConnectorEndpointReconnectionRegistrations.Single(item =>
            item.ReconnectionId.Value == "bpmn:endpoint-reconnection/message-flow");
        var original = endpoint == ConnectorEndpointKind.Source ? "a" : "b";
        var role = endpoint == ConnectorEndpointKind.Source ? "source" : "target";
        var request = new ConnectorEndpointReconnectionRequest(context, plan, snapshot, snapshot.Revision,
            Id("message"), Visual("message"), endpoint, Id(original), Anchor(original, role),
            Id(candidate), Visual(candidate), Anchor(candidate, role));
        Assert.False(registration.CandidateEligibility!.IsEligible(request));
        Assert.False(registration.CommandFactory.CreatePlan(request).Succeeded);
        Assert.False(registration.CommandFactory.CanStart(new ConnectorEndpointReconnectionStartRequest(snapshot,
            snapshot.Revision, Id("message"), Visual("message"), endpoint, Id(original), Anchor(original, role))));
        Assert.Same(snapshot, fixture.Snapshot);
    }
}
