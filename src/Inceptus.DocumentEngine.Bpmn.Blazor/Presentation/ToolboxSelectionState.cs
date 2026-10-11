using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Component-local Toolbox selection. Connection capability selection is synchronized
/// with the Editing Session's transient tool state; neither enters the Document.
/// </summary>
internal sealed class ToolboxSelectionState
{
    private readonly object _sync = new();
    private ToolboxItemId? _selectedItemId;
    private AnchorConnectionCreationId? _selectedConnectionCreationId;

    internal AnchorConnectionCreationId? SelectedConnectionCreationId
    {
        get { lock (_sync) { return _selectedConnectionCreationId; } }
    }

    internal ToolboxItemId? SelectedItemId
    {
        get
        {
            lock (_sync)
            {
                return _selectedItemId;
            }
        }
    }

    internal bool Select(ToolboxItemId itemId) => Select(itemId, null);

    internal bool Select(ToolboxItemId itemId, AnchorConnectionCreationId? creationId)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        lock (_sync)
        {
            if (_selectedItemId == itemId && _selectedConnectionCreationId == creationId)
            {
                return false;
            }

            _selectedItemId = itemId;
            _selectedConnectionCreationId = creationId;
            return true;
        }
    }

    internal bool Clear()
    {
        lock (_sync)
        {
            if (_selectedItemId is null)
            {
                return false;
            }

            _selectedItemId = null;
            _selectedConnectionCreationId = null;
            return true;
        }
    }

    internal bool Clear(ToolboxItemId expectedItemId)
    {
        ArgumentNullException.ThrowIfNull(expectedItemId);
        lock (_sync)
        {
            if (_selectedItemId != expectedItemId)
            {
                return false;
            }

            _selectedItemId = null;
            _selectedConnectionCreationId = null;
            return true;
        }
    }
}
