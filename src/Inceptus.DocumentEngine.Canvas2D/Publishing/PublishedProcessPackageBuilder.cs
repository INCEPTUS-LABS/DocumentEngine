using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Publishing;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.Canvas2D.Publishing;

public sealed record PublishedProcessPackage(
    PublishedProcessSnapshot Snapshot,
    ImmutableArray<byte> ProcessJson,
    ImmutableArray<byte> Archive);

public sealed record PublishedProcessPackageBuildResult
{
    private PublishedProcessPackageBuildResult(
        PublishedProcessPackage? package,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Package = package;
        Diagnostics = diagnostics?.ToImmutableArray() ?? [];
    }

    public bool Succeeded => Package is not null;

    public PublishedProcessPackage? Package { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    internal static PublishedProcessPackageBuildResult Success(
        PublishedProcessPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new(package, null);
    }

    internal static PublishedProcessPackageBuildResult Failure(
        IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(null, diagnostics);
    }
}

/// <summary>
/// Builds the deterministic, dependency-free static Publish package from frozen Scene geometry.
/// </summary>
public sealed class PublishedProcessPackageBuilder
{
    public const string Format = "Inceptus.PublishedProcess";
    public const int FormatVersion = 1;
    public const int DefaultActivityDelayMs = 2000;
    public const double DefaultTokenSpeedPxPerSecond = 300d;
    public const int DefaultMinimumConnectorDurationMs = 150;

    private const string PackageBuildFailed = "PUBLISH_PACKAGE_BUILD_FAILED";
    private const string InvalidCapture = "PUBLISH_CAPTURE_INVALID";
    private const string MissingPublication = "PUBLISH_PUBLICATION_REQUIRED";
    private const string MissingNode = "PUBLISH_TOKEN_NODE_MISSING";
    private const string MissingRoute = "PUBLISH_CONNECTOR_ROUTE_MISSING";
    private const string AmbiguousContinuation = "PUBLISH_TOKEN_CONTINUATION_AMBIGUOUS";
    private const string InvalidSplit = "PUBLISH_TOKEN_SPLIT_INVALID";
    private const string InvalidMerge = "PUBLISH_TOKEN_MERGE_INVALID";
    private const string InvalidParallel = "PUBLISH_TOKEN_PARALLEL_INVALID";
    private const string InvalidStart = "PUBLISH_TOKEN_START_INVALID";
    private const string InvalidEnd = "PUBLISH_TOKEN_END_INVALID";
    private const string UnsupportedImage = "PUBLISH_PRESENTATION_IMAGE_UNSUPPORTED";
    private const string DataBootstrapPrefix =
        "globalThis.__INCEPTUS_PUBLISHED_PROCESS__ = ";
    private const string DataBootstrapSuffix = ";\n";
    private const Canvas2DSceneOriginCategory ExcludedOrigin =
        Canvas2DSceneOriginCategory.EditorState;

    private static readonly DateTimeOffset ArchiveTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Default,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly IPublishedTokenRoleClassifier _tokenRoleClassifier;
    private readonly IPublishedNodeDataMapper _nodeDataMapper;
    private readonly PublishedProcessStaticAssets _assets;

    public PublishedProcessPackageBuilder(
        IPublishedTokenRoleClassifier tokenRoleClassifier)
        : this(
            tokenRoleClassifier,
            EmptyPublishedNodeDataMapper.Instance,
            PublishedProcessStaticAssets.LoadEmbedded())
    {
    }

    public PublishedProcessPackageBuilder(
        IPublishedTokenRoleClassifier tokenRoleClassifier,
        IPublishedNodeDataMapper nodeDataMapper)
        : this(
            tokenRoleClassifier,
            nodeDataMapper,
            PublishedProcessStaticAssets.LoadEmbedded())
    {
    }

    internal PublishedProcessPackageBuilder(
        IPublishedTokenRoleClassifier tokenRoleClassifier,
        PublishedProcessStaticAssets assets)
        : this(tokenRoleClassifier, EmptyPublishedNodeDataMapper.Instance, assets)
    {
    }

