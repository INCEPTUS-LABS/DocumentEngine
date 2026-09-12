using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class CommandValidationResultTests
{
    [Fact]
    public void ResultRetainsValidatedSnapshotAndCommandIdentity()
    {
        var result = new CommandValidationResult(
            new DocumentId("test:document"),
            new DocumentRevision(6),
            new CommandTypeId("test:command"));

        Assert.Equal(new DocumentId("test:document"), result.DocumentId);
        Assert.Equal(new DocumentRevision(6), result.Revision);
        Assert.Equal(new CommandTypeId("test:command"), result.CommandTypeId);
        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void DiagnosticsAreDefensivelyCopiedAndOrderedDeterministically()
    {
        var source = new List<Diagnostic>
        {
            new("TEST_Z", DiagnosticSeverity.Warning, "warning", "test:z"),
            new("TEST_A", DiagnosticSeverity.Information, "information", "test:a"),
        };
        var result = Result(source);
        source.Clear();
        source.Add(new Diagnostic("TEST_OTHER", DiagnosticSeverity.Error, "later"));

        Assert.Equal(["TEST_A", "TEST_Z"],
            result.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void AnyErrorMakesTheResultInvalidWhileWarningsDoNot()
    {
        var warning = Result(
            [new Diagnostic("TEST_WARNING", DiagnosticSeverity.Warning, "warning")]);
        var error = Result(
        [
            new Diagnostic("TEST_WARNING", DiagnosticSeverity.Warning, "warning"),
            new Diagnostic("TEST_ERROR", DiagnosticSeverity.Error, "error"),
        ]);

        Assert.True(warning.IsValid);
        Assert.False(error.IsValid);
    }

    [Fact]
    public void NullDiagnosticsAndIdentityAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Result([null!]));
        Assert.Throws<ArgumentNullException>(() => new CommandValidationResult(
            null!,
            DocumentRevision.Zero,
            new CommandTypeId("test:command")));
        Assert.Throws<ArgumentNullException>(() => new CommandValidationResult(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            null!));
    }

    [Fact]
    public void IndependentResultsUseDeepDiagnosticEqualityAndHashing()
    {
        var first = Result(
        [
            new Diagnostic(
                "TEST_B",
                DiagnosticSeverity.Warning,
                "second",
                "test:source",
                [new KeyValuePair<string, string>("test:key", "value")]),
            new Diagnostic("TEST_A", DiagnosticSeverity.Information, "first"),
        ]);
        var same = Result(
        [
            new Diagnostic("TEST_A", DiagnosticSeverity.Information, "first"),
            new Diagnostic(
                "TEST_B",
                DiagnosticSeverity.Warning,
                "second",
                "test:source",
                [new KeyValuePair<string, string>("test:key", "value")]),
        ]);
        var different = Result(
            [new Diagnostic("TEST_A", DiagnosticSeverity.Information, "changed")]);

        Assert.Equal(first, same);
        Assert.True(first == same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
        Assert.True(first != different);
    }

    [Fact]
    public void ExposedDiagnosticsCannotBeModified()
    {
        var result = Result(
            [new Diagnostic("TEST", DiagnosticSeverity.Warning, "warning")]);
        var mutableView = (IList<Diagnostic>)result.Diagnostics;

        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
        Assert.Single(result.Diagnostics);
    }

    private static CommandValidationResult Result(IEnumerable<Diagnostic> diagnostics) =>
        new(
            new DocumentId("test:document"),
            new DocumentRevision(6),
            new CommandTypeId("test:command"),
            diagnostics);
}
