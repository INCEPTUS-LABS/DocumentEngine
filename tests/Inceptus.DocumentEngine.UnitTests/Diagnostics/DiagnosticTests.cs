using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.UnitTests.Diagnostics;

public sealed class DiagnosticTests
{
    [Theory]
    [InlineData(DiagnosticSeverity.Information)]
    [InlineData(DiagnosticSeverity.Warning)]
    [InlineData(DiagnosticSeverity.Error)]
    public void DiagnosticRetainsImmutableValues(DiagnosticSeverity severity)
    {
        var sourceContext = new Dictionary<string, string>
        {
            ["zeta"] = "last",
            ["Alpha"] = "first",
        };

        var diagnostic = new Diagnostic(
            "ARCH001",
            severity,
            "A diagnostic message.",
            "semantic:42",
            sourceContext);

        sourceContext["zeta"] = "changed";
        sourceContext["added"] = "later";

        Assert.Equal("ARCH001", diagnostic.Code);
        Assert.Equal(severity, diagnostic.Severity);
        Assert.Equal("A diagnostic message.", diagnostic.Message);
        Assert.Equal("semantic:42", diagnostic.SourceIdentity);
        Assert.Equal("last", diagnostic.Context["zeta"]);
        Assert.False(diagnostic.Context.ContainsKey("added"));
        Assert.Equal(["Alpha", "zeta"], diagnostic.Context.Keys);
        Assert.Equal(2, diagnostic.Context.Count);

        var extended = diagnostic.Context.Add("later", "value");
        Assert.Equal(2, diagnostic.Context.Count);
        Assert.Equal(3, extended.Count);
    }

    [Fact]
    public void ContextDefaultsToAnImmutableEmptyCollection()
    {
        var diagnostic = new Diagnostic("CODE", DiagnosticSeverity.Information, "Message");

        Assert.Null(diagnostic.SourceIdentity);
        Assert.Empty(diagnostic.Context);
    }

    [Fact]
    public void DiagnosticRejectsInvalidValues()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new Diagnostic(null!, DiagnosticSeverity.Error, "Message"));
        Assert.Throws<ArgumentException>(() =>
            new Diagnostic(" ", DiagnosticSeverity.Error, "Message"));
        Assert.Throws<ArgumentNullException>(() =>
            new Diagnostic("CODE", DiagnosticSeverity.Error, null!));
        Assert.Throws<ArgumentException>(() =>
            new Diagnostic("CODE", DiagnosticSeverity.Error, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Diagnostic("CODE", (DiagnosticSeverity)99, "Message"));
        Assert.Throws<ArgumentException>(() =>
            new Diagnostic("CODE", DiagnosticSeverity.Error, "Message", " "));
        Assert.Throws<ArgumentException>(() =>
            new Diagnostic(
                "CODE",
                DiagnosticSeverity.Error,
                "Message",
                context: [new KeyValuePair<string, string>(" ", "value")]));
        Assert.Throws<ArgumentNullException>(() =>
            new Diagnostic(
                "CODE",
                DiagnosticSeverity.Error,
                "Message",
                context: [new KeyValuePair<string, string>("key", null!)]));
    }
}

