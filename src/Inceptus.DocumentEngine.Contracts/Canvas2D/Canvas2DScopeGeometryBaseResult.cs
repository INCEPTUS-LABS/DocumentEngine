using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>Node-only base appearance and typed text requests, without routing or spatial plans.</summary>
public sealed class Canvas2DScopeGeometryBaseResult
{
    private Canvas2DScopeGeometryBaseResult(
        Canvas2DSceneContribution? contribution,
        IEnumerable<KeyValuePair<SceneObjectId, TextMeasurementRequest>>? textRequests,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Diagnostics = Canvas2DSceneDiagnosticCollection.CopyAndOrder(diagnostics, nameof(diagnostics));
        Succeeded = contribution is not null;
        if (Succeeded == Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw new ArgumentException("Success requires a contribution and no errors; failure requires an error.");
        if (contribution is not null && (contribution.SpatialPresentationPlan is not null ||
            contribution.Metadata.Count != 0 ||
            contribution.Items.Any(static item => item.Layer == Canvas2DSceneLayer.Connector ||
                (item.Origin.Categories & Canvas2DSceneOriginCategory.EditorState) != 0)))
            throw new ArgumentException("Pre-route base contributions cannot contain connector/editor items, metadata or a spatial plan.", nameof(contribution));
        Contribution = contribution;
        var requests = ImmutableDictionary.CreateBuilder<SceneObjectId, TextMeasurementRequest>();
        foreach (var entry in textRequests ?? [])
        {
            ArgumentNullException.ThrowIfNull(entry.Key);
            ArgumentNullException.ThrowIfNull(entry.Value);
            requests.Add(entry.Key, entry.Value);
        }
        TextRequests = requests.ToImmutable();
    }

    public bool Succeeded { get; }
    public Canvas2DSceneContribution? Contribution { get; }
    public ImmutableDictionary<SceneObjectId, TextMeasurementRequest> TextRequests { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static Canvas2DScopeGeometryBaseResult Success(
        Canvas2DSceneContribution contribution,
        IEnumerable<KeyValuePair<SceneObjectId, TextMeasurementRequest>>? textRequests = null,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        return new(contribution, textRequests, diagnostics);
    }

    public static Canvas2DScopeGeometryBaseResult Failure(IEnumerable<Diagnostic> diagnostics) =>
        new(null, null, diagnostics);
}
