using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// Immutable generic inputs supplied to a notation-owned Toolbox placement factory.
/// </summary>
public sealed class ToolboxPlacementRequest
{
    public ToolboxPlacementRequest(
        ToolboxItemId toolboxItemId,
        DocumentSnapshot document,
        DocumentRevision expectedRevision,
        PointD documentPoint,
        IDocumentCreationIdentityProvider identityProvider,
        DocumentScopeId? targetScopeId = null,
        IEnumerable<ToolboxPlacementTarget>? visibleTargets = null,
        ToolboxPlacementCandidate? candidate = null)
    {
        ArgumentNullException.ThrowIfNull(toolboxItemId);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(identityProvider);

        if (expectedRevision != document.Revision)
        {
            throw new ArgumentException(
                "The expected placement revision must match the supplied Document snapshot.",
                nameof(expectedRevision));
        }

        targetScopeId ??= document.SemanticModel.RootScopeId;
        if (targetScopeId != document.SemanticModel.RootScopeId &&
            !document.SemanticModel.NestedScopes.Any(scope => scope.Id == targetScopeId))
        {
            throw new ArgumentException(
                $"Target scope '{targetScopeId}' does not exist in the placement Document.",
                nameof(targetScopeId));
        }

        ToolboxItemId = toolboxItemId;
        Document = document;
        ExpectedRevision = expectedRevision;
        DocumentPoint = documentPoint;
        IdentityProvider = identityProvider;
        TargetScopeId = targetScopeId;
        var targets = visibleTargets?.ToArray() ?? [];
        if (Array.Exists(targets, static target => target is null))
        {
            throw new ArgumentException(
                "Visible Toolbox placement targets cannot contain null values.",
                nameof(visibleTargets));
        }

        Array.Sort(
            targets,
            static (left, right) => StringComparer.Ordinal.Compare(
                left.VisualStateId.Value,
                right.VisualStateId.Value));
        if (targets.Select(static target => target.VisualStateId).Distinct().Count() !=
            targets.Length)
        {
            throw new ArgumentException(
                "Visible Toolbox placement targets must have unique Visual State identities.",
                nameof(visibleTargets));
        }

        if (candidate is not null && !targets.Any(target => target == candidate.Target))
        {
            throw new ArgumentException(
                "The resolved Toolbox placement candidate must reference a visible target.",
                nameof(candidate));
        }

        VisibleTargets = [.. targets];
        Candidate = candidate;
    }

    public ToolboxItemId ToolboxItemId { get; }

    public DocumentSnapshot Document { get; }

    public DocumentRevision ExpectedRevision { get; }

    public PointD DocumentPoint { get; }

    public IDocumentCreationIdentityProvider IdentityProvider { get; }

    public DocumentScopeId TargetScopeId { get; }

    public ImmutableArray<ToolboxPlacementTarget> VisibleTargets { get; }

    public ToolboxPlacementCandidate? Candidate { get; }
}
