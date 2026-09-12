using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.Contracts.EditorState;

/// <summary>
/// Describes one piece of temporary interaction feedback without rendering resources.
/// </summary>
public sealed class EditorFeedbackSnapshot : IEquatable<EditorFeedbackSnapshot>
{
    public EditorFeedbackSnapshot(
        string id,
        string kind,
        RectD? bounds = null,
        IEnumerable<PointD>? points = null,
        IEnumerable<KeyValuePair<string, PropertyValue>>? properties = null,
        EditorFeedbackPresentationMode presentationMode =
            EditorFeedbackPresentationMode.Default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (!Enum.IsDefined(presentationMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(presentationMode),
                presentationMode,
                "The editor-feedback presentation mode must be defined.");
        }

        Id = id;
        Kind = kind;
        Bounds = bounds;
        Points = points?.ToImmutableArray() ?? [];
        Properties = new PropertyMap(properties);
        PresentationMode = presentationMode;
    }

    public string Id { get; }

    public string Kind { get; }

    public RectD? Bounds { get; }

    public ImmutableArray<PointD> Points { get; }

    public PropertyMap Properties { get; }

    public EditorFeedbackPresentationMode PresentationMode { get; }

    public bool Equals(EditorFeedbackSnapshot? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        StringComparer.Ordinal.Equals(Id, other.Id) &&
        StringComparer.Ordinal.Equals(Kind, other.Kind) &&
        Bounds == other.Bounds &&
        Points.AsSpan().SequenceEqual(other.Points.AsSpan()) &&
        Properties.Equals(other.Properties) &&
        PresentationMode == other.PresentationMode;

    public override bool Equals(object? obj) => Equals(obj as EditorFeedbackSnapshot);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id, StringComparer.Ordinal);
        hash.Add(Kind, StringComparer.Ordinal);
        hash.Add(Bounds);
        foreach (var point in Points)
        {
            hash.Add(point);
        }

        hash.Add(Properties);
        hash.Add(PresentationMode);
        return hash.ToHashCode();
    }
}
