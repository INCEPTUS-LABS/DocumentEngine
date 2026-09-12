using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.UnitTests.Runtime.Documents;

public sealed class NativeDocumentSerializationTests
{
    private static readonly DocumentId CompleteDocumentId = new("test:native:complete");
    private static readonly DocumentRevision CompleteRevision = new(23);
    private static readonly SemanticElementId SourceId = new("test:native:source");
    private static readonly SemanticElementId TargetId = new("test:native:target");
    private static readonly SemanticElementId RelationshipId = new("test:native:relationship");
    private static readonly VisualStateId SourceVisualId = new("test:native:source:visual");
    private static readonly VisualStateId TargetVisualId = new("test:native:target:visual");
    private static readonly VisualStateId RelationshipVisualId =
        new("test:native:relationship:visual");
    private static readonly ConnectorAnchorId SourceAnchorId =
        new("test:native:source:anchor");
    private static readonly ConnectorAnchorId TargetAnchorId =
        new("test:native:target:anchor");

    [Fact]
    public void MinimalDocumentRoundTripsThroughStableUtf8Envelope()
    {
        var source = RequireSuccess(DocumentFactory.CreateEmpty(
            new DocumentId("test:native:minimal")));
        var sourceSnapshot = source.CaptureSnapshot();

        var payload = NativeDocumentSerializer.Export(source);

        Assert.Equal("Inceptus.Document", NativeDocumentSerializer.FormatIdentifier);
        Assert.Equal(1, NativeDocumentSerializer.FormatVersion);
        Assert.NotEmpty(payload);
        Assert.False(HasUtf8Bom(payload));
        var json = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(payload.AsSpan());
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        Assert.Equal(
            NativeDocumentSerializer.FormatIdentifier,
            root.GetProperty("format").GetString());
        Assert.Equal(
            NativeDocumentSerializer.FormatVersion,
            root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("document").ValueKind);

        var imported = NativeDocumentSerializer.Import(payload.ToArray());

        Assert.True(imported.Succeeded, Diagnostics(imported));
        Assert.Empty(imported.Diagnostics);
        var reconstructed = Assert.IsType<Document>(imported.Document);
        Assert.NotSame(source, reconstructed);
        Assert.Equal(sourceSnapshot, reconstructed.CaptureSnapshot());
        Assert.Same(sourceSnapshot, source.CaptureSnapshot());
        Assert.Equal(DocumentRevision.Zero, source.Revision);
    }

    [Fact]
    public void CompleteDocumentPreservesEveryPropertyKindMetadataAndPersistentIdentity()
    {
        var source = CreateCompleteDocument();
        var expected = source.CaptureSnapshot();

        var imported = NativeDocumentSerializer.Import(
            NativeDocumentSerializer.Export(source).ToArray());

        Assert.True(imported.Succeeded, Diagnostics(imported));
        var actual = Assert.IsType<Document>(imported.Document).CaptureSnapshot();
        Assert.Equal(expected, actual);
        Assert.Equal(CompleteDocumentId, actual.DocumentId);
        Assert.Equal(CompleteRevision, actual.Revision);
        Assert.Equal(
            [SourceId, TargetId],
            actual.SemanticModel.Elements.Select(static element => element.Id));
        Assert.Equal(
            RelationshipId,
            Assert.Single(actual.SemanticModel.Relationships).Id);
        Assert.Equal(
            [RelationshipVisualId, SourceVisualId, TargetVisualId],
            actual.VisualModel.VisualStates.Select(static visual => visual.Id));

        var sourceElement = actual.SemanticModel.Elements.Single(
            static element => element.Id == SourceId);
        Assert.Equal(PropertyValueKind.Text, sourceElement.Properties["test:text"].Kind);
        Assert.Equal("Zażółć gęślą jaźń — 你好", sourceElement.Properties["test:text"].TextValue);
        Assert.Equal(PropertyValueKind.Boolean, sourceElement.Properties["test:boolean"].Kind);
        Assert.True(sourceElement.Properties["test:boolean"].BooleanValue);
        Assert.Equal(PropertyValueKind.Integer, sourceElement.Properties["test:integer"].Kind);
        Assert.Equal(long.MaxValue, sourceElement.Properties["test:integer"].IntegerValue);
        Assert.Equal(PropertyValueKind.Number, sourceElement.Properties["test:number"].Kind);
        Assert.Equal(123.5d, sourceElement.Properties["test:number"].NumberValue);

        Assert.Equal(
            "native-v1",
            actual.Metadata.SystemManagedProperties["test:schema"].TextValue);
        Assert.Equal(
            17L,
            actual.Metadata.SystemManagedProperties["test:sequence"].IntegerValue);
        Assert.True(actual.Metadata.ExtensionProperties["test:enabled"].BooleanValue);
        Assert.Equal(
            0.125d,
            actual.Metadata.ExtensionProperties["test:scale"].NumberValue);

        var connector = actual.VisualModel.VisualStates.Single(
            static visual => visual.Id == RelationshipVisualId);
        Assert.Equal(SourceAnchorId, connector.SourceAnchorId);
        Assert.Equal(TargetAnchorId, connector.TargetAnchorId);
        Assert.True(connector.Route.AsSpan().SequenceEqual(
        [
            new PointD(130d, 50d),
            new PointD(200d, 50d),
            new PointD(200d, 110d),
        ]));
        Assert.Equal(
            SourceAnchorId,
            actual.VisualModel.VisualStates.Single(static visual => visual.Id == SourceVisualId)
                .ConnectorAnchors.Single().Id);
        Assert.Equal(
            TargetAnchorId,
            actual.VisualModel.VisualStates.Single(static visual => visual.Id == TargetVisualId)
                .ConnectorAnchors.Single().Id);
    }

