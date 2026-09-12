using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

public enum Canvas2DSpatialEditKind
{
    Creation,
    Move,
}

/// <summary>Read-only command planning inputs. No session or mutation capability is exposed.</summary>
public sealed record Canvas2DSpatialEditRequest
{
    public Canvas2DSpatialEditRequest(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        Canvas2DSpatialEditKind kind,
        ICommand baseCommand,
        SemanticElementId semanticElementId,
        Canvas2DSpatialRegion destinationRegion)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        ArgumentNullException.ThrowIfNull(baseCommand);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentNullException.ThrowIfNull(destinationRegion);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        Document = document;
        ActiveScopeId = activeScopeId;
        Kind = kind;
        BaseCommand = baseCommand;
        SemanticElementId = semanticElementId;
        DestinationRegion = destinationRegion;
    }
    public DocumentSnapshot Document { get; }
    public DocumentScopeId ActiveScopeId { get; }
    public Canvas2DSpatialEditKind Kind { get; }
    public ICommand BaseCommand { get; }
    public SemanticElementId SemanticElementId { get; }
    public Canvas2DSpatialRegion DestinationRegion { get; }
}

public interface ICanvas2DSpatialEditPlanner
{
    Canvas2DSpatialEditPlanResult Plan(Canvas2DSpatialEditRequest request);
}

public sealed class Canvas2DSpatialEditPlanResult
{
    private Canvas2DSpatialEditPlanResult(ICommand? command, IEnumerable<Diagnostic>? diagnostics)
    {
        Command = command;
        Diagnostics = diagnostics?.ToImmutableArray() ?? [];
    }

    public ICommand? Command { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public bool Succeeded => Command is not null && !Diagnostics.Any(static d => d.Severity == DiagnosticSeverity.Error);
    public static Canvas2DSpatialEditPlanResult Success(ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new(command, null);
    }
    public static Canvas2DSpatialEditPlanResult Failure(IEnumerable<Diagnostic> diagnostics) => new(null, diagnostics);
}

public sealed record Canvas2DSpatialEditPlannerRegistration
{
    public Canvas2DSpatialEditPlannerRegistration(ModelProfileId profileId, ICanvas2DSpatialEditPlanner planner)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(planner);
        ProfileId = profileId;
        Planner = planner;
    }
    public ModelProfileId ProfileId { get; }
    public ICanvas2DSpatialEditPlanner Planner { get; }
}

/// <summary>Deterministic profile-keyed pure planning; a missing policy rejects a spatial edit.</summary>
public sealed class Canvas2DSpatialEditPlannerCatalog
{
    private readonly ImmutableDictionary<ModelProfileId, ICanvas2DSpatialEditPlanner> _planners;

    public Canvas2DSpatialEditPlannerCatalog(IEnumerable<Canvas2DSpatialEditPlannerRegistration>? registrations = null)
    {
        var copy = registrations?.ToArray() ?? [];
        if (copy.Any(static entry => entry is null || entry.ProfileId is null || entry.Planner is null))
        {
            throw new ArgumentException("Spatial planner registrations must be complete.", nameof(registrations));
        }
        _planners = copy.ToImmutableDictionary(static entry => entry.ProfileId, static entry => entry.Planner);
    }

    public static Canvas2DSpatialEditPlannerCatalog Empty { get; } = new();

    public Canvas2DSpatialEditPlanResult Plan(Canvas2DSpatialEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _planners.TryGetValue(request.DestinationRegion.ModelProfileId, out var planner)
            ? planner.Plan(request)
            : Canvas2DSpatialEditPlanResult.Failure([new Diagnostic(
                "INCEPTUS.SPATIAL.PLANNER.MISSING", DiagnosticSeverity.Error,
                "No spatial edit policy is registered for this presentation region.", request.DestinationRegion.Id.Value)]);
    }
}
