using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Runtime.History;

internal sealed class HistoryMutationPreparationResult
{
    private HistoryMutationPreparationResult(
        bool succeeded,
        PreparedHistoryMutation? mutation,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Succeeded = succeeded;
        Mutation = mutation;
        Diagnostics = diagnostics is null ? [] : [.. diagnostics];
    }

    internal bool Succeeded { get; }

    internal PreparedHistoryMutation? Mutation { get; }

    internal ImmutableArray<Diagnostic> Diagnostics { get; }

    internal static HistoryMutationPreparationResult Success(
        PreparedHistoryMutation? mutation = null,
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(true, mutation, diagnostics);

    internal static HistoryMutationPreparationResult Failure(
        IEnumerable<Diagnostic> diagnostics) =>
        new(false, null, diagnostics);
}
