using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Canvas2D.Scene;

public sealed class Canvas2DSceneBuildResult : IEquatable<Canvas2DSceneBuildResult>
{
    private Canvas2DSceneBuildResult(
        Canvas2DSceneBuildStatus status,
        Canvas2DScene? scene,
        IEnumerable<Diagnostic>? diagnostics)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "The Canvas2D scene build status must be defined.");
        }

        Status = status;
        Diagnostics = Canvas2DSceneDiagnosticCollection.CopyAndOrder(
            diagnostics,
            nameof(diagnostics));
        var hasError = Diagnostics.Any(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        if (status == Canvas2DSceneBuildStatus.Succeeded && (scene is null || hasError))
        {
            throw new ArgumentException(
                "A successful Canvas2D scene build requires one complete scene and no error diagnostics.",
                nameof(scene));
        }

        if (status == Canvas2DSceneBuildStatus.Failed && (scene is not null || !hasError))
        {
            throw new ArgumentException(
                "A failed Canvas2D scene build requires errors and cannot expose a partial scene.",
                nameof(diagnostics));
        }

        Scene = scene;
    }

    public Canvas2DSceneBuildStatus Status { get; }

    public bool Succeeded => Status == Canvas2DSceneBuildStatus.Succeeded;

    public Canvas2DScene? Scene { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    internal static Canvas2DSceneBuildResult Success(Canvas2DScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new(Canvas2DSceneBuildStatus.Succeeded, scene, scene.Diagnostics);
    }

    internal static Canvas2DSceneBuildResult Failure(IEnumerable<Diagnostic> diagnostics) =>
        new(Canvas2DSceneBuildStatus.Failed, null, diagnostics);

    public bool Equals(Canvas2DSceneBuildResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        Status == other.Status &&
        Equals(Scene, other.Scene) &&
        Canvas2DSceneDiagnosticCollection.SequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as Canvas2DSceneBuildResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Status);
        hash.Add(Scene);
        Canvas2DSceneDiagnosticCollection.AddHashCode(ref hash, Diagnostics);
        return hash.ToHashCode();
    }
}
