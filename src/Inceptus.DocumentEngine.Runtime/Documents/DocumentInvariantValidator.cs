using System.Collections.Immutable;
using System.Globalization;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Documents;

internal static class DocumentInvariantValidator
{
    internal const string SemanticIdentityDuplicateCode = "DOC_SEMANTIC_ID_DUPLICATE";
    internal const string RelationshipSourceMissingCode = "DOC_RELATIONSHIP_SOURCE_MISSING";
    internal const string RelationshipTargetMissingCode = "DOC_RELATIONSHIP_TARGET_MISSING";
    internal const string SemanticAttachmentReferenceMissingCode =
        "DOC_SEMANTIC_ATTACHMENT_REFERENCE_MISSING";
    internal const string SemanticAttachmentScopeMismatchCode =
        "DOC_SEMANTIC_ATTACHMENT_SCOPE_MISMATCH";
    internal const string SemanticAttachmentCycleCode =
        "DOC_SEMANTIC_ATTACHMENT_CYCLE";
    internal const string ScopeIdentityDuplicateCode = "DOC_SCOPE_ID_DUPLICATE";
    internal const string ScopeRootCollisionCode = "DOC_SCOPE_ROOT_COLLISION";
    internal const string ScopeParentMissingCode = "DOC_SCOPE_PARENT_MISSING";
    internal const string ScopeSelfParentCode = "DOC_SCOPE_SELF_PARENT";
    internal const string ScopeCycleCode = "DOC_SCOPE_CYCLE";
    internal const string ScopeMembershipElementMissingCode =
        "DOC_SCOPE_MEMBERSHIP_ELEMENT_MISSING";
    internal const string ScopeMembershipScopeMissingCode =
        "DOC_SCOPE_MEMBERSHIP_SCOPE_MISSING";
    internal const string ScopeMembershipDuplicateCode = "DOC_SCOPE_MEMBERSHIP_DUPLICATE";
    internal const string ScopeMembershipRootExplicitCode =
        "DOC_SCOPE_MEMBERSHIP_ROOT_EXPLICIT";
    internal const string DocumentElementScopeMembershipCode =
        "DOC_DOCUMENT_ELEMENT_SCOPE_MEMBERSHIP";
    internal const string ScopeOwnerMissingCode = "DOC_SCOPE_OWNER_MISSING";
    internal const string ScopeOwnerContainmentInvalidCode =
        "DOC_SCOPE_OWNER_CONTAINMENT_INVALID";
    internal const string ScopeOwnerParentMismatchCode = "DOC_SCOPE_OWNER_PARENT_MISMATCH";
    internal const string ScopeOwnerDuplicateCode = "DOC_SCOPE_OWNER_DUPLICATE";
    internal const string VisualIdentityDuplicateCode = "DOC_VISUAL_ID_DUPLICATE";
    internal const string VisualSemanticReferenceMissingCode =
        "DOC_VISUAL_SEMANTIC_REFERENCE_MISSING";
    internal const string ProfileAssignmentReferenceMissingCode =
        "DOC_PROFILE_ASSIGNMENT_REFERENCE_MISSING";
    internal const string ProfileAssignmentContainmentInvalidCode =
        "DOC_PROFILE_ASSIGNMENT_CONTAINMENT_INVALID";
    internal const string ProfileAssignmentScopeMismatchCode =
        "DOC_PROFILE_ASSIGNMENT_SCOPE_MISMATCH";
    internal const string ProfilePresentationReferenceMissingCode =
        "DOC_PROFILE_PRESENTATION_REFERENCE_MISSING";
    internal const string ComponentDocumentMismatchCode = "DOC_COMPONENT_ID_MISMATCH";
    internal const string ComponentRevisionMismatchCode = "DOC_COMPONENT_REVISION_MISMATCH";
    internal const string VisualGeometryInvalidCode = "DOC_VISUAL_GEOMETRY_INVALID";
    internal const string VisualRouteInvalidCode = "DOC_VISUAL_ROUTE_INVALID";
    internal const string VisualPlacementInvalidCode = "DOC_VISUAL_PLACEMENT_INVALID";
    internal const string VisualBoundaryAttachmentInvalidCode =
        "DOC_VISUAL_BOUNDARY_ATTACHMENT_INVALID";
    internal const string VisualLabelPlacementInvalidCode =
        "DOC_VISUAL_LABEL_PLACEMENT_INVALID";
    internal const string VisualNodeLabelOverrideInvalidCode =
        "DOC_VISUAL_NODE_LABEL_OVERRIDE_INVALID";
    internal const string VisualConnectorAnchorInvalidCode =
        "DOC_VISUAL_CONNECTOR_ANCHOR_INVALID";
    internal const string VisualConnectorAnchorIdentityDuplicateCode =
        "DOC_VISUAL_CONNECTOR_ANCHOR_ID_DUPLICATE";
    internal const string VisualConnectorAnchorOwnershipInvalidCode =
        "DOC_VISUAL_CONNECTOR_ANCHOR_OWNERSHIP_INVALID";
    internal const string VisualConnectorAnchorReferenceInvalidCode =
        "DOC_VISUAL_CONNECTOR_ANCHOR_REFERENCE_INVALID";
    internal const string VisualConnectorAnchorOccupancyInvalidCode =
        "DOC_VISUAL_CONNECTOR_ANCHOR_OCCUPANCY_INVALID";
    internal const string VisualConnectorAnchorPolicyInvalidCode =
        "DOC_VISUAL_CONNECTOR_ANCHOR_POLICY_INVALID";

