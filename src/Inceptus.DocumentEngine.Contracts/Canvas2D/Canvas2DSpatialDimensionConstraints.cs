using System.Collections.Immutable;
using System.Globalization;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Routing;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>Immutable capacity limits shared by preview and authoritative dimension validation.</summary>
public sealed class Canvas2DSpatialDimensionConstraints : IEquatable<Canvas2DSpatialDimensionConstraints>
{
    public Canvas2DSpatialDimensionConstraints(double minimum, double maximum,
        IEnumerable<VisualStateId> limitingVisualStateIds, IEnumerable<Diagnostic>? diagnostics = null)
    {
        if (!double.IsFinite(minimum) || minimum <= 0d) throw new ArgumentOutOfRangeException(nameof(minimum));
        if (!double.IsFinite(maximum) || maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum));
        Minimum = minimum;
        Maximum = maximum;
        LimitingVisualStateIds = RoutingStateCollection.Unique(limitingVisualStateIds, static id => id.Value, nameof(limitingVisualStateIds));
        Diagnostics = Canvas2DSceneDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
    }

    public double Minimum { get; }
    public double Maximum { get; }
    public ImmutableArray<VisualStateId> LimitingVisualStateIds { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public ImmutableArray<Diagnostic> Validate(double extent)
    {
        if (!double.IsFinite(extent) || extent < Minimum || extent > Maximum)
            return Diagnostics.Add(new Diagnostic("INCEPTUS.SPATIAL.DIMENSION.CAPACITY", DiagnosticSeverity.Error,
                string.Format(CultureInfo.InvariantCulture, "The requested extent must be finite and between {0:G17} and {1:G17}; complete child bodies must fit.", Minimum, Maximum),
                LimitingVisualStateIds.FirstOrDefault()?.Value));
        return Diagnostics;
    }

    public bool Equals(Canvas2DSpatialDimensionConstraints? other) => ReferenceEquals(this, other) ||
        other is not null && Minimum == other.Minimum && Maximum == other.Maximum &&
        LimitingVisualStateIds.AsSpan().SequenceEqual(other.LimitingVisualStateIds.AsSpan()) &&
        Diagnostics.AsSpan().SequenceEqual(other.Diagnostics.AsSpan());
    public override bool Equals(object? obj) => Equals(obj as Canvas2DSpatialDimensionConstraints);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Minimum); hash.Add(Maximum);
        foreach (var id in LimitingVisualStateIds) hash.Add(id);
        foreach (var diagnostic in Diagnostics) hash.Add(diagnostic);
        return hash.ToHashCode();
    }
}
