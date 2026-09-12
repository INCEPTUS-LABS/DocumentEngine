namespace Inceptus.DocumentEngine.Contracts.Commands;

/// <summary>
/// Allows a Command with proven processing requirements to narrow the safe default pipeline
/// invalidation without coupling framework orchestration to concrete Command types.
/// </summary>
public interface ICommandPipelineInvalidation
{
    PipelineInvalidation PipelineInvalidation { get; }
}

/// <summary>
/// Provides the single framework authority for resolving Command pipeline invalidation.
/// Commands without an explicit declaration conservatively invalidate the full pipeline.
/// </summary>
public static class CommandPipelineInvalidation
{
    public const PipelineInvalidation Full =
        PipelineInvalidation.Projection |
        PipelineInvalidation.NodeLayout |
        PipelineInvalidation.Routing |
        PipelineInvalidation.Scene;

    public const PipelineInvalidation WithoutNodeLayout =
        PipelineInvalidation.Projection |
        PipelineInvalidation.Routing |
        PipelineInvalidation.Scene;

    public const PipelineInvalidation ConnectorOnly = WithoutNodeLayout;

    public static PipelineInvalidation Resolve(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command is ICommandPipelineInvalidation declared
            ? Validate(declared.PipelineInvalidation, nameof(command))
            : Full;
    }

    public static PipelineInvalidation Validate(
        PipelineInvalidation invalidation,
        string? parameterName = null)
    {
        if ((invalidation & ~Full) != 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName ?? nameof(invalidation),
                invalidation,
                "Pipeline invalidation may contain only known processing stages.");
        }

        return invalidation;
    }

    public static NodeGeometryPipelineImpact? ResolveNodeGeometryImpact(
        PipelineInvalidation invalidation,
        NodeGeometryPipelineImpact? declaredImpact = null,
        string? parameterName = null)
    {
        Validate(invalidation, nameof(invalidation));
        var invalidatesNodeLayout =
            (invalidation & PipelineInvalidation.NodeLayout) != 0;
        if (invalidatesNodeLayout && declaredImpact is not null)
        {
            throw new ArgumentException(
                "A full node-layout invalidation cannot also declare preserved or explicit node geometry.",
                parameterName ?? nameof(declaredImpact));
        }

        return invalidatesNodeLayout
            ? null
            : declaredImpact ?? NodeGeometryPipelineImpact.PreserveAll;
    }
}