    public static ImmutableArray<Diagnostic> Validate(
        DocumentSnapshot snapshot,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var policyProvider = connectorAnchorPolicyProvider ??
            ElementConnectorAnchorPolicyRegistry.Default;

        var diagnostics = new List<Diagnostic>();
        ValidateComponentCoherence(snapshot, diagnostics);

        var elementIds = new HashSet<SemanticElementId>();
        var elementsById = new Dictionary<SemanticElementId, SemanticElementSnapshot>();
        var relationshipIds = new HashSet<SemanticElementId>();
        var relationshipsById = new Dictionary<SemanticElementId, SemanticRelationshipSnapshot>();
        var allSemanticIds = new HashSet<SemanticElementId>();

        foreach (var element in snapshot.SemanticModel.Elements)
        {
            elementsById.TryAdd(element.Id, element);
            if (!elementIds.Add(element.Id) || !allSemanticIds.Add(element.Id))
            {
                diagnostics.Add(Error(
                    SemanticIdentityDuplicateCode,
                    $"Semantic identity '{element.Id}' occurs more than once.",
                    element.Id.Value));
            }
        }

        ValidateScopes(snapshot.SemanticModel, elementsById, diagnostics);
        ValidateSemanticAttachments(snapshot.SemanticModel, elementsById, diagnostics);
        ValidateProfileRecords(snapshot, elementsById, diagnostics);

        foreach (var relationship in snapshot.SemanticModel.Relationships)
        {
            relationshipIds.Add(relationship.Id);
            relationshipsById.TryAdd(relationship.Id, relationship);
            if (!allSemanticIds.Add(relationship.Id))
            {
                diagnostics.Add(Error(
                    SemanticIdentityDuplicateCode,
                    $"Semantic identity '{relationship.Id}' occurs more than once.",
                    relationship.Id.Value));
            }

            if (!elementIds.Contains(relationship.SourceId))
            {
                diagnostics.Add(Error(
                    RelationshipSourceMissingCode,
                    $"Relationship '{relationship.Id}' references missing source element '{relationship.SourceId}'.",
                    relationship.Id.Value,
                    new KeyValuePair<string, string>("ReferenceId", relationship.SourceId.Value)));
            }

            if (!elementIds.Contains(relationship.TargetId))
            {
                diagnostics.Add(Error(
                    RelationshipTargetMissingCode,
                    $"Relationship '{relationship.Id}' references missing target element '{relationship.TargetId}'.",
                    relationship.Id.Value,
                    new KeyValuePair<string, string>("ReferenceId", relationship.TargetId.Value)));
            }
        }

        var visualIds = new HashSet<VisualStateId>();
        var anchorsById = new Dictionary<ConnectorAnchorId, ResolvedAnchorOwner>();

        foreach (var visualState in snapshot.VisualModel.VisualStates)
        {
            if (!visualIds.Add(visualState.Id))
            {
                diagnostics.Add(Error(
                    VisualIdentityDuplicateCode,
                    $"Visual identity '{visualState.Id}' occurs more than once.",
                    visualState.Id.Value));
            }

            if (!allSemanticIds.Contains(visualState.SemanticElementId))
            {
                diagnostics.Add(Error(
                    VisualSemanticReferenceMissingCode,
                    $"Visual state '{visualState.Id}' references missing semantic identity '{visualState.SemanticElementId}'.",
                    visualState.Id.Value,
                    new KeyValuePair<string, string>(
                        "ReferenceId",
                        visualState.SemanticElementId.Value)));
            }

            ValidateVisualState(
                visualState,
                elementIds.Contains(visualState.SemanticElementId),
                relationshipIds.Contains(visualState.SemanticElementId),
                diagnostics);

            ValidateConnectorAnchorOwnership(
                visualState,
                elementIds.Contains(visualState.SemanticElementId),
                relationshipIds.Contains(visualState.SemanticElementId),
                diagnostics);
            if (!elementsById.TryGetValue(
                    visualState.SemanticElementId,
                    out var owningElement))
            {
                continue;
            }

            ImmutableArray<ResolvedConnectorAnchor> resolvedAnchors;
            try
            {
                resolvedAnchors = ElementConnectorAnchorResolver.Resolve(
                    visualState,
                    owningElement.TypeId,
                    policyProvider);
            }
            catch (InvalidOperationException exception)
            {
                diagnostics.Add(Error(
                    VisualConnectorAnchorPolicyInvalidCode,
                    exception.Message,
                    visualState.Id.Value,
                    new KeyValuePair<string, string>(
                        "ElementTypeId",
                        owningElement.TypeId.Value)));
                continue;
            }

            foreach (var anchor in resolvedAnchors)
            {
                if (!anchorsById.TryAdd(
                        anchor.Id,
                        new ResolvedAnchorOwner(visualState, anchor)))
                {
                    diagnostics.Add(Error(
                        VisualConnectorAnchorIdentityDuplicateCode,
                        $"Connector-anchor identity '{anchor.Id}' occurs more than once in the Document.",
                        visualState.Id.Value,
                        new KeyValuePair<string, string>("AnchorId", anchor.Id.Value)));
                }
            }
        }

        ValidateBoundaryAttachments(snapshot, elementsById, diagnostics);

        foreach (var visualState in snapshot.VisualModel.VisualStates)
        {
            if (relationshipsById.TryGetValue(
                    visualState.SemanticElementId,
                    out var relationship))
            {
                ValidateConnectorAnchorReference(
                    visualState,
                    relationship,
                    visualState.SourceAnchorId,
                    ConnectorAnchorRole.Source,
                    relationship.SourceId,
                    anchorsById,
                    diagnostics);
                ValidateConnectorAnchorReference(
                    visualState,
                    relationship,
                    visualState.TargetAnchorId,
                    ConnectorAnchorRole.Target,
                    relationship.TargetId,
                    anchorsById,
                    diagnostics);
            }
        }

        ValidateConnectorAnchorOccupancy(snapshot.VisualModel, diagnostics);

        return [.. diagnostics];
    }

