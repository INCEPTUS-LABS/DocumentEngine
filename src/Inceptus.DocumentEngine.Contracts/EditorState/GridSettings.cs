namespace Inceptus.DocumentEngine.Contracts.EditorState;

/// <summary>Editor-global preferences, never part of a Document or its History.</summary>
public sealed record GridSettings
{
    private double _pointDistance = 20d;

    public bool ShowGrid { get; init; } = true;

    public bool SnapToGrid { get; init; } = true;

    public double PointDistance
    {
        get => _pointDistance;
        init
        {
            if (!double.IsFinite(value) || value <= 0d)
                throw new ArgumentOutOfRangeException(nameof(value), "Grid spacing must be finite and strictly positive.");
            _pointDistance = value;
        }
    }
}

/// <summary>Read access to the editor's single settings owner across document replacements.</summary>
public interface IGridSettingsSource
{
    GridSettings Grid { get; }
}
