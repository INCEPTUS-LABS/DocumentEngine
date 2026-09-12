using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Contracts.Diagnostics;

public sealed class Diagnostic
{
    public Diagnostic(
        string code,
        DiagnosticSeverity severity,
        string message,
        string? sourceIdentity = null,
        IEnumerable<KeyValuePair<string, string>>? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity), severity, "The severity must be defined.");
        }

        if (sourceIdentity is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceIdentity);
        }

        Code = code;
        Severity = severity;
        Message = message;
        SourceIdentity = sourceIdentity;
        Context = CreateContext(context);
    }

    public string Code { get; }

    public DiagnosticSeverity Severity { get; }

    public string Message { get; }

    public string? SourceIdentity { get; }

    public ImmutableSortedDictionary<string, string> Context { get; }

    private static ImmutableSortedDictionary<string, string> CreateContext(
        IEnumerable<KeyValuePair<string, string>>? context)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        if (context is null)
        {
            return builder.ToImmutable();
        }

        foreach (var item in context)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Key, nameof(context));
            ArgumentNullException.ThrowIfNull(item.Value, nameof(context));
            builder.Add(item.Key, item.Value);
        }

        return builder.ToImmutable();
    }
}

