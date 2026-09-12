using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Properties;

public sealed class ElementPropertiesSchemaTests
{
    [Fact]
    public void FieldDefinitionIsImmutableEquatableAndKeepsUiAndSemanticIdentitiesSeparate()
    {
        var first = Field(
            "test:name",
            "Name",
            "domain:name",
            ElementPropertyEditorKind.SingleLineText,
            SemanticPropertyMutationKind.Name,
            isEditable: true,
            order: int.MinValue);
        var equal = Field(
            "test:name",
            "Name",
            "domain:name",
            ElementPropertyEditorKind.SingleLineText,
            SemanticPropertyMutationKind.Name,
            isEditable: true,
            order: int.MinValue);
        var different = Field(
            "test:name",
            "Name",
            "domain:description",
            ElementPropertyEditorKind.SingleLineText,
            SemanticPropertyMutationKind.Property,
            isEditable: true,
            order: int.MinValue);

        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(first, different);
        Assert.Equal("test:name", first.FieldId.Value);
        Assert.Equal("Name", first.DisplayName);
        Assert.Equal("domain:name", first.SemanticPropertyKey);
        Assert.Equal(ElementPropertyEditorKind.SingleLineText, first.EditorKind);
        Assert.Equal(SemanticPropertyMutationKind.Name, first.MutationKind);
        Assert.True(first.IsEditable);
        Assert.Equal(int.MinValue, first.Order);
        Assert.NotEqual(first.FieldId.Value, first.SemanticPropertyKey);
        Assert.All(
            typeof(ElementPropertyFieldDefinition).GetProperties(),
            static property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void FieldDefinitionRejectsInvalidDataAndUnsupportedEnumValues()
    {
        var fieldId = new ElementPropertyFieldId("test:field");

        Assert.Throws<ArgumentException>(() => new ElementPropertyFieldId(" "));
        Assert.Throws<ArgumentNullException>(() => new ElementPropertyFieldDefinition(
            null!,
            "Field",
            "domain:field",
            ElementPropertyEditorKind.SingleLineText,
            SemanticPropertyMutationKind.Property,
            true,
            0));
        Assert.Throws<ArgumentException>(() => new ElementPropertyFieldDefinition(
            fieldId,
            " ",
            "domain:field",
            ElementPropertyEditorKind.SingleLineText,
            SemanticPropertyMutationKind.Property,
            true,
            0));
        Assert.Throws<ArgumentException>(() => new ElementPropertyFieldDefinition(
            fieldId,
            "Field",
            " ",
            ElementPropertyEditorKind.SingleLineText,
            SemanticPropertyMutationKind.Property,
            true,
            0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ElementPropertyFieldDefinition(
            fieldId,
            "Field",
            "domain:field",
            (ElementPropertyEditorKind)int.MaxValue,
            SemanticPropertyMutationKind.Property,
            true,
            0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ElementPropertyFieldDefinition(
            fieldId,
            "Field",
            "domain:field",
            ElementPropertyEditorKind.SingleLineText,
            (SemanticPropertyMutationKind)int.MaxValue,
            true,
            0));
    }

    [Theory]
    [InlineData(ElementPropertyEditorKind.Integer)]
    [InlineData(ElementPropertyEditorKind.MultilineText)]
    [InlineData(ElementPropertyEditorKind.Boolean)]
    public void NameMutationRequiresSingleLineTextEditor(
        ElementPropertyEditorKind editorKind)
    {
        var exception = Assert.Throws<ArgumentException>(() => Field(
            "test:name",
            "Name",
            "domain:name",
            editorKind,
            SemanticPropertyMutationKind.Name,
            isEditable: true,
            order: 0));

        Assert.Equal("mutationKind", exception.ParamName);
    }

    [Fact]
    public void SchemaCopiesAndDeterministicallyOrdersFieldsWithoutRestrictingOrderValues()
    {
        var highestId = Field("test:z", "Z", "domain:z", order: -10);
        var lowerId = Field("test:a", "A", "domain:a", order: 20);
        var higherId = Field("test:b", "B", "domain:b", order: 20);
        var source = new List<ElementPropertyFieldDefinition>
        {
            higherId,
            highestId,
            lowerId,
        };

        var schema = new ElementPropertiesSchema(new SemanticTypeId("test:type"), source);
        source.Clear();

        Assert.Equal("test:type", schema.SemanticTypeId.Value);
        Assert.Equal([highestId, lowerId, higherId], schema.Fields.ToArray());
        Assert.All(
            typeof(ElementPropertiesSchema).GetProperties(),
            static property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void SchemaRejectsNullsDuplicateFieldIdsAndDuplicatePropertyBindings()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ElementPropertiesSchema(null!, []));
        Assert.Throws<ArgumentNullException>(() =>
            new ElementPropertiesSchema(new SemanticTypeId("test:type"), null!));
        Assert.Throws<ArgumentException>(() => new ElementPropertiesSchema(
            new SemanticTypeId("test:type"),
            [null!]));

        var duplicateId = Assert.Throws<ArgumentException>(() =>
            new ElementPropertiesSchema(
                new SemanticTypeId("test:type"),
                [
                    Field("test:duplicate", "A", "domain:a", order: 10),
                    Field("test:duplicate", "B", "domain:b", order: -10),
                ]));
        Assert.Contains("test:duplicate", duplicateId.Message, StringComparison.Ordinal);

        var duplicateProperty = Assert.Throws<ArgumentException>(() =>
            new ElementPropertiesSchema(
                new SemanticTypeId("test:type"),
                [
                    Field("test:a", "A", "domain:duplicate", order: 10),
                    Field("test:b", "B", "domain:duplicate", order: -10),
                ]));
        Assert.Contains("domain:duplicate", duplicateProperty.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogCopiesSortsAndLooksUpSchemasWithoutFallback()
    {
        var schemaB = new ElementPropertiesSchema(new SemanticTypeId("test:b"), []);
        var schemaA = new ElementPropertiesSchema(new SemanticTypeId("test:a"), []);
        var source = new List<ElementPropertiesSchema> { schemaB, schemaA };

        var catalog = new ElementPropertiesSchemaCatalog(source);
        source.Clear();

        Assert.Equal([schemaA, schemaB], catalog.Schemas.ToArray());
        Assert.True(catalog.TryGetSchema(schemaA.SemanticTypeId, out var resolved));
        Assert.Same(schemaA, resolved);
        Assert.False(catalog.TryGetSchema(new SemanticTypeId("test:unknown"), out resolved));
        Assert.Null(resolved);
        Assert.Empty(ElementPropertiesSchemaCatalog.Empty.Schemas);
        Assert.Throws<ArgumentNullException>(() => catalog.TryGetSchema(null!, out _));
    }

    [Fact]
    public void CatalogRejectsNullAndDuplicateSchemasDeterministically()
    {
        Assert.Throws<ArgumentException>(() =>
            new ElementPropertiesSchemaCatalog([null!]));

        var first = new ElementPropertiesSchema(new SemanticTypeId("test:duplicate"), []);
        var second = new ElementPropertiesSchema(new SemanticTypeId("test:duplicate"), []);
        var exception = Assert.Throws<ArgumentException>(() =>
            new ElementPropertiesSchemaCatalog([second, first]));

        Assert.Contains("test:duplicate", exception.Message, StringComparison.Ordinal);
    }

    private static ElementPropertyFieldDefinition Field(
        string fieldId,
        string displayName,
        string semanticPropertyKey,
        ElementPropertyEditorKind editorKind = ElementPropertyEditorKind.SingleLineText,
        SemanticPropertyMutationKind mutationKind = SemanticPropertyMutationKind.Property,
        bool isEditable = true,
        int order = 0) =>
        new(
            new ElementPropertyFieldId(fieldId),
            displayName,
            semanticPropertyKey,
            editorKind,
            mutationKind,
            isEditable,
            order);
}
