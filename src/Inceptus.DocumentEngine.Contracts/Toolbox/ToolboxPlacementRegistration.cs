using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// Associates one Toolbox item with its notation-owned placement Command factory.
/// </summary>
public sealed class ToolboxPlacementRegistration
{
    public ToolboxPlacementRegistration(
        ToolboxItemId toolboxItemId,
        IToolboxPlacementCommandFactory commandFactory,
        IToolboxPlacementCandidateProvider? candidateProvider = null)
    {
        ArgumentNullException.ThrowIfNull(toolboxItemId);
        ArgumentNullException.ThrowIfNull(commandFactory);

        ToolboxItemId = toolboxItemId;
        CommandFactory = commandFactory;
        CandidateProvider = candidateProvider;
    }

    public ToolboxItemId ToolboxItemId { get; }

    public IToolboxPlacementCommandFactory CommandFactory { get; }

    public IToolboxPlacementCandidateProvider? CandidateProvider { get; }
}
