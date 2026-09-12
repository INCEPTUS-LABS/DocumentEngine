using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class ResizeVisualStateHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(ResizeVisualStateCommand.KnownTypeId, new ResizeVisualStateHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(committed);

        if (command is not ResizeVisualStateCommand resize ||
            !before.VisualModel.TryGetVisualState(resize.TargetVisualStateId, out var oldState) ||
            oldState is null ||
            !committed.VisualModel.TryGetVisualState(resize.TargetVisualStateId, out var newState) ||
            newState is null)
        {
            return CommandHistoryPreparationResult.Failure(
            [
                new Diagnostic(
                    HistoryDiagnosticCodes.InvalidPreparation,
                    DiagnosticSeverity.Error,
                    "Resize History requires the target Visual state before and after commit.",
                    command.TypeId.Value),
            ]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new ResizeVisualStateHistoryCommandFactory(
                resize.TargetVisualStateId,
                Bounds(oldState),
                oldState.PlacementMode),
            new ResizeVisualStateHistoryCommandFactory(
                resize.TargetVisualStateId,
                Bounds(newState),
                newState.PlacementMode));
    }

    private static RectD Bounds(VisualStateSnapshot visualState) =>
        new(
            visualState.Position.X,
            visualState.Position.Y,
            visualState.Size.Width,
            visualState.Size.Height);
}

internal sealed class ResizeVisualStateHistoryCommandFactory : IHistoryCommandFactory
{
    private readonly RectD _bounds;
    private readonly VisualPlacementMode _placementMode;
    private readonly VisualStateId _visualStateId;

    internal ResizeVisualStateHistoryCommandFactory(
        VisualStateId visualStateId,
        RectD bounds,
        VisualPlacementMode placementMode)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        _visualStateId = visualStateId;
        _bounds = bounds;
        _placementMode = placementMode;
    }

    public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
        new ResizeVisualStateCommand(
            documentId,
            expectedRevision,
            _visualStateId,
            _bounds,
            _placementMode);
}
