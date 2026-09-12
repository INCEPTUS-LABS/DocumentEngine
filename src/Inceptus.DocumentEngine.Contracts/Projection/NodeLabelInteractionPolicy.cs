namespace Inceptus.DocumentEngine.Contracts.Projection;

/// <summary>
/// Declares whether a projected node label participates in manual presentation interaction.
/// The policy is transient projection intent and is never persistent instance state.
/// </summary>
public enum NodeLabelInteractionPolicy
{
    Fixed,
    MoveAndResize,
}
