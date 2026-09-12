using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal enum ToolboxViewMode
{
    Expanded,
    Compact,
}

internal sealed class ToolboxPresentationState
{
    private ImmutableHashSet<ToolboxGroupId> _knownGroupIds = [];
    private bool _initialized;

    internal ToolboxViewMode ViewMode { get; private set; } = ToolboxViewMode.Expanded;

    internal ToolboxGroupId? ExpandedToolboxGroupId { get; private set; }

    internal void Reconcile(IEnumerable<ToolboxGroupId> visibleGroupIds)
    {
        ArgumentNullException.ThrowIfNull(visibleGroupIds);
        var orderedIds = visibleGroupIds.ToImmutableArray();
        if (orderedIds.Any(static groupId => groupId is null))
        {
            throw new ArgumentException(
                "Visible Toolbox groups cannot contain null identifiers.",
                nameof(visibleGroupIds));
        }

        _knownGroupIds = orderedIds.ToImmutableHashSet();
        if (!_initialized ||
            ExpandedToolboxGroupId is not null &&
            !_knownGroupIds.Contains(ExpandedToolboxGroupId))
        {
            ExpandedToolboxGroupId = orderedIds.FirstOrDefault();
        }

        _initialized = true;
    }

    internal bool ToggleGroup(ToolboxGroupId groupId)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        if (!_knownGroupIds.Contains(groupId))
        {
            return false;
        }

        ExpandedToolboxGroupId = ExpandedToolboxGroupId == groupId ? null : groupId;
        return true;
    }

    internal void ToggleViewMode() => ViewMode = ViewMode == ToolboxViewMode.Expanded
        ? ToolboxViewMode.Compact
        : ToolboxViewMode.Expanded;
}
