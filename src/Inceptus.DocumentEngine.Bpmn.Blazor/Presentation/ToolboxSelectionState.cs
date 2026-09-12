using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

/// <summary>
/// Component-local Toolbox selection. This state never enters the Editing Session or Document.
/// </summary>
internal sealed class ToolboxSelectionState
{
    private readonly object _sync = new();
    private ToolboxItemId? _selectedItemId;

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

    internal bool Select(ToolboxItemId itemId)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        lock (_sync)
        {
            if (_selectedItemId == itemId)
            {
                return false;
            }

            _selectedItemId = itemId;
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
            return true;
        }
    }
}
