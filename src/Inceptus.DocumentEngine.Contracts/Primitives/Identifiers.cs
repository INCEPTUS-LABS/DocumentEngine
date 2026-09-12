namespace Inceptus.DocumentEngine.Contracts.Primitives;

public sealed record DocumentId
{
    public DocumentId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies one notation-neutral semantic scope within a Document.
/// </summary>
public sealed record DocumentScopeId
{
    public DocumentScopeId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies one optional notation-neutral model profile independently of its
/// display name or the plugin that contributes its definition.
/// </summary>
public sealed record ModelProfileId
{
    public ModelProfileId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies a semantic object, including either an element or a relationship.
/// </summary>
public sealed record SemanticElementId
{
    public SemanticElementId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies a semantic type independently of CLR and assembly type names.
/// </summary>
public sealed record SemanticTypeId
{
    public SemanticTypeId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies one Toolbox section independently of its display name or registration order.
/// </summary>
public sealed record ToolboxSectionId
{
    public ToolboxSectionId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies one Toolbox group independently of its display name or registration order.
/// </summary>
public sealed record ToolboxGroupId
{
    public ToolboxGroupId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies one Toolbox creation option independently of its semantic element type.
/// </summary>
public sealed record ToolboxItemId
{
    public ToolboxItemId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies one field in an element Properties schema independently of its
/// domain-owned Semantic property key.
/// </summary>
public sealed record ElementPropertyFieldId
{
    public ElementPropertyFieldId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record VisualStateId
{
    public VisualStateId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies one persistent connector anchor owned by a Visual State.
/// </summary>
public sealed record ConnectorAnchorId
{
    public ConnectorAnchorId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies one immutable connector-anchor definition supplied by an element type policy.
/// </summary>
public sealed record PredefinedConnectorAnchorDefinitionId
{
    public PredefinedConnectorAnchorDefinitionId(string value) =>
        Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record ProjectedObjectId
{
    public ProjectedObjectId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies a projection rule independently of its CLR or assembly type name.
/// </summary>
public sealed record ProjectionRuleId
{
    public ProjectionRuleId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SceneObjectId
{
    public SceneObjectId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record AlgorithmId
{
    public AlgorithmId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies a Command independently of its CLR or assembly type name.
/// </summary>
public sealed record CommandTypeId
{
    public CommandTypeId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Identifies a registered Command validator independently of its CLR or assembly type name.
/// </summary>
public sealed record CommandValidatorId
{
    public CommandValidatorId(string value) => Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}

internal static class IdentifierValue
{
    public static string Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
