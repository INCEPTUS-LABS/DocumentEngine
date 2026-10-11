namespace Inceptus.DocumentEngine.Contracts.Visuals;

public enum VisualPlacementMode
{
    Automatic,
    Manual,
    Pinned,
    /// <summary>Owns endpoint identity and anchors; boundary geometry comes from projected spatial regions.</summary>
    Derived,
}
