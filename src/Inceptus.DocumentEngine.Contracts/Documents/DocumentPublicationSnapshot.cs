using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Inceptus.DocumentEngine.Contracts.Documents;

/// <summary>
/// Immutable, notation-neutral Publication metadata owned by a Document.
/// </summary>
public sealed record DocumentPublicationSnapshot
{
    private static readonly Regex CodePattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public DocumentPublicationSnapshot(
        string code,
        string title,
        string? description = null)
    {
        if (!TryNormalize(
                code,
                title,
                description,
                out var normalizedCode,
                out var normalizedTitle,
                out var normalizedDescription))
        {
            throw new ArgumentException(
                "Publication requires a valid Code and a non-empty Title.",
                nameof(code));
        }

        Code = normalizedCode;
        Title = normalizedTitle;
        Description = normalizedDescription;
    }

    public string Code { get; }

    public string Title { get; }

    public string Description { get; }

    public static bool TryCreate(
        string? code,
        string? title,
        string? description,
        [NotNullWhen(true)] out DocumentPublicationSnapshot? publication)
    {
        if (!TryNormalize(
                code,
                title,
                description,
                out var normalizedCode,
                out var normalizedTitle,
                out var normalizedDescription))
        {
            publication = null;
            return false;
        }

        publication = new DocumentPublicationSnapshot(
            normalizedCode,
            normalizedTitle,
            normalizedDescription,
            normalized: true);
        return true;
    }

    public static bool IsValidCode(string? code) =>
        code is not null && CodePattern.IsMatch(code.Trim());

    private DocumentPublicationSnapshot(
        string code,
        string title,
        string description,
        bool normalized)
    {
        _ = normalized;
        Code = code;
        Title = title;
        Description = description;
    }

    private static bool TryNormalize(
        string? code,
        string? title,
        string? description,
        [NotNullWhen(true)] out string? normalizedCode,
        [NotNullWhen(true)] out string? normalizedTitle,
        [NotNullWhen(true)] out string? normalizedDescription)
    {
        normalizedCode = code?.Trim();
        normalizedTitle = title?.Trim();
        normalizedDescription = description?.Trim() ?? string.Empty;
        return normalizedCode is not null &&
            normalizedTitle is not null &&
            CodePattern.IsMatch(normalizedCode) &&
            normalizedTitle.Length > 0;
    }
}
