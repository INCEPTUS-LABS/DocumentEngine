using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Projection;

public sealed class ProjectionResultTests
{
    private static readonly DocumentId DocumentId = new("test:document");
    private static readonly DocumentRevision Revision = new(11);

    [Fact]
    public void SuccessfulProjectionCarriesTheExactCompatibleGraph()
    {
        var graph = new ProjectedGraph(DocumentId, Revision);
        var result = ProjectionResult.Success(
            graph,
            [new Diagnostic("TEST_WARNING", DiagnosticSeverity.Warning, "warning")]);

        Assert.True(result.IsSuccessful);
        Assert.Equal(ProjectionStatus.Succeeded, result.Status);
        Assert.Equal(DocumentId, result.DocumentId);
        Assert.Equal(Revision, result.SourceRevision);
        Assert.Same(graph, result.Graph);
        Assert.Single(result.Diagnostics);
        Assert.Throws<ArgumentNullException>(() => ProjectionResult.Success(null!));
        Assert.Throws<ArgumentException>(() => ProjectionResult.Success(
            graph,
            [new Diagnostic("TEST_ERROR", DiagnosticSeverity.Error, "error")]));
    }

    [Fact]
    public void FailedAndCancelledProjectionNeverExposeAPartialGraph()
    {
        var failure = ProjectionResult.Failure(
            DocumentId,
            Revision,
            [new Diagnostic("TEST_ERROR", DiagnosticSeverity.Error, "error")]);
        var cancelled = ProjectionResult.Cancelled(
            DocumentId,
            Revision,
            [new Diagnostic(
                ProjectionDiagnosticCodes.Cancelled,
                DiagnosticSeverity.Information,
                "cancelled")]);

        Assert.False(failure.IsSuccessful);
        Assert.Equal(ProjectionStatus.Failed, failure.Status);
        Assert.Null(failure.Graph);
        Assert.False(cancelled.IsSuccessful);
        Assert.Equal(ProjectionStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.Graph);
        Assert.Throws<ArgumentException>(() => ProjectionResult.Failure(
            DocumentId,
            Revision,
            []));
    }

    [Fact]
    public void ProjectionResultsDefensivelyCopyAndStructurallyCompareDiagnostics()
    {
        var source = new List<Diagnostic>
        {
            new("TEST_Z", DiagnosticSeverity.Warning, "z", "test:z"),
            new("TEST_A", DiagnosticSeverity.Information, "a", "test:a"),
        };
        var first = ProjectionResult.Success(new ProjectedGraph(DocumentId, Revision), source);
        source.Clear();
        var same = ProjectionResult.Success(
            new ProjectedGraph(new DocumentId("test:document"), new DocumentRevision(11)),
            [
                new Diagnostic("TEST_A", DiagnosticSeverity.Information, "a", "test:a"),
                new Diagnostic("TEST_Z", DiagnosticSeverity.Warning, "z", "test:z"),
            ]);

        Assert.Equal(["TEST_A", "TEST_Z"],
            first.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        var mutableView = (IList<Diagnostic>)first.Diagnostics;
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
    }

    [Fact]
    public void SuccessfulRuleResultRequiresAContributionAndContainsNoErrors()
    {
        var warning = new Diagnostic(
            "TEST_WARNING",
            DiagnosticSeverity.Warning,
            "warning");

        var result = ProjectionRuleResult.Success(
            ProjectionRuleContribution.Empty,
            [warning]);

        Assert.True(result.Succeeded);
        Assert.Same(ProjectionRuleContribution.Empty, result.Contribution);
        Assert.Equal(warning, Assert.Single(result.Diagnostics));
        Assert.Throws<ArgumentNullException>(() => ProjectionRuleResult.Success(null!));
        Assert.Throws<ArgumentException>(() => ProjectionRuleResult.Success(
            ProjectionRuleContribution.Empty,
            [new Diagnostic("TEST_ERROR", DiagnosticSeverity.Error, "error")]));
    }

    [Fact]
    public void FailedRuleResultRequiresAnErrorAndExposesNoContribution()
    {
        var error = new Diagnostic("TEST_ERROR", DiagnosticSeverity.Error, "error");

        var result = ProjectionRuleResult.Failure([error]);

        Assert.False(result.Succeeded);
        Assert.Null(result.Contribution);
        Assert.Equal(error, Assert.Single(result.Diagnostics));
        Assert.Throws<ArgumentException>(() => ProjectionRuleResult.Failure([]));
        Assert.Throws<ArgumentException>(() => ProjectionRuleResult.Failure(
            [new Diagnostic("TEST_WARNING", DiagnosticSeverity.Warning, "warning")]));
    }

    [Fact]
    public void RuleDiagnosticsAreDefensivelyCopiedCanonicallyOrderedAndStructurallyCompared()
    {
        var source = new List<Diagnostic>
        {
            new("TEST_Z", DiagnosticSeverity.Warning, "z", "test:z"),
            new("TEST_A", DiagnosticSeverity.Information, "a", "test:a"),
        };
        var first = ProjectionRuleResult.Success(ProjectionRuleContribution.Empty, source);
        source.Clear();
        var same = ProjectionRuleResult.Success(
            new ProjectionRuleContribution(),
            [
                new Diagnostic("TEST_A", DiagnosticSeverity.Information, "a", "test:a"),
                new Diagnostic("TEST_Z", DiagnosticSeverity.Warning, "z", "test:z"),
            ]);

        Assert.Equal(["TEST_A", "TEST_Z"],
            first.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        var mutableView = (IList<Diagnostic>)first.Diagnostics;
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
    }

    [Fact]
    public void RuleResultsRejectNullDiagnostics()
    {
        Assert.Throws<ArgumentException>(() => ProjectionRuleResult.Success(
            ProjectionRuleContribution.Empty,
            [null!]));
        Assert.Throws<ArgumentException>(() => ProjectionRuleResult.Failure([null!]));
    }
}
