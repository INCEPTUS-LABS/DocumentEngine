using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Documents;

/// <summary>
/// Reconstructs a new authoritative Document without Commands, events, or runtime processing.
/// </summary>
public static class DocumentReconstructor
{
    /// <summary>
    /// Reconstructs the supplied persistent state exactly, including placement modes and routes.
    /// </summary>
    /// <remarks>
    /// Reconstruction preserves the supplied revision and performs no layout or routing.
    /// </remarks>
    public static DocumentConstructionResult Reconstruct(
        DocumentSnapshot snapshot,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return DocumentMaterializer.Materialize(
            snapshot,
            connectorAnchorPolicyProvider);
    }
}