    internal PublishedProcessPackageBuilder(
        IPublishedTokenRoleClassifier tokenRoleClassifier,
        IPublishedNodeDataMapper nodeDataMapper,
        PublishedProcessStaticAssets assets)
    {
        ArgumentNullException.ThrowIfNull(tokenRoleClassifier);
        ArgumentNullException.ThrowIfNull(nodeDataMapper);
        ArgumentNullException.ThrowIfNull(assets);
        _tokenRoleClassifier = tokenRoleClassifier;
        _nodeDataMapper = nodeDataMapper;
        _assets = assets;
    }

    public PublishedProcessPackageBuildResult Build(
        EditingSessionPresentationCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var diagnostics = new List<Diagnostic>();
        ValidateCapture(capture, diagnostics);
        if (HasErrors(diagnostics))
        {
            return PublishedProcessPackageBuildResult.Failure(
                DescribeDiagnostics(capture, diagnostics));
        }

        var buildStage = "presentation";
#pragma warning disable CA1031 // Package failures are returned as bounded transient diagnostics.
        try
        {
            var presentationItems = CreatePresentationItems(capture, diagnostics);
            buildStage = "connectors";
            var connectors = CreateConnectors(capture, diagnostics);
            buildStage = "nodes";
            var nodes = CreateNodes(capture, presentationItems, diagnostics);
            buildStage = "token-graph";
            var tokenNodes = CreateTokenNodes(capture, connectors, diagnostics);
            if (HasErrors(diagnostics))
            {
                return PublishedProcessPackageBuildResult.Failure(
                    DescribeDiagnostics(capture, diagnostics));
            }

            buildStage = "serialization";
            var contentBounds = CalculateContentBounds(presentationItems);
            var snapshot = new PublishedProcessSnapshot(
                Format,
                FormatVersion,
                new PublishedPublication(
                    capture.Document.Publication!.Code,
                    capture.Document.Publication.Title,
                    capture.Document.Publication.Description),
                new PublishedSource(
                    capture.Document.DocumentId.Value,
                    capture.Document.Revision.Value,
                    capture.ActiveScopeId.Value),
                new PublishedPresentation(nodes, connectors, presentationItems, contentBounds),
                new PublishedTokenGraph(tokenNodes),
                new PublishedRuntimeConfiguration(
                    DefaultActivityDelayMs,
                    DefaultTokenSpeedPxPerSecond,
                    DefaultMinimumConnectorDurationMs));
            var processJson = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions)
                .ToImmutableArray();
            var processDataJavaScript = CreateDataBootstrap(processJson);
            buildStage = "archive";
            var archive = CreateArchive(processJson, processDataJavaScript);
            return PublishedProcessPackageBuildResult.Success(
                new PublishedProcessPackage(snapshot, processJson, archive));
        }
        catch (Exception exception) when (IsNonFatal(exception))
        {
            diagnostics.Add(Error(
                PackageBuildFailed,
                "The static Publish package could not be generated.",
                capture.Document.DocumentId.Value,
                exception.GetType().FullName ?? exception.GetType().Name,
                buildStage));
            return PublishedProcessPackageBuildResult.Failure(
                DescribeDiagnostics(capture, diagnostics));
        }
#pragma warning restore CA1031
    }

