namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Defines one pure notation-owned semantic Scene action backed by an ordinary command.
/// </summary>
public sealed class SemanticSceneCommandActionDefinition
{
    private readonly Func<SemanticSceneCommandActionRequest, SemanticSceneCommandActionPlan>
        _planFactory;
    private readonly Func<SemanticSceneCommandActionRequest, bool>? _applicability;

    public SemanticSceneCommandActionDefinition(
        SemanticSceneCommandActionId id,
        string displayName,
        Func<SemanticSceneCommandActionRequest, SemanticSceneCommandActionPlan> planFactory,
        int order = 0,
        Func<SemanticSceneCommandActionRequest, bool>? applicability = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(planFactory);
        Id = id;
        DisplayName = displayName;
        Order = order;
        _planFactory = planFactory;
        _applicability = applicability;
    }

    public SemanticSceneCommandActionId Id { get; }

    public string DisplayName { get; }

    public int Order { get; }

    public bool IsApplicable(SemanticSceneCommandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _applicability?.Invoke(request) ?? true;
    }

    public SemanticSceneCommandActionPlan CreatePlan(
        SemanticSceneCommandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsApplicable(request))
        {
            throw new InvalidOperationException(
                $"Semantic Scene command action '{Id}' is not applicable to the supplied context.");
        }

        var plan = _planFactory(request) ?? throw new InvalidOperationException(
            $"Semantic Scene command action '{Id}' returned no execution plan.");
        if (plan.SemanticElementId != request.TargetSemanticElementId)
        {
            throw new InvalidOperationException(
                $"Semantic Scene command action '{Id}' targeted a different semantic element.");
        }

        return plan;
    }
}