    private static void ValidateProfileRecords(
        DocumentSnapshot snapshot,
        Dictionary<SemanticElementId, SemanticElementSnapshot> elementsById,
        List<Diagnostic> diagnostics)
    {
        var semanticModel = snapshot.SemanticModel;
        var memberships = semanticModel.ScopeMemberships.ToDictionary(
            static membership => membership.SemanticElementId,
            static membership => membership.ScopeId);
        foreach (var assignment in semanticModel.ProfileAssignments)
        {
            if (!elementsById.TryGetValue(assignment.SemanticElementId, out var source) ||
                !elementsById.TryGetValue(assignment.ContainerSemanticElementId, out var container))
            {
                diagnostics.Add(Error(
                    ProfileAssignmentReferenceMissingCode,
                    "Profile assignments must reference existing semantic elements.",
                    assignment.SemanticElementId.Value));
                continue;
            }

            if (source.Id == container.Id ||
                source.ContainmentKind != SemanticElementContainmentKind.Scope ||
                container.ContainmentKind != SemanticElementContainmentKind.Scope)
            {
                diagnostics.Add(Error(
                    ProfileAssignmentContainmentInvalidCode,
                    "Profile assignments require distinct scope-contained source and container elements.",
                    source.Id.Value));
                continue;
            }

            var sourceScopeId = memberships.GetValueOrDefault(source.Id, semanticModel.RootScopeId);
            var containerScopeId = memberships.GetValueOrDefault(container.Id, semanticModel.RootScopeId);
            if (sourceScopeId != containerScopeId)
            {
                diagnostics.Add(Error(
                    ProfileAssignmentScopeMismatchCode,
                    "Profile assignment endpoints must belong to the same exact semantic scope.",
                    source.Id.Value));
            }
        }

        foreach (var presentation in snapshot.VisualModel.ProfileElementPresentations)
        {
            if (!elementsById.ContainsKey(presentation.SemanticElementId))
            {
                diagnostics.Add(Error(
                    ProfilePresentationReferenceMissingCode,
                    "Profile presentation records must reference existing semantic elements.",
                    presentation.SemanticElementId.Value));
            }
        }
    }

    private static void ValidateSemanticAttachments(
        SemanticModelSnapshot semanticModel,
        Dictionary<SemanticElementId, SemanticElementSnapshot> elementsById,
        List<Diagnostic> diagnostics)
    {
        var membershipsByElementId = new Dictionary<
            SemanticElementId,
            DocumentScopeId>();
        foreach (var membership in semanticModel.ScopeMemberships)
        {
            // Duplicate memberships are diagnosed by ValidateScopes. Keep the first
            // deterministic value here so attachment validation never masks that result
            // with a Dictionary construction exception.
            _ = membershipsByElementId.TryAdd(
                membership.SemanticElementId,
                membership.ScopeId);
        }

        foreach (var element in semanticModel.Elements)
        {
            if (element.AttachedToElementId is not { } ownerId)
            {
                continue;
            }

            if (!elementsById.ContainsKey(ownerId))
            {
                diagnostics.Add(Error(
                    SemanticAttachmentReferenceMissingCode,
                    $"Semantic element '{element.Id}' references missing attachment owner '{ownerId}'.",
                    element.Id.Value,
                    new KeyValuePair<string, string>("AttachedToElementId", ownerId.Value)));
                continue;
            }

            var owner = elementsById[ownerId];
            if (element.ContainmentKind != owner.ContainmentKind)
            {
                diagnostics.Add(Error(
                    SemanticAttachmentScopeMismatchCode,
                    $"Semantic element '{element.Id}' and attachment owner '{ownerId}' use different containment kinds.",
                    element.Id.Value,
                    new KeyValuePair<string, string>(
                        "ElementContainmentKind",
                        element.ContainmentKind.ToString()),
                    new KeyValuePair<string, string>(
                        "OwnerContainmentKind",
                        owner.ContainmentKind.ToString())));
                continue;
            }

            if (element.ContainmentKind == SemanticElementContainmentKind.Scope)
            {
                var elementScopeId = membershipsByElementId.TryGetValue(
                    element.Id,
                    out var elementScope)
                    ? elementScope
                    : semanticModel.RootScopeId;
                var ownerScopeId = membershipsByElementId.TryGetValue(ownerId, out var ownerScope)
                    ? ownerScope
                    : semanticModel.RootScopeId;
                if (elementScopeId != ownerScopeId)
                {
                    diagnostics.Add(Error(
                        SemanticAttachmentScopeMismatchCode,
                        $"Semantic element '{element.Id}' and attachment owner '{ownerId}' belong to different Document scopes.",
                        element.Id.Value,
                        new KeyValuePair<string, string>(
                            "AttachedElementScopeId",
                            elementScopeId.Value),
                        new KeyValuePair<string, string>("OwnerScopeId", ownerScopeId.Value)));
                }
            }
        }

        foreach (var cycleMember in FindSemanticAttachmentCycleMembers(elementsById))
        {
            diagnostics.Add(Error(
                SemanticAttachmentCycleCode,
                $"Semantic element '{cycleMember}' participates in an attachment cycle.",
                cycleMember.Value));
        }
    }