    [Fact]
    public void IllFormedUtf16DataUsesALosslessJsonFallback()
    {
        var illFormedText = new string(['\ud800', 'X', '\udfff']);
        var documentId = new DocumentId($"test:native:{illFormedText}");
        var elementId = new SemanticElementId($"test:native:element:{illFormedText}");
        var snapshot = new DocumentSnapshot(
            new SemanticModelSnapshot(
                documentId,
                DocumentRevision.Zero,
                [new SemanticElementSnapshot(
                    elementId,
                    new SemanticTypeId($"test:native:type:{illFormedText}"),
                    [new(illFormedText, PropertyValue.FromText(illFormedText))])]),
            new VisualModelSnapshot(documentId, DocumentRevision.Zero),
            new DocumentMetadataSnapshot(documentId, DocumentRevision.Zero));
        var source = RequireSuccess(DocumentReconstructor.Reconstruct(snapshot));

        var payload = NativeDocumentSerializer.Export(source);

        using var parsed = JsonDocument.Parse(payload.AsMemory());
        Assert.Equal(JsonValueKind.Object, parsed.RootElement
            .GetProperty("document")
            .GetProperty("id")
            .ValueKind);
        Assert.Contains(
            "utf16LeBase64",
            Encoding.UTF8.GetString(payload.AsSpan()),
            StringComparison.Ordinal);

        var imported = NativeDocumentSerializer.Import(payload.AsMemory());

        Assert.True(imported.Succeeded, Diagnostics(imported));
        var reconstructed = Assert.IsType<Document>(imported.Document);
        Assert.Equal(snapshot, reconstructed.CaptureSnapshot());
        Assert.True(payload.AsSpan().SequenceEqual(
            NativeDocumentSerializer.Export(reconstructed).AsSpan()));
    }

    [Fact]
    public void ExportAndReExportAreByteStableAndDoNotMutateTheSource()
    {
        var source = CreateCompleteDocument();
        var before = source.CaptureSnapshot();
        var revision = source.Revision;

        var first = NativeDocumentSerializer.Export(source);
        var second = NativeDocumentSerializer.Export(source);
        var imported = NativeDocumentSerializer.Import(first.ToArray());
        var reconstructed = Assert.IsType<Document>(imported.Document);
        var reExported = NativeDocumentSerializer.Export(reconstructed);

        Assert.True(first.AsSpan().SequenceEqual(second.AsSpan()));
        Assert.True(first.AsSpan().SequenceEqual(reExported.AsSpan()));
        Assert.Same(before, source.CaptureSnapshot());
        Assert.Equal(revision, source.Revision);
        Assert.Equal(before, reconstructed.CaptureSnapshot());
    }

