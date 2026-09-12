namespace Inceptus.DocumentEngine.Contracts.Commands;

public static class CommandExecutionDiagnosticCodes
{
    public const string MissingHandler = "CMD_MISSING_HANDLER";

    public const string InvalidCommandStructure = "CMD_INVALID_COMMAND_STRUCTURE";

    public const string DuplicateHandlerRegistration = "CMD_DUPLICATE_HANDLER_REGISTRATION";

    public const string TransactionCreationFailure = "CMD_TRANSACTION_CREATION_FAILURE";

    public const string HandlerFailure = "CMD_HANDLER_FAILURE";

    public const string ProposedStateInvalid = "CMD_PROPOSED_STATE_INVALID";

    public const string AffectedComponentViolation = "CMD_AFFECTED_COMPONENT_VIOLATION";

    public const string RevisionPreparationFailure = "CMD_REVISION_PREPARATION_FAILURE";

    public const string EventPayloadPreparationFailure = "CMD_EVENT_PAYLOAD_PREPARATION_FAILURE";

    public const string Cancelled = "CMD_CANCELLED";

    public const string InternalFailure = "CMD_INTERNAL_FAILURE";

    public const string SemanticElementNotFound = "CMD_SEMANTIC_ELEMENT_NOT_FOUND";

    public const string SemanticNamePropertyUnsupported =
        "CMD_SEMANTIC_NAME_PROPERTY_UNSUPPORTED";

    public const string SemanticNameInvalid = "CMD_SEMANTIC_NAME_INVALID";

    public const string SemanticNameUnchanged = "CMD_SEMANTIC_NAME_UNCHANGED";

    public const string SemanticPropertyUnsupported =
        "CMD_SEMANTIC_PROPERTY_UNSUPPORTED";

    public const string SemanticPropertyUnchanged =
        "CMD_SEMANTIC_PROPERTY_UNCHANGED";

    public const string ModelProfileAvailabilityUnchanged =
        "CMD_MODEL_PROFILE_AVAILABILITY_UNCHANGED";

    public const string DocumentPublicationInvalid =
        "CMD_DOCUMENT_PUBLICATION_INVALID";

    public const string DocumentPublicationUnchanged =
        "CMD_DOCUMENT_PUBLICATION_UNCHANGED";

    public const string DocumentScopeIdentityExists =
        "CMD_DOCUMENT_SCOPE_IDENTITY_EXISTS";

    public const string DocumentScopeNotFound =
        "CMD_DOCUMENT_SCOPE_NOT_FOUND";

    public const string DocumentScopeNotEmpty =
        "CMD_DOCUMENT_SCOPE_NOT_EMPTY";

    public const string VisualStateNotFound = "CMD_VISUAL_STATE_NOT_FOUND";

    public const string VisualStateDoesNotSupportPosition =
        "CMD_VISUAL_STATE_POSITION_UNSUPPORTED";

    public const string VisualStateDoesNotSupportRoute =
        "CMD_VISUAL_STATE_ROUTE_UNSUPPORTED";

    public const string VisualStateDoesNotSupportLabelPlacement =
        "CMD_VISUAL_STATE_LABEL_PLACEMENT_UNSUPPORTED";

    public const string VisualStateDoesNotSupportNodeLabelVisualOverride =
        "CMD_VISUAL_STATE_NODE_LABEL_OVERRIDE_UNSUPPORTED";

    public const string VisualStateDoesNotSupportBoundaryAttachment =
        "CMD_VISUAL_STATE_BOUNDARY_ATTACHMENT_UNSUPPORTED";

    public const string VisualStateDoesNotSupportConnectorAnchors =
        "CMD_VISUAL_STATE_CONNECTOR_ANCHORS_UNSUPPORTED";

    public const string ConnectorAnchorIdentityDuplicate =
        "CMD_CONNECTOR_ANCHOR_ID_DUPLICATE";

    public const string ConnectorAnchorInsertionIndexInvalid =
        "CMD_CONNECTOR_ANCHOR_INSERTION_INDEX_INVALID";

    public const string ConnectorAnchorNotFound =
        "CMD_CONNECTOR_ANCHOR_NOT_FOUND";

    public const string ConnectorAnchorInUse =
        "CMD_CONNECTOR_ANCHOR_IN_USE";

    public const string ConnectorAnchorPolicyViolation =
        "CMD_CONNECTOR_ANCHOR_POLICY_VIOLATION";

    public const string LabelPlacementUnchanged =
        "CMD_LABEL_PLACEMENT_UNCHANGED";

    public const string NodeLabelVisualOverrideUnchanged =
        "CMD_NODE_LABEL_VISUAL_OVERRIDE_UNCHANGED";

    public const string BoundaryAttachmentUnchanged =
        "CMD_BOUNDARY_ATTACHMENT_UNCHANGED";

    public const string BoundaryAttachmentOwnerUnavailable =
        "CMD_BOUNDARY_ATTACHMENT_OWNER_UNAVAILABLE";

    public const string VisualStateGeometryInvalid =
        "CMD_VISUAL_STATE_GEOMETRY_INVALID";

    public const string SubscriberFailure = "CMD_SUBSCRIBER_FAILURE";

    public const string EventDispatchSchedulingFailure =
        "CMD_EVENT_DISPATCH_SCHEDULING_FAILURE";
}