    private static IEnumerable<SemanticElementId> FindSemanticAttachmentCycleMembers(
        Dictionary<SemanticElementId, SemanticElementSnapshot> elementsById)
    {
        var completed = new HashSet<SemanticElementId>();
        var cycleMembers = new HashSet<SemanticElementId>();

        foreach (var startId in elementsById.Keys.OrderBy(
                     static id => id.Value,
                     StringComparer.Ordinal))
        {
            if (completed.Contains(startId))
            {
                continue;
            }

            var path = new List<SemanticElementId>();
            var pathIndexes = new Dictionary<SemanticElementId, int>();
            var currentId = startId;
            while (elementsById.TryGetValue(currentId, out var current) &&
                current.AttachedToElementId is { } ownerId &&
                elementsById.ContainsKey(ownerId))
            {
                if (pathIndexes.TryGetValue(currentId, out var cycleStartIndex))
                {
                    foreach (var cycleMember in path.Skip(cycleStartIndex))
                    {
                        cycleMembers.Add(cycleMember);
                    }

                    break;
                }

                if (completed.Contains(currentId))
                {
                    break;
                }

                pathIndexes.Add(currentId, path.Count);
                path.Add(currentId);
                currentId = ownerId;
            }

            completed.UnionWith(path);
        }

        return cycleMembers.OrderBy(static id => id.Value, StringComparer.Ordinal);
    }

    private static void ValidateBoundaryAttachments(
        DocumentSnapshot snapshot,
        Dictionary<SemanticElementId, SemanticElementSnapshot> elementsById,
        List<Diagnostic> diagnostics)
    {
        var visualStatesBySemanticId = snapshot.VisualModel.VisualStates
            .GroupBy(static visualState => visualState.SemanticElementId)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToArray());

        foreach (var visualState in snapshot.VisualModel.VisualStates)
        {
            elementsById.TryGetValue(visualState.SemanticElementId, out var element);
            var structuralOwnerId = element?.AttachedToElementId;
            if (visualState.BoundaryAttachment is null)
            {
                if (structuralOwnerId is not null)
                {
                    diagnostics.Add(Error(
                        VisualBoundaryAttachmentInvalidCode,
                        $"Visual state '{visualState.Id}' belongs to a structurally attached element but has no boundary placement.",
                        visualState.Id.Value));
                }

                continue;
            }

            if (structuralOwnerId is null)
            {
                diagnostics.Add(Error(
                    VisualBoundaryAttachmentInvalidCode,
                    $"Visual state '{visualState.Id}' declares boundary placement without a structural semantic attachment owner.",
                    visualState.Id.Value));
                continue;
            }

            if (!visualStatesBySemanticId.TryGetValue(
                    structuralOwnerId,
                    out var ownerVisualStates) ||
                ownerVisualStates.Length != 1)
            {
                diagnostics.Add(Error(
                    VisualBoundaryAttachmentInvalidCode,
                    $"Visual state '{visualState.Id}' requires exactly one visual owner for semantic element '{structuralOwnerId}'.",
                    visualState.Id.Value,
                    new KeyValuePair<string, string>(
                        "AttachedToElementId",
                        structuralOwnerId.Value)));
                continue;
            }

            try
            {
                var ownerVisual = ownerVisualStates[0];
                var fallbackBounds = new RectD(
                    visualState.Position.X,
                    visualState.Position.Y,
                    visualState.Size.Width,
                    visualState.Size.Height);
                if (ownerVisual.PlacementMode != VisualPlacementMode.Pinned &&
                    DocumentGeometryBoundary.Contains(fallbackBounds))
                {
                    // Automatic and Manual owner positions are Layout inputs rather than
                    // authoritative rectangles. Their attached compatibility bounds may
                    // therefore be derived from effective Layout geometry that is not
                    // present in the persistent snapshot.
                    continue;
                }

                var ownerBounds = new RectD(
                    ownerVisual.Position.X,
                    ownerVisual.Position.Y,
                    ownerVisual.Size.Width,
                    ownerVisual.Size.Height);
                var expectedBounds = visualState.BoundaryAttachment.ResolveBounds(
                    ownerBounds,
                    visualState.Size);
                if (expectedBounds.TopLeft == visualState.Position &&
                    DocumentGeometryBoundary.Contains(expectedBounds))
                {
                    continue;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // The deterministic diagnostic below covers invalid derived geometry.
            }

            diagnostics.Add(Error(
                VisualBoundaryAttachmentInvalidCode,
                $"Visual state '{visualState.Id}' does not contain legal fallback bounds synchronized to its pinned owner and boundary placement.",
                visualState.Id.Value));
        }
    }

