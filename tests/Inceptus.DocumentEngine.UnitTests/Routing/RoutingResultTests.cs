using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.UnitTests.Routing;

public sealed class RoutingResultTests
{
    private static readonly DocumentId DocumentId = new("test:routing-document");
    private static readonly DocumentRevision Revision = new(7);
    private static readonly AlgorithmId LayoutAlgorithmId = new("test:layout:algorithm");
    private static readonly AlgorithmId RoutingAlgorithmId = new("test:routing:algorithm");
    private static readonly ProjectedObjectId EdgeAId = new("test:projected:edge-a");
    private static readonly ProjectedObjectId EdgeBId = new("test:projected:edge-b");
    private static readonly ProjectedObjectId EdgeCId = new("test:projected:edge-c");
    private static readonly ProjectedObjectId EdgeDId = new("test:projected:edge-d");
    private static readonly ProjectedObjectId SourcePortId = new("test:projected:port-source");
    private static readonly ProjectedObjectId TargetPortId = new("test:projected:port-target");

    [Fact]
    public void ConnectorGeometryDefensivelyCopiesPathAndPreservesTraceability()
    {
        var bends = new List<PointD> { new(30d, 20d), new(30d, 80d) };
        var metadata = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:style", PropertyValue.FromText("orthogonal")),
        };
        var route = new RoutedConnectorGeometry(
            EdgeAId,
            new PointD(10d, 20d),
            new PointD(90d, 80d),
            bends,
            SourcePortId,
            TargetPortId,
            metadata);
        bends.Clear();
        metadata.Clear();

