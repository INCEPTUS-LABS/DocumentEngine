using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Documents;

internal static class NativeDocumentJsonCodec
{
    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
    };

    public static ImmutableArray<byte> Write(DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("format", NativeDocumentSerializer.FormatIdentifier);
            writer.WriteNumber("formatVersion", NativeDocumentSerializer.FormatVersion);
            WriteDocument(writer, snapshot);
            writer.WriteEndObject();
        }

        return ImmutableArray.CreateRange(buffer.WrittenSpan.ToArray());
    }

    public static DocumentSnapshot Read(ReadOnlyMemory<byte> utf8Json)
    {
        using var parsed = JsonDocument.Parse(utf8Json, ReaderOptions);
        var root = parsed.RootElement;
        ValidateObject(root, "$", "format", "formatVersion", "document");

        var format = ReadString(Required(root, "format"), "$.format");
        if (!StringComparer.Ordinal.Equals(format, NativeDocumentSerializer.FormatIdentifier))
        {
            throw new NativeDocumentReadException(
                NativeDocumentReadFailureKind.InvalidFormat,
                "$.format",
                "The native format identifier is not supported.",
                format);
        }

        var versionElement = Required(root, "formatVersion");
        if (versionElement.ValueKind != JsonValueKind.Number ||
            !BigInteger.TryParse(
                versionElement.GetRawText(),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var version))
        {
            throw Structure(
                "$.formatVersion",
                "The native format version must be an integer.");
        }

        if (version != NativeDocumentSerializer.FormatVersion)
        {
            throw new NativeDocumentReadException(
                NativeDocumentReadFailureKind.UnsupportedVersion,
                "$.formatVersion",
                "The native format version is not supported.",
                version.ToString(CultureInfo.InvariantCulture));
        }

        return ReadDocument(Required(root, "document"), "$.document");
    }

    private static void WriteDocument(Utf8JsonWriter writer, DocumentSnapshot snapshot)
    {
        writer.WriteStartObject("document");
        WriteDataString(writer, "id", snapshot.DocumentId.Value);
        writer.WriteNumber("revision", snapshot.Revision.Value);
        WriteSemanticModel(writer, snapshot.SemanticModel);
        WriteVisualModel(writer, snapshot.VisualModel);
        WriteMetadata(writer, snapshot.Metadata);
        if (snapshot.Publication is { } publication)
        {
            WritePublication(writer, publication);
        }
        writer.WriteEndObject();
    }

    private static void WriteSemanticModel(
        Utf8JsonWriter writer,
        SemanticModelSnapshot semanticModel)
    {
        writer.WriteStartObject("semanticModel");

        writer.WriteStartArray("elements");
        foreach (var element in semanticModel.Elements)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "id", element.Id.Value);
            WriteDataString(writer, "typeId", element.TypeId.Value);
            WriteProperties(writer, element.Properties);
            WriteNullableDataString(
                writer,
                "attachedToElementId",
                element.AttachedToElementId?.Value);
            writer.WriteString("containmentKind", element.ContainmentKind.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("relationships");
        foreach (var relationship in semanticModel.Relationships)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "id", relationship.Id.Value);
            WriteDataString(writer, "typeId", relationship.TypeId.Value);
            WriteDataString(writer, "sourceId", relationship.SourceId.Value);
            WriteDataString(writer, "targetId", relationship.TargetId.Value);
            WriteProperties(writer, relationship.Properties);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("scopes");
        foreach (var scope in semanticModel.NestedScopes)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "id", scope.Id.Value);
            WriteNullableDataString(writer, "parentScopeId", scope.ParentScopeId?.Value);
            WriteNullableDataString(
                writer,
                "ownerSemanticElementId",
                scope.OwnerSemanticElementId?.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("scopeMemberships");
        foreach (var membership in semanticModel.ScopeMemberships)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "semanticElementId", membership.SemanticElementId.Value);
            WriteDataString(writer, "scopeId", membership.ScopeId.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartObject("modelProfiles");
        writer.WriteStartArray("availableProfileIds");
        foreach (var profileId in semanticModel.ModelProfiles.AvailableProfileIds)
        {
            WriteDataStringValue(writer, profileId.Value);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteStartArray("profileAssignments");
        foreach (var assignment in semanticModel.ProfileAssignments)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "profileId", assignment.ProfileId.Value);
            WriteDataString(writer, "semanticElementId", assignment.SemanticElementId.Value);
            WriteDataString(
                writer,
                "containerSemanticElementId",
                assignment.ContainerSemanticElementId.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteVisualModel(
        Utf8JsonWriter writer,
        VisualModelSnapshot visualModel)
    {
        writer.WriteStartObject("visualModel");
        writer.WriteStartArray("visualStates");
        foreach (var visualState in visualModel.VisualStates)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "id", visualState.Id.Value);
            WriteDataString(writer, "semanticElementId", visualState.SemanticElementId.Value);
            WritePoint(writer, "position", visualState.Position);
            WriteSize(writer, visualState.Size);
            writer.WriteString("placementMode", visualState.PlacementMode.ToString());

            writer.WriteStartArray("route");
            foreach (var point in visualState.Route)
            {
                WritePoint(writer, point);
            }

            writer.WriteEndArray();
            WriteProperties(writer, visualState.Properties);

            writer.WriteStartArray("connectorAnchors");
            foreach (var anchor in visualState.ConnectorAnchors)
            {
                writer.WriteStartObject();
                WriteDataString(writer, "id", anchor.Id.Value);
                writer.WriteString("side", anchor.Side.ToString());
                writer.WriteString("role", anchor.Role.ToString());
                writer.WriteNumber("order", anchor.Order);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            WriteNullableDataString(writer, "sourceAnchorId", visualState.SourceAnchorId?.Value);
            WriteNullableDataString(writer, "targetAnchorId", visualState.TargetAnchorId?.Value);
            WriteBoundaryAttachment(writer, visualState.BoundaryAttachment);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("profileElementPresentations");
        foreach (var presentation in visualModel.ProfileElementPresentations)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "profileId", presentation.ProfileId.Value);
            WriteDataString(writer, "semanticElementId", presentation.SemanticElementId.Value);
            writer.WriteNumber("order", presentation.Order);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteMetadata(
        Utf8JsonWriter writer,
        DocumentMetadataSnapshot metadata)
    {
        writer.WriteStartObject("metadata");
        WriteProperties(writer, "systemManagedProperties", metadata.SystemManagedProperties);
        WriteProperties(writer, "extensionProperties", metadata.ExtensionProperties);
        writer.WriteEndObject();
    }

    private static void WritePublication(
        Utf8JsonWriter writer,
        DocumentPublicationSnapshot publication)
    {
        writer.WriteStartObject("publication");
        WriteDataString(writer, "code", publication.Code);
        WriteDataString(writer, "title", publication.Title);
        WriteDataString(writer, "description", publication.Description);
        writer.WriteEndObject();
    }

    private static void WriteProperties(Utf8JsonWriter writer, PropertyMap properties) =>
        WriteProperties(writer, "properties", properties);

    private static void WriteProperties(
        Utf8JsonWriter writer,
        string propertyName,
        PropertyMap properties)
    {
        writer.WriteStartArray(propertyName);
        foreach (var (key, value) in properties)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "key", key);
            writer.WriteString("kind", PropertyKindName(value.Kind));
            writer.WritePropertyName("value");
            WritePropertyValue(writer, value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePropertyValue(Utf8JsonWriter writer, PropertyValue value)
    {
        switch (value.Kind)
        {
            case PropertyValueKind.Text:
                WriteDataStringValue(writer, value.TextValue);
                break;
            case PropertyValueKind.Boolean:
                writer.WriteBooleanValue(value.BooleanValue);
                break;
            case PropertyValueKind.Integer:
                writer.WriteNumberValue(value.IntegerValue);
                break;
            case PropertyValueKind.Number:
                writer.WriteNumberValue(value.NumberValue);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported persistent property kind '{value.Kind}'.");
        }
    }

    private static string PropertyKindName(PropertyValueKind kind) => kind switch
    {
        PropertyValueKind.Text => "text",
        PropertyValueKind.Boolean => "boolean",
        PropertyValueKind.Integer => "integer",
        PropertyValueKind.Number => "number",
        _ => throw new InvalidOperationException($"Unsupported persistent property kind '{kind}'."),
    };

    private static void WritePoint(Utf8JsonWriter writer, string propertyName, PointD point)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteNumber("x", point.X);
        writer.WriteNumber("y", point.Y);
        writer.WriteEndObject();
    }

    private static void WritePoint(Utf8JsonWriter writer, PointD point)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", point.X);
        writer.WriteNumber("y", point.Y);
        writer.WriteEndObject();
    }

    private static void WriteSize(Utf8JsonWriter writer, SizeD size)
    {
        writer.WriteStartObject("size");
        writer.WriteNumber("width", size.Width);
        writer.WriteNumber("height", size.Height);
        writer.WriteEndObject();
    }

    private static void WriteBoundaryAttachment(
        Utf8JsonWriter writer,
        BoundaryAttachmentPlacement? boundaryAttachment)
    {
        if (boundaryAttachment is null)
        {
            writer.WriteNull("boundaryAttachment");
            return;
        }

        writer.WriteStartObject("boundaryAttachment");
        writer.WriteString("side", boundaryAttachment.Side.ToString());
        writer.WriteNumber("positionOnSide", boundaryAttachment.PositionOnSide);
        writer.WriteEndObject();
    }

    private static void WriteDataString(
        Utf8JsonWriter writer,
        string propertyName,
        string value)
    {
        writer.WritePropertyName(propertyName);
        WriteDataStringValue(writer, value);
    }

    private static void WriteDataStringValue(Utf8JsonWriter writer, string value)
    {
        if (IsWellFormedUtf16(value))
        {
            writer.WriteStringValue(value);
            return;
        }

        var codeUnits = new byte[checked(value.Length * 2)];
        for (var index = 0; index < value.Length; index++)
        {
            var codeUnit = value[index];
            codeUnits[index * 2] = (byte)codeUnit;
            codeUnits[(index * 2) + 1] = (byte)(codeUnit >> 8);
        }

        writer.WriteStartObject();
        writer.WriteString("utf16LeBase64", Convert.ToBase64String(codeUnits));
        writer.WriteEndObject();
    }

    private static void WriteNullableDataString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            WriteDataString(writer, propertyName, value);
        }
    }

    private static DocumentSnapshot ReadDocument(JsonElement value, string path)
    {
        ValidateObjectWithOptionalProperties(
            value,
            path,
            ["id", "revision", "semanticModel", "visualModel", "metadata"],
            ["publication"]);

        var documentId = new DocumentId(ReadDataString(
            Required(value, "id"),
            $"{path}.id"));
        var revision = new DocumentRevision(
            ReadUInt64(Required(value, "revision"), $"{path}.revision"));
        var semanticModel = ReadSemanticModel(
            Required(value, "semanticModel"),
            $"{path}.semanticModel",
            documentId,
            revision);
        var visualModel = ReadVisualModel(
            Required(value, "visualModel"),
            $"{path}.visualModel",
            documentId,
            revision);
        var metadata = ReadMetadata(
            Required(value, "metadata"),
            $"{path}.metadata",
            documentId,
            revision);
        var publication = value.TryGetProperty("publication", out var publicationValue)
            ? ReadPublication(publicationValue, $"{path}.publication")
            : null;

        return new DocumentSnapshot(semanticModel, visualModel, metadata, publication);
    }

    private static DocumentPublicationSnapshot ReadPublication(
        JsonElement value,
        string path)
    {
        ValidateObject(value, path, "code", "title", "description");
        var code = ReadDataString(Required(value, "code"), $"{path}.code");
        var title = ReadDataString(Required(value, "title"), $"{path}.title");
        var description = ReadDataString(
            Required(value, "description"),
            $"{path}.description");
        if (!DocumentPublicationSnapshot.TryCreate(
                code,
                title,
                description,
                out var publication) ||
            !StringComparer.Ordinal.Equals(code, publication.Code) ||
            !StringComparer.Ordinal.Equals(title, publication.Title) ||
            !StringComparer.Ordinal.Equals(description, publication.Description))
        {
            throw Structure(
                path,
                "Publication must contain a canonical Code, Title, and Description.");
        }

        return publication;
    }

    private static SemanticModelSnapshot ReadSemanticModel(
        JsonElement value,
        string path,
        DocumentId documentId,
        DocumentRevision revision)
    {
        ValidateObject(
            value,
            path,
            "elements",
            "relationships",
            "scopes",
            "scopeMemberships",
            "modelProfiles",
            "profileAssignments");

        var elements = ReadArray(
            Required(value, "elements"),
            $"{path}.elements",
            ReadSemanticElement);
        var relationships = ReadArray(
            Required(value, "relationships"),
            $"{path}.relationships",
            ReadSemanticRelationship);
        var scopes = ReadArray(
            Required(value, "scopes"),
            $"{path}.scopes",
            ReadScope);
        var memberships = ReadArray(
            Required(value, "scopeMemberships"),
            $"{path}.scopeMemberships",
            ReadScopeMembership);
        var modelProfiles = ReadModelProfiles(
            Required(value, "modelProfiles"),
            $"{path}.modelProfiles");
        var assignments = ReadArray(
            Required(value, "profileAssignments"),
            $"{path}.profileAssignments",
            ReadProfileAssignment);

        return new SemanticModelSnapshot(
            documentId,
            revision,
            elements,
            relationships,
            scopes,
            memberships,
            modelProfiles,
            assignments);
    }

    private static SemanticElementSnapshot ReadSemanticElement(JsonElement value, string path)
    {
        ValidateObject(
            value,
            path,
            "id",
            "typeId",
            "properties",
            "attachedToElementId",
            "containmentKind");

        var attachedTo = ReadNullableDataString(
            Required(value, "attachedToElementId"),
            $"{path}.attachedToElementId");
        return new SemanticElementSnapshot(
            new SemanticElementId(ReadDataString(Required(value, "id"), $"{path}.id")),
            new SemanticTypeId(ReadDataString(
                Required(value, "typeId"),
                $"{path}.typeId")),
            ReadProperties(Required(value, "properties"), $"{path}.properties"),
            attachedTo is null ? null : new SemanticElementId(attachedTo),
            ReadEnum<SemanticElementContainmentKind>(
                Required(value, "containmentKind"),
                $"{path}.containmentKind"));
    }

    private static SemanticRelationshipSnapshot ReadSemanticRelationship(
        JsonElement value,
        string path)
    {
        ValidateObject(value, path, "id", "typeId", "sourceId", "targetId", "properties");
        return new SemanticRelationshipSnapshot(
            new SemanticElementId(ReadDataString(Required(value, "id"), $"{path}.id")),
            new SemanticTypeId(ReadDataString(
                Required(value, "typeId"),
                $"{path}.typeId")),
            new SemanticElementId(ReadDataString(
                Required(value, "sourceId"),
                $"{path}.sourceId")),
            new SemanticElementId(ReadDataString(
                Required(value, "targetId"),
                $"{path}.targetId")),
            ReadProperties(Required(value, "properties"), $"{path}.properties"));
    }

    private static DocumentScopeSnapshot ReadScope(JsonElement value, string path)
    {
        ValidateObject(value, path, "id", "parentScopeId", "ownerSemanticElementId");
        var parentId = ReadNullableDataString(
            Required(value, "parentScopeId"),
            $"{path}.parentScopeId");
        var ownerId = ReadNullableDataString(
            Required(value, "ownerSemanticElementId"),
            $"{path}.ownerSemanticElementId");
        return new DocumentScopeSnapshot(
            new DocumentScopeId(ReadDataString(Required(value, "id"), $"{path}.id")),
            parentId is null ? null : new DocumentScopeId(parentId),
            ownerId is null ? null : new SemanticElementId(ownerId));
    }

    private static SemanticElementScopeMembershipSnapshot ReadScopeMembership(
        JsonElement value,
        string path)
    {
        ValidateObject(value, path, "semanticElementId", "scopeId");
        return new SemanticElementScopeMembershipSnapshot(
            new SemanticElementId(ReadDataString(
                Required(value, "semanticElementId"),
                $"{path}.semanticElementId")),
            new DocumentScopeId(ReadDataString(
                Required(value, "scopeId"),
                $"{path}.scopeId")));
    }

    private static ModelProfileStateSnapshot ReadModelProfiles(JsonElement value, string path)
    {
        ValidateObject(value, path, "availableProfileIds");
        return new ModelProfileStateSnapshot(ReadArray(
            Required(value, "availableProfileIds"),
            $"{path}.availableProfileIds",
            static (entry, entryPath) => new ModelProfileId(ReadDataString(entry, entryPath))));
    }

    private static ModelProfileElementAssignmentSnapshot ReadProfileAssignment(
        JsonElement value,
        string path)
    {
        ValidateObject(
            value,
            path,
            "profileId",
            "semanticElementId",
            "containerSemanticElementId");
        return new ModelProfileElementAssignmentSnapshot(
            new ModelProfileId(ReadDataString(
                Required(value, "profileId"),
                $"{path}.profileId")),
            new SemanticElementId(ReadDataString(
                Required(value, "semanticElementId"),
                $"{path}.semanticElementId")),
            new SemanticElementId(ReadDataString(
                Required(value, "containerSemanticElementId"),
                $"{path}.containerSemanticElementId")));
    }

    private static VisualModelSnapshot ReadVisualModel(
        JsonElement value,
        string path,
        DocumentId documentId,
        DocumentRevision revision)
    {
        ValidateObject(value, path, "visualStates", "profileElementPresentations");
        return new VisualModelSnapshot(
            documentId,
            revision,
            ReadArray(
                Required(value, "visualStates"),
                $"{path}.visualStates",
                ReadVisualState),
            ReadArray(
                Required(value, "profileElementPresentations"),
                $"{path}.profileElementPresentations",
                ReadProfilePresentation));
    }

    private static VisualStateSnapshot ReadVisualState(JsonElement value, string path)
    {
        ValidateObject(
            value,
            path,
            "id",
            "semanticElementId",
            "position",
            "size",
            "placementMode",
            "route",
            "properties",
            "connectorAnchors",
            "sourceAnchorId",
            "targetAnchorId",
            "boundaryAttachment");

        var sourceAnchorId = ReadNullableDataString(
            Required(value, "sourceAnchorId"),
            $"{path}.sourceAnchorId");
        var targetAnchorId = ReadNullableDataString(
            Required(value, "targetAnchorId"),
            $"{path}.targetAnchorId");
        return new VisualStateSnapshot(
            new VisualStateId(ReadDataString(Required(value, "id"), $"{path}.id")),
            new SemanticElementId(ReadDataString(
                Required(value, "semanticElementId"),
                $"{path}.semanticElementId")),
            ReadPoint(Required(value, "position"), $"{path}.position"),
            ReadSize(Required(value, "size"), $"{path}.size"),
            ReadEnum<VisualPlacementMode>(
                Required(value, "placementMode"),
                $"{path}.placementMode"),
            ReadArray(Required(value, "route"), $"{path}.route", ReadPoint),
            ReadProperties(Required(value, "properties"), $"{path}.properties"),
            ReadArray(
                Required(value, "connectorAnchors"),
                $"{path}.connectorAnchors",
                ReadConnectorAnchor),
            sourceAnchorId is null ? null : new ConnectorAnchorId(sourceAnchorId),
            targetAnchorId is null ? null : new ConnectorAnchorId(targetAnchorId),
            ReadBoundaryAttachment(
                Required(value, "boundaryAttachment"),
                $"{path}.boundaryAttachment"));
    }

    private static ConnectorAnchor ReadConnectorAnchor(JsonElement value, string path)
    {
        ValidateObject(value, path, "id", "side", "role", "order");
        return new ConnectorAnchor(
            new ConnectorAnchorId(ReadDataString(Required(value, "id"), $"{path}.id")),
            ReadEnum<ConnectorAnchorSide>(Required(value, "side"), $"{path}.side"),
            ReadEnum<ConnectorAnchorRole>(Required(value, "role"), $"{path}.role"),
            ReadInt32(Required(value, "order"), $"{path}.order"));
    }

    private static BoundaryAttachmentPlacement? ReadBoundaryAttachment(
        JsonElement value,
        string path)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        ValidateObject(value, path, "side", "positionOnSide");
        return new BoundaryAttachmentPlacement(
            ReadEnum<BoundaryAttachmentSide>(Required(value, "side"), $"{path}.side"),
            ReadDouble(
                Required(value, "positionOnSide"),
                $"{path}.positionOnSide"));
    }

    private static ModelProfileElementPresentationSnapshot ReadProfilePresentation(
        JsonElement value,
        string path)
    {
        ValidateObject(value, path, "profileId", "semanticElementId", "order");
        return new ModelProfileElementPresentationSnapshot(
            new ModelProfileId(ReadDataString(
                Required(value, "profileId"),
                $"{path}.profileId")),
            new SemanticElementId(ReadDataString(
                Required(value, "semanticElementId"),
                $"{path}.semanticElementId")),
            ReadInt32(Required(value, "order"), $"{path}.order"));
    }

    private static DocumentMetadataSnapshot ReadMetadata(
        JsonElement value,
        string path,
        DocumentId documentId,
        DocumentRevision revision)
    {
        ValidateObject(value, path, "systemManagedProperties", "extensionProperties");
        return new DocumentMetadataSnapshot(
            documentId,
            revision,
            ReadProperties(
                Required(value, "systemManagedProperties"),
                $"{path}.systemManagedProperties"),
            ReadProperties(
                Required(value, "extensionProperties"),
                $"{path}.extensionProperties"));
    }

    private static List<KeyValuePair<string, PropertyValue>> ReadProperties(
        JsonElement value,
        string path) =>
        ReadArray(value, path, ReadProperty);

    private static KeyValuePair<string, PropertyValue> ReadProperty(
        JsonElement value,
        string path)
    {
        ValidateObject(value, path, "key", "kind", "value");
        var key = ReadDataString(Required(value, "key"), $"{path}.key");
        var kind = ReadString(Required(value, "kind"), $"{path}.kind");
        var propertyValue = Required(value, "value");
        return new KeyValuePair<string, PropertyValue>(
            key,
            kind switch
            {
                "text" => PropertyValue.FromText(ReadDataString(
                    propertyValue,
                    $"{path}.value")),
                "boolean" => PropertyValue.FromBoolean(ReadBoolean(
                    propertyValue,
                    $"{path}.value")),
                "integer" => PropertyValue.FromInteger(ReadInt64(
                    propertyValue,
                    $"{path}.value")),
                "number" => PropertyValue.FromNumber(ReadDouble(
                    propertyValue,
                    $"{path}.value")),
                _ => throw Structure(
                    $"{path}.kind",
                    $"Property value kind '{kind}' is not supported."),
            });
    }

    private static PointD ReadPoint(JsonElement value, string path)
    {
        ValidateObject(value, path, "x", "y");
        return new PointD(
            ReadDouble(Required(value, "x"), $"{path}.x"),
            ReadDouble(Required(value, "y"), $"{path}.y"));
    }

    private static SizeD ReadSize(JsonElement value, string path)
    {
        ValidateObject(value, path, "width", "height");
        return new SizeD(
            ReadDouble(Required(value, "width"), $"{path}.width"),
            ReadDouble(Required(value, "height"), $"{path}.height"));
    }

    private static List<T> ReadArray<T>(
        JsonElement value,
        string path,
        Func<JsonElement, string, T> readEntry)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Structure(path, "The value must be a JSON array.");
        }

        var result = new List<T>();
        var index = 0;
        foreach (var entry in value.EnumerateArray())
        {
            result.Add(readEntry(entry, $"{path}[{index}]"));
            index++;
        }

        return result;
    }

    private static void ValidateObject(
        JsonElement value,
        string path,
        params string[] requiredProperties)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Structure(path, "The value must be a JSON object.");
        }

        var required = new HashSet<string>(requiredProperties, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw Structure(
                    $"{path}.{property.Name}",
                    $"The JSON member '{property.Name}' occurs more than once.");
            }

            if (!required.Contains(property.Name))
            {
                throw Structure(
                    $"{path}.{property.Name}",
                    $"The JSON member '{property.Name}' is not defined by native format version 1.");
            }
        }

        foreach (var propertyName in requiredProperties)
        {
            if (!seen.Contains(propertyName))
            {
                throw Structure(
                    path,
                    $"The required JSON member '{propertyName}' is missing.");
            }
        }
    }

    private static void ValidateObjectWithOptionalProperties(
        JsonElement value,
        string path,
        IReadOnlyCollection<string> requiredProperties,
        IReadOnlyCollection<string> optionalProperties)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Structure(path, "The value must be a JSON object.");
        }

        var required = new HashSet<string>(requiredProperties, StringComparer.Ordinal);
        var allowed = new HashSet<string>(required, StringComparer.Ordinal);
        allowed.UnionWith(optionalProperties);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw Structure(
                    $"{path}.{property.Name}",
                    $"The JSON member '{property.Name}' occurs more than once.");
            }

            if (!allowed.Contains(property.Name))
            {
                throw Structure(
                    $"{path}.{property.Name}",
                    $"The JSON member '{property.Name}' is not defined by native format version 1.");
            }
        }

        foreach (var propertyName in required)
        {
            if (!seen.Contains(propertyName))
            {
                throw Structure(
                    path,
                    $"The required JSON member '{propertyName}' is missing.");
            }
        }
    }

    private static JsonElement Required(JsonElement value, string propertyName) =>
        value.GetProperty(propertyName);

    private static string ReadString(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Structure(path, "The value must be a JSON string.");
        }

        return value.GetString()!;
    }

    private static string ReadDataString(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()!;
        }

        ValidateObject(value, path, "utf16LeBase64");
        var encoded = ReadString(
            Required(value, "utf16LeBase64"),
            $"{path}.utf16LeBase64");
        byte[] codeUnits;
        try
        {
            codeUnits = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw Structure(
                $"{path}.utf16LeBase64",
                "The UTF-16 code-unit fallback must contain valid canonical Base64.",
                exception);
        }

        if (codeUnits.Length % 2 != 0 ||
            !StringComparer.Ordinal.Equals(Convert.ToBase64String(codeUnits), encoded))
        {
            throw Structure(
                $"{path}.utf16LeBase64",
                "The UTF-16 code-unit fallback must contain valid canonical Base64.");
        }

        var characters = new char[codeUnits.Length / 2];
        for (var index = 0; index < characters.Length; index++)
        {
            characters[index] = (char)(codeUnits[index * 2] |
                (codeUnits[(index * 2) + 1] << 8));
        }

        var result = new string(characters);
        if (IsWellFormedUtf16(result))
        {
            throw Structure(
                path,
                "The UTF-16 code-unit fallback is permitted only for text that cannot be represented as a JSON string.");
        }

        return result;
    }

    private static string? ReadNullableDataString(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadDataString(value, path);
    }

    private static bool ReadBoolean(JsonElement value, string path)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Structure(path, "The value must be a JSON Boolean.");
        }

        return value.GetBoolean();
    }

    private static ulong ReadUInt64(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out var result))
        {
            throw Structure(path, "The value must be an unsigned 64-bit integer.");
        }

        return result;
    }

    private static long ReadInt64(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
        {
            throw Structure(path, "The value must be a signed 64-bit integer.");
        }

        return result;
    }

    private static int ReadInt32(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            throw Structure(path, "The value must be a 32-bit integer.");
        }

        return result;
    }

    private static double ReadDouble(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
        {
            throw Structure(path, "The value must be a finite JSON number.");
        }

        return result;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement value, string path)
        where TEnum : struct, Enum
    {
        var name = ReadString(value, path);
        if (!Enum.TryParse<TEnum>(name, ignoreCase: false, out var result) ||
            !Enum.IsDefined(result))
        {
            throw Structure(path, $"Enum value '{name}' is not supported.");
        }

        return result;
    }

    private static bool IsWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var codeUnit = value[index];
            if (char.IsLowSurrogate(codeUnit))
            {
                return false;
            }

            if (!char.IsHighSurrogate(codeUnit))
            {
                continue;
            }

            if (++index >= value.Length || !char.IsLowSurrogate(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static NativeDocumentReadException Structure(
        string path,
        string message,
        Exception? innerException = null) =>
        new(
            NativeDocumentReadFailureKind.InvalidStructure,
            path,
            $"The native Document structure is invalid at '{path}': {message}",
            innerException: innerException);
}

internal enum NativeDocumentReadFailureKind
{
    InvalidStructure,
    InvalidFormat,
    UnsupportedVersion,
}

#pragma warning disable CA1032 // Internal parser control flow carries required structured details.
internal sealed class NativeDocumentReadException : Exception
{
    public NativeDocumentReadException(
        NativeDocumentReadFailureKind kind,
        string path,
        string message,
        string? actualValue = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        Path = path;
        ActualValue = actualValue;
    }

    public NativeDocumentReadFailureKind Kind { get; }

    public string Path { get; }

    public string? ActualValue { get; }
}
#pragma warning restore CA1032