    private static void ValidateScopes(
        SemanticModelSnapshot semanticModel,
        Dictionary<SemanticElementId, SemanticElementSnapshot> elementsById,
        List<Diagnostic> diagnostics)
    {
        var scopesById = new Dictionary<DocumentScopeId, DocumentScopeSnapshot>();
        foreach (var scope in semanticModel.NestedScopes)
        {
            if (scope.Id == semanticModel.RootScopeId)
            {
                diagnostics.Add(Error(
                    ScopeRootCollisionCode,
                    $"Nested scope '{scope.Id}' duplicates the canonical root scope identity.",
                    scope.Id.Value));
                continue;
            }

            if (!scopesById.TryAdd(scope.Id, scope))
            {
                diagnostics.Add(Error(
                    ScopeIdentityDuplicateCode,
                    $"Document scope identity '{scope.Id}' occurs more than once.",
                    scope.Id.Value));
            }
        }

        var membershipsByElementId =
            new Dictionary<SemanticElementId, SemanticElementScopeMembershipSnapshot>();
        foreach (var membership in semanticModel.ScopeMemberships)
        {
            if (!membershipsByElementId.TryAdd(membership.SemanticElementId, membership))
            {
                diagnostics.Add(Error(
                    ScopeMembershipDuplicateCode,
                    $"Semantic element '{membership.SemanticElementId}' has more than one scope membership.",
                    membership.SemanticElementId.Value));
            }

            if (!elementsById.ContainsKey(membership.SemanticElementId))
            {
                diagnostics.Add(Error(
                    ScopeMembershipElementMissingCode,
                    $"Scope membership references missing semantic element '{membership.SemanticElementId}'.",
                    membership.SemanticElementId.Value,
                    new KeyValuePair<string, string>(
                        "ScopeId",
                        membership.ScopeId.Value)));
            }

            if (elementsById.TryGetValue(membership.SemanticElementId, out var member) &&
                member.ContainmentKind == SemanticElementContainmentKind.Document)
            {
                diagnostics.Add(Error(
                    DocumentElementScopeMembershipCode,
                    $"Document-contained semantic element '{membership.SemanticElementId}' cannot have semantic-scope membership.",
                    membership.SemanticElementId.Value,
                    new KeyValuePair<string, string>(
                        "ScopeId",
                        membership.ScopeId.Value)));
            }

            if (membership.ScopeId == semanticModel.RootScopeId)
            {
                diagnostics.Add(Error(
                    ScopeMembershipRootExplicitCode,
                    $"Semantic element '{membership.SemanticElementId}' explicitly references the canonical root scope; root membership must remain implicit.",
                    membership.SemanticElementId.Value,
                    new KeyValuePair<string, string>(
                        "ScopeId",
                        membership.ScopeId.Value)));
            }
            else if (!scopesById.ContainsKey(membership.ScopeId))
            {
                diagnostics.Add(Error(
                    ScopeMembershipScopeMissingCode,
                    $"Semantic element '{membership.SemanticElementId}' references missing scope '{membership.ScopeId}'.",
                    membership.SemanticElementId.Value,
                    new KeyValuePair<string, string>(
                        "ScopeId",
                        membership.ScopeId.Value)));
            }
        }

        var scopesByOwner = new Dictionary<SemanticElementId, DocumentScopeSnapshot>();
        foreach (var scope in scopesById.Values.OrderBy(
                     static scope => scope.Id.Value,
                     StringComparer.Ordinal))
        {
            if (scope.ParentScopeId is null && scope.OwnerSemanticElementId is not null)
            {
                diagnostics.Add(Error(
                    ScopeParentMissingCode,
                    $"Owned scope '{scope.Id}' does not reference a parent scope.",
                    scope.Id.Value));
            }
            else if (scope.ParentScopeId == scope.Id)
            {
                diagnostics.Add(Error(
                    ScopeSelfParentCode,
                    $"Nested scope '{scope.Id}' cannot be its own parent.",
                    scope.Id.Value));
            }
            else if (scope.ParentScopeId is { } referencedParentScopeId &&
                referencedParentScopeId != semanticModel.RootScopeId &&
                !scopesById.ContainsKey(referencedParentScopeId))
            {
                diagnostics.Add(Error(
                    ScopeParentMissingCode,
                    $"Nested scope '{scope.Id}' references missing parent scope '{scope.ParentScopeId}'.",
                    scope.Id.Value,
                    new KeyValuePair<string, string>(
                        "ParentScopeId",
                        referencedParentScopeId.Value)));
            }

            if (scope.OwnerSemanticElementId is not { } ownerId)
            {
                continue;
            }

            if (!scopesByOwner.TryAdd(ownerId, scope))
            {
                diagnostics.Add(Error(
                    ScopeOwnerDuplicateCode,
                    $"Semantic element '{ownerId}' owns more than one child scope.",
                    ownerId.Value,
                    new KeyValuePair<string, string>("ScopeId", scope.Id.Value)));
            }

            if (!elementsById.TryGetValue(ownerId, out var ownerElement))
            {
                diagnostics.Add(Error(
                    ScopeOwnerMissingCode,
                    $"Nested scope '{scope.Id}' references missing owner semantic element '{ownerId}'.",
                    scope.Id.Value,
                    new KeyValuePair<string, string>("OwnerSemanticElementId", ownerId.Value)));
                continue;
            }

            if (ownerElement.ContainmentKind != SemanticElementContainmentKind.Scope)
            {
                diagnostics.Add(Error(
                    ScopeOwnerContainmentInvalidCode,
                    $"Document-contained semantic element '{ownerId}' cannot own semantic scope '{scope.Id}'.",
                    scope.Id.Value,
                    new KeyValuePair<string, string>("OwnerSemanticElementId", ownerId.Value)));
                continue;
            }

            if (scope.ParentScopeId is not { } parentScopeId ||
                (parentScopeId != semanticModel.RootScopeId &&
                    !scopesById.ContainsKey(parentScopeId)))
            {
                continue;
            }

            var ownerScopeId = membershipsByElementId.TryGetValue(ownerId, out var membership)
                ? membership.ScopeId
                : semanticModel.RootScopeId;
            if (ownerScopeId != parentScopeId)
            {
                diagnostics.Add(Error(
                    ScopeOwnerParentMismatchCode,
                    $"Owner semantic element '{ownerId}' does not belong to parent scope '{parentScopeId}' of child scope '{scope.Id}'.",
                    scope.Id.Value,
                    new KeyValuePair<string, string>("OwnerSemanticElementId", ownerId.Value),
                    new KeyValuePair<string, string>("OwnerScopeId", ownerScopeId.Value),
                    new KeyValuePair<string, string>("ParentScopeId", parentScopeId.Value)));
            }
        }

        foreach (var cycleMember in FindScopeCycleMembers(
                     semanticModel.RootScopeId,
                     scopesById))
        {
            diagnostics.Add(Error(
                ScopeCycleCode,
                $"Document scope '{cycleMember}' participates in a containment cycle.",
                cycleMember.Value));
        }
    }