        Assert.Equal(EdgeAId, route.ProjectedEdgeId);
        Assert.Equal(new PointD(10d, 20d), route.SourceAnchor);
        Assert.Equal(new PointD(90d, 80d), route.DestinationAnchor);
        Assert.Equal(
            [new PointD(30d, 20d), new PointD(30d, 80d)],
            route.BendPoints.AsEnumerable());
        Assert.Equal(
            [
                new PointD(10d, 20d),
                new PointD(30d, 20d),
                new PointD(30d, 80d),
                new PointD(90d, 80d),
            ],
            route.Path.AsEnumerable());
        Assert.Equal(SourcePortId, route.SourcePortId);
        Assert.Equal(TargetPortId, route.TargetPortId);
        Assert.Equal("orthogonal", route.Metadata["test:style"].TextValue);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PointD>)route.BendPoints).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PointD>)route.Path).Clear());
        Assert.Throws<ArgumentNullException>(() => new RoutedConnectorGeometry(
            null!,
            new PointD(0d, 0d),
            new PointD(1d, 1d)));
    }

    [Fact]
    public void ConnectorGeometryHasStructuralEquality()
    {
        var first = Route(EdgeAId, 0d);
        var same = new RoutedConnectorGeometry(
            new ProjectedObjectId(EdgeAId.Value),
            first.SourceAnchor,
            first.DestinationAnchor,
            first.BendPoints);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void ComputationDefensivelyCopiesCanonicallyOrdersAndRejectsDuplicates()
    {
        var routes = new List<RoutedConnectorGeometry>
        {
            Route(EdgeBId, 100d),
            Route(EdgeAId, 0d),
        };
        var metadata = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:z", PropertyValue.FromInteger(2)),
            new("test:a", PropertyValue.FromInteger(1)),
        };
        var noRouteEdgeIds = new List<ProjectedObjectId> { EdgeDId, EdgeCId };
        var first = new RoutingComputation(routes, metadata, noRouteEdgeIds);
        routes.Clear();
        metadata.Clear();
        noRouteEdgeIds.Clear();
        var same = new RoutingComputation(
            [Route(EdgeAId, 0d), Route(EdgeBId, 100d)],
            [
                new("test:a", PropertyValue.FromInteger(1)),
                new("test:z", PropertyValue.FromInteger(2)),
            ],
            [EdgeCId, EdgeDId]);

        Assert.Equal([EdgeAId, EdgeBId],
            first.Routes.Select(static route => route.ProjectedEdgeId));
        Assert.Equal([EdgeCId, EdgeDId], first.NoRouteEdgeIds.AsEnumerable());
        Assert.Equal(["test:a", "test:z"], first.Metadata.Keys);
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, new RoutingComputation(
            [Route(EdgeAId, 0d), Route(EdgeBId, 100d)],
            [
                new("test:a", PropertyValue.FromInteger(1)),
                new("test:z", PropertyValue.FromInteger(2)),
            ],
            [EdgeCId]));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<RoutedConnectorGeometry>)first.Routes).Clear());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ProjectedObjectId>)first.NoRouteEdgeIds).Clear());
        Assert.Throws<ArgumentException>(() => new RoutingComputation(
            [Route(EdgeAId, 0d), Route(EdgeAId, 50d)]));
        Assert.Throws<ArgumentException>(() => new RoutingComputation([null!]));
        Assert.Throws<ArgumentException>(() => new RoutingComputation(
            noRouteEdgeIds: [EdgeCId, EdgeCId]));
        Assert.Throws<ArgumentException>(() => new RoutingComputation(
            noRouteEdgeIds: [null!]));
    }

    [Fact]
    public void SuccessfulResultCarriesProvenanceRoutesMetadataAndWarnings()
    {
        var warning = new Diagnostic("TEST_ROUTING_WARNING", DiagnosticSeverity.Warning, "warning");
        var diagnostics = new List<Diagnostic> { warning };
        var computation = new RoutingComputation(
            [Route(EdgeAId, 0d)],
            [new("test:style", PropertyValue.FromText("polyline"))],
            [EdgeBId]);
        var result = new RoutingResult(
            DocumentId,
            Revision,
            LayoutAlgorithmId,
            RoutingAlgorithmId,
            computation,
            diagnostics);
        diagnostics.Clear();
        var execution = RoutingExecutionResult.Success(result);

        Assert.Equal(DocumentId, result.DocumentId);
        Assert.Equal(Revision, result.SourceRevision);
        Assert.Equal(LayoutAlgorithmId, result.LayoutAlgorithmId);
        Assert.Equal(RoutingAlgorithmId, result.RoutingAlgorithmId);
        Assert.Same(computation, result.Computation);
        Assert.Single(result.Routes);
        Assert.Equal(EdgeBId, Assert.Single(result.NoRouteEdgeIds));
        Assert.Equal("polyline", result.Metadata["test:style"].TextValue);
        Assert.Equal(warning, Assert.Single(result.Diagnostics));
        Assert.True(execution.IsSuccessful);
        Assert.Equal(RoutingExecutionStatus.Succeeded, execution.Status);
        Assert.Same(result, execution.Result);
        Assert.Equal(result.Diagnostics.Length, execution.Diagnostics.Length);
    }

    [Fact]
    public void FailuresAndCancellationExposeNoPartialResult()
    {
        var error = new Diagnostic(
            RoutingDiagnosticCodes.InvalidPath,
            DiagnosticSeverity.Error,
            "invalid path");
        var computation = new RoutingComputation([Route(EdgeAId, 0d)]);

        Assert.Throws<ArgumentException>(() => new RoutingResult(
            DocumentId,
            Revision,
            LayoutAlgorithmId,
            RoutingAlgorithmId,
            computation,
            [error]));

        var failure = RoutingExecutionResult.Failure(
            DocumentId,
            Revision,
            LayoutAlgorithmId,
            RoutingAlgorithmId,
            [error]);
        var cancelled = RoutingExecutionResult.Cancelled(
            DocumentId,
            Revision,
            LayoutAlgorithmId,
            RoutingAlgorithmId,
            [new Diagnostic(
                RoutingDiagnosticCodes.Cancelled,
                DiagnosticSeverity.Information,
                "cancelled")]);

        Assert.False(failure.IsSuccessful);
        Assert.Equal(RoutingExecutionStatus.Failed, failure.Status);
        Assert.Null(failure.Result);
        Assert.Equal(RoutingExecutionStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.Result);
        Assert.Throws<ArgumentException>(() => RoutingExecutionResult.Failure(
            DocumentId,
            Revision,
            LayoutAlgorithmId,
            RoutingAlgorithmId,
            []));
    }

    [Fact]
    public void AlgorithmResultsAreImmutableAndStructurallyComparable()
    {
        var diagnostics = new List<Diagnostic>
        {
            new("TEST_Z", DiagnosticSeverity.Warning, "z"),
            new("TEST_A", DiagnosticSeverity.Information, "a"),
        };
        var computation = new RoutingComputation([Route(EdgeAId, 0d)]);
        var first = RoutingAlgorithmResult.Success(computation, diagnostics);
        diagnostics.Clear();
        var same = RoutingAlgorithmResult.Success(
            new RoutingComputation([Route(EdgeAId, 0d)]),
            [
                new Diagnostic("TEST_A", DiagnosticSeverity.Information, "a"),
                new Diagnostic("TEST_Z", DiagnosticSeverity.Warning, "z"),
            ]);

        Assert.True(first.Succeeded);
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.Equal(["TEST_A", "TEST_Z"],
            first.Diagnostics.Select(static diagnostic => diagnostic.Code));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Diagnostic>)first.Diagnostics).Clear());

        var failure = RoutingAlgorithmResult.Failure(
            [new Diagnostic("TEST_ERROR", DiagnosticSeverity.Error, "failed")]);
        Assert.False(failure.Succeeded);
        Assert.Null(failure.Computation);
        Assert.Throws<ArgumentException>(() => RoutingAlgorithmResult.Failure([]));
        Assert.Throws<ArgumentException>(() => RoutingAlgorithmResult.Success(
            computation,
            [new Diagnostic("TEST_ERROR", DiagnosticSeverity.Error, "failed")]));
    }

    private static RoutedConnectorGeometry Route(ProjectedObjectId edgeId, double offset) =>
        new(
            edgeId,
            new PointD(offset, 10d),
            new PointD(offset + 80d, 50d),
            [new PointD(offset + 40d, 10d), new PointD(offset + 40d, 50d)]);
}
