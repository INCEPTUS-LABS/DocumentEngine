namespace Inceptus.DocumentEngine.Bpmn.Blazor;

/// <summary>Explicit recovery for structurally complete native v2 with incompatible saved policy or geometry.</summary>
public sealed class BpmnModelerNativeImportOptions
{
    public BpmnModelerNativeImportOptions(bool reprepareIncompatibleV2 = false)
    {
        ReprepareIncompatibleV2 = reprepareIncompatibleV2;
    }

    /// <summary>Reprepare supported incompatible v2 as unsaved content; never accepts v1 or malformed files.</summary>
    public bool ReprepareIncompatibleV2 { get; }
}