    [Fact]
    public void WrongFormatIsRejectedWithoutADocument()
    {
        var payload = Rewrite(
            NativeDocumentSerializer.Export(CreateCompleteDocument()),
            root => root["format"] = "Other.Document");

        AssertRejected(payload, "NATIVE_DOCUMENT_FORMAT_INVALID");
    }

    [Fact]
    public void UnsupportedVersionIsRejectedWithoutFallback()
    {
        var payload = Rewrite(
            NativeDocumentSerializer.Export(CreateCompleteDocument()),
            root => root["formatVersion"] = NativeDocumentSerializer.FormatVersion + 1);

        AssertRejected(payload, "NATIVE_DOCUMENT_VERSION_UNSUPPORTED");
    }

    [Fact]
    public void UnsupportedIntegralVersionOutsideInt32IsStillClassifiedAsUnsupported()
    {
        var payload = Rewrite(
            NativeDocumentSerializer.Export(CreateCompleteDocument()),
            root => root["formatVersion"] = (long)int.MaxValue + 1L);

        AssertRejected(payload, "NATIVE_DOCUMENT_VERSION_UNSUPPORTED");
    }

    [Theory]
    [InlineData("***")]
    [InlineData("AA==")]
    [InlineData("QQA=")]
    [InlineData("2AA= ")]
    public void InvalidUtf16FallbackIsRejectedAsInvalidStructure(string encoded)
    {
        var payload = Rewrite(
            NativeDocumentSerializer.Export(CreateCompleteDocument()),
            root => root["document"]!["id"] = new JsonObject
            {
                ["utf16LeBase64"] = encoded,
            });

        AssertRejected(payload, "NATIVE_DOCUMENT_STRUCTURE_INVALID");
    }

    [Fact]
    public void InvalidPersistentValueReturnsAControlledDiagnosticWithoutConstructorDetails()
    {
        var payload = Rewrite(
            NativeDocumentSerializer.Export(CreateCompleteDocument()),
            root => root["document"]!["id"] = string.Empty);

        var imported = NativeDocumentSerializer.Import(payload);

        Assert.False(imported.Succeeded);
        Assert.Null(imported.Document);
        var diagnostic = Assert.Single(imported.Diagnostics);
        Assert.Equal("NATIVE_DOCUMENT_STRUCTURE_INVALID", diagnostic.Code);
        Assert.Equal(
            "The native Document contains a persistent value that violates a structural invariant.",
            diagnostic.Message);
        Assert.Equal("$.document", diagnostic.SourceIdentity);
    }