    private static void ValidateCapture(
        EditingSessionPresentationCapture capture,
        List<Diagnostic> diagnostics)
    {
        var documentId = capture.Document.DocumentId;
        var revision = capture.Document.Revision;
        if (capture.Document.Publication is null)
        {
            diagnostics.Add(Error(
                MissingPublication,
                "Publish requires saved Publication metadata.",
                documentId.Value));
        }

        if (capture.ProjectedGraph.DocumentId != documentId ||
            capture.ProjectedGraph.SourceRevision != revision ||
            capture.LayoutResult.DocumentId != documentId ||
            capture.LayoutResult.SourceRevision != revision ||
            capture.RoutingResult.DocumentId != documentId ||
            capture.RoutingResult.SourceRevision != revision ||
            capture.Scene.DocumentId != documentId ||
            capture.Scene.SourceRevision != revision ||
            !capture.Document.SemanticModel.TryGetScope(capture.ActiveScopeId, out _))
        {
            diagnostics.Add(Error(
                InvalidCapture,
                "Publish requires one coherent current Document, scope, and presentation.",
                documentId.Value));
        }
    }

    private static ImmutableArray<PublishedPresentationItem> CreatePresentationItems(
        EditingSessionPresentationCapture capture,
        List<Diagnostic> diagnostics)
    {
        var items = ImmutableArray.CreateBuilder<PublishedPresentationItem>();
        foreach (var item in capture.Scene.Items
                     .Where(static item =>
                         item.IsVisible &&
                         item.Origin.ProjectedObjectId is not null &&
                         (item.Origin.Categories & ExcludedOrigin) == 0)
                     .OrderBy(static item => item.Layer)
                     .ThenBy(static item => item.ZIndex)
                     .ThenBy(static item => item.Id.Value, StringComparer.Ordinal))
        {
            if (item.Geometry.Kind == Canvas2DSceneGeometryKind.Image)
            {
                diagnostics.Add(Error(
                    UnsupportedImage,
                    $"Published item '{item.Id}' requires an image asset that is not self-contained.",
                    item.Id.Value));
                continue;
            }

            items.Add(new PublishedPresentationItem(
                item.Id.Value,
                (int)item.Layer,
                item.ZIndex,
                (int)item.Geometry.Kind,
                Rect(item.Geometry.Bounds),
                item.Geometry.Points.Select(Point).ToImmutableArray(),
                item.Geometry.Content,
                item.Geometry.IsClosed,
                Point(item.Geometry.TextAnchor),
                (int)item.Geometry.TextAlignment,
                (int)item.Geometry.TextBaseline,
                Matrix(item.Transform),
                item.Clip is { } clip ? Rect(clip) : null,
                item.Style.Fill,
                item.Style.Stroke,
                item.Style.StrokeWidth,
                item.Style.DashPattern,
                item.Style.Opacity,
                item.Style.FontFamily,
                item.Style.FontSize));
        }

        return items.ToImmutable();
    }

    private static ImmutableArray<PublishedPresentationConnector> CreateConnectors(
        EditingSessionPresentationCapture capture,
        List<Diagnostic> diagnostics)
    {
        var nodesById = capture.ProjectedGraph.Nodes.ToDictionary(static node => node.Id);
        var connectors = ImmutableArray.CreateBuilder<PublishedPresentationConnector>();
        foreach (var edge in capture.ProjectedGraph.Edges.OrderBy(
                     static edge => edge.Source.SemanticElementId.Value,
                     StringComparer.Ordinal))
        {
            if (!nodesById.TryGetValue(edge.SourceNodeId, out var source) ||
                !nodesById.TryGetValue(edge.TargetNodeId, out var target))
            {
                diagnostics.Add(ConnectorError(
                    MissingNode,
                    $"Connector '{edge.Source.SemanticElementId}' references a missing published node.",
                    edge.Source.SemanticElementId.Value));
                continue;
            }

            var connectorId = Canvas2DSceneObjectIdentity.ForProjected(edge.Id, "connector");
            var connectorItem = capture.Scene.Items.SingleOrDefault(item => item.Id == connectorId);
            if (capture.RoutingResult.NoRouteEdgeIds.Contains(edge.Id) ||
                connectorItem is null ||
                !connectorItem.IsVisible)
            {
                diagnostics.Add(ConnectorError(
                    MissingRoute,
                    $"Connector '{edge.Source.SemanticElementId}' has no usable frozen route.",
                    edge.Source.SemanticElementId.Value));
                continue;
            }

            var points = Canvas2DConnectorPathMetadata.Resolve(connectorItem);
            if (points.Length < 2)
            {
                diagnostics.Add(ConnectorError(
                    MissingRoute,
                    $"Connector '{edge.Source.SemanticElementId}' has no usable frozen route.",
                    edge.Source.SemanticElementId.Value));
                continue;
            }

            connectors.Add(new PublishedPresentationConnector(
                edge.Source.SemanticElementId.Value,
                source.Source.SemanticElementId.Value,
                target.Source.SemanticElementId.Value,
                points.Select(Point).ToImmutableArray()));
        }

        return connectors.ToImmutable();
    }

