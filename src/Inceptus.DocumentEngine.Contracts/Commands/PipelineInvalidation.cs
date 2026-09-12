namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Identifies the transient processing stages invalidated by one committed Command.
/// </summary>
[Flags]
public enum PipelineInvalidation
{
    None = 0,
    Projection = 1 << 0,
    NodeLayout = 1 << 1,
    Routing = 1 << 2,
    Scene = 1 << 3,
}