    private static IEnumerable<DocumentScopeId> FindScopeCycleMembers(
        DocumentScopeId rootScopeId,
        Dictionary<DocumentScopeId, DocumentScopeSnapshot> scopesById)
    {
        var completed = new HashSet<DocumentScopeId>();
        var cycleMembers = new HashSet<DocumentScopeId>();

        foreach (var startScopeId in scopesById.Keys.OrderBy(
                     static id => id.Value,
                     StringComparer.Ordinal))
        {
            if (completed.Contains(startScopeId))
            {
                continue;
            }

            var path = new List<DocumentScopeId>();
            var pathIndexes = new Dictionary<DocumentScopeId, int>();
            var currentScopeId = startScopeId;
            while (currentScopeId != rootScopeId &&
                scopesById.TryGetValue(currentScopeId, out var currentScope))
            {
                if (pathIndexes.TryGetValue(currentScopeId, out var cycleStartIndex))
                {
                    foreach (var cycleMember in path.Skip(cycleStartIndex))
                    {
                        cycleMembers.Add(cycleMember);
                    }

                    break;
                }

                if (completed.Contains(currentScopeId))
                {
                    break;
                }

                pathIndexes.Add(currentScopeId, path.Count);
                path.Add(currentScopeId);
                if (currentScope.ParentScopeId is not { } parentScopeId)
                {
                    break;
                }

                currentScopeId = parentScopeId;
            }

            completed.UnionWith(path);
        }

        return cycleMembers.OrderBy(static id => id.Value, StringComparer.Ordinal);
    }

    private static void ValidateConnectorAnchorOccupancy(
        IVisualModelView visualModel,
        List<Diagnostic> diagnostics)
    {
        foreach (var references in ConnectorAnchorOccupancy
                     .EnumerateEndpointReferences(visualModel)
                     .GroupBy(static anchorId => anchorId)
                     .OrderBy(static group => group.Key.Value, StringComparer.Ordinal))
        {
            var referenceCount = references.Count();
            if (referenceCount <= 1)
            {
                continue;
            }

            diagnostics.Add(Error(
                VisualConnectorAnchorOccupancyInvalidCode,
                FormattableString.Invariant(
                    $"Connector anchor '{references.Key}' is referenced by {referenceCount} connector endpoints; at most one endpoint reference is permitted."),
                references.Key.Value,
                new KeyValuePair<string, string>("AnchorId", references.Key.Value),
                new KeyValuePair<string, string>(
                    "EndpointReferenceCount",
                    referenceCount.ToString(CultureInfo.InvariantCulture))));
        }
    }

    private static void ValidateComponentCoherence(
        DocumentSnapshot snapshot,
        List<Diagnostic> diagnostics)
    {
        ValidateComponent(
            snapshot,
            "SemanticModel",
            snapshot.SemanticModel.DocumentId,
            snapshot.SemanticModel.Revision,
            diagnostics);
        ValidateComponent(
            snapshot,
            "VisualModel",
            snapshot.VisualModel.DocumentId,
            snapshot.VisualModel.Revision,
            diagnostics);
        ValidateComponent(
            snapshot,
            "Metadata",
            snapshot.Metadata.DocumentId,
            snapshot.Metadata.Revision,
            diagnostics);
    }