    private ImmutableArray<PublishedPresentationNode> CreateNodes(
        EditingSessionPresentationCapture capture,
        ImmutableArray<PublishedPresentationItem> presentationItems,
        List<Diagnostic> diagnostics)
    {
        var publishedItemIds = presentationItems.Select(static item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var nodes = ImmutableArray.CreateBuilder<PublishedPresentationNode>();
        foreach (var node in capture.ProjectedGraph.Nodes.OrderBy(
                     static node => node.Source.SemanticElementId.Value,
                     StringComparer.Ordinal))
        {
            if (!capture.Document.SemanticModel.TryGetElement(
                    node.Source.SemanticElementId,
                    out var semanticElement) ||
                semanticElement is null ||
                capture.Document.SemanticModel.GetScope(semanticElement.Id).Id !=
                    capture.ActiveScopeId)
            {
                diagnostics.Add(NodeError(
                    MissingNode,
                    $"Published node '{node.Source.SemanticElementId}' is not in the captured scope.",
                    node));
                continue;
            }

            var primaryId = Canvas2DSceneObjectIdentity.ForProjected(node.Id, "node");
            var primary = capture.Scene.Items.SingleOrDefault(item =>
                item.Id == primaryId && item.IsVisible && publishedItemIds.Contains(item.Id.Value));
            if (primary is null)
            {
                diagnostics.Add(NodeError(
                    MissingNode,
                    $"Published node '{node.Source.SemanticElementId}' has no frozen geometry.",
                    node));
                continue;
            }

            var label = capture.Scene.Items
                .Where(item =>
                    item.IsVisible &&
                    item.Layer == Canvas2DSceneLayer.Label &&
                    item.Origin.SemanticElementId == node.Source.SemanticElementId &&
                    item.Geometry.Kind == Canvas2DSceneGeometryKind.Text &&
                    publishedItemIds.Contains(item.Id.Value))
                .OrderBy(static item => item.Id.Value, StringComparer.Ordinal)
                .Select(static item => item.Geometry.Content)
                .FirstOrDefault(static content => content is not null);
            var nodeData = _nodeDataMapper.Map(semanticElement);
            ArgumentNullException.ThrowIfNull(nodeData);
            nodes.Add(new PublishedPresentationNode(
                node.Source.SemanticElementId.Value,
                Descriptor(primary.Geometry.Kind),
                Rect(primary.Bounds),
                label)
            {
                Description = nodeData.Description,
            });
        }

        return nodes.ToImmutable();
    }

    private ImmutableArray<PublishedTokenNode> CreateTokenNodes(
        EditingSessionPresentationCapture capture,
        ImmutableArray<PublishedPresentationConnector> connectors,
        List<Diagnostic> diagnostics)
    {
        var publishedNodeIds = capture.Scene.Items
            .Where(static item => item.IsVisible)
            .Select(static item => item.Origin.SemanticElementId?.Value)
            .Where(static id => id is not null)
            .Select(static id => id!)
            .ToHashSet(StringComparer.Ordinal);
        var connectorIds = connectors.Select(static connector => connector.Id)
            .ToHashSet(StringComparer.Ordinal);
        var projectedNodes = capture.ProjectedGraph.Nodes
            .OrderBy(static node => node.Source.SemanticElementId.Value, StringComparer.Ordinal)
            .ToArray();
        var startIds = projectedNodes
            .Where(node =>
            {
                var nodeId = node.Source.SemanticElementId.Value;
                var classification = _tokenRoleClassifier.Classify(
                    new PublishedTokenRoleClassificationRequest(
                        node.Source.SemanticElementId,
                        node.Source.SemanticTypeId,
                        connectors.Count(connector => StringComparer.Ordinal.Equals(
                            connector.TargetElementId,
                            nodeId)),
                        connectors.Count(connector => StringComparer.Ordinal.Equals(
                            connector.SourceElementId,
                            nodeId))));
                return classification.Role == PublishedTokenRole.Start;
            })
            .Select(static node => node.Source.SemanticElementId.Value)
            .ToArray();
        if (startIds.Length == 0)
        {
            diagnostics.Add(Error(
                InvalidStart,
                "Publish requires at least one Start element in the active process scope.",
                capture.ActiveScopeId.Value,
                additionalContext:
                [new KeyValuePair<string, string>("ScopeId", capture.ActiveScopeId.Value)]));
            return [];
        }

        var reachableNodeIds = FindReachableNodeIds(startIds, connectors, publishedNodeIds);
        var tokenConnectors = connectors
            .Where(connector =>
                reachableNodeIds.Contains(connector.SourceElementId) &&
                reachableNodeIds.Contains(connector.TargetElementId))
            .ToImmutableArray();
        var tokenNodes = ImmutableArray.CreateBuilder<PublishedTokenNode>();
        foreach (var node in projectedNodes.Where(node =>
                     reachableNodeIds.Contains(node.Source.SemanticElementId.Value)))
        {
            var nodeId = node.Source.SemanticElementId.Value;
            if (!publishedNodeIds.Contains(nodeId))
            {
                diagnostics.Add(NodeError(
                    MissingNode,
                    $"Token node '{nodeId}' is missing from the published presentation.",
                    node));
                continue;
            }

            var incoming = tokenConnectors
                .Where(connector => StringComparer.Ordinal.Equals(
                    connector.TargetElementId,
                    nodeId))
                .Select(static connector => connector.Id)
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToImmutableArray();
            var outgoing = tokenConnectors
                .Where(connector => StringComparer.Ordinal.Equals(
                    connector.SourceElementId,
                    nodeId))
                .Select(static connector => connector.Id)
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToImmutableArray();
            if (incoming.Any(id => !connectorIds.Contains(id)) ||
                outgoing.Any(id => !connectorIds.Contains(id)))
            {
                diagnostics.Add(NodeError(
                    MissingRoute,
                    $"Token node '{nodeId}' references missing route geometry.",
                    node));
                continue;
            }

            var classification = _tokenRoleClassifier.Classify(
                new PublishedTokenRoleClassificationRequest(
                    node.Source.SemanticElementId,
                    node.Source.SemanticTypeId,
                    incoming.Length,
                    outgoing.Length));
            if (!classification.Succeeded || classification.Role is not { } role)
            {
                diagnostics.Add(NodeError(
                    classification.DiagnosticCode ?? AmbiguousContinuation,
                    classification.DiagnosticMessage ??
                        $"Token behavior for '{nodeId}' is ambiguous.",
                    node));
                continue;
            }

            ValidateRole(node, role, incoming.Length, outgoing.Length, diagnostics);
            tokenNodes.Add(new PublishedTokenNode(nodeId, role, incoming, outgoing));
        }

        return tokenNodes.ToImmutable();
    }

    private static HashSet<string> FindReachableNodeIds(
        IEnumerable<string> startIds,
        ImmutableArray<PublishedPresentationConnector> connectors,
        HashSet<string> publishedNodeIds)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        foreach (var startId in startIds)
        {
            if (reachable.Add(startId))
            {
                pending.Enqueue(startId);
            }
        }
        while (pending.TryDequeue(out var sourceId))
        {
            foreach (var targetId in connectors
                         .Where(connector => StringComparer.Ordinal.Equals(
                             connector.SourceElementId,
                             sourceId))
                         .OrderBy(static connector => connector.Id, StringComparer.Ordinal)
                         .Select(static connector => connector.TargetElementId))
            {
                if (publishedNodeIds.Contains(targetId) && reachable.Add(targetId))
                {
                    pending.Enqueue(targetId);
                }
            }
        }

        return reachable;
    }

