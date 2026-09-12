namespace Inceptus.DocumentEngine.Contracts.ContextMenus;

/// <summary>
/// Describes one notation-neutral action contributed to the empty-Canvas model menu.
/// </summary>
public sealed class CanvasBackgroundActionDefinition
{
    private readonly Func<CanvasBackgroundActionRequest, CanvasBackgroundActionPlan> _planFactory;
    private readonly Func<CanvasBackgroundActionApplicabilityRequest, bool>? _applicability;

    public CanvasBackgroundActionDefinition(
        CanvasBackgroundActionId id,
        string groupLabel,
        string displayName,
        Func<CanvasBackgroundActionRequest, CanvasBackgroundActionPlan> planFactory,
        int order = 0,
        Func<CanvasBackgroundActionApplicabilityRequest, bool>? applicability = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupLabel);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(planFactory);

        Id = id;
        GroupLabel = groupLabel;
        DisplayName = displayName;
        Order = order;
        _planFactory = planFactory;
        _applicability = applicability;
    }

    public CanvasBackgroundActionId Id { get; }

    public string GroupLabel { get; }

    public string DisplayName { get; }

    public int Order { get; }

    /// <summary>
    /// Evaluates the contributor-owned, read-only applicability rule for the supplied snapshot.
    /// </summary>
    public bool IsApplicable(CanvasBackgroundActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _applicability?.Invoke(new CanvasBackgroundActionApplicabilityRequest(
            request.Document,
            request.ActiveScopeId)) ?? true;
    }

    public CanvasBackgroundActionPlan CreatePlan(CanvasBackgroundActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsApplicable(request))
        {
            throw new InvalidOperationException(
                $"Canvas background action '{Id}' is not applicable to the supplied context.");
        }

        return _planFactory(request) ?? throw new InvalidOperationException(
            $"Canvas background action '{Id}' returned no execution plan.");
    }
}