    private static void ValidateComponent(
        DocumentSnapshot snapshot,
        string componentName,
        DocumentId componentDocumentId,
        DocumentRevision componentRevision,
        List<Diagnostic> diagnostics)
    {
        if (componentDocumentId != snapshot.DocumentId)
        {
            diagnostics.Add(Error(
                ComponentDocumentMismatchCode,
                $"{componentName} belongs to a different Document.",
                componentName));
        }

        if (componentRevision != snapshot.Revision)
        {
            diagnostics.Add(Error(
                ComponentRevisionMismatchCode,
                $"{componentName} describes a different Document revision.",
                componentName));
        }
    }

    private static void ValidateVisualState(
        VisualStateSnapshot visualState,
        bool isElementOwned,
        bool isRelationshipOwned,
        List<Diagnostic> diagnostics)
    {
        if (!double.IsFinite(visualState.Position.X) ||
            !double.IsFinite(visualState.Position.Y) ||
            !double.IsFinite(visualState.Size.Width) ||
            !double.IsFinite(visualState.Size.Height) ||
            visualState.Size.Width < 0d ||
            visualState.Size.Height < 0d ||
            (isElementOwned &&
                (visualState.Size.Width <= 0d || visualState.Size.Height <= 0d)) ||
            !DocumentGeometryBoundary.Contains(visualState.Position))
        {
            diagnostics.Add(Error(
                VisualGeometryInvalidCode,
                $"Visual state '{visualState.Id}' contains invalid persistent geometry.",
                visualState.Id.Value));
        }

        if (!Enum.IsDefined(visualState.PlacementMode))
        {
            diagnostics.Add(Error(
                VisualPlacementInvalidCode,
                $"Visual state '{visualState.Id}' contains an undefined placement mode.",
                visualState.Id.Value));
        }

        if (visualState.Route.Any(static point =>
                !double.IsFinite(point.X) ||
                !double.IsFinite(point.Y) ||
                !DocumentGeometryBoundary.Contains(point)))
        {
            diagnostics.Add(Error(
                VisualRouteInvalidCode,
                $"Visual state '{visualState.Id}' contains an invalid persistent route point.",
                visualState.Id.Value));
        }

        ValidateConnectorLabelPlacement(
            visualState,
            isRelationshipOwned,
            diagnostics);
        ValidateNodeLabelVisualOverride(
            visualState,
            isElementOwned,
            diagnostics);
    }

    private static void ValidateConnectorLabelPlacement(
        VisualStateSnapshot visualState,
        bool isRelationshipOwned,
        List<Diagnostic> diagnostics)
    {
        var properties = visualState.Properties;
        var hasPathPosition = properties.TryGetValue(
            ConnectorLabelPlacement.PathPositionPropertyKey,
            out var pathPosition);
        var hasOffsetX = properties.TryGetValue(
            ConnectorLabelPlacement.OffsetXPropertyKey,
            out var offsetX);
        var hasOffsetY = properties.TryGetValue(
            ConnectorLabelPlacement.OffsetYPropertyKey,
            out var offsetY);
        if (!hasPathPosition && !hasOffsetX && !hasOffsetY)
        {
            return;
        }

        var isCompleteNumericPlacement = hasPathPosition &&
            pathPosition!.Kind == PropertyValueKind.Number &&
            hasOffsetX &&
            offsetX!.Kind == PropertyValueKind.Number &&
            hasOffsetY &&
            offsetY!.Kind == PropertyValueKind.Number;
        var pathPositionIsValid = isCompleteNumericPlacement &&
            double.IsFinite(pathPosition!.NumberValue) &&
            pathPosition.NumberValue is >= 0d and <= 1d;
        var offsetsAreValid = isCompleteNumericPlacement &&
            double.IsFinite(offsetX!.NumberValue) &&
            double.IsFinite(offsetY!.NumberValue);
        if (isRelationshipOwned && pathPositionIsValid && offsetsAreValid)
        {
            return;
        }

        diagnostics.Add(Error(
            VisualLabelPlacementInvalidCode,
            $"Visual state '{visualState.Id}' contains an invalid connector-label placement. The reserved placement properties must be a complete numeric triplet on a relationship-owned Visual State, with PathPosition between zero and one.",
            visualState.Id.Value));
    }

