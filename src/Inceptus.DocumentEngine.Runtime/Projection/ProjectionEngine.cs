using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.Runtime.Projection;

/// <summary>
/// Framework-owned orchestration for deriving an immutable ProjectedGraph from one Document snapshot.
/// </summary>
public sealed class ProjectionEngine
{
    private readonly ProjectionRuleRegistry _registry;

    public ProjectionEngine(IEnumerable<ProjectionRuleRegistration>? registrations = null) =>
        _registry = new ProjectionRuleRegistry(registrations ?? []);

    /// <summary>
    /// Projects one coherent immutable Document snapshot without modifying authoritative state.
    /// </summary>
    public ProjectionResult Project(
        DocumentSnapshot document,
        ProjectionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Project(
            document,
            document.SemanticModel.RootScopeId,
            context,
            cancellationToken);
    }

    /// <summary>
    /// Projects one exact semantic scope from a coherent immutable Document snapshot without
    /// modifying authoritative state.
    /// </summary>
    public ProjectionResult Project(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ProjectionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(activeScopeId);
        context ??= ProjectionContext.Empty;

        try
        {
            return ProjectCore(document, activeScopeId, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Cancelled(document);
        }
    }

    private ProjectionResult ProjectCore(
        DocumentSnapshot document,
        DocumentScopeId activeScopeId,
        ProjectionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<Diagnostic>();
        if (!ScopeExists(document.SemanticModel, activeScopeId))
        {
            diagnostics.Add(Error(
                ProjectionDiagnosticCodes.InvalidInput,
                $"Document scope '{activeScopeId}' does not exist in the Semantic Model.",
                activeScopeId.Value));
            return ProjectionResult.Failure(
                document.DocumentId,
                document.Revision,
                diagnostics);
        }

        var accumulator = new ProjectionAccumulator();
        var visualStatesBySemanticId = BuildVisualStateIndex(document, diagnostics);

        foreach (var element in document.SemanticModel.Elements.Where(
                     element => IsInScope(document, element.Id, activeScopeId)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var registrations = _registry.Find(
                ProjectionSourceKind.SemanticElement,
                element.TypeId);
            if (registrations.IsEmpty)
            {
                diagnostics.Add(UnsupportedSemanticType(
                    ProjectionSourceKind.SemanticElement,
                    element.Id,
                    element.TypeId));
                continue;
            }

            var input = new ElementProjectionRuleInput(
                document.DocumentId,
                document.Revision,
                context,
                element,
                FindVisualStates(visualStatesBySemanticId, element.Id));
            InvokeRules(
                registrations,
                input,
                accumulator,
                diagnostics,
                cancellationToken);
        }

        foreach (var relationship in document.SemanticModel.Relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (document.SemanticModel.TryGetElement(
                    relationship.SourceId,
                    out var scopedSource) &&
                scopedSource is not null &&
                document.SemanticModel.TryGetElement(
                    relationship.TargetId,
                    out var scopedTarget) &&
                scopedTarget is not null &&
                (!IsInScope(document, scopedSource.Id, activeScopeId) ||
                    !IsInScope(document, scopedTarget.Id, activeScopeId)))
            {
                continue;
            }

            var registrations = _registry.Find(
                ProjectionSourceKind.SemanticRelationship,
                relationship.TypeId);
            if (registrations.IsEmpty)
            {
                diagnostics.Add(UnsupportedSemanticType(
                    ProjectionSourceKind.SemanticRelationship,
                    relationship.Id,
                    relationship.TypeId));
                continue;
            }

            if (!TryResolveRelationshipEndpoints(
                    document,
                    relationship,
                    diagnostics,
                    out var sourceElement,
                    out var targetElement))
            {
                continue;
            }

            if (!IsInScope(document, sourceElement.Id, activeScopeId) ||
                !IsInScope(document, targetElement.Id, activeScopeId))
            {
                continue;
            }

            var input = new RelationshipProjectionRuleInput(
                document.DocumentId,
                document.Revision,
                context,
                relationship,
                sourceElement,
                targetElement,
                FindVisualStates(visualStatesBySemanticId, relationship.Id));
            InvokeRules(
                registrations,
                input,
                accumulator,
                diagnostics,
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        FindDuplicateProjectedObjectIds(accumulator, diagnostics);

        if (HasErrors(diagnostics))
        {
            return ProjectionResult.Failure(
                document.DocumentId,
                document.Revision,
                diagnostics);
        }

        ProjectedGraph graph;
        try
        {
            graph = new ProjectedGraph(
                document.DocumentId,
                document.Revision,
                accumulator.Nodes,
                accumulator.Edges,
                accumulator.Groups,
                accumulator.Ports,
                accumulator.Labels);
        }
        catch (ArgumentException exception)
        {
            diagnostics.Add(Error(
                ProjectionDiagnosticCodes.InvalidGraph,
                "Projection contributions do not form a valid ProjectedGraph.",
                document.DocumentId.Value,
                new KeyValuePair<string, string>(
                    "ExceptionType",
                    exception.GetType().FullName ?? exception.GetType().Name)));
            return ProjectionResult.Failure(
                document.DocumentId,
                document.Revision,
                diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ProjectionResult.Success(graph, diagnostics);
    }

    private static bool IsInScope(
        DocumentSnapshot document,
        SemanticElementId semanticElementId,
        DocumentScopeId activeScopeId) =>
        document.SemanticModel.TryGetScope(semanticElementId, out var scope) &&
        scope?.Id == activeScopeId;

    private static bool ScopeExists(
        SemanticModelSnapshot semanticModel,
        DocumentScopeId scopeId) =>
        semanticModel.TryGetScope(scopeId, out _);

    private static Dictionary<SemanticElementId, List<VisualStateSnapshot>> BuildVisualStateIndex(
        DocumentSnapshot document,
        List<Diagnostic> diagnostics)
    {
        var knownSemanticIds = document.SemanticModel.Elements
            .Select(static element => element.Id)
            .Concat(document.SemanticModel.Relationships.Select(static relationship => relationship.Id))
            .ToHashSet();
        var index = new Dictionary<SemanticElementId, List<VisualStateSnapshot>>();

        foreach (var visualState in document.VisualModel.VisualStates)
        {
            if (!knownSemanticIds.Contains(visualState.SemanticElementId))
            {
                diagnostics.Add(Error(
                    ProjectionDiagnosticCodes.InvalidInput,
                    $"Visual state '{visualState.Id}' references a missing semantic source.",
                    visualState.Id.Value,
                    new KeyValuePair<string, string>(
                        "SemanticElementId",
                        visualState.SemanticElementId.Value)));
                continue;
            }

            if (!index.TryGetValue(visualState.SemanticElementId, out var visualStates))
            {
                visualStates = [];
                index.Add(visualState.SemanticElementId, visualStates);
            }

            visualStates.Add(visualState);
        }

        return index;
    }

    private static List<VisualStateSnapshot> FindVisualStates(
        Dictionary<SemanticElementId, List<VisualStateSnapshot>> index,
        SemanticElementId semanticElementId) =>
        index.TryGetValue(semanticElementId, out var visualStates) ? visualStates : [];

    private static bool TryResolveRelationshipEndpoints(
        DocumentSnapshot document,
        SemanticRelationshipSnapshot relationship,
        List<Diagnostic> diagnostics,
        out SemanticElementSnapshot sourceElement,
        out SemanticElementSnapshot targetElement)
    {
        var hasSource = document.SemanticModel.TryGetElement(
            relationship.SourceId,
            out var resolvedSource);
        var hasTarget = document.SemanticModel.TryGetElement(
            relationship.TargetId,
            out var resolvedTarget);

        if (!hasSource)
        {
            diagnostics.Add(MissingRelationshipEndpoint(
                relationship,
                relationship.SourceId,
                "Source"));
        }

        if (!hasTarget)
        {
            diagnostics.Add(MissingRelationshipEndpoint(
                relationship,
                relationship.TargetId,
                "Target"));
        }

        sourceElement = resolvedSource!;
        targetElement = resolvedTarget!;
        return hasSource && hasTarget;
    }

    private static void InvokeRules(
        IEnumerable<ProjectionRuleRegistration> registrations,
        ProjectionRuleInput input,
        ProjectionAccumulator accumulator,
        List<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ProjectionRuleResult? result;
#pragma warning disable CA1031 // Plugin policy faults are converted to deterministic Projection diagnostics.
            try
            {
                result = registration.Rule.Project(input, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsNonFatal(exception))
            {
                diagnostics.Add(RuleFailure(registration, input, exception));
                continue;
            }
#pragma warning restore CA1031

            cancellationToken.ThrowIfCancellationRequested();
            if (result is null)
            {
                diagnostics.Add(InvalidRuleResult(registration, input, "NullResult"));
                continue;
            }

            diagnostics.AddRange(result.Diagnostics);
            if (!result.Succeeded)
            {
                continue;
            }

            if (result.Contribution is null)
            {
                diagnostics.Add(InvalidRuleResult(registration, input, "MissingContribution"));
                continue;
            }

            if (!ValidateContribution(
                    registration,
                    input,
                    result.Contribution,
                    diagnostics))
            {
                continue;
            }

            accumulator.Add(result.Contribution);
        }
    }

    private static bool ValidateContribution(
        ProjectionRuleRegistration registration,
        ProjectionRuleInput input,
        ProjectionRuleContribution contribution,
        List<Diagnostic> diagnostics)
    {
        var isValid = true;
        isValid &= ValidateObjects(
            contribution.Nodes,
            ProjectedObjectKind.Node,
            registration,
            input,
            diagnostics);
        isValid &= ValidateObjects(
            contribution.Edges,
            ProjectedObjectKind.Edge,
            registration,
            input,
            diagnostics);
        isValid &= ValidateObjects(
            contribution.Groups,
            ProjectedObjectKind.Group,
            registration,
            input,
            diagnostics);
        isValid &= ValidateObjects(
            contribution.Ports,
            ProjectedObjectKind.Port,
            registration,
            input,
            diagnostics);
        isValid &= ValidateObjects(
            contribution.Labels,
            ProjectedObjectKind.Label,
            registration,
            input,
            diagnostics);
        return isValid;
    }

    private static bool ValidateObjects<T>(
        IEnumerable<T> projectedObjects,
        ProjectedObjectKind expectedKind,
        ProjectionRuleRegistration registration,
        ProjectionRuleInput input,
        List<Diagnostic> diagnostics)
        where T : IProjectedObject
    {
        var isValid = true;
        foreach (var projectedObject in projectedObjects)
        {
            var source = projectedObject.Source;
            var expectedId = ProjectedObjectIdentity.Create(source, expectedKind);
            var visualAssociationIsValid =
                source.VisualStateId is null ||
                input.VisualStates.Any(visualState => visualState.Id == source.VisualStateId);
            var objectIsValid =
                projectedObject.Kind == expectedKind &&
                projectedObject.Id == expectedId &&
                source.DocumentId == input.DocumentId &&
                source.RuleId == registration.RuleId &&
                source.SourceKind == input.SourceKind &&
                source.SemanticElementId == input.SemanticId &&
                source.SemanticTypeId == input.SemanticTypeId &&
                visualAssociationIsValid;

            if (objectIsValid)
            {
                continue;
            }

            isValid = false;
            diagnostics.Add(Error(
                ProjectionDiagnosticCodes.InvalidSourceTraceability,
                $"Projected object '{projectedObject.Id}' has invalid source traceability.",
                projectedObject.Id.Value,
                new KeyValuePair<string, string>(
                    "ExpectedRuleId",
                    registration.RuleId.Value),
                new KeyValuePair<string, string>(
                    "ExpectedSemanticElementId",
                    input.SemanticId.Value),
                new KeyValuePair<string, string>(
                    "ExpectedSemanticTypeId",
                    input.SemanticTypeId.Value),
                new KeyValuePair<string, string>(
                    "ProjectedObjectKind",
                    expectedKind.ToString())));
        }

        return isValid;
    }

    private static void FindDuplicateProjectedObjectIds(
        ProjectionAccumulator accumulator,
        List<Diagnostic> diagnostics)
    {
        var objects = accumulator.AllObjects
            .OrderBy(static projectedObject => projectedObject.Id.Value, StringComparer.Ordinal)
            .ThenBy(static projectedObject => projectedObject.Kind)
            .ToArray();

        for (var index = 1; index < objects.Length; index++)
        {
            if (objects[index - 1].Id != objects[index].Id)
            {
                continue;
            }

            if (index > 1 && objects[index - 2].Id == objects[index].Id)
            {
                continue;
            }

            diagnostics.Add(Error(
                ProjectionDiagnosticCodes.DuplicateProjectedObjectId,
                $"Projected object ID '{objects[index].Id}' occurs more than once.",
                objects[index].Id.Value));
        }
    }

    private static bool HasErrors(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static ProjectionResult Cancelled(DocumentSnapshot document) =>
        ProjectionResult.Cancelled(
            document.DocumentId,
            document.Revision,
            [
                new Diagnostic(
                    ProjectionDiagnosticCodes.Cancelled,
                    DiagnosticSeverity.Information,
                    "Projection was cancelled.",
                    document.DocumentId.Value,
                    [
                        new KeyValuePair<string, string>(
                            "SourceRevision",
                            document.Revision.ToString()),
                    ]),
            ]);

    private static Diagnostic UnsupportedSemanticType(
        ProjectionSourceKind sourceKind,
        SemanticElementId semanticElementId,
        SemanticTypeId semanticTypeId) =>
        Error(
            ProjectionDiagnosticCodes.UnsupportedSemanticType,
            $"No Projection rule supports semantic type '{semanticTypeId}'.",
            semanticElementId.Value,
            new KeyValuePair<string, string>("SemanticTypeId", semanticTypeId.Value),
            new KeyValuePair<string, string>("SourceKind", sourceKind.ToString()));

    private static Diagnostic MissingRelationshipEndpoint(
        SemanticRelationshipSnapshot relationship,
        SemanticElementId endpointId,
        string endpointRole) =>
        Error(
            ProjectionDiagnosticCodes.InvalidInput,
            $"Semantic relationship '{relationship.Id}' has a missing {endpointRole.ToLowerInvariant()} endpoint.",
            relationship.Id.Value,
            new KeyValuePair<string, string>("EndpointId", endpointId.Value),
            new KeyValuePair<string, string>("EndpointRole", endpointRole));

    private static Diagnostic RuleFailure(
        ProjectionRuleRegistration registration,
        ProjectionRuleInput input,
        Exception exception) =>
        Error(
            ProjectionDiagnosticCodes.RuleFailure,
            $"Projection rule '{registration.RuleId}' failed.",
            registration.RuleId.Value,
            new KeyValuePair<string, string>(
                "ExceptionType",
                exception.GetType().FullName ?? exception.GetType().Name),
            new KeyValuePair<string, string>("SemanticElementId", input.SemanticId.Value),
            new KeyValuePair<string, string>("SemanticTypeId", input.SemanticTypeId.Value));

    private static Diagnostic InvalidRuleResult(
        ProjectionRuleRegistration registration,
        ProjectionRuleInput input,
        string reason) =>
        Error(
            ProjectionDiagnosticCodes.InvalidRuleResult,
            $"Projection rule '{registration.RuleId}' returned an invalid result.",
            registration.RuleId.Value,
            new KeyValuePair<string, string>("Reason", reason),
            new KeyValuePair<string, string>("SemanticElementId", input.SemanticId.Value),
            new KeyValuePair<string, string>("SemanticTypeId", input.SemanticTypeId.Value));

    private static Diagnostic Error(
        string code,
        string message,
        string sourceIdentity,
        params KeyValuePair<string, string>[] context) =>
        new(code, DiagnosticSeverity.Error, message, sourceIdentity, context);

    private static bool IsNonFatal(Exception exception) =>
        exception is not OutOfMemoryException and
        not StackOverflowException and
        not AccessViolationException;

    private sealed class ProjectionAccumulator
    {
        internal List<ProjectedNode> Nodes { get; } = [];

        internal List<ProjectedEdge> Edges { get; } = [];

        internal List<ProjectedGroup> Groups { get; } = [];

        internal List<ProjectedPort> Ports { get; } = [];

        internal List<ProjectedLabel> Labels { get; } = [];

        internal IEnumerable<IProjectedObject> AllObjects =>
            Nodes.Cast<IProjectedObject>()
                .Concat(Edges)
                .Concat(Groups)
                .Concat(Ports)
                .Concat(Labels);

        internal void Add(ProjectionRuleContribution contribution)
        {
            Nodes.AddRange(contribution.Nodes);
            Edges.AddRange(contribution.Edges);
            Groups.AddRange(contribution.Groups);
            Ports.AddRange(contribution.Ports);
            Labels.AddRange(contribution.Labels);
        }
    }
}
