using Inceptus.DocumentEngine.Contracts.Primitives;

namespace Inceptus.DocumentEngine.UnitTests.Primitives;

public sealed class IdentifierTests
{
    [Fact]
    public void IdentifiersUseTypeSpecificOrdinalValueEquality()
    {
        Func<string, object>[] factories =
        [
            value => new DocumentId(value),
            value => new DocumentScopeId(value),
            value => new SemanticElementId(value),
            value => new SemanticTypeId(value),
            value => new ToolboxGroupId(value),
            value => new ToolboxItemId(value),
            value => new VisualStateId(value),
            value => new ConnectorAnchorId(value),
            value => new PredefinedConnectorAnchorDefinitionId(value),
            value => new ProjectedObjectId(value),
            value => new ProjectionRuleId(value),
            value => new SceneObjectId(value),
            value => new AlgorithmId(value),
            value => new CommandTypeId(value),
            value => new CommandValidatorId(value),
        ];

        foreach (var factory in factories)
        {
            var first = factory("stable-id");
            var same = factory("stable-id");
            var different = factory("other-id");
            var differentCase = factory("STABLE-ID");

            Assert.Equal(first, same);
            Assert.Equal(first.GetHashCode(), same.GetHashCode());
            Assert.NotEqual(first, different);
            Assert.NotEqual(first, differentCase);
            Assert.Equal("stable-id", first.ToString());
        }

        Assert.NotEqual<object>(new DocumentId("stable-id"), new SemanticElementId("stable-id"));
        Assert.NotEqual<object>(new DocumentId("stable-id"), new DocumentScopeId("stable-id"));
        Assert.NotEqual<object>(new ToolboxItemId("stable-id"), new SemanticTypeId("stable-id"));
    }

    [Fact]
    public void IdentifiersRejectNullEmptyAndWhitespaceValues()
    {
        Func<string, object>[] factories =
        [
            value => new DocumentId(value),
            value => new DocumentScopeId(value),
            value => new SemanticElementId(value),
            value => new SemanticTypeId(value),
            value => new ToolboxGroupId(value),
            value => new ToolboxItemId(value),
            value => new VisualStateId(value),
            value => new ConnectorAnchorId(value),
            value => new PredefinedConnectorAnchorDefinitionId(value),
            value => new ProjectedObjectId(value),
            value => new ProjectionRuleId(value),
            value => new SceneObjectId(value),
            value => new AlgorithmId(value),
            value => new CommandTypeId(value),
            value => new CommandValidatorId(value),
        ];

        foreach (var factory in factories)
        {
            Assert.Throws<ArgumentNullException>(() => factory(null!));
            Assert.Throws<ArgumentException>(() => factory(string.Empty));
            Assert.Throws<ArgumentException>(() => factory("   "));
        }
    }

    [Fact]
    public void IdentifierStringRepresentationPreservesTheSuppliedValue()
    {
        const string supplied = "  namespace:element/42  ";

        var identifier = new SemanticElementId(supplied);

        Assert.Equal(supplied, identifier.Value);
        Assert.Equal(supplied, identifier.ToString());
        Assert.Equal(identifier, new SemanticElementId(identifier.ToString()));
    }

    [Fact]
    public void DocumentScopeIdentityHasAStableStringRoundTrip()
    {
        const string supplied = "scope:child/A";

        var identifier = new DocumentScopeId(supplied);

        Assert.Equal(supplied, identifier.Value);
        Assert.Equal(supplied, identifier.ToString());
        Assert.Equal(identifier, new DocumentScopeId(identifier.ToString()));
    }
}
