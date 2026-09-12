using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Deletion;

/// <summary>
/// Describes one immutable notation-owned deletion Command and its exact target.
/// </summary>
public sealed class DiagramDeletionPlan
{
    public DiagramDeletionPlan(
        ICommand command,
        DiagramDeletionTargetKind targetKind,
        SemanticElementId semanticId,
        VisualStateId? visualStateId)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(semanticId);
        if (!Enum.IsDefined(targetKind))
        {
            throw new ArgumentOutOfRangeException(nameof(targetKind));
        }

        Command = command;
        TargetKind = targetKind;
        SemanticId = semanticId;
        VisualStateId = visualStateId;
    }

    public ICommand Command { get; }

    public DiagramDeletionTargetKind TargetKind { get; }

    public SemanticElementId SemanticId { get; }

    public VisualStateId? VisualStateId { get; }
}