    private static void ValidateNodeLabelVisualOverride(
        VisualStateSnapshot visualState,
        bool isElementOwned,
        List<Diagnostic> diagnostics)
    {
        var properties = visualState.Properties;
        var hasOffsetX = properties.TryGetValue(
            NodeLabelVisualOverride.OffsetXPropertyKey,
            out var offsetX);
        var hasOffsetY = properties.TryGetValue(
            NodeLabelVisualOverride.OffsetYPropertyKey,
            out var offsetY);
        var hasWidth = properties.TryGetValue(
            NodeLabelVisualOverride.WidthPropertyKey,
            out var width);
        var hasHeight = properties.TryGetValue(
            NodeLabelVisualOverride.HeightPropertyKey,
            out var height);
        if (!hasOffsetX && !hasOffsetY && !hasWidth && !hasHeight)
        {
            return;
        }

        var isCompleteNumericOverride = hasOffsetX &&
            offsetX!.Kind == PropertyValueKind.Number &&
            hasOffsetY &&
            offsetY!.Kind == PropertyValueKind.Number &&
            hasWidth &&
            width!.Kind == PropertyValueKind.Number &&
            hasHeight &&
            height!.Kind == PropertyValueKind.Number;
        var offsetsAreValid = isCompleteNumericOverride &&
            double.IsFinite(offsetX!.NumberValue) &&
            double.IsFinite(offsetY!.NumberValue);
        var dimensionsAreValid = isCompleteNumericOverride &&
            double.IsFinite(width!.NumberValue) &&
            width.NumberValue >= NodeLabelVisualOverride.MinimumWidth &&
            double.IsFinite(height!.NumberValue) &&
            height.NumberValue >= NodeLabelVisualOverride.MinimumHeight;
        var overrideIsWithinDocument = true;
        if (offsetsAreValid && dimensionsAreValid)
        {
            var visualOverride = new NodeLabelVisualOverride(
                offsetX!.NumberValue,
                offsetY!.NumberValue,
                width!.NumberValue,
                height!.NumberValue);
            if (VisualStatePersistentGeometry.TryResolveAuthoritativeNodeBounds(
                    visualState,
                    out var ownerBounds))
            {
                overrideIsWithinDocument = DocumentGeometryBoundary.Contains(
                    visualOverride.ResolveBounds(ownerBounds));
            }
        }

        if (isElementOwned && offsetsAreValid && dimensionsAreValid &&
            overrideIsWithinDocument)
        {
            return;
        }

        diagnostics.Add(Error(
            VisualNodeLabelOverrideInvalidCode,
            $"Visual state '{visualState.Id}' contains an invalid node-label visual override. The reserved override properties must be a complete numeric quartet on an element-owned Visual State, with Width and Height at least one logical document unit and a Document-space box inside the Document boundary.",
            visualState.Id.Value));
    }

    private static void ValidateConnectorAnchorOwnership(
        VisualStateSnapshot visualState,
        bool isElementOwned,
        bool isRelationshipOwned,
        List<Diagnostic> diagnostics)
    {
        if (visualState.ConnectorAnchors.Any(anchor =>
                !Enum.IsDefined(anchor.Side) ||
                !Enum.IsDefined(anchor.Role) ||
                anchor.Order < 0))
        {
            diagnostics.Add(Error(
                VisualConnectorAnchorInvalidCode,
                $"Visual state '{visualState.Id}' contains invalid connector-anchor side, role, or order data.",
                visualState.Id.Value));
        }

        foreach (var sideGroup in visualState.ConnectorAnchors.GroupBy(static anchor => anchor.Side))
        {
            var orders = sideGroup
                .Select(static anchor => anchor.Order)
                .Order()
                .ToArray();
            if (orders.Where((order, index) => order != index).Any())
            {
                diagnostics.Add(Error(
                    VisualConnectorAnchorInvalidCode,
                    $"Visual state '{visualState.Id}' has non-contiguous connector-anchor order on side '{sideGroup.Key}'.",
                    visualState.Id.Value));
            }
        }

        if (visualState.ConnectorAnchors.Length > 0 && !isElementOwned)
        {
            diagnostics.Add(Error(
                VisualConnectorAnchorOwnershipInvalidCode,
                $"Visual state '{visualState.Id}' owns connector anchors but does not represent a semantic element.",
                visualState.Id.Value));
        }

        if ((visualState.SourceAnchorId is not null ||
             visualState.TargetAnchorId is not null) &&
            !isRelationshipOwned)
        {
            diagnostics.Add(Error(
                VisualConnectorAnchorOwnershipInvalidCode,
                $"Visual state '{visualState.Id}' references connector anchors but does not represent a semantic relationship.",
                visualState.Id.Value));
        }
    }

    private static void ValidateConnectorAnchorReference(
        VisualStateSnapshot connectorVisualState,
        SemanticRelationshipSnapshot relationship,
        ConnectorAnchorId? anchorId,
        ConnectorAnchorRole expectedRole,
        SemanticElementId expectedOwnerSemanticId,
        IReadOnlyDictionary<ConnectorAnchorId, ResolvedAnchorOwner> anchorsById,
        List<Diagnostic> diagnostics)
    {
        if (anchorId is null)
        {
            return;
        }

        if (anchorsById.TryGetValue(anchorId, out var referenced) &&
            referenced.Anchor.Allows(expectedRole) &&
            referenced.VisualState.SemanticElementId == expectedOwnerSemanticId)
        {
            return;
        }

        diagnostics.Add(Error(
            VisualConnectorAnchorReferenceInvalidCode,
            $"Relationship visual state '{connectorVisualState.Id}' has an invalid {expectedRole} connector-anchor reference '{anchorId}'.",
            connectorVisualState.Id.Value,
            new KeyValuePair<string, string>("AnchorId", anchorId.Value),
            new KeyValuePair<string, string>("RelationshipId", relationship.Id.Value),
            new KeyValuePair<string, string>("ExpectedOwnerSemanticId", expectedOwnerSemanticId.Value),
            new KeyValuePair<string, string>("ExpectedRole", expectedRole.ToString())));
    }

    private static Diagnostic Error(
        string code,
        string message,
        string sourceIdentity,
        params KeyValuePair<string, string>[] context) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity, context);

    private sealed record ResolvedAnchorOwner(
        VisualStateSnapshot VisualState,
        ResolvedConnectorAnchor Anchor);
}
