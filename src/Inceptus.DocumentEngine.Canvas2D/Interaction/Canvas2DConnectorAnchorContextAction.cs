using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Canvas2D.Interaction;

/// <summary>
/// Describes one transient, revision-bound node connector-anchor context action.
/// </summary>
public sealed class Canvas2DConnectorAnchorContextAction
{
    internal Canvas2DConnectorAnchorContextAction(
        Canvas2DConnectorAnchorContextActionKind kind,
        VisualStateId targetVisualStateId,
        SceneObjectId sourceSceneObjectId,
        SceneObjectId targetSceneObjectId,
        ConnectorAnchorSide side,
        int insertionIndex,
        double edgeParameter,
        ConnectorAnchorId? anchorId = null,
        ConnectorAnchorRole? anchorRole = null,
        bool canDelete = false,
        ConnectorAnchorRoleCapability allowedRoles =
            ConnectorAnchorRoleCapability.SourceOrTarget)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The action kind must be defined.");
        }

        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(sourceSceneObjectId);
        ArgumentNullException.ThrowIfNull(targetSceneObjectId);
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "The connector-anchor side must be defined.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(insertionIndex);
        if (!double.IsFinite(edgeParameter) || edgeParameter is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(edgeParameter),
                edgeParameter,
                "The edge parameter must be finite and normalized.");
        }

        if (kind == Canvas2DConnectorAnchorContextActionKind.AddAnchor &&
            (anchorId is not null || anchorRole is not null || canDelete))
        {
            throw new ArgumentException("An Add Anchor context cannot identify an existing anchor.");
        }

        const ConnectorAnchorRoleCapability knownRoles =
            ConnectorAnchorRoleCapability.SourceOrTarget;
        if ((allowedRoles & ~knownRoles) != ConnectorAnchorRoleCapability.None ||
            (kind == Canvas2DConnectorAnchorContextActionKind.AddAnchor &&
             allowedRoles == ConnectorAnchorRoleCapability.None))
        {
            throw new ArgumentOutOfRangeException(
                nameof(allowedRoles),
                allowedRoles,
                "An Add Anchor context must identify at least one defined connector role.");
        }

        if (kind == Canvas2DConnectorAnchorContextActionKind.DeleteAnchor &&
            (anchorId is null || anchorRole is null))
        {
            throw new ArgumentException("A Delete Anchor context must identify the existing anchor.");
        }

        TargetVisualStateId = targetVisualStateId;
        SourceSceneObjectId = sourceSceneObjectId;
        TargetSceneObjectId = targetSceneObjectId;
        Kind = kind;
        Side = side;
        InsertionIndex = insertionIndex;
        EdgeParameter = edgeParameter;
        AnchorId = anchorId;
        AnchorRole = anchorRole;
        CanDelete = canDelete;
        AllowedRoles = kind == Canvas2DConnectorAnchorContextActionKind.AddAnchor
            ? allowedRoles
            : ConnectorAnchorRoleCapability.None;
    }

    public Canvas2DConnectorAnchorContextActionKind Kind { get; }

    public VisualStateId TargetVisualStateId { get; }

    /// <summary>
    /// Gets the transient Scene item that supplied this exact context.
    /// Persistent addressing never uses this identity.
    /// </summary>
    public SceneObjectId SourceSceneObjectId { get; }

    /// <summary>
    /// Gets the owning node's transient Scene identity for revision-bound revalidation.
    /// </summary>
    public SceneObjectId TargetSceneObjectId { get; }

    public ConnectorAnchorSide Side { get; }

    /// <summary>
    /// Gets the side-local insertion index resolved from the context point, or the existing
    /// side-local order for a Delete Anchor context.
    /// </summary>
    public int InsertionIndex { get; }

    /// <summary>
    /// Gets the transient normalized edge parameter. It is never persisted as anchor geometry.
    /// </summary>
    public double EdgeParameter { get; }

    public ConnectorAnchorId? AnchorId { get; }

    public ConnectorAnchorRole? AnchorRole { get; }

    /// <summary>
    /// Gets whether the current Scene, policy and reference state permit deleting the existing
    /// dynamic anchor. The host and Command handler independently revalidate these invariants.
    /// </summary>
    public bool CanDelete { get; }

    /// <summary>
    /// Gets the policy-approved roles offered by an Add Anchor context.
    /// </summary>
    public ConnectorAnchorRoleCapability AllowedRoles { get; }

    public bool CanAdd(ConnectorAnchorRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "The role must be defined.");
        }

        var capability = role switch
        {
            ConnectorAnchorRole.Source => ConnectorAnchorRoleCapability.Source,
            ConnectorAnchorRole.Target => ConnectorAnchorRoleCapability.Target,
            _ => ConnectorAnchorRoleCapability.None,
        };
        return Kind == Canvas2DConnectorAnchorContextActionKind.AddAnchor &&
            (AllowedRoles & capability) != ConnectorAnchorRoleCapability.None;
    }

    public bool IsCurrent(Canvas2DScene scene, VisualStateSnapshot visualState)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(visualState);
        if (visualState.Id != TargetVisualStateId)
        {
            return false;
        }

        var target = scene.Items.SingleOrDefault(item =>
            item.Id == TargetSceneObjectId &&
            item.Origin.VisualStateId == TargetVisualStateId &&
            (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) == 0 &&
            item.Layer == Canvas2DSceneLayer.Content);
        var source = scene.Items.SingleOrDefault(item => item.Id == SourceSceneObjectId);
        var isCanonicalFixedSizeAddSource =
            Kind == Canvas2DConnectorAnchorContextActionKind.AddAnchor &&
            source is not null &&
            source.Id == target?.Id &&
            Canvas2DNodeBodyMetadata.IsNodeBody(source) &&
            !source.Bounds.IsEmpty &&
            (!source.Metadata.TryGetValue(
                 Canvas2DResizeGestureMetadata.ResizeCapable,
                 out var resizeCapable) ||
             (resizeCapable.Kind == PropertyValueKind.Boolean &&
              !resizeCapable.BooleanValue));
        if (target is null || source is null ||
            source.Origin.VisualStateId != TargetVisualStateId ||
            (!source.Origin.RelatedSceneObjectIds.Contains(TargetSceneObjectId) &&
             !isCanonicalFixedSizeAddSource))
        {
            return false;
        }

        return Kind switch
        {
            Canvas2DConnectorAnchorContextActionKind.AddAnchor =>
                ConnectorAnchorGeometryResolver.ResolveInsertionIndex(
                    visualState.ConnectorAnchors,
                    Side,
                    EdgeParameter) == InsertionIndex,
            Canvas2DConnectorAnchorContextActionKind.DeleteAnchor =>
                IsCurrentDelete(source, visualState),
            _ => false,
        };
    }

    private bool IsCurrentDelete(Canvas2DSceneItem source, VisualStateSnapshot visualState)
    {
        if (AnchorId is null || AnchorRole is null ||
            !TryGetText(source, Canvas2DConnectorAnchorMetadata.AnchorId, out var anchorId) ||
            !StringComparer.Ordinal.Equals(anchorId, AnchorId.Value) ||
            !TryGetText(source, Canvas2DConnectorAnchorMetadata.Side, out var side) ||
            !StringComparer.Ordinal.Equals(side, Side.ToString()) ||
            !TryGetText(source, Canvas2DConnectorAnchorMetadata.Role, out var role) ||
            !StringComparer.Ordinal.Equals(role, AnchorRole.Value.ToString()) ||
            !source.Metadata.TryGetValue(
                Canvas2DConnectorAnchorMetadata.DeleteCapable,
                out var deleteCapable) ||
            deleteCapable.Kind != PropertyValueKind.Boolean ||
            deleteCapable.BooleanValue != CanDelete)
        {
            return false;
        }

        return visualState.ConnectorAnchors.Any(anchor =>
            anchor.Id == AnchorId &&
            anchor.Side == Side &&
            anchor.Role == AnchorRole &&
            anchor.Order == InsertionIndex);
    }

    private static bool TryGetText(Canvas2DSceneItem item, string key, out string value)
    {
        if (item.Metadata.TryGetValue(key, out var property) &&
            property.Kind == PropertyValueKind.Text &&
            !string.IsNullOrWhiteSpace(property.TextValue))
        {
            value = property.TextValue;
            return true;
        }

        value = string.Empty;
        return false;
    }
}

public enum Canvas2DConnectorAnchorContextActionKind
{
    AddAnchor,
    DeleteAnchor,
}
