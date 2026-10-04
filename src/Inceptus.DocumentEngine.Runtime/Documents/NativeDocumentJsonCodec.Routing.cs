using System.Collections.Immutable;
using System.Text.Json;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Documents;

internal static partial class NativeDocumentJsonCodec
{
    private const string LogicalSpace = "scopeLogical";

    private static void WriteRoutingScopes(Utf8JsonWriter writer, ImmutableArray<ScopeRoutingSnapshot> scopes)
    {
        writer.WriteStartArray("routingScopes");
        foreach (var scope in scopes)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "scopeId", scope.ScopeId.Value);
            WriteScopeGeometry(writer, scope.Geometry);
            writer.WriteStartArray("connectors");
            // This sequence is authored priority; never sort it by identity.
            foreach (var connector in scope.Connectors)
            {
                writer.WriteStartObject();
                WriteDataString(writer, "visualStateId", connector.VisualStateId.Value);
                writer.WriteString("routingType", connector.RoutingType switch
                {
                    ConnectorRoutingType.Automatic => "automatic",
                    ConnectorRoutingType.Straight => "straight",
                    ConnectorRoutingType.Manual => "manual",
                    _ => throw new InvalidOperationException("Unsupported connector routing type."),
                });
                writer.WriteString("outcome", connector.Outcome == ConnectorRoutingOutcome.Path ? "path" : "noRoute");
                writer.WriteString("space", LogicalSpace);
                WritePoints(writer, "path", connector.Path);
                if (connector.ManualDefinition is { } definition)
                    WritePoints(writer, "manualDefinition", definition);
                else
                    writer.WriteNull("manualDefinition");
                WriteAutomaticProof(writer, connector.AutomaticProof);
                if (connector.NoRouteReason is null)
                    writer.WriteNull("noRouteReason");
                else
                    writer.WriteString("noRouteReason", "noFeasiblePath");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteScopeGeometry(Utf8JsonWriter writer, ScopeGeometrySnapshot geometry)
    {
        writer.WriteStartObject("geometry");
        writer.WriteString("space", LogicalSpace);
        WriteDataString(writer, "policyId", geometry.PolicyId);
        WriteDataString(writer, "policyVersion", geometry.PolicyVersion);
        WriteDataString(writer, "layoutAlgorithmId", geometry.LayoutAlgorithmId.Value);
        writer.WriteStartObject("configuration");
        WriteDataString(writer, "id", geometry.Configuration.ConfigurationId);
        WriteDataString(writer, "version", geometry.Configuration.Version);
        WriteProperties(writer, "options", geometry.Configuration.Options);
        writer.WriteEndObject();
        writer.WritePropertyName("textConfiguration");
        WriteTextRequest(writer, geometry.TextConfiguration);
        writer.WriteStartArray("contributors");
        foreach (var contributor in geometry.Contributors)
        {
            var descriptor = contributor.Descriptor;
            writer.WriteStartObject();
            WriteDataString(writer, "id", descriptor.ContributorId.Value);
            WriteDataString(writer, "version", descriptor.Version);
            writer.WriteString("stage", contributor.Stage.ToString());
            writer.WriteString("panDependency", descriptor.PanDependency.ToString());
            writer.WriteString("moveGestureDependency", descriptor.MoveGestureDependency.ToString());
            writer.WriteString("hoverDependency", descriptor.HoverDependency.ToString());
            writer.WriteString("visualSelectionDependency", descriptor.VisualSelectionDependency.ToString());
            writer.WriteString("placementDependency", descriptor.PlacementDependency.ToString());
            writer.WriteString("regionResizeDependency", descriptor.RegionResizeDependency.ToString());
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("nodes");
        foreach (var node in geometry.Nodes)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "visualStateId", node.VisualStateId.Value);
            WriteRect(writer, "localBounds", node.LocalBounds);
            WriteMatrix(writer, "transform", node.Transform);
            WriteNullableDataString(writer, "regionId", node.RegionId?.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("regions");
        foreach (var region in geometry.Regions)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "id", region.Id.Value);
            WriteDataString(writer, "profileId", region.ProfileId.Value);
            WriteNullableDataString(writer, "containerSemanticElementId", region.ContainerSemanticElementId?.Value);
            writer.WriteNumber("expandedHeight", region.ExpandedHeight);
            if (region.LocalToScopeTransform is { } transform) WriteMatrix(writer, "localToScope", transform);
            else writer.WriteNull("localToScope");
            if (region.ContentBounds is { } bounds) WriteRect(writer, "contentBounds", bounds);
            else writer.WriteNull("contentBounds");
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("spatialWidths");
        foreach (var width in geometry.SpatialWidths)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "profileId", width.ProfileId.Value);
            writer.WriteNumber("outerWidth", width.OuterWidth);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteStartArray("captions");
        foreach (var caption in geometry.Captions) WriteCaption(writer, caption);
        writer.WriteEndArray();
        writer.WriteStartArray("textMeasurements");
        foreach (var measurement in geometry.TextMeasurements) WriteTextMeasurement(writer, measurement);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteCaption(Utf8JsonWriter writer, ScopeNodeCaptionSnapshot caption)
    {
        writer.WriteStartObject();
        WriteDataString(writer, "ownerVisualStateId", caption.OwnerVisualStateId.Value);
        WriteDataString(writer, "labelId", caption.LabelId.Value);
        writer.WriteStartObject("placement");
        writer.WriteString("kind", caption.Placement.Kind.ToString());
        writer.WriteNumber("gap", caption.Placement.Gap);
        WriteNullableNumber(writer, "maximumWidth", caption.Placement.MaximumWidth);
        writer.WriteEndObject();
        if (caption.VisualOverride is { } visualOverride)
        {
            writer.WriteStartObject("visualOverride");
            writer.WriteNumber("offsetX", visualOverride.OffsetX);
            writer.WriteNumber("offsetY", visualOverride.OffsetY);
            writer.WriteNumber("width", visualOverride.Width);
            writer.WriteNumber("height", visualOverride.Height);
            writer.WriteEndObject();
        }
        else writer.WriteNull("visualOverride");
        WriteRect(writer, "placementBounds", caption.PlacementBounds);
        WriteRect(writer, "contentBounds", caption.ContentBounds);
        WriteMatrix(writer, "transform", caption.Transform);
        writer.WriteStartArray("lines");
        foreach (var line in caption.Lines)
        {
            WriteTextMeasurement(writer, line);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteTextMeasurement(Utf8JsonWriter writer, ScopeTextMeasurementSnapshot line)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("request");
        WriteTextRequest(writer, line.Request);
        writer.WriteStartObject("metrics");
        writer.WriteNumber("width", line.Metrics.Width);
        writer.WriteNumber("ascent", line.Metrics.Ascent);
        writer.WriteNumber("descent", line.Metrics.Descent);
        writer.WriteNumber("lineHeight", line.Metrics.LineHeight);
        WriteRect(writer, "boundingBox", line.Metrics.BoundingBox);
        WriteDataString(writer, "resolvedFontIdentity", line.Metrics.ResolvedFontIdentity);
        writer.WriteStartArray("diagnostics");
        foreach (var diagnostic in line.Metrics.Diagnostics)
        {
            writer.WriteStartObject();
            WriteDataString(writer, "code", diagnostic.Code);
            writer.WriteString("severity", diagnostic.Severity.ToString());
            WriteDataString(writer, "message", diagnostic.Message);
            WriteNullableDataString(writer, "sourceIdentity", diagnostic.SourceIdentity);
            writer.WriteStartArray("context");
            foreach (var entry in diagnostic.Context)
            {
                writer.WriteStartObject();
                WriteDataString(writer, "key", entry.Key);
                WriteDataString(writer, "value", entry.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteTextRequest(Utf8JsonWriter writer, TextMeasurementRequest request)
    {
        writer.WriteStartObject();
        WriteDataString(writer, "text", request.Text);
        WriteDataString(writer, "fontFamily", request.FontFamily);
        WriteDataString(writer, "fontIdentity", request.FontIdentity);
        WriteDataString(writer, "fontVersion", request.FontVersion);
        writer.WriteNumber("fontSize", request.FontSize);
        writer.WriteNumber("lineHeight", request.LineHeight);
        writer.WriteNumber("fontWeight", request.FontWeight);
        writer.WriteString("fontStyle", request.FontStyle.ToString());
        WriteDataString(writer, "locale", request.Locale);
        writer.WriteString("direction", request.Direction.ToString());
        writer.WriteString("writingMode", request.WritingMode.ToString());
        writer.WriteNumber("scale", request.Scale);
        WriteDataString(writer, "configurationId", request.ConfigurationId);
        WriteDataString(writer, "configurationVersion", request.ConfigurationVersion);
        writer.WriteEndObject();
    }

    private static void WriteAutomaticProof(Utf8JsonWriter writer, ConnectorAutomaticRouteProof? proof)
    {
        if (proof is null)
        {
            writer.WriteNull("automaticProof");
            return;
        }
        writer.WriteStartObject("automaticProof");
        WriteDataString(writer, "algorithmId", proof.AlgorithmId.Value);
        WriteDataString(writer, "policyVersion", proof.PolicyVersion);
        WriteNullableNumber(writer, "obstacleClearance", proof.ObstacleClearance);
        WriteNullableNumber(writer, "endpointLead", proof.EndpointLead);
        WriteEndpoint(writer, "source", proof.Source);
        WriteEndpoint(writer, "target", proof.Target);
        writer.WriteEndObject();
    }

    private static void WriteEndpoint(Utf8JsonWriter writer, string name, ConnectorRoutingEndpointObservation endpoint)
    {
        writer.WriteStartObject(name);
        WriteDataString(writer, "semanticElementId", endpoint.SemanticElementId.Value);
        WriteDataString(writer, "visualStateId", endpoint.VisualStateId.Value);
        WriteNullableDataString(writer, "anchorId", endpoint.AnchorId?.Value);
        writer.WriteString("role", endpoint.Role.ToString());
        writer.WriteString("side", endpoint.Side.ToString());
        writer.WriteNumber("order", endpoint.Order);
        writer.WriteNumber("sideCount", endpoint.SideCount);
        WritePoint(writer, "point", endpoint.Point);
        WritePoint(writer, "direction", new PointD(endpoint.Direction.X, endpoint.Direction.Y));
        writer.WriteEndObject();
    }

    private static void WritePoints(Utf8JsonWriter writer, string name, ImmutableArray<PointD> points)
    {
        writer.WriteStartArray(name);
        foreach (var point in points) WritePoint(writer, point);
        writer.WriteEndArray();
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } number) writer.WriteNumber(name, number);
        else writer.WriteNull(name);
    }

    private static void WriteRect(Utf8JsonWriter writer, string name, RectD rectangle)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("x", rectangle.X);
        writer.WriteNumber("y", rectangle.Y);
        writer.WriteNumber("width", rectangle.Width);
        writer.WriteNumber("height", rectangle.Height);
        writer.WriteEndObject();
    }

    private static void WriteMatrix(Utf8JsonWriter writer, string name, Matrix2D matrix)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("m11", matrix.M11);
        writer.WriteNumber("m12", matrix.M12);
        writer.WriteNumber("m21", matrix.M21);
        writer.WriteNumber("m22", matrix.M22);
        writer.WriteNumber("offsetX", matrix.OffsetX);
        writer.WriteNumber("offsetY", matrix.OffsetY);
        writer.WriteEndObject();
    }

    private static ScopeRoutingSnapshot ReadRoutingScope(JsonElement value, string path)
    {
        ValidateObject(value, path, "scopeId", "geometry", "connectors");
        return new ScopeRoutingSnapshot(new DocumentScopeId(Data(value, "scopeId", path)),
            ReadScopeGeometry(Required(value, "geometry"), $"{path}.geometry"),
            ReadArray(Required(value, "connectors"), $"{path}.connectors", ReadConnectorRecord));
    }

    private static ScopeGeometrySnapshot ReadScopeGeometry(JsonElement value, string path)
    {
        ValidateObject(value, path, "space", "policyId", "policyVersion", "layoutAlgorithmId", "configuration",
            "textConfiguration", "contributors", "nodes", "regions", "captions", "textMeasurements", "spatialWidths");
        RequireLogicalSpace(value, path);
        var configuration = Required(value, "configuration");
        var configPath = $"{path}.configuration";
        ValidateObject(configuration, configPath, "id", "version", "options");
        return new ScopeGeometrySnapshot(Data(value, "policyId", path), Data(value, "policyVersion", path),
            new AlgorithmId(Data(value, "layoutAlgorithmId", path)),
            new Canvas2DSceneConfiguration(Data(configuration, "id", configPath), Data(configuration, "version", configPath),
                ReadProperties(Required(configuration, "options"), $"{configPath}.options")),
            ReadTextRequest(Required(value, "textConfiguration"), $"{path}.textConfiguration"),
            ReadArray(Required(value, "contributors"), $"{path}.contributors", ReadGeometryContributor),
            ReadArray(Required(value, "nodes"), $"{path}.nodes", ReadGeometryNode),
            ReadArray(Required(value, "regions"), $"{path}.regions", ReadGeometryRegion),
            ReadArray(Required(value, "captions"), $"{path}.captions", ReadCaption),
            ReadArray(Required(value, "textMeasurements"), $"{path}.textMeasurements", ReadCaptionLine),
            ReadArray(Required(value, "spatialWidths"), $"{path}.spatialWidths", ReadSpatialWidth));
    }

    private static ScopeGeometryContributorSnapshot ReadGeometryContributor(JsonElement value, string path)
    {
        ValidateObject(value, path, "id", "version", "stage", "panDependency", "moveGestureDependency", "hoverDependency",
            "visualSelectionDependency", "placementDependency", "regionResizeDependency");
        return new ScopeGeometryContributorSnapshot(new Canvas2DSceneContributorDescriptor(
            new Canvas2DSceneContributorId(Data(value, "id", path)), Data(value, "version", path),
            ReadEnum<Canvas2DScenePanDependency>(Required(value, "panDependency"), $"{path}.panDependency"),
            ReadEnum<Canvas2DSceneMoveGestureDependency>(Required(value, "moveGestureDependency"), $"{path}.moveGestureDependency"),
            ReadEnum<Canvas2DSceneTransientDependency>(Required(value, "hoverDependency"), $"{path}.hoverDependency"),
            ReadEnum<Canvas2DSceneTransientDependency>(Required(value, "visualSelectionDependency"), $"{path}.visualSelectionDependency"),
            ReadEnum<Canvas2DScenePlacementDependency>(Required(value, "placementDependency"), $"{path}.placementDependency"),
            ReadEnum<Canvas2DSceneTransientDependency>(Required(value, "regionResizeDependency"), $"{path}.regionResizeDependency")),
            ReadEnum<Canvas2DSceneContributionStage>(Required(value, "stage"), $"{path}.stage"));
    }

    private static ScopeNodeGeometrySnapshot ReadGeometryNode(JsonElement value, string path)
    {
        ValidateObject(value, path, "visualStateId", "localBounds", "transform", "regionId");
        return new ScopeNodeGeometrySnapshot(new VisualStateId(Data(value, "visualStateId", path)),
            ReadRect(Required(value, "localBounds"), $"{path}.localBounds"),
            ReadMatrix(Required(value, "transform"), $"{path}.transform"),
            ReadNullableDataString(Required(value, "regionId"), $"{path}.regionId") is { } id ? new Canvas2DSpatialRegionId(id) : null);
    }

    private static SpatialScopeWidthSnapshot ReadSpatialWidth(JsonElement value, string path)
    {
        ValidateObject(value, path, "profileId", "outerWidth");
        return new SpatialScopeWidthSnapshot(new ModelProfileId(Data(value, "profileId", path)),
            Number(value, "outerWidth", path));
    }

    private static SpatialRegionGeometrySnapshot ReadGeometryRegion(JsonElement value, string path)
    {
        ValidateObject(value, path, "id", "profileId", "containerSemanticElementId", "expandedHeight", "localToScope", "contentBounds");
        return new SpatialRegionGeometrySnapshot(new Canvas2DSpatialRegionId(Data(value, "id", path)),
            new ModelProfileId(Data(value, "profileId", path)),
            ReadNullableDataString(Required(value, "containerSemanticElementId"), $"{path}.containerSemanticElementId") is { } id ? new SemanticElementId(id) : null,
            Number(value, "expandedHeight", path),
            Required(value, "localToScope").ValueKind == JsonValueKind.Null ? null : ReadMatrix(Required(value, "localToScope"), $"{path}.localToScope"),
            Required(value, "contentBounds").ValueKind == JsonValueKind.Null ? null : ReadRect(Required(value, "contentBounds"), $"{path}.contentBounds"));
    }

    private static ConnectorRoutingRecord ReadConnectorRecord(JsonElement value, string path)
    {
        ValidateObject(value, path, "visualStateId", "routingType", "outcome", "space", "path", "manualDefinition", "automaticProof", "noRouteReason");
        RequireLogicalSpace(value, path);
        var type = ReadString(Required(value, "routingType"), $"{path}.routingType") switch
        {
            "automatic" => ConnectorRoutingType.Automatic,
            "straight" => ConnectorRoutingType.Straight,
            "manual" => ConnectorRoutingType.Manual,
            _ => throw Structure($"{path}.routingType", "Unknown connector routing type."),
        };
        var outcome = ReadString(Required(value, "outcome"), $"{path}.outcome") switch
        {
            "path" => ConnectorRoutingOutcome.Path,
            "noRoute" => ConnectorRoutingOutcome.NoRoute,
            _ => throw Structure($"{path}.outcome", "Unknown connector routing outcome."),
        };
        var reasonValue = Required(value, "noRouteReason");
        ConnectorNoRouteReason? reason = reasonValue.ValueKind == JsonValueKind.Null ? null :
            ReadString(reasonValue, $"{path}.noRouteReason") == "noFeasiblePath" ? ConnectorNoRouteReason.NoFeasiblePath :
            throw Structure($"{path}.noRouteReason", "Unknown NoRoute reason.");
        return new ConnectorRoutingRecord(new VisualStateId(Data(value, "visualStateId", path)), type, outcome,
            ReadArray(Required(value, "path"), $"{path}.path", ReadPoint),
            Required(value, "manualDefinition").ValueKind == JsonValueKind.Null ? null : ReadArray(Required(value, "manualDefinition"), $"{path}.manualDefinition", ReadPoint),
            Required(value, "automaticProof").ValueKind == JsonValueKind.Null ? null : ReadAutomaticProof(Required(value, "automaticProof"), $"{path}.automaticProof"), reason);
    }

    private static ConnectorAutomaticRouteProof ReadAutomaticProof(JsonElement value, string path)
    {
        ValidateObject(value, path, "algorithmId", "policyVersion", "obstacleClearance", "endpointLead", "source", "target");
        return new ConnectorAutomaticRouteProof(new AlgorithmId(Data(value, "algorithmId", path)), Data(value, "policyVersion", path),
            NullableNumber(value, "obstacleClearance", path), NullableNumber(value, "endpointLead", path),
            ReadEndpoint(Required(value, "source"), $"{path}.source"), ReadEndpoint(Required(value, "target"), $"{path}.target"));
    }

    private static ConnectorRoutingEndpointObservation ReadEndpoint(JsonElement value, string path)
    {
        ValidateObject(value, path, "semanticElementId", "visualStateId", "anchorId", "role", "side", "order", "sideCount", "point", "direction");
        var direction = ReadPoint(Required(value, "direction"), $"{path}.direction");
        return new ConnectorRoutingEndpointObservation(new SemanticElementId(Data(value, "semanticElementId", path)),
            new VisualStateId(Data(value, "visualStateId", path)),
            ReadNullableDataString(Required(value, "anchorId"), $"{path}.anchorId") is { } anchor ? new ConnectorAnchorId(anchor) : null,
            ReadEnum<ConnectorAnchorRole>(Required(value, "role"), $"{path}.role"),
            ReadEnum<ConnectorAnchorSide>(Required(value, "side"), $"{path}.side"),
            ReadInt32(Required(value, "order"), $"{path}.order"), ReadInt32(Required(value, "sideCount"), $"{path}.sideCount"),
            ReadPoint(Required(value, "point"), $"{path}.point"), new VectorD(direction.X, direction.Y));
    }

    private static ScopeNodeCaptionSnapshot ReadCaption(JsonElement value, string path)
    {
        ValidateObject(value, path, "ownerVisualStateId", "labelId", "placement", "visualOverride", "placementBounds", "contentBounds", "transform", "lines");
        var placement = Required(value, "placement");
        ValidateObject(placement, $"{path}.placement", "kind", "gap", "maximumWidth");
        var visualOverride = Required(value, "visualOverride");
        NodeLabelVisualOverride? parsedOverride = null;
        if (visualOverride.ValueKind != JsonValueKind.Null)
        {
            var overridePath = $"{path}.visualOverride";
            ValidateObject(visualOverride, overridePath, "offsetX", "offsetY", "width", "height");
            parsedOverride = new NodeLabelVisualOverride(Number(visualOverride, "offsetX", overridePath), Number(visualOverride, "offsetY", overridePath),
                Number(visualOverride, "width", overridePath), Number(visualOverride, "height", overridePath));
        }
        return new ScopeNodeCaptionSnapshot(new VisualStateId(Data(value, "ownerVisualStateId", path)), new ProjectedObjectId(Data(value, "labelId", path)),
            new NodeLabelPlacement(ReadEnum<NodeLabelPlacementKind>(Required(placement, "kind"), $"{path}.placement.kind"),
                Number(placement, "gap", $"{path}.placement"), NullableNumber(placement, "maximumWidth", $"{path}.placement")),
            parsedOverride, ReadRect(Required(value, "placementBounds"), $"{path}.placementBounds"),
            ReadRect(Required(value, "contentBounds"), $"{path}.contentBounds"), ReadMatrix(Required(value, "transform"), $"{path}.transform"),
            ReadArray(Required(value, "lines"), $"{path}.lines", ReadCaptionLine));
    }

    private static ScopeTextMeasurementSnapshot ReadCaptionLine(JsonElement value, string path)
    {
        ValidateObject(value, path, "request", "metrics");
        var metrics = Required(value, "metrics");
        var metricsPath = $"{path}.metrics";
        ValidateObject(metrics, metricsPath, "width", "ascent", "descent", "lineHeight", "boundingBox", "resolvedFontIdentity", "diagnostics");
        return new ScopeTextMeasurementSnapshot(ReadTextRequest(Required(value, "request"), $"{path}.request"),
            new TextMetrics(Number(metrics, "width", metricsPath), Number(metrics, "ascent", metricsPath), Number(metrics, "descent", metricsPath),
                Number(metrics, "lineHeight", metricsPath), ReadRect(Required(metrics, "boundingBox"), $"{metricsPath}.boundingBox"),
                Data(metrics, "resolvedFontIdentity", metricsPath), ReadArray(Required(metrics, "diagnostics"), $"{metricsPath}.diagnostics", ReadMetricDiagnostic)));
    }

    private static Diagnostic ReadMetricDiagnostic(JsonElement value, string path)
    {
        ValidateObject(value, path, "code", "severity", "message", "sourceIdentity", "context");
        return new Diagnostic(Data(value, "code", path), ReadEnum<DiagnosticSeverity>(Required(value, "severity"), $"{path}.severity"),
            Data(value, "message", path), ReadNullableDataString(Required(value, "sourceIdentity"), $"{path}.sourceIdentity"),
            ReadArray(Required(value, "context"), $"{path}.context", static (entry, entryPath) =>
            {
                ValidateObject(entry, entryPath, "key", "value");
                return new KeyValuePair<string, string>(Data(entry, "key", entryPath), Data(entry, "value", entryPath));
            }));
    }

    private static TextMeasurementRequest ReadTextRequest(JsonElement value, string path)
    {
        ValidateObject(value, path, "text", "fontFamily", "fontIdentity", "fontVersion", "fontSize", "lineHeight", "fontWeight",
            "fontStyle", "locale", "direction", "writingMode", "scale", "configurationId", "configurationVersion");
        return new TextMeasurementRequest(Data(value, "text", path), Data(value, "fontFamily", path), Data(value, "fontIdentity", path),
            Data(value, "fontVersion", path), Number(value, "fontSize", path), Number(value, "lineHeight", path),
            ReadInt32(Required(value, "fontWeight"), $"{path}.fontWeight"), ReadEnum<TextFontStyle>(Required(value, "fontStyle"), $"{path}.fontStyle"),
            Data(value, "locale", path), ReadEnum<TextDirection>(Required(value, "direction"), $"{path}.direction"),
            ReadEnum<TextWritingMode>(Required(value, "writingMode"), $"{path}.writingMode"), Number(value, "scale", path),
            Data(value, "configurationId", path), Data(value, "configurationVersion", path));
    }

    private static RectD ReadRect(JsonElement value, string path)
    {
        ValidateObject(value, path, "x", "y", "width", "height");
        return new RectD(Number(value, "x", path), Number(value, "y", path), Number(value, "width", path), Number(value, "height", path));
    }

    private static Matrix2D ReadMatrix(JsonElement value, string path)
    {
        ValidateObject(value, path, "m11", "m12", "m21", "m22", "offsetX", "offsetY");
        return new Matrix2D(Number(value, "m11", path), Number(value, "m12", path), Number(value, "m21", path), Number(value, "m22", path),
            Number(value, "offsetX", path), Number(value, "offsetY", path));
    }

    private static void RequireLogicalSpace(JsonElement value, string path)
    {
        if (ReadString(Required(value, "space"), $"{path}.space") != LogicalSpace)
            throw Structure($"{path}.space", "Saved geometry and paths must use scopeLogical coordinates.");
    }

    private static string Data(JsonElement value, string name, string path) => ReadDataString(Required(value, name), $"{path}.{name}");
    private static double Number(JsonElement value, string name, string path) => ReadDouble(Required(value, name), $"{path}.{name}");
    private static double? NullableNumber(JsonElement value, string name, string path) =>
        Required(value, name).ValueKind == JsonValueKind.Null ? null : Number(value, name, path);
}
