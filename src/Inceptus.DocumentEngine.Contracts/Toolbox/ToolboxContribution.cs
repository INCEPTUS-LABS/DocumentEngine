using System.Collections.Immutable;

namespace Inceptus.DocumentEngine.Contracts.Toolbox;

/// <summary>
/// One immutable, data-only Toolbox contribution supplied by an application or plugin.
/// </summary>
public sealed class ToolboxContribution
{
    public ToolboxContribution(
        IEnumerable<ToolboxGroupDefinition>? groups = null,
        IEnumerable<ToolboxItemDefinition>? items = null,
        IEnumerable<ToolboxSectionDefinition>? sections = null)
    {
        Sections = Copy(sections, nameof(sections));
        Groups = Copy(groups, nameof(groups));
        Items = Copy(items, nameof(items));
    }

    public static ToolboxContribution Empty { get; } = new();

    public ImmutableArray<ToolboxSectionDefinition> Sections { get; }

    public ImmutableArray<ToolboxGroupDefinition> Groups { get; }

    public ImmutableArray<ToolboxItemDefinition> Items { get; }

    private static ImmutableArray<T> Copy<T>(
        IEnumerable<T>? values,
        string parameterName)
        where T : class
    {
        if (values is null)
        {
            return [];
        }

        var copy = values.ToArray();
        if (Array.Exists(copy, static value => value is null))
        {
            throw new ArgumentException(
                "Toolbox contributions cannot contain null definitions.",
                parameterName);
        }

        return [.. copy];
    }
}
