using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.Contracts.Commands;

public sealed class CommandValidationResult : IEquatable<CommandValidationResult>
{
    public CommandValidationResult(
        DocumentId documentId,
        DocumentRevision revision,
        CommandTypeId commandTypeId,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        ArgumentNullException.ThrowIfNull(commandTypeId);

        DocumentId = documentId;
        Revision = revision;
        CommandTypeId = commandTypeId;
        Diagnostics = CopyAndOrderDiagnostics(diagnostics);
    }

    public DocumentId DocumentId { get; }

    public DocumentRevision Revision { get; }

    public CommandTypeId CommandTypeId { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool IsValid =>
        !Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    public bool Equals(CommandValidationResult? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        DocumentId == other.DocumentId &&
        Revision == other.Revision &&
        CommandTypeId == other.CommandTypeId &&
        DiagnosticSequenceEquals(Diagnostics, other.Diagnostics);

    public override bool Equals(object? obj) => Equals(obj as CommandValidationResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DocumentId);
        hash.Add(Revision);
        hash.Add(CommandTypeId);

        foreach (var diagnostic in Diagnostics)
        {
            AddDiagnosticHash(ref hash, diagnostic);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(CommandValidationResult? left, CommandValidationResult? right) =>
        EqualityComparer<CommandValidationResult>.Default.Equals(left, right);

    public static bool operator !=(CommandValidationResult? left, CommandValidationResult? right) =>
        !(left == right);

    private static ImmutableArray<Diagnostic> CopyAndOrderDiagnostics(
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
                "Command validation diagnostics cannot contain null values.",
                nameof(diagnostics));
        }

        Array.Sort(copy, DiagnosticStructuralComparer.Instance);
        return [.. copy];
    }

    private static bool DiagnosticSequenceEquals(
        ImmutableArray<Diagnostic> left,
        ImmutableArray<Diagnostic> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (DiagnosticStructuralComparer.Instance.Compare(left[index], right[index]) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddDiagnosticHash(ref HashCode hash, Diagnostic diagnostic)
    {
        hash.Add(diagnostic.Code, StringComparer.Ordinal);
        hash.Add(diagnostic.Severity);
        hash.Add(diagnostic.Message, StringComparer.Ordinal);
        hash.Add(diagnostic.SourceIdentity, StringComparer.Ordinal);

        foreach (var entry in diagnostic.Context)
        {
            hash.Add(entry.Key, StringComparer.Ordinal);
            hash.Add(entry.Value, StringComparer.Ordinal);
        }
    }

    private sealed class DiagnosticStructuralComparer : IComparer<Diagnostic>
    {
        public static DiagnosticStructuralComparer Instance { get; } = new();

        public int Compare(Diagnostic? left, Diagnostic? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var comparison = StringComparer.Ordinal.Compare(left.Code, right.Code);
            comparison = comparison != 0
                ? comparison
                : left.Severity.CompareTo(right.Severity);
            comparison = comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.Message, right.Message);
            comparison = comparison != 0
                ? comparison
                : StringComparer.Ordinal.Compare(left.SourceIdentity, right.SourceIdentity);

            if (comparison != 0)
            {
                return comparison;
            }

            using var leftContext = left.Context.GetEnumerator();
            using var rightContext = right.Context.GetEnumerator();

            while (leftContext.MoveNext())
            {
                if (!rightContext.MoveNext())
                {
                    return 1;
                }

                comparison = StringComparer.Ordinal.Compare(
                    leftContext.Current.Key,
                    rightContext.Current.Key);
                comparison = comparison != 0
                    ? comparison
                    : StringComparer.Ordinal.Compare(
                        leftContext.Current.Value,
                        rightContext.Current.Value);

                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return rightContext.MoveNext() ? -1 : 0;
        }
    }
}
