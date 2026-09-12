using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN82MixedHistoryArchitectureTests
{
    [Fact]
    public void EditingSessionOwnsOneGlobalHistoryAndNoScopeKeyedHistoryStore()
    {
        var fields = typeof(EditingSession).GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.Single(fields, field => field.FieldType == typeof(HistoryManager));
        Assert.DoesNotContain(fields, field =>
            IsScopeKeyed(field.FieldType) && ContainsHistoryType(field.FieldType));

        var stateProperties = typeof(HistoryState).GetProperties(
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Single(stateProperties, property => property.Name == "Entries");
        Assert.Single(stateProperties, property => property.Name == "Cursor");
    }

    [Fact]
    public void ScopeNavigationEntryIsNotationNeutralRuntimeIdentityNotCommand()
    {
        var entryType = typeof(ScopeNavigationHistoryEntry);
        var properties = entryType.GetProperties(
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(property => property.PropertyType == typeof(DocumentScopeId))
            .ToArray();

        Assert.Equal(
            ["FromScopeId", "ToScopeId"],
            properties.Select(static property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.All(properties, property =>
            Assert.Equal(typeof(DocumentScopeId), property.PropertyType));
        Assert.False(typeof(ICommand).IsAssignableFrom(entryType));
        Assert.DoesNotContain(
            entryType.Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.Contains("Bpmn", StringComparison.Ordinal) == true);
    }

    private static bool IsScopeKeyed(Type type) =>
        type.IsGenericType &&
        type.GetGenericArguments().Contains(typeof(DocumentScopeId));

    private static bool ContainsHistoryType(Type type) =>
        type.GetGenericArguments().Any(argument =>
            argument == typeof(HistoryManager) ||
            argument.Namespace?.Contains(".History", StringComparison.Ordinal) == true);
}
