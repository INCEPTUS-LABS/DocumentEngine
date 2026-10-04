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
        : this(toolboxItemId, commandFactory, candidateProvider, previewProvider: null)
    {
    }

    public ToolboxPlacementRegistration(
        ToolboxItemId toolboxItemId,
        IToolboxPlacementCommandFactory commandFactory,
        IToolboxPlacementCandidateProvider? candidateProvider,
        IToolboxPlacementPreviewProvider? previewProvider)
    {
        ArgumentNullException.ThrowIfNull(toolboxItemId);
        ArgumentNullException.ThrowIfNull(commandFactory);

        ToolboxItemId = toolboxItemId;
        CommandFactory = commandFactory;
        CandidateProvider = candidateProvider;
        PreviewProvider = previewProvider;
    }

    public ToolboxItemId ToolboxItemId { get; }

    public IToolboxPlacementCommandFactory CommandFactory { get; }

    public IToolboxPlacementCandidateProvider? CandidateProvider { get; }

    public IToolboxPlacementPreviewProvider? PreviewProvider { get; }
}
