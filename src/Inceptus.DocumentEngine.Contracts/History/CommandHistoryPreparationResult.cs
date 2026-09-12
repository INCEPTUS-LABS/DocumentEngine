using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;

namespace Inceptus.DocumentEngine.Contracts.History;

/// <summary>
/// Immutable output of a Command History policy.
/// </summary>
public sealed class CommandHistoryPreparationResult
{
    private CommandHistoryPreparationResult(
        bool succeeded,
        HistoryRecordingBehavior behavior,
        IHistoryCommandFactory? undoFactory,
        IHistoryCommandFactory? redoFactory,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Succeeded = succeeded;
        Behavior = behavior;
        UndoFactory = undoFactory;
        RedoFactory = redoFactory;
        Diagnostics = CopyDiagnostics(diagnostics);
    }

    public bool Succeeded { get; }

    public HistoryRecordingBehavior Behavior { get; }

    public IHistoryCommandFactory? UndoFactory { get; }

    public IHistoryCommandFactory? RedoFactory { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public static CommandHistoryPreparationResult Undoable(
        IHistoryCommandFactory undoFactory,
        IHistoryCommandFactory redoFactory,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(undoFactory);
        ArgumentNullException.ThrowIfNull(redoFactory);
        return new(true, HistoryRecordingBehavior.Undoable, undoFactory, redoFactory, diagnostics);
    }

    public static CommandHistoryPreparationResult NotUndoable(
        IEnumerable<Diagnostic>? diagnostics = null) =>
        new(true, HistoryRecordingBehavior.NotUndoable, null, null, diagnostics);

    public static CommandHistoryPreparationResult Failure(
        IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(false, HistoryRecordingBehavior.NotUndoable, null, null, diagnostics);
    }

    private static ImmutableArray<Diagnostic> CopyDiagnostics(
        IEnumerable<Diagnostic>? diagnostics)
    {
        if (diagnostics is null)
        {
            return [];
        }

        var copy = diagnostics.ToArray();
        if (Array.Exists(copy, static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "History diagnostics cannot contain null values.",
                nameof(diagnostics));
        }

        Array.Sort(copy, static (left, right) =>
        {
            var code = StringComparer.Ordinal.Compare(left.Code, right.Code);
            if (code != 0)
            {
                return code;
            }

            var source = StringComparer.Ordinal.Compare(
                left.SourceIdentity ?? string.Empty,
                right.SourceIdentity ?? string.Empty);
            return source != 0
                ? source
                : StringComparer.Ordinal.Compare(left.Message, right.Message);
        });
        return [.. copy];
    }
}
