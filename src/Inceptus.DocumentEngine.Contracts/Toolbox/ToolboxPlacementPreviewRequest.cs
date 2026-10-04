using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// Read-only placement inputs. Unlike a committed placement request, this request exposes no
/// identity allocator. Coordinates and visible target bounds use the same canonical space.
/// </summary>
public sealed class ToolboxPlacementPreviewRequest
{
    public ToolboxPlacementPreviewRequest(
        ToolboxItemId toolboxItemId,
        DocumentSnapshot document,
        PointD documentPoint,
        DocumentScopeId? targetScopeId = null,
        IEnumerable<ToolboxPlacementTarget>? visibleTargets = null)
    {
        ArgumentNullException.ThrowIfNull(toolboxItemId);
        ArgumentNullException.ThrowIfNull(document);
        targetScopeId ??= document.SemanticModel.RootScopeId;
        if (targetScopeId != document.SemanticModel.RootScopeId &&
            !document.SemanticModel.NestedScopes.Any(scope => scope.Id == targetScopeId))
        {
            throw new ArgumentException("The target scope must exist in the Document.", nameof(targetScopeId));
        }

        var targets = visibleTargets?.ToArray() ?? [];
        if (Array.Exists(targets, static target => target is null))
        {
            throw new ArgumentException("Placement targets cannot contain null values.", nameof(visibleTargets));
        }

        Array.Sort(targets, static (left, right) =>
            StringComparer.Ordinal.Compare(left.VisualStateId.Value, right.VisualStateId.Value));
        if (targets.Select(static target => target.VisualStateId).Distinct().Count() != targets.Length)
        {
            throw new ArgumentException("Placement targets must have unique Visual State identities.", nameof(visibleTargets));
        }

        ToolboxItemId = toolboxItemId;
        Document = document;
        DocumentPoint = documentPoint;
        TargetScopeId = targetScopeId;
        VisibleTargets = [.. targets];
    }

    public ToolboxItemId ToolboxItemId { get; }

    public DocumentSnapshot Document { get; }

    public PointD DocumentPoint { get; }

    public DocumentScopeId TargetScopeId { get; }

    public ImmutableArray<ToolboxPlacementTarget> VisibleTargets { get; }
}
