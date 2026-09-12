using System.Collections.Immutable;
using System.Globalization;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

/// <summary>
/// Preserves connector centerline and editable-route geometry when display geometry is decorated.
/// </summary>
internal static class Canvas2DConnectorPathMetadata
{
    internal const string LogicalPathPointCount =
        "inceptus.canvas2d:connector-logical-path-point-count";
    internal const string EditablePathPointCount =
        "inceptus.canvas2d:connector-editable-path-point-count";
    internal const string NoRouteFallback =
        "inceptus.canvas2d:connector-no-route-fallback";

    private const string LogicalPathPointPrefix =
        "inceptus.canvas2d:connector-logical-path-point:";
    private const string EditablePathPointPrefix =
        "inceptus.canvas2d:connector-editable-path-point:";

    internal static PropertyMap CreateProperties(
        IReadOnlyList<PointD> logicalPath,
        IReadOnlyList<PointD>? editablePath = null,
        bool isNoRouteFallback = false)
    {
        ArgumentNullException.ThrowIfNull(logicalPath);
        ValidatePath(logicalPath, nameof(logicalPath));
        if (editablePath is not null)
        {
            ValidatePath(editablePath, nameof(editablePath));
        }

        var properties = new List<KeyValuePair<string, PropertyValue>>();
        AddPath(properties, LogicalPathPointCount, LogicalPathPointPrefix, logicalPath);
        if (editablePath is not null)
        {
            AddPath(properties, EditablePathPointCount, EditablePathPointPrefix, editablePath);
        }

        if (isNoRouteFallback)
        {
            properties.Add(new KeyValuePair<string, PropertyValue>(
                NoRouteFallback,
                PropertyValue.FromBoolean(true)));
        }

        return new PropertyMap(properties);
    }

    internal static bool IsNoRouteFallbackPath(Canvas2DSceneItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Metadata.TryGetValue(NoRouteFallback, out var value) &&
            value.Kind == PropertyValueKind.Boolean &&
            value.BooleanValue;
    }

    internal static ImmutableArray<PointD> Resolve(Canvas2DSceneItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return TryReadPath(
            item.Metadata,
            LogicalPathPointCount,
            LogicalPathPointPrefix,
            out var logicalPath)
                ? logicalPath
                : item.Geometry.Points;
    }

    internal static ImmutableArray<PointD> ResolveEditable(Canvas2DSceneItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return TryReadPath(
            item.Metadata,
            EditablePathPointCount,
            EditablePathPointPrefix,
            out var editablePath)
                ? editablePath
                : Resolve(item);
    }

    internal static bool IsReservedKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return StringComparer.Ordinal.Equals(key, LogicalPathPointCount) ||
            StringComparer.Ordinal.Equals(key, EditablePathPointCount) ||
            StringComparer.Ordinal.Equals(key, NoRouteFallback) ||
            key.StartsWith(LogicalPathPointPrefix, StringComparison.Ordinal) ||
            key.StartsWith(EditablePathPointPrefix, StringComparison.Ordinal);
    }

    private static void AddPath(
        List<KeyValuePair<string, PropertyValue>> properties,
        string countKey,
        string pointPrefix,
        IReadOnlyList<PointD> path)
    {
        properties.Add(new(countKey, PropertyValue.FromInteger(path.Count)));
        for (var index = 0; index < path.Count; index++)
        {
            properties.Add(new(
                CoordinateKey(pointPrefix, index, 'x'),
                PropertyValue.FromNumber(path[index].X)));
            properties.Add(new(
                CoordinateKey(pointPrefix, index, 'y'),
                PropertyValue.FromNumber(path[index].Y)));
        }
    }

    private static bool TryReadPath(
        PropertyMap metadata,
        string countKey,
        string pointPrefix,
        out ImmutableArray<PointD> path)
    {
        path = [];
        if (!metadata.TryGetValue(countKey, out var countValue) ||
            countValue.Kind != PropertyValueKind.Integer ||
            countValue.IntegerValue is < 2 or > int.MaxValue ||
            1L + (2L * countValue.IntegerValue) > metadata.Count)
        {
            return false;
        }

        var pointCount = (int)countValue.IntegerValue;
        var points = ImmutableArray.CreateBuilder<PointD>(pointCount);
        for (var index = 0; index < pointCount; index++)
        {
            if (!TryReadCoordinate(metadata, CoordinateKey(pointPrefix, index, 'x'), out var x) ||
                !TryReadCoordinate(metadata, CoordinateKey(pointPrefix, index, 'y'), out var y))
            {
                return false;
            }

            points.Add(new PointD(x, y));
        }

        path = points.MoveToImmutable();
        return true;
    }

    private static bool TryReadCoordinate(
        PropertyMap metadata,
        string key,
        out double coordinate)
    {
        if (metadata.TryGetValue(key, out var value) &&
            value.Kind == PropertyValueKind.Number)
        {
            coordinate = value.NumberValue;
            return true;
        }

        coordinate = default;
        return false;
    }

    private static string CoordinateKey(string prefix, int index, char axis) =>
        string.Concat(
            prefix,
            index.ToString(CultureInfo.InvariantCulture),
            ":",
            axis);

    private static void ValidatePath(IReadOnlyList<PointD> path, string parameterName)
    {
        if (path.Count < 2)
        {
            throw new ArgumentException(
                "Connector path metadata requires at least two points.",
                parameterName);
        }
    }
}