    private static void ValidateRole(
        ProjectedNode node,
        PublishedTokenRole role,
        int incomingCount,
        int outgoingCount,
        List<Diagnostic> diagnostics)
    {
        var nodeId = node.Source.SemanticElementId.Value;
        string? code = null;
        string? message = null;
        switch (role)
        {
            case PublishedTokenRole.Start when outgoingCount != 1:
                code = InvalidStart;
                message = $"Start '{nodeId}' requires one unambiguous outgoing connector.";
                break;
            case PublishedTokenRole.ActivityDelay or PublishedTokenRole.PassThrough
                when outgoingCount != 1:
                code = AmbiguousContinuation;
                message = $"Element '{nodeId}' requires one unambiguous outgoing connector.";
                break;
            case PublishedTokenRole.SplitInvariant when outgoingCount < 2:
                code = InvalidSplit;
                message = $"Split '{nodeId}' requires at least two outgoing connectors.";
                break;
            case PublishedTokenRole.MergeInvariant when incomingCount < 2 || outgoingCount != 1:
                code = InvalidMerge;
                message = $"Merge '{nodeId}' requires at least two incoming connectors and one outgoing connector.";
                break;
            case PublishedTokenRole.ParallelSynchronize
                when incomingCount < 1 || outgoingCount < 1:
                code = InvalidParallel;
                message = $"Parallel synchronization '{nodeId}' requires at least one " +
                    "incoming and one outgoing connector.";
                break;
            case PublishedTokenRole.End when outgoingCount != 0:
                code = InvalidEnd;
                message = $"End '{nodeId}' cannot have an outgoing connector.";
                break;
        }

        if (code is not null)
        {
            diagnostics.Add(NodeError(code, message!, node));
        }
    }

