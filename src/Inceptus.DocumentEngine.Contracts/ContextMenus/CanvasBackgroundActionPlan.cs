using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Contains the one canonical persistent command for a model action and an optional
/// separate scope-navigation destination after that command succeeds.
/// </summary>
public sealed record CanvasBackgroundActionPlan
{
    public CanvasBackgroundActionPlan(
        ICommand command,
        DocumentScopeId? navigateToScopeId = null,
        SemanticElementId? selectSemanticElementId = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        Command = command;
        NavigateToScopeId = navigateToScopeId;
        SelectSemanticElementId = selectSemanticElementId;
    }

    public ICommand Command { get; }

    public DocumentScopeId? NavigateToScopeId { get; }

    public SemanticElementId? SelectSemanticElementId { get; }
}