    [Fact]
    public void LoneSurrogateJsonEscapeIsRejectedWithoutIdentitySubstitutionOrExceptionDetails()
    {
        var json = Encoding.UTF8.GetString(
            NativeDocumentSerializer.Export(CreateCompleteDocument()).AsSpan());
        var invalidJsonText = json.Replace(
            $"\"{CompleteDocumentId.Value}\"",
            "\"\\uD800\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, invalidJsonText);

        var imported = NativeDocumentSerializer.Import(
            Encoding.UTF8.GetBytes(invalidJsonText));

        Assert.False(imported.Succeeded);
        Assert.Null(imported.Document);
        var diagnostic = Assert.Single(imported.Diagnostics);
        Assert.Equal("NATIVE_DOCUMENT_STRUCTURE_INVALID", diagnostic.Code);
        Assert.Equal(
            "The native Document contains a persistent value that cannot be reconstructed.",
            diagnostic.Message);
        Assert.Equal("$.document", diagnostic.SourceIdentity);
    }

    [Fact]
    public void MalformedJsonIsRejectedWithoutADocument()
    {
        AssertRejected(
            Encoding.UTF8.GetBytes("{ \"format\": \"Inceptus.Document\""),
            "NATIVE_DOCUMENT_JSON_MALFORMED");
    }

    [Fact]
    public void MissingDocumentStructureIsRejectedAtomically()
    {
        var payload = Rewrite(
            NativeDocumentSerializer.Export(CreateCompleteDocument()),
            root => Assert.True(root.Remove("document")));

        AssertRejected(payload, "NATIVE_DOCUMENT_STRUCTURE_INVALID");
    }

    [Fact]
    public void BrokenMandatoryRelationshipReferenceIsRejectedAtomically()
    {
        var source = CreateCompleteDocument();
        var sourceSnapshot = source.CaptureSnapshot();
        var payload = Rewrite(
            NativeDocumentSerializer.Export(source),
            root => Assert.True(ReplaceFirstIdentifier(
                root,
                "targetId",
                "test:native:missing-target")));

        var imported = NativeDocumentSerializer.Import(payload);

        Assert.False(imported.Succeeded);
        Assert.Null(imported.Document);
        Assert.Contains(imported.Diagnostics, static diagnostic =>
            diagnostic.Code == DocumentInvariantValidator.RelationshipTargetMissingCode);
        Assert.Same(sourceSnapshot, source.CaptureSnapshot());
        Assert.Equal(CompleteRevision, source.Revision);
    }

    [Fact]
    public void StructurallyValidBpmnQualityIssuesDoNotBlockImport()
    {
        var documentId = new DocumentId("test:native:model-quality");
        var revision = new DocumentRevision(8);
        var taskId = new SemanticElementId("test:native:model-quality:task");
        var candidate = new DocumentSnapshot(
            new SemanticModelSnapshot(
                documentId,
                revision,
                [BpmnSemanticFactory.CreateTask(
                    taskId,
                    "LONE_TASK",
                    "Lone task",
                    1,
                    "Structurally valid but incomplete process")]),
            new VisualModelSnapshot(
                documentId,
                revision,
                [new VisualStateSnapshot(
                    new VisualStateId("test:native:model-quality:task:visual"),
                    taskId,
                    new PointD(20d, 30d),
                    new SizeD(120d, 80d),
                    VisualPlacementMode.Pinned)]),
            new DocumentMetadataSnapshot(documentId, revision));
        var policyProvider = new ElementConnectorAnchorPolicyRegistry(
            BpmnPluginRegistration.N100.ConnectorAnchorPolicies);
        var source = RequireSuccess(DocumentReconstructor.Reconstruct(candidate, policyProvider));

        var imported = NativeDocumentSerializer.Import(
            NativeDocumentSerializer.Export(source).ToArray(),
            policyProvider);

        Assert.True(imported.Succeeded, Diagnostics(imported));
        var reconstructed = Assert.IsType<Document>(imported.Document);
        Assert.Equal(candidate, reconstructed.CaptureSnapshot());
        var validation = new ModelValidationEngine(new ModelValidationCatalog(
                BpmnPluginRegistration.N100.ModelValidationRules))
            .Validate(new ModelValidationContext(reconstructed.CaptureSnapshot()));
        Assert.NotEmpty(validation.Issues);
        Assert.Contains(validation.Issues, static issue =>
            issue.Code == BpmnModelValidationCodes.NoStartEvent);
        Assert.Contains(validation.Issues, static issue =>
            issue.Code == BpmnModelValidationCodes.NoEndEvent);
    }

    private static Document CreateCompleteDocument()
    {
        var semanticModel = new SemanticModelSnapshot(
            CompleteDocumentId,
            CompleteRevision,
            [
                new SemanticElementSnapshot(
                    SourceId,
                    new SemanticTypeId("test:native:node"),
                    [
                        new("test:text", PropertyValue.FromText("Zażółć gęślą jaźń — 你好")),
                        new("test:boolean", PropertyValue.FromBoolean(true)),
                        new("test:integer", PropertyValue.FromInteger(long.MaxValue)),
                        new("test:number", PropertyValue.FromNumber(123.5d)),
                    ]),
                new SemanticElementSnapshot(
                    TargetId,
                    new SemanticTypeId("test:native:node")),
            ],
            [new SemanticRelationshipSnapshot(
                RelationshipId,
                new SemanticTypeId("test:native:relationship"),
                SourceId,
                TargetId,
                [new("test:conditional", PropertyValue.FromBoolean(false))])]);
        var sourceLabel = NodeLabelVisualOverride.UpdateProperties(
            PropertyMap.Empty,
            new NodeLabelVisualOverride(8d, -6d, 96d, 36d));
        var connectorLabel = ConnectorLabelPlacement.UpdateProperties(
            PropertyMap.Empty,
            new ConnectorLabelPlacement(0.65d, new VectorD(4d, -10d)));
        var visualModel = new VisualModelSnapshot(
            CompleteDocumentId,
            CompleteRevision,
            [
                new VisualStateSnapshot(
                    SourceVisualId,
                    SourceId,
                    new PointD(20d, 20d),
                    new SizeD(110d, 60d),
                    VisualPlacementMode.Manual,
                    properties: sourceLabel,
                    connectorAnchors:
                    [new ConnectorAnchor(
                        SourceAnchorId,
                        ConnectorAnchorSide.Right,
                        ConnectorAnchorRole.Source,
                        0)]),
                new VisualStateSnapshot(
                    TargetVisualId,
                    TargetId,
                    new PointD(200d, 80d),
                    new SizeD(120d, 60d),
                    VisualPlacementMode.Pinned,
                    connectorAnchors:
                    [new ConnectorAnchor(
                        TargetAnchorId,
                        ConnectorAnchorSide.Left,
                        ConnectorAnchorRole.Target,
                        0)]),
                new VisualStateSnapshot(
                    RelationshipVisualId,
                    RelationshipId,
                    default,
                    default,
                    VisualPlacementMode.Automatic,
                    [
                        new PointD(130d, 50d),
                        new PointD(200d, 50d),
                        new PointD(200d, 110d),
                    ],
                    connectorLabel,
                    sourceAnchorId: SourceAnchorId,
                    targetAnchorId: TargetAnchorId),
            ]);
        var metadata = new DocumentMetadataSnapshot(
            CompleteDocumentId,
            CompleteRevision,
            [
                new("test:schema", PropertyValue.FromText("native-v1")),
                new("test:sequence", PropertyValue.FromInteger(17)),
            ],
            [
                new("test:enabled", PropertyValue.FromBoolean(true)),
                new("test:scale", PropertyValue.FromNumber(0.125d)),
            ]);

        return RequireSuccess(DocumentReconstructor.Reconstruct(
            new DocumentSnapshot(semanticModel, visualModel, metadata)));
    }

    private static Document RequireSuccess(DocumentConstructionResult result)
    {
        Assert.True(result.Succeeded, Diagnostics(result));
        Assert.Empty(result.Diagnostics);
        return Assert.IsType<Document>(result.Document);
    }

    private static void AssertRejected(ReadOnlyMemory<byte> payload, string expectedCode)
    {
        var imported = NativeDocumentSerializer.Import(payload);

        Assert.False(imported.Succeeded);
        Assert.Null(imported.Document);
        Assert.Contains(imported.Diagnostics, diagnostic => diagnostic.Code == expectedCode);
    }

    private static byte[] Rewrite(
        ImmutableArray<byte> payload,
        Action<JsonObject> rewrite)
    {
        var root = Assert.IsType<JsonObject>(JsonNode.Parse(
            Encoding.UTF8.GetString(payload.AsSpan())));
        rewrite(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static bool ReplaceFirstIdentifier(
        JsonNode? node,
        string propertyName,
        string replacement)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(property.Key, propertyName))
                {
                    if (property.Value is JsonObject identifierObject)
                    {
                        var valueKey = identifierObject
                            .Select(static entry => entry.Key)
                            .FirstOrDefault(static key =>
                                StringComparer.OrdinalIgnoreCase.Equals(key, "value"));
                        if (valueKey is not null)
                        {
                            identifierObject[valueKey] = replacement;
                            return true;
                        }
                    }

                    jsonObject[property.Key] = replacement;
                    return true;
                }

                if (ReplaceFirstIdentifier(property.Value, propertyName, replacement))
                {
                    return true;
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var child in jsonArray)
            {
                if (ReplaceFirstIdentifier(child, propertyName, replacement))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasUtf8Bom(ImmutableArray<byte> payload) =>
        payload.Length >= 3 &&
        payload[0] == 0xef &&
        payload[1] == 0xbb &&
        payload[2] == 0xbf;

    private static string Diagnostics(DocumentConstructionResult result) =>
        string.Join(
            " | ",
            result.Diagnostics.Select(static diagnostic =>
                $"{diagnostic.Code}: {diagnostic.Message}"));
}
