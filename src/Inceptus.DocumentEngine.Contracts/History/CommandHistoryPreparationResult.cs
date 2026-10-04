using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.History;

/// <summary>
/// Immutable output of a Command History policy.
/// </summary>
public sealed class CommandHistoryPreparationResult
{
    private CommandHistoryPreparationResult(
        bool succeeded,
        HistoryRecordingBehavior behavior,
        IHistoryCommandFactory? undoFactory,
        IHistoryCommandFactory? redoFactory,
        IEnumerable<Diagnostic>? diagnostics,
        IEnumerable<ConnectorRoutingTypeHistoryDelta>? routingTypeDeltas = null,
        IEnumerable<SpatialRegionHeightHistoryDelta>? spatialHeightDeltas = null,
        IEnumerable<SpatialScopeWidthHistoryDelta>? spatialWidthDeltas = null)
    {
        Succeeded = succeeded;
        Behavior = behavior;
        UndoFactory = undoFactory;
        RedoFactory = redoFactory;
        Diagnostics = CopyDiagnostics(diagnostics);
        RoutingTypeDeltas = routingTypeDeltas?.ToImmutableArray() ?? [];
        SpatialHeightDeltas = spatialHeightDeltas?.ToImmutableArray() ?? [];
        SpatialWidthDeltas = spatialWidthDeltas?.ToImmutableArray() ?? [];
        if (RoutingTypeDeltas.Any(static delta => delta is null) || SpatialHeightDeltas.Any(static delta => delta is null) ||
            RoutingTypeDeltas.Select(static delta => delta.VisualStateId).Distinct().Count() != RoutingTypeDeltas.Length ||
            SpatialHeightDeltas.Select(static delta => (delta.ScopeId, delta.RegionId)).Distinct().Count() != SpatialHeightDeltas.Length ||
            SpatialWidthDeltas.Any(static delta => delta is null) ||
            SpatialWidthDeltas.Select(static delta => (delta.ScopeId, delta.ProfileId)).Distinct().Count() != SpatialWidthDeltas.Length)
        {
            throw new ArgumentException("Owned History deltas must be non-null and unique.", nameof(routingTypeDeltas));
        }
    }

    public bool Succeeded { get; }

    public HistoryRecordingBehavior Behavior { get; }

    public IHistoryCommandFactory? UndoFactory { get; }

    public IHistoryCommandFactory? RedoFactory { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public ImmutableArray<ConnectorRoutingTypeHistoryDelta> RoutingTypeDeltas { get; }

    public ImmutableArray<SpatialRegionHeightHistoryDelta> SpatialHeightDeltas { get; }
    public ImmutableArray<SpatialScopeWidthHistoryDelta> SpatialWidthDeltas { get; }

    public static CommandHistoryPreparationResult Undoable(
        IHistoryCommandFactory undoFactory,
        IHistoryCommandFactory redoFactory,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(undoFactory);
        ArgumentNullException.ThrowIfNull(redoFactory);
        return new(true, HistoryRecordingBehavior.Undoable, undoFactory, redoFactory, diagnostics);
    }

    public static CommandHistoryPreparationResult NotUndoable(
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(true, HistoryRecordingBehavior.NotUndoable, null, null, diagnostics);

    public static CommandHistoryPreparationResult PreserveExistingHistory(
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(true, HistoryRecordingBehavior.PreserveExistingHistory, null, null, diagnostics);

    public static CommandHistoryPreparationResult Undoable(
        IHistoryCommandFactory undoFactory, IHistoryCommandFactory redoFactory,
        IEnumerable<ConnectorRoutingTypeHistoryDelta> routingTypeDeltas,
        IEnumerable<SpatialRegionHeightHistoryDelta> spatialHeightDeltas,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(undoFactory); ArgumentNullException.ThrowIfNull(redoFactory);
        ArgumentNullException.ThrowIfNull(routingTypeDeltas); ArgumentNullException.ThrowIfNull(spatialHeightDeltas);
        return new(true, HistoryRecordingBehavior.Undoable, undoFactory, redoFactory, diagnostics,
            routingTypeDeltas, spatialHeightDeltas);
    }

    public static CommandHistoryPreparationResult Failure(
        IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(false, HistoryRecordingBehavior.NotUndoable, null, null, diagnostics);
    }

    public static CommandHistoryPreparationResult Undoable(
        IHistoryCommandFactory undoFactory, IHistoryCommandFactory redoFactory,
        IEnumerable<ConnectorRoutingTypeHistoryDelta> routingTypeDeltas,
        IEnumerable<SpatialRegionHeightHistoryDelta> spatialHeightDeltas,
        IEnumerable<SpatialScopeWidthHistoryDelta> spatialWidthDeltas,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(undoFactory);
        ArgumentNullException.ThrowIfNull(redoFactory);
        ArgumentNullException.ThrowIfNull(routingTypeDeltas);
        ArgumentNullException.ThrowIfNull(spatialHeightDeltas);
        ArgumentNullException.ThrowIfNull(spatialWidthDeltas);
        return new(true, HistoryRecordingBehavior.Undoable, undoFactory, redoFactory, diagnostics,
            routingTypeDeltas, spatialHeightDeltas, spatialWidthDeltas);
    }

    private static ImmutableArray<Diagnostic> CopyDiagnostics(
        IEnumerable<Diagnostic>? diagnostics)
    {
        if (diagnostics is null)
        {
            return [];
        }

        var copy = diagnostics.ToArray();
        if (Array.Exists(copy, static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "History diagnostics cannot contain null values.",
                nameof(diagnostics));
        }

        Array.Sort(copy, static (left, right) =>
        {
            var code = StringComparer.Ordinal.Compare(left.Code, right.Code);
            if (code != 0)
            {
                return code;
            }

            var source = StringComparer.Ordinal.Compare(
                left.SourceIdentity ?? string.Empty,
                right.SourceIdentity ?? string.Empty);
            return source != 0
                ? source
                : StringComparer.Ordinal.Compare(left.Message, right.Message);
        });
        return [.. copy];
    }
}
