using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Documents;

internal static class DocumentMaterializer
{
    public static DocumentConstructionResult Materialize(
        DocumentSnapshot candidate,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var diagnostics = DocumentInvariantValidator.Validate(
            candidate,
            connectorAnchorPolicyProvider);

        if (!diagnostics.IsEmpty)
        {
            return DocumentConstructionResult.Failure(diagnostics);
        }

        var ownedSnapshot = DocumentSnapshotCloner.Clone(candidate);
        return DocumentConstructionResult.Success(new Document(ownedSnapshot));
    }
}
