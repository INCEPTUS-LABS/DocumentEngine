using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Layout;

public sealed class LayoutResultTests
{
    private static readonly DocumentId DocumentId = new("test:layout-document");
    private static readonly DocumentRevision Revision = new(7);
    private static readonly AlgorithmId AlgorithmId = new("test:layout:algorithm");
    private static readonly ProjectedObjectId NodeAId = new("test:projected:node-a");
    private static readonly ProjectedObjectId NodeBId = new("test:projected:node-b");
    private static readonly ProjectedObjectId GroupId = new("test:projected:group");

    [Fact]
    public void GeometryPreservesProjectedIdentityAndDocumentCoordinateValues()
    {
        var bounds = new RectD(10d, 20d, 100d, 40d);
        var transform = Matrix2D.CreateTranslation(10d, 20d);
        var node = new LayoutNodeGeometry(NodeAId, bounds, transform);
        var group = new LayoutGroupGeometry(GroupId, new RectD(0d, 0d, 300d, 200d));

        Assert.Equal(NodeAId, node.ProjectedObjectId);
        Assert.Equal(bounds, node.Bounds);
        Assert.Equal(bounds.TopLeft, node.Position);
        Assert.Equal(bounds.Size, node.Size);
        Assert.Equal(transform, node.Transform);
        Assert.Equal(GroupId, group.ProjectedObjectId);
        Assert.Equal(new RectD(0d, 0d, 300d, 200d), group.Bounds);
        Assert.Throws<ArgumentNullException>(() =>
            new LayoutNodeGeometry(null!, bounds, transform));
        Assert.Throws<ArgumentNullException>(() =>
            new LayoutGroupGeometry(null!, bounds));
    }

    [Fact]
    public void ComputationDefensivelyCopiesCanonicallyOrdersAndStructurallyCompares()
    {
        var nodes = new List<LayoutNodeGeometry>
        {
            Node(NodeBId, 200d, 20d),
            Node(NodeAId, 10d, 20d),
        };
        var groups = new List<LayoutGroupGeometry>
        {
            new(GroupId, new RectD(0d, 0d, 400d, 200d)),
        };
        var metadata = new List<KeyValuePair<string, PropertyValue>>
        {
            new("test:z", PropertyValue.FromInteger(2)),
            new("test:a", PropertyValue.FromInteger(1)),
        };

        var first = new LayoutComputation(nodes, groups, metadata);
        nodes.Clear();
        groups.Clear();
        metadata.Clear();
        var same = new LayoutComputation(
            [Node(NodeAId, 10d, 20d), Node(NodeBId, 200d, 20d)],
            [new LayoutGroupGeometry(GroupId, new RectD(0d, 0d, 400d, 200d))],
            [
                new("test:a", PropertyValue.FromInteger(1)),
                new("test:z", PropertyValue.FromInteger(2)),
            ]);

        Assert.Equal([NodeAId, NodeBId],
            first.Nodes.Select(static geometry => geometry.ProjectedObjectId));
        Assert.Single(first.Groups);
        Assert.Equal(["test:a", "test:z"], first.Metadata.Keys);
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.Throws<NotSupportedException>(() =>
            ((IList<LayoutNodeGeometry>)first.Nodes).Clear());
        Assert.DoesNotContain(
            typeof(IDictionary<string, PropertyValue>),
            first.Metadata.GetType().GetInterfaces());
    }

    [Fact]
    public void ComputationRejectsDuplicateNullAndCrossCategoryGeometry()
    {
        var node = Node(NodeAId, 10d, 20d);

        Assert.Throws<ArgumentException>(() => new LayoutComputation([node, node]));
        Assert.Throws<ArgumentException>(() => new LayoutComputation(nodes: [null!]));
        Assert.Throws<ArgumentException>(() => new LayoutComputation(
            [node],
            [new LayoutGroupGeometry(NodeAId, node.Bounds)]));
    }

