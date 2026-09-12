using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseL51SemanticNameArchitectureTests
{
    private static readonly Type[] ConstructorParameterTypes =
    [
        typeof(DocumentId),
        typeof(DocumentRevision),
        typeof(SemanticElementId),
        typeof(string),
        typeof(string),
    ];

    private static readonly string[] ForbiddenOperationFragments =
        ["Execute", "Mutate", "Undo", "Redo"];

    [Fact]
    public void SemanticNameCommandHasOnlyTheApprovedImmutableDescription()
    {
        var type = typeof(UpdateSemanticElementNameCommand);
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(UpdateSemanticElementNameCommand.TypeId)] = typeof(CommandTypeId),
            [nameof(UpdateSemanticElementNameCommand.TargetDocumentId)] = typeof(DocumentId),
            [nameof(UpdateSemanticElementNameCommand.ExpectedRevision)] = typeof(DocumentRevision),
            [nameof(UpdateSemanticElementNameCommand.Category)] = typeof(CommandCategory),
            [nameof(UpdateSemanticElementNameCommand.AffectedComponents)] =
                typeof(AuthoritativeDocumentComponent),
            [nameof(UpdateSemanticElementNameCommand.TargetSemanticElementId)] =
                typeof(SemanticElementId),
            [nameof(UpdateSemanticElementNameCommand.NamePropertyKey)] = typeof(string),
            [nameof(UpdateSemanticElementNameCommand.TargetName)] = typeof(string),
        };

        Assert.True(type.IsSealed);
        Assert.Contains(typeof(ICommand), type.GetInterfaces());
        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .ToDictionary(property => property.Name, property => property.PropertyType)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Equal(
            ConstructorParameterTypes,
            Assert.Single(type.GetConstructors()).GetParameters()
                .Select(parameter => parameter.ParameterType));
        Assert.Equal(CommandCategory.Semantic, Create().Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.SemanticModel,
            Create().AffectedComponents);
    }

    [Fact]
    public void SemanticNameCommandHasNoPresentationVisualOrMutationDependency()
    {
        var signatureTypes = typeof(UpdateSemanticElementNameCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(property => property.PropertyType)
            .Concat(Assert.Single(typeof(UpdateSemanticElementNameCommand).GetConstructors())
                .GetParameters()
                .Select(parameter => parameter.ParameterType))
            .ToArray();

        Assert.DoesNotContain(signatureTypes, type =>
            type.Namespace?.Contains("Blazor", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("Canvas2D", StringComparison.Ordinal) == true ||
            type.Namespace?.Contains("Visuals", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            typeof(UpdateSemanticElementNameCommand).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => !method.IsSpecialName &&
                ForbiddenOperationFragments.Any(fragment =>
                    method.Name.Contains(fragment, StringComparison.Ordinal)));
    }

    private static UpdateSemanticElementNameCommand Create() =>
        new(
            new DocumentId("test:architecture"),
            DocumentRevision.Zero,
            new SemanticElementId("test:element"),
            "test:name",
            "Name");
}
