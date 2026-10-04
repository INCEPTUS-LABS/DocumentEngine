namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// Evaluates prospective node creation without allocating identities or creating Commands.
/// </summary>
public interface IToolboxPlacementPreviewProvider
{
    ToolboxPlacementPreview Evaluate(ToolboxPlacementPreviewRequest request);
}
