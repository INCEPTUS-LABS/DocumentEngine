using System.Text;
using System.Text.Json;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Organizational.Scene;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class PhaseA3IntegrationTests
{
    [Fact]
    public async Task NativeRoundTripRetainsCompleteCanonicalSampleAndExactCodecBytes()
    {
        var source = (await BpmnDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync()).CaptureSnapshot();
        var bytes = BpmnDocumentSerializer.Serialize(source);
        using var json = JsonDocument.Parse(bytes.AsMemory());
        Assert.Equal("Inceptus.Document", json.RootElement.GetProperty("format").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("formatVersion").GetInt32());
        var read = BpmnDocumentSerializer.Deserialize(bytes.AsMemory());
        Assert.True(read.Succeeded);
        Assert.Equal(source, read.Document);
        Assert.Equal(bytes.ToArray(), BpmnDocumentSerializer.Serialize(read.Document!).ToArray());
        Assert.NotEmpty(read.Document!.SemanticModel.NestedScopes);
        Assert.NotNull(read.Document.VisualModel.RoutingScopes);
    }

    [Fact]
    public void CurrentMigrationPreservesTheExactSnapshotWithoutMutatingIt()
    {
        var document = CreateDocument();
        var migrated = BpmnDocumentMigrator.Migrate(document, BpmnDocumentSerializer.CurrentVersion);
        Assert.True(migrated.Succeeded);
        Assert.Same(document, migrated.Document);
        Assert.Empty(migrated.Diagnostics);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(999)]
    public void UnsupportedNativeVersionsAreExplicitAtBothCompatibilityBoundaries(int version)
    {
        var migrated = BpmnDocumentMigrator.Migrate(CreateDocument(), version);
        Assert.False(migrated.Succeeded);
        Assert.Null(migrated.Document);
        Assert.Equal("NATIVE_DOCUMENT_VERSION_UNSUPPORTED", Assert.Single(migrated.Diagnostics).Code);
        var json = Encoding.UTF8.GetBytes($$$"""{"format":"Inceptus.Document","formatVersion":{{{version}}},"document":{}} """);
        var read = BpmnDocumentSerializer.Deserialize(json);
        Assert.False(read.Succeeded);
        Assert.Null(read.Document);
        Assert.Equal("NATIVE_DOCUMENT_VERSION_UNSUPPORTED", Assert.Single(read.Diagnostics).Code);
    }

    [Fact]
    public void StandaloneValidatorAcceptsConnectedStartAndEndWithNoIssues()
    {
        var document = CreateDocument();
        var result = new BpmnDocumentValidator().Validate(document);
        Assert.True(result.IsStructurallyValid);
        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.Single(result.Reports);
    }

    [Fact]
    public void StandaloneValidatorPreservesTheExistingRulesAndDoesNotChangeInput()
    {
        var document = CreateDocument(includeFlow: false);
        var originalElements = document.SemanticModel.Elements;
        var actual = new BpmnDocumentValidator().Validate(document);
        var eligibility = new OrganizationalElementEligibilityPolicy(BpmnSemanticTypes.IsFlowNode);
        var organizational = OrganizationalPluginRegistration.Create(eligibility,
            OrganizationalPoolSceneContributor.CreateRegistration(eligibility));
        var catalog = new ModelValidationCatalog(BpmnPluginRegistration.N100.ModelValidationRules
            .AddRange(organizational.ModelValidationRules));
        var expected = new ModelValidationEngine(catalog).Validate(new ModelValidationContext(document));
        Assert.Equal(expected.Issues.Select(issue => (issue.Id, issue.Code, issue.Message, issue.Severity,
            issue.Target.SemanticElementId, issue.Target.VisualStateId)),
            actual.Reports[0].Issues.Select(issue => (issue.Id, issue.Code, issue.Message, issue.Severity,
                issue.Target.SemanticElementId, issue.Target.VisualStateId)));
        Assert.Contains(actual.Issues, issue => issue.Code == BpmnModelValidationCodes.StartEventNoOutgoing);
        Assert.Contains(actual.Issues, issue => issue.Code == BpmnModelValidationCodes.EndEventNoIncoming);
        Assert.Equal(originalElements, document.SemanticModel.Elements);
        Assert.Equal(DocumentRevision.Zero, document.Revision);
    }

    [Fact]
    public void MissingRelationshipEndpointIsRejectedWithoutAnEditor()
    {
        var document = CreateDocument(missingTarget: true);
        var result = new BpmnDocumentValidator().Validate(document);
        Assert.False(result.IsStructurallyValid);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Diagnostics);
        Assert.Empty(result.Reports);
    }

    [Fact]
    public void StandardConverterExposesActualInterchangeConversionWithDiagnostics()
    {
        Assert.True(BpmnStandardConverter.IsSupported);
        Assert.Equal("2.0.2", BpmnStandardConverter.StandardVersion);
        var export = BpmnStandardConverter.Export(CreateDocument());
        Assert.True(export.Succeeded, string.Join("; ", export.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var import = BpmnStandardConverter.Import(export.Xml!);
        foreach (var result in new[] { export, import })
        {
            Assert.True(result.Succeeded, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        }
        Assert.NotEmpty(export.ConversionLog); // Native-only fields remain explicit export losses.
        Assert.Empty(import.ConversionLog); // A3.2 accepts compatible defaults/metadata without false losses.
        Assert.Equal(2, import.Document!.SemanticModel.ElementCount);
        Assert.Single(import.Document.SemanticModel.Relationships);
    }

    [Fact]
    public void IntegrationAssemblyHasNoEditorDemoOrBrowserUiReference()
    {
        var references = typeof(BpmnDocumentSerializer).Assembly.GetReferencedAssemblies().Select(name => name.Name);
        Assert.DoesNotContain("Inceptus.DocumentEngine.Bpmn.Blazor", references);
        Assert.DoesNotContain("Inceptus.DocumentEngine.Blazor", references);
        Assert.DoesNotContain("Microsoft.AspNetCore.Components", references);
        Assert.DoesNotContain("Microsoft.JSInterop", references);
    }

    private static DocumentSnapshot CreateDocument(bool includeFlow = true, bool missingTarget = false)
    {
        var id = new DocumentId("a3:standalone");
        var start = new SemanticElementSnapshot(new SemanticElementId("a3:start"), BpmnSemanticTypes.StartEvent);
        var end = new SemanticElementSnapshot(new SemanticElementId("a3:end"), BpmnSemanticTypes.EndEvent);
        return new(new SemanticModelSnapshot(id, DocumentRevision.Zero, [start, end],
            includeFlow ? [new SemanticRelationshipSnapshot(new SemanticElementId("a3:flow"),
                BpmnSemanticTypes.SequenceFlow, start.Id, missingTarget ? new SemanticElementId("a3:missing") : end.Id)] : []),
            new VisualModelSnapshot(id, DocumentRevision.Zero), new DocumentMetadataSnapshot(id, DocumentRevision.Zero));
    }
}