    [Fact]
    public void SuccessfulResultCarriesProvenanceGeometryMetadataAndWarnings()
    {
        var warning = new Diagnostic(
            "TEST_LAYOUT_WARNING",
            DiagnosticSeverity.Warning,
            "warning");
        var diagnostics = new List<Diagnostic> { warning };
        var computation = new LayoutComputation(
            [Node(NodeAId, 10d, 20d)],
            metadata: [new("test:direction", PropertyValue.FromText("right"))]);
        var result = new LayoutResult(
            DocumentId,
            Revision,
            AlgorithmId,
            computation,
            diagnostics);
        diagnostics.Clear();
        var execution = LayoutExecutionResult.Success(result);

        Assert.Equal(DocumentId, result.DocumentId);
        Assert.Equal(Revision, result.SourceRevision);
        Assert.Equal(AlgorithmId, result.AlgorithmId);
        Assert.Same(computation, result.Computation);
        Assert.Single(result.Nodes);
        Assert.Empty(result.Groups);
        Assert.Equal("right", result.Metadata["test:direction"].TextValue);
        Assert.Equal(warning, Assert.Single(result.Diagnostics));
        Assert.True(execution.IsSuccessful);
        Assert.Equal(LayoutExecutionStatus.Succeeded, execution.Status);
        Assert.Same(result, execution.Result);
        Assert.Equal(result.Diagnostics.Length, execution.Diagnostics.Length);
        Assert.Same(result.Diagnostics[0], execution.Diagnostics[0]);
    }

    [Fact]
    public void ResultRejectsErrorDiagnosticsAndExecutionFailureExposesNoPartialResult()
    {
        var error = new Diagnostic(
            LayoutDiagnosticCodes.InvalidGeometry,
            DiagnosticSeverity.Error,
            "invalid geometry");
        var computation = new LayoutComputation([Node(NodeAId, 10d, 20d)]);

        Assert.Throws<ArgumentException>(() => new LayoutResult(
            DocumentId,
            Revision,
            AlgorithmId,
            computation,
            [error]));

        var failure = LayoutExecutionResult.Failure(
            DocumentId,
            Revision,
            AlgorithmId,
            [error]);
        var cancelled = LayoutExecutionResult.Cancelled(
            DocumentId,
            Revision,
            AlgorithmId,
            [new Diagnostic(
                LayoutDiagnosticCodes.Cancelled,
                DiagnosticSeverity.Information,
                "cancelled")]);

        Assert.False(failure.IsSuccessful);
        Assert.Equal(LayoutExecutionStatus.Failed, failure.Status);
        Assert.Null(failure.Result);
        Assert.Equal(LayoutExecutionStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.Result);
        Assert.Throws<ArgumentException>(() => LayoutExecutionResult.Failure(
            DocumentId,
            Revision,
            AlgorithmId,
            []));
    }

    [Fact]
    public void AlgorithmResultsAreDeeplyImmutableAndStructurallyComparable()
    {
        var diagnostics = new List<Diagnostic>
        {
            new("TEST_Z", DiagnosticSeverity.Warning, "z"),
            new("TEST_A", DiagnosticSeverity.Information, "a"),
        };
        var computation = new LayoutComputation([Node(NodeAId, 10d, 20d)]);
        var first = LayoutAlgorithmResult.Success(computation, diagnostics);
        diagnostics.Clear();
        var same = LayoutAlgorithmResult.Success(
            new LayoutComputation([Node(NodeAId, 10d, 20d)]),
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

        var failure = LayoutAlgorithmResult.Failure(
            [new Diagnostic("TEST_ERROR", DiagnosticSeverity.Error, "failed")]);
        Assert.False(failure.Succeeded);
        Assert.Null(failure.Computation);
        Assert.Throws<ArgumentException>(() => LayoutAlgorithmResult.Failure([]));
        Assert.Throws<ArgumentException>(() => LayoutAlgorithmResult.Success(
            computation,
            [new Diagnostic("TEST_ERROR", DiagnosticSeverity.Error, "failed")]));
    }

    private static LayoutNodeGeometry Node(
        ProjectedObjectId id,
        double x,
        double y) =>
        new(
            id,
            new RectD(x, y, 100d, 40d),
            Matrix2D.CreateTranslation(x, y));
}
