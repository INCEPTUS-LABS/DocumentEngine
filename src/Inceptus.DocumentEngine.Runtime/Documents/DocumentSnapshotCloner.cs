using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Documents;

internal static class DocumentSnapshotCloner
{
    public static DocumentSnapshot Clone(DocumentSnapshot source)
        => CloneAtRevision(source, source.Revision);

    internal static DocumentSnapshot CloneAtRevision(
        DocumentSnapshot source,
        DocumentRevision revision)
    {
        ArgumentNullException.ThrowIfNull(source);

        var semanticModel = new SemanticModelSnapshot(
            source.DocumentId,
            revision,
            source.SemanticModel.Elements.Select(CloneElement),
            source.SemanticModel.Relationships.Select(CloneRelationship),
            source.SemanticModel.NestedScopes.Select(CloneScope),
            source.SemanticModel.ScopeMemberships.Select(CloneMembership),
            source.SemanticModel.ModelProfiles,
            source.SemanticModel.ProfileAssignments);
        var visualModel = new VisualModelSnapshot(
            source.DocumentId,
            revision,
            source.VisualModel.VisualStates.Select(CloneVisualState),
            source.VisualModel.ProfileElementPresentations);
        var metadata = new DocumentMetadataSnapshot(
            source.DocumentId,
            revision,
            source.Metadata.SystemManagedProperties,
            source.Metadata.ExtensionProperties);

        var publication = source.Publication is null
            ? null
            : new DocumentPublicationSnapshot(
                source.Publication.Code,
                source.Publication.Title,
                source.Publication.Description);

        return new DocumentSnapshot(semanticModel, visualModel, metadata, publication);
    }

    private static SemanticElementSnapshot CloneElement(SemanticElementSnapshot element) =>
        new(
            element.Id,
            element.TypeId,
            element.Properties,
            element.AttachedToElementId,
            element.ContainmentKind);

    private static SemanticRelationshipSnapshot CloneRelationship(
        SemanticRelationshipSnapshot relationship) =>
        new(
            relationship.Id,
            relationship.TypeId,
            relationship.SourceId,
            relationship.TargetId,
            relationship.Properties);

    private static DocumentScopeSnapshot CloneScope(DocumentScopeSnapshot scope) =>
        new(scope.Id, scope.ParentScopeId, scope.OwnerSemanticElementId);

    private static SemanticElementScopeMembershipSnapshot CloneMembership(
        SemanticElementScopeMembershipSnapshot membership) =>
        new(membership.SemanticElementId, membership.ScopeId);

    private static VisualStateSnapshot CloneVisualState(VisualStateSnapshot visualState) =>
        new(
            visualState.Id,
            visualState.SemanticElementId,
            visualState.Position,
            visualState.Size,
            visualState.PlacementMode,
            visualState.Route,
            visualState.Properties,
            visualState.ConnectorAnchors,
            visualState.SourceAnchorId,
            visualState.TargetAnchorId,
            visualState.BoundaryAttachment);
}
