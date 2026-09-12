namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Defines one pure notation-owned semantic Scene action that changes transient View state.
/// </summary>
public sealed class SemanticSceneViewActionDefinition
{
    private readonly Func<SemanticSceneViewActionRequest, SemanticSceneViewActionPlan>
        _planFactory;
    private readonly Func<SemanticSceneViewActionRequest, bool>? _applicability;

    public SemanticSceneViewActionDefinition(
        SemanticSceneViewActionId id,
        string displayName,
        Func<SemanticSceneViewActionRequest, SemanticSceneViewActionPlan> planFactory,
        int order = 0,
        Func<SemanticSceneViewActionRequest, bool>? applicability = null)
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

    public SemanticSceneViewActionId Id { get; }

    public string DisplayName { get; }

    public int Order { get; }

    public bool IsApplicable(SemanticSceneViewActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _applicability?.Invoke(request) ?? true;
    }

    public SemanticSceneViewActionPlan CreatePlan(SemanticSceneViewActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsApplicable(request))
        {
            throw new InvalidOperationException(
                $"Semantic Scene View action '{Id}' is not applicable to the supplied context.");
        }

        var plan = _planFactory(request) ?? throw new InvalidOperationException(
            $"Semantic Scene View action '{Id}' returned no execution plan.");
        if (plan.SemanticElementId != request.TargetSemanticElementId)
        {
            throw new InvalidOperationException(
                $"Semantic Scene View action '{Id}' targeted a different semantic element.");
        }

        return plan;
    }
}