    private ImmutableArray<byte> CreateArchive(
        ImmutableArray<byte> processJson,
        ImmutableArray<byte> processDataJavaScript)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "index.html", _assets.IndexHtml);
            AddEntry(archive, "process.json", processJson);
            AddEntry(archive, "process.data.js", processDataJavaScript);
            AddEntry(archive, "inceptus.publish.js", _assets.JavaScript);
            AddEntry(archive, "styles.css", _assets.Styles);
        }

        return output.ToArray().ToImmutableArray();
    }

    private static ImmutableArray<byte> CreateDataBootstrap(
        ImmutableArray<byte> processJson)
    {
        var prefix = Encoding.UTF8.GetBytes(DataBootstrapPrefix);
        var suffix = Encoding.UTF8.GetBytes(DataBootstrapSuffix);
        var bootstrap = ImmutableArray.CreateBuilder<byte>(
            prefix.Length + processJson.Length + suffix.Length);
        bootstrap.AddRange(prefix);
        bootstrap.AddRange(processJson);
        bootstrap.AddRange(suffix);
        return bootstrap.MoveToImmutable();
    }

    private static void AddEntry(
        ZipArchive archive,
        string name,
        ImmutableArray<byte> bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = ArchiveTimestamp;
        using var stream = entry.Open();
        stream.Write(bytes.AsSpan());
    }

    private static PublishedRect CalculateContentBounds(
        ImmutableArray<PublishedPresentationItem> items)
    {
        if (items.IsEmpty)
        {
            return new PublishedRect(0d, 0d, 1d, 1d);
        }

        var minimumX = double.PositiveInfinity;
        var minimumY = double.PositiveInfinity;
        var maximumX = double.NegativeInfinity;
        var maximumY = double.NegativeInfinity;
        foreach (var item in items)
        {
            var bounds = TransformBounds(item.GeometryBounds, item.Transform);
            minimumX = Math.Min(minimumX, bounds.X);
            minimumY = Math.Min(minimumY, bounds.Y);
            maximumX = Math.Max(maximumX, bounds.X + bounds.Width);
            maximumY = Math.Max(maximumY, bounds.Y + bounds.Height);
        }

        return new PublishedRect(
            minimumX,
            minimumY,
            Math.Max(1d, maximumX - minimumX),
            Math.Max(1d, maximumY - minimumY));
    }

    private static PublishedRect TransformBounds(PublishedRect bounds, PublishedMatrix matrix)
    {
        var points = new[]
        {
            Transform(new PublishedPoint(bounds.X, bounds.Y), matrix),
            Transform(new PublishedPoint(bounds.X + bounds.Width, bounds.Y), matrix),
            Transform(new PublishedPoint(bounds.X, bounds.Y + bounds.Height), matrix),
            Transform(new PublishedPoint(bounds.X + bounds.Width, bounds.Y + bounds.Height), matrix),
        };
        var minimumX = points.Min(static point => point.X);
        var minimumY = points.Min(static point => point.Y);
        var maximumX = points.Max(static point => point.X);
        var maximumY = points.Max(static point => point.Y);
        return new PublishedRect(
            minimumX,
            minimumY,
            maximumX - minimumX,
            maximumY - minimumY);
    }

    private static PublishedPoint Transform(PublishedPoint point, PublishedMatrix matrix) =>
        new(
            (matrix.M11 * point.X) + (matrix.M21 * point.Y) + matrix.OffsetX,
            (matrix.M12 * point.X) + (matrix.M22 * point.Y) + matrix.OffsetY);

    private static PublishedPoint Point(PointD point) => new(point.X, point.Y);

    private static PublishedRect Rect(RectD bounds) =>
        new(bounds.X, bounds.Y, bounds.Width, bounds.Height);

    private static PublishedMatrix Matrix(Matrix2D matrix) =>
        new(
            matrix.M11,
            matrix.M12,
            matrix.M21,
            matrix.M22,
            matrix.OffsetX,
            matrix.OffsetY);

    private static string Descriptor(Canvas2DSceneGeometryKind kind) => kind switch
    {
        Canvas2DSceneGeometryKind.Rectangle => "rectangle",
        Canvas2DSceneGeometryKind.Ellipse => "ellipse",
        Canvas2DSceneGeometryKind.Path => "path",
        Canvas2DSceneGeometryKind.Text => "text",
        Canvas2DSceneGeometryKind.Image => "image",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static bool HasErrors(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static IEnumerable<Diagnostic> DescribeDiagnostics(
        EditingSessionPresentationCapture capture,
        IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            var context = diagnostic.Context.ToBuilder();
            var node = context.TryGetValue("ElementId", out var elementId)
                ? capture.ProjectedGraph.Nodes.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Source.SemanticElementId.Value,
                    elementId)) : null;
            var connector = context.TryGetValue("ConnectorId", out var connectorId)
                ? capture.Document.SemanticModel.Relationships
                .FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Id.Value,
                    connectorId)) : null;
            if (node is not null)
            {
                var label = capture.ProjectedGraph.Labels
                    .Where(candidate => candidate.OwnerId == node.Id)
                    .OrderBy(static candidate => candidate.Id.Value, StringComparer.Ordinal)
                    .Select(static candidate => candidate.Text)
                    .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));
                if (label is not null)
                {
                    context["ElementName"] = label;
                }
            }

            if (connector is not null)
            {
                context["SourceElementId"] = connector.SourceId.Value;
                context["TargetElementId"] = connector.TargetId.Value;
            }

            var missingStart = diagnostic.Code == InvalidStart && context.ContainsKey("ScopeId");

            var suggestedAction = diagnostic.Code switch
            {
                InvalidStart when missingStart =>
                    "Add a Start element with one outgoing connector to the active process scope.",
                InvalidStart => "Give this Start element exactly one outgoing connector.",
                AmbiguousContinuation => "Give this element exactly one outgoing connector.",
                InvalidSplit => "Connect this split to at least two outgoing paths.",
                InvalidMerge => "Connect at least two incoming paths and exactly one outgoing path.",
                InvalidParallel => "Connect at least one incoming path and one outgoing path.",
                InvalidEnd => "Remove outgoing connectors from this End element.",
                MissingRoute => "Check this connector's endpoints and route in the current process scope.",
                MissingNode => "Check that this element and its connector endpoints have current visible geometry.",
                MissingPublication => "Save Publication metadata before publishing the process.",
                InvalidCapture => "Return to a ready process view and capture its current presentation again.",
                UnsupportedImage => "Use self-contained presentation geometry for the published item.",
                PackageBuildFailed => "Retry Publish; if it still fails, retain these diagnostics for investigation.",
                _ => null,
            };
            if (suggestedAction is not null)
            {
                context["SuggestedAction"] = suggestedAction;
            }
            yield return new Diagnostic(diagnostic.Code, diagnostic.Severity, diagnostic.Message,
                diagnostic.SourceIdentity, context);
        }
    }

    private static Diagnostic NodeError(string code, string message, ProjectedNode node) =>
        Error(code, message, node.Source.SemanticElementId.Value, additionalContext:
        [
            new KeyValuePair<string, string>("ElementId", node.Source.SemanticElementId.Value),
            new KeyValuePair<string, string>("ElementType", node.Source.SemanticTypeId.Value),
        ]);

    private static Diagnostic ConnectorError(string code, string message, string connectorId) =>
        Error(code, message, connectorId, additionalContext:
        [new KeyValuePair<string, string>("ConnectorId", connectorId)]);

    private static Diagnostic Error(
        string code,
        string message,
        string sourceIdentity,
        string? exceptionType = null,
        string? buildStage = null,
        IEnumerable<KeyValuePair<string, string>>? additionalContext = null) =>
        new(
            code,
            DiagnosticSeverity.Error,
            message,
            sourceIdentity,
            new[]
            {
                exceptionType is null
                    ? default(KeyValuePair<string, string>?)
                    : new KeyValuePair<string, string>("ExceptionType", exceptionType),
                buildStage is null
                    ? default(KeyValuePair<string, string>?)
                    : new KeyValuePair<string, string>("BuildStage", buildStage),
            }.Where(static entry => entry.HasValue)
                .Select(static entry => entry!.Value)
                .Concat(additionalContext ?? []));

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

    private sealed class EmptyPublishedNodeDataMapper : IPublishedNodeDataMapper
    {
        internal static EmptyPublishedNodeDataMapper Instance { get; } = new();

        public PublishedNodeData Map(SemanticElementSnapshot semanticElement)
        {
            ArgumentNullException.ThrowIfNull(semanticElement);
            return PublishedNodeData.Empty;
        }
    }
}

internal sealed record PublishedProcessStaticAssets(
    ImmutableArray<byte> IndexHtml,
    ImmutableArray<byte> JavaScript,
    ImmutableArray<byte> Styles)
{
    private const string ResourcePrefix =
        "Inceptus.DocumentEngine.Canvas2D.Publishing.";

    internal static PublishedProcessStaticAssets LoadEmbedded() =>
        new(
            Read("index.html"),
            Read("inceptus.publish.js"),
            Read("styles.css"));

    private static ImmutableArray<byte> Read(string name)
    {
        using var stream = typeof(PublishedProcessStaticAssets).Assembly
            .GetManifestResourceStream(ResourcePrefix + name) ??
            throw new InvalidOperationException(
                $"The embedded Publish asset '{name}' is unavailable.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray().ToImmutableArray();
    }
}
