using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Contracts.Routing;

/// <summary>Scoped immutable source inputs, without recursive saved routing or mutable Document access.</summary>
public sealed class ScopeGeometryInputs
{
    public ScopeGeometryInputs(
        DocumentScopeId scopeId,
        IEnumerable<SemanticElementSnapshot> elements,
        IEnumerable<SemanticRelationshipSnapshot> relationships,
        IEnumerable<SemanticElementScopeMembershipSnapshot> scopeMemberships,
        IEnumerable<VisualStateSnapshot> visualStates,
        ModelProfileStateSnapshot modelProfiles,
        IEnumerable<ModelProfileElementAssignmentSnapshot> profileAssignments,
        IEnumerable<ModelProfileElementPresentationSnapshot> profileElementPresentations)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentNullException.ThrowIfNull(modelProfiles);
        ScopeId = scopeId;
        Elements = RoutingStateCollection.Copy(elements, nameof(elements));
        Relationships = RoutingStateCollection.Copy(relationships, nameof(relationships));
        ScopeMemberships = RoutingStateCollection.Copy(scopeMemberships, nameof(scopeMemberships));
        VisualStates = RoutingStateCollection.Copy(visualStates, nameof(visualStates));
        ModelProfiles = modelProfiles;
        ProfileAssignments = RoutingStateCollection.Copy(profileAssignments, nameof(profileAssignments));
        ProfileElementPresentations = RoutingStateCollection.Copy(profileElementPresentations, nameof(profileElementPresentations));
    }

    public DocumentScopeId ScopeId { get; }
    public ImmutableArray<SemanticElementSnapshot> Elements { get; }
    public ImmutableArray<SemanticRelationshipSnapshot> Relationships { get; }
    public ImmutableArray<SemanticElementScopeMembershipSnapshot> ScopeMemberships { get; }
    public ImmutableArray<VisualStateSnapshot> VisualStates { get; }
    public ModelProfileStateSnapshot ModelProfiles { get; }
    public ImmutableArray<ModelProfileElementAssignmentSnapshot> ProfileAssignments { get; }
    public ImmutableArray<ModelProfileElementPresentationSnapshot> ProfileElementPresentations { get; }
}
