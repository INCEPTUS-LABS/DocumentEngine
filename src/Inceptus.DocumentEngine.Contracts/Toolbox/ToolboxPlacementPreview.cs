using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;

namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// Immutable prospective node geometry and notation-owned appearance intent. It has no
/// persistent identity, renderer resources, or authority to change the Document.
/// </summary>
public sealed class ToolboxPlacementPreview : IEquatable<ToolboxPlacementPreview>
{
    public const string FeedbackKind = "inceptus:toolbox-placement-preview";

    public ToolboxPlacementPreview(
        ToolboxItemId toolboxItemId,
        SemanticTypeId semanticTypeId,
        RectD bounds,
        PointD hotspot,
        string? label = null,
        NodeLabelPlacement? labelPlacement = null,
        ToolboxPlacementCandidate? attachmentCandidate = null,
        bool isAllowed = true,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(toolboxItemId);
        ArgumentNullException.ThrowIfNull(semanticTypeId);
        if (bounds.IsEmpty)
        {
            throw new ArgumentException("A prospective node requires non-empty body bounds.", nameof(bounds));
        }

        if (label is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(label);
        }

        var copied = diagnostics?.ToArray() ?? [];
        if (Array.Exists(copied, static diagnostic => diagnostic is null))
        {
            throw new ArgumentException("Preview diagnostics cannot contain null values.", nameof(diagnostics));
        }

        if (isAllowed && copied.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new ArgumentException("An allowed preview cannot contain error diagnostics.", nameof(diagnostics));
        }

        if (!isAllowed && copied.Length == 0)
        {
            throw new ArgumentException("A rejected preview requires a diagnostic reason.", nameof(diagnostics));
        }

        ToolboxItemId = toolboxItemId;
        SemanticTypeId = semanticTypeId;
        Bounds = bounds;
        Hotspot = hotspot;
        Label = label;
        LabelPlacement = labelPlacement ?? NodeLabelPlacement.InsideCentered;
        AttachmentCandidate = attachmentCandidate;
        IsAllowed = isAllowed;
        Diagnostics = [.. copied.OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.SourceIdentity, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)];
    }

    public ToolboxItemId ToolboxItemId { get; }

    public SemanticTypeId SemanticTypeId { get; }

    public RectD Bounds { get; }

    /// <summary>Gets the effective placement hotspot in the same coordinate space as Bounds.</summary>
    public PointD Hotspot { get; }

    public string? Label { get; }

    public NodeLabelPlacement LabelPlacement { get; }

    public ToolboxPlacementCandidate? AttachmentCandidate { get; }

    public bool IsAllowed { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool Equals(ToolboxPlacementPreview? other) =>
        ReferenceEquals(this, other) || other is not null &&
        ToolboxItemId == other.ToolboxItemId && SemanticTypeId == other.SemanticTypeId &&
        Bounds == other.Bounds && Hotspot == other.Hotspot &&
        StringComparer.Ordinal.Equals(Label, other.Label) && LabelPlacement.Equals(other.LabelPlacement) &&
        IsAllowed == other.IsAllowed && CandidatesEqual(AttachmentCandidate, other.AttachmentCandidate) &&
        Diagnostics.Length == other.Diagnostics.Length &&
        Diagnostics.Zip(other.Diagnostics).All(static pair => DiagnosticsEqual(pair.First, pair.Second));

    public override bool Equals(object? obj) => Equals(obj as ToolboxPlacementPreview);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ToolboxItemId);
        hash.Add(SemanticTypeId);
        hash.Add(Bounds);
        hash.Add(Hotspot);
        hash.Add(Label, StringComparer.Ordinal);
        hash.Add(LabelPlacement);
        hash.Add(IsAllowed);
        if (AttachmentCandidate is { } candidate)
        {
            hash.Add(candidate.Target);
            hash.Add(candidate.FeedbackKind, StringComparer.Ordinal);
            hash.Add(candidate.PreviewBounds);
            hash.Add(candidate.Properties);
            hash.Add(candidate.FeedbackPresentationMode);
        }

        foreach (var diagnostic in Diagnostics)
        {
            hash.Add(diagnostic.Code, StringComparer.Ordinal);
            hash.Add(diagnostic.Severity);
            hash.Add(diagnostic.Message, StringComparer.Ordinal);
            hash.Add(diagnostic.SourceIdentity, StringComparer.Ordinal);
            foreach (var pair in diagnostic.Context)
            {
                hash.Add(pair.Key, StringComparer.Ordinal);
                hash.Add(pair.Value, StringComparer.Ordinal);
            }
        }

        return hash.ToHashCode();
    }

    private static bool CandidatesEqual(ToolboxPlacementCandidate? left, ToolboxPlacementCandidate? right) =>
        ReferenceEquals(left, right) || left is not null && right is not null &&
        left.Target == right.Target && left.PreviewBounds == right.PreviewBounds &&
        StringComparer.Ordinal.Equals(left.FeedbackKind, right.FeedbackKind) &&
        left.FeedbackPresentationMode == right.FeedbackPresentationMode && left.Properties.Equals(right.Properties);

    private static bool DiagnosticsEqual(Diagnostic left, Diagnostic right) =>
        left.Code == right.Code && left.Severity == right.Severity && left.Message == right.Message &&
        left.SourceIdentity == right.SourceIdentity && left.Context.SequenceEqual(right.Context);
}
