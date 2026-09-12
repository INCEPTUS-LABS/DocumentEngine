using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Documents;

/// <summary>
/// Creates new authoritative Documents without modifying an existing Document.
/// </summary>
public static class DocumentFactory
{
    internal const string InitialRevisionInvalidCode = "DOC_CREATION_REVISION_NOT_ZERO";

    /// <summary>
    /// Creates an empty Document with an explicitly supplied identity at revision zero.
    /// </summary>
    public static DocumentConstructionResult CreateEmpty(
        DocumentId documentId,
        PropertyMap? systemManagedMetadata = null,
        PropertyMap? extensionMetadata = null,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        var revision = DocumentRevision.Zero;
        var semanticModel = new SemanticModelSnapshot(documentId, revision);
        var visualModel = new VisualModelSnapshot(documentId, revision);
        var metadata = new DocumentMetadataSnapshot(
            documentId,
            revision,
            systemManagedMetadata,
            extensionMetadata);

        return DocumentMaterializer.Materialize(
            new DocumentSnapshot(semanticModel, visualModel, metadata),
            connectorAnchorPolicyProvider);
    }

    /// <summary>
    /// Creates a new Document from coherent, revision-zero initial authoritative state.
    /// </summary>
    /// <remarks>
    /// Automatic placement remains eligible for transient layout computation. Manual and pinned
    /// geometry, and any explicitly persistent route, are copied unchanged. This operation never
    /// calculates or persists layout or routing output.
    /// </remarks>
    public static DocumentConstructionResult Create(
        DocumentSnapshot initialState,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        ArgumentNullException.ThrowIfNull(initialState);

        if (initialState.Revision != DocumentRevision.Zero)
        {
            return DocumentConstructionResult.Failure(
            [
                new Diagnostic(
                    InitialRevisionInvalidCode,
                    DiagnosticSeverity.Error,
                    "A newly created Document must begin at revision zero.",
                    initialState.DocumentId.Value,
                    [
                        new KeyValuePair<string, string>(
                            "ActualRevision",
                            initialState.Revision.ToString()),
                    ]),
            ]);
        }

        return DocumentMaterializer.Materialize(
            initialState,
            connectorAnchorPolicyProvider);
    }
}
