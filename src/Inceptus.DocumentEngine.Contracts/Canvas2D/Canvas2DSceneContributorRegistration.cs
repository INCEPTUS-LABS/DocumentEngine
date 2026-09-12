namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>
/// Associates one stable contributor descriptor with its immutable contribution policy.
/// </summary>
public sealed class Canvas2DSceneContributorRegistration
{
    public Canvas2DSceneContributorRegistration(
        Canvas2DSceneContributorDescriptor descriptor,
        ICanvas2DSceneContributor contributor,
        Canvas2DSceneContributionStage stage = Canvas2DSceneContributionStage.Canonical)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(contributor);
        if (!Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage), stage, "The contribution stage must be defined.");
        }

        Descriptor = descriptor;
        Contributor = contributor;
        Stage = stage;
    }

    public Canvas2DSceneContributorDescriptor Descriptor { get; }

    public ICanvas2DSceneContributor Contributor { get; }

    public Canvas2DSceneContributionStage Stage { get; }
}
