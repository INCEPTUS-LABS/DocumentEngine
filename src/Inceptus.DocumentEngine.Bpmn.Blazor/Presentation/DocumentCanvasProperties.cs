using System.Collections.Immutable;
using System.Globalization;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Deletion;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Microsoft.Extensions.Localization;

namespace Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;

internal enum DocumentCanvasContextMenuKind
{
    Element,
    Background,
}

internal sealed record DocumentCanvasContextMenuState(
    DocumentCanvasContextMenuKind Kind,
    PointD CssPosition,
    VisualStateId? TargetVisualStateId,
    SemanticElementId? TargetSemanticElementId,
    SceneObjectId? TargetSceneObjectId,
    Canvas2DConnectorRouteContextAction? ConnectorRouteAction,
    Canvas2DConnectorAnchorContextAction? ConnectorAnchorAction,
    Canvas2DNodeLabelContextAction? NodeLabelAction,
    DocumentCanvasDeletionContextAction? DeletionAction,
    DocumentRevision DocumentRevision,
    EditingSessionGeneration SessionGeneration,
    Canvas2DScene SourceScene,
    DocumentScopeId ScopeId,
    DocumentCanvasScopeNavigationContextAction? ScopeNavigationAction,
    ImmutableArray<CanvasBackgroundActionDefinition> BackgroundActions,
    ImmutableArray<SemanticSceneViewActionDefinition> SemanticViewActions,
    ImmutableArray<SemanticSceneCommandActionDefinition> SemanticCommandActions,
    DocumentScopeId InteractionScopeId,
    Canvas2DSpatialRegion? TargetPresentation)
{
    private const double HorizontalInset = 8d;
    private const double VerticalInset = 8d;
    private const double ExpectedWidth = 240d;
    private const double SingleActionExpectedHeight = 44d;
    private const double AdditionalActionExpectedHeight = 36d;

    internal static DocumentCanvasContextMenuState CreateClamped(
        PointD cssPosition,
        Canvas2DSurfaceSize surfaceSize,
        VisualStateId targetVisualStateId,
        SceneObjectId targetSceneObjectId,
        Canvas2DConnectorRouteContextAction? connectorRouteAction,
        DocumentRevision documentRevision,
        EditingSessionGeneration sessionGeneration,
        Canvas2DScene sourceScene,
        Canvas2DConnectorAnchorContextAction? connectorAnchorAction = null,
        Canvas2DNodeLabelContextAction? nodeLabelAction = null,
        DocumentCanvasDeletionContextAction? deletionAction = null,
        DocumentCanvasScopeNavigationContextAction? scopeNavigationAction = null,
        DocumentScopeId? scopeId = null,
        DocumentScopeId? interactionScopeId = null,
        Canvas2DSpatialRegion? targetPresentation = null)
    {
        ArgumentNullException.ThrowIfNull(targetVisualStateId);
        ArgumentNullException.ThrowIfNull(targetSceneObjectId);
        ArgumentNullException.ThrowIfNull(sourceScene);
        var maximumX = Math.Max(HorizontalInset, surfaceSize.CssWidth - ExpectedWidth - HorizontalInset);
        var actionCount = 1 + (connectorRouteAction is null ? 0 : 1) +
            AnchorActionCount(connectorAnchorAction) +
            (nodeLabelAction is null ? 0 : 1) +
            (deletionAction is null ? 0 : 1) +
            (scopeNavigationAction is null ? 0 : 1);
        var expectedHeight = SingleActionExpectedHeight +
            ((actionCount - 1) * AdditionalActionExpectedHeight);
        var maximumY = Math.Max(
            VerticalInset,
            surfaceSize.CssHeight - expectedHeight - VerticalInset);
        return new DocumentCanvasContextMenuState(
            DocumentCanvasContextMenuKind.Element,
            new PointD(
                Math.Clamp(cssPosition.X, HorizontalInset, maximumX),
                Math.Clamp(cssPosition.Y, VerticalInset, maximumY)),
            targetVisualStateId,
            null,
            targetSceneObjectId,
            connectorRouteAction,
            connectorAnchorAction,
            nodeLabelAction,
            deletionAction,
            documentRevision,
            sessionGeneration,
            sourceScene,
            scopeId ?? new DocumentScopeId(sourceScene.DocumentId.Value),
            scopeNavigationAction,
            [],
            [],
            [],
            interactionScopeId ?? scopeId ?? new DocumentScopeId(sourceScene.DocumentId.Value),
            targetPresentation);
    }

    internal static DocumentCanvasContextMenuState CreateBackgroundClamped(
        PointD cssPosition,
        Canvas2DSurfaceSize surfaceSize,
        DocumentRevision documentRevision,
        EditingSessionGeneration sessionGeneration,
        Canvas2DScene sourceScene,
        DocumentScopeId scopeId,
        IEnumerable<CanvasBackgroundActionDefinition>? backgroundActions = null)
    {
        ArgumentNullException.ThrowIfNull(sourceScene);
        ArgumentNullException.ThrowIfNull(scopeId);
        var actions = backgroundActions?.ToImmutableArray() ?? [];
        if (actions.Any(static action => action is null))
        {
            throw new ArgumentException(
                "Background menu actions cannot contain null values.",
                nameof(backgroundActions));
        }

        var maximumX = Math.Max(
            HorizontalInset,
            surfaceSize.CssWidth - ExpectedWidth - HorizontalInset);
        var groupCount = actions.Select(static action => action.GroupLabel)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var actionCount = 1 + groupCount + actions.Length;
        var expectedHeight = SingleActionExpectedHeight +
            ((actionCount - 1) * AdditionalActionExpectedHeight);
        var maximumY = Math.Max(
            VerticalInset,
            surfaceSize.CssHeight - expectedHeight - VerticalInset);
        return new DocumentCanvasContextMenuState(
            DocumentCanvasContextMenuKind.Background,
            new PointD(
                Math.Clamp(cssPosition.X, HorizontalInset, maximumX),
                Math.Clamp(cssPosition.Y, VerticalInset, maximumY)),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            documentRevision,
            sessionGeneration,
            sourceScene,
            scopeId,
            null,
            actions,
            [],
            [],
            scopeId,
            null);
    }

    internal static DocumentCanvasContextMenuState CreateSemanticClamped(
        PointD cssPosition,
        Canvas2DSurfaceSize surfaceSize,
        SemanticElementId targetSemanticElementId,
        SceneObjectId targetSceneObjectId,
        DocumentCanvasDeletionContextAction? deletionAction,
        DocumentRevision documentRevision,
        EditingSessionGeneration sessionGeneration,
        Canvas2DScene sourceScene,
        DocumentScopeId scopeId,
        IEnumerable<SemanticSceneViewActionDefinition>? semanticViewActions = null,
        IEnumerable<SemanticSceneCommandActionDefinition>? semanticCommandActions = null,
        Canvas2DSpatialRegion? targetPresentation = null)
    {
        ArgumentNullException.ThrowIfNull(targetSemanticElementId);
        ArgumentNullException.ThrowIfNull(targetSceneObjectId);
        ArgumentNullException.ThrowIfNull(sourceScene);
        ArgumentNullException.ThrowIfNull(scopeId);
        var actions = semanticViewActions?.ToImmutableArray() ?? [];
        var commandActions = semanticCommandActions?.ToImmutableArray() ?? [];
        if (actions.Any(static action => action is null))
        {
            throw new ArgumentException(
                "Semantic Scene View actions cannot contain null values.",
                nameof(semanticViewActions));
        }

        if (commandActions.Any(static action => action is null))
        {
            throw new ArgumentException(
                "Semantic Scene command actions cannot contain null values.",
                nameof(semanticCommandActions));
        }

        var actionCount = 1 + (deletionAction is null ? 0 : 1) +
            actions.Length + commandActions.Length;
        var maximumX = Math.Max(
            HorizontalInset,
            surfaceSize.CssWidth - ExpectedWidth - HorizontalInset);
        var expectedHeight = SingleActionExpectedHeight +
            ((actionCount - 1) * AdditionalActionExpectedHeight);
        var maximumY = Math.Max(
            VerticalInset,
            surfaceSize.CssHeight - expectedHeight - VerticalInset);
        return new DocumentCanvasContextMenuState(
            DocumentCanvasContextMenuKind.Element,
            new PointD(
                Math.Clamp(cssPosition.X, HorizontalInset, maximumX),
                Math.Clamp(cssPosition.Y, VerticalInset, maximumY)),
            null,
            targetSemanticElementId,
            targetSceneObjectId,
            null,
            null,
            null,
            deletionAction,
            documentRevision,
            sessionGeneration,
            sourceScene,
            scopeId,
            null,
            [],
            actions,
            commandActions,
            scopeId,
            targetPresentation);
    }

    private static int AnchorActionCount(
        Canvas2DConnectorAnchorContextAction? action) => action switch
        {
            { Kind: Canvas2DConnectorAnchorContextActionKind.AddAnchor } =>
                (action.CanAdd(ConnectorAnchorRole.Source) ? 1 : 0) +
                (action.CanAdd(ConnectorAnchorRole.Target) ? 1 : 0),
            {
                Kind: Canvas2DConnectorAnchorContextActionKind.DeleteAnchor,
                CanDelete: true,
            } => 1,
            _ => 0,
        };
}

internal sealed record DocumentCanvasDeletionContextAction(
    DiagramDeletionId DeletionId,
    DiagramDeletionTargetKind TargetKind,
    SemanticElementId SemanticId,
    VisualStateId? VisualStateId);

internal sealed record DocumentCanvasDataPropertySnapshot(
    ElementPropertyFieldDefinition Definition,
    PropertyValue? Value,
    string EditorValue,
    bool IsAvailable)
{
    internal ElementPropertyFieldId FieldId => Definition.FieldId;

    internal bool CanEdit => Definition.IsEditable && IsAvailable;
}

internal sealed record DocumentCanvasPropertySnapshot(
    DocumentId DocumentId,
    DocumentRevision Revision,
    VisualStateId? VisualStateId,
    SemanticElementId SemanticId,
    SemanticTypeId TypeId,
    ImmutableArray<DocumentCanvasDataPropertySnapshot> DataFields,
    VisualPlacementMode PlacementMode,
    RectD Bounds,
    bool IsConnector,
    SemanticElementId? SourceId,
    SemanticElementId? TargetId,
    ConnectorLabelPlacement? LabelPlacement,
    bool IsBoundaryAttached = false,
    DocumentScopeId? InteractionScopeId = null,
    SceneObjectId? TargetSceneObjectId = null,
    Canvas2DSpatialRegion? SpatialRegion = null,
    EditingSessionGeneration? SessionGeneration = null,
    Canvas2DScene? SourceScene = null)
{
    internal bool CanEditBounds => VisualStateId is not null &&
        !IsConnector && !IsBoundaryAttached;

    internal static bool TryCreate(
        DocumentSnapshot document,
        VisualStateId visualStateId,
        ElementPropertiesSchemaCatalog schemaCatalog,
        out DocumentCanvasPropertySnapshot? snapshot,
        DocumentScopeId? interactionScopeId = null,
        SceneObjectId? targetSceneObjectId = null,
        Canvas2DSpatialRegion? spatialRegion = null,
        EditingSessionGeneration? sessionGeneration = null,
        Canvas2DScene? sourceScene = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(visualStateId);
        ArgumentNullException.ThrowIfNull(schemaCatalog);
        snapshot = null;
        if (!document.VisualModel.TryGetVisualState(visualStateId, out var visualState) ||
            visualState is null)
        {
            return false;
        }

        SemanticTypeId typeId;
        PropertyMap semanticProperties;
        var isConnector = false;
        SemanticElementId? sourceId = null;
        SemanticElementId? targetId = null;
        ConnectorLabelPlacement? labelPlacement = null;
        if (document.SemanticModel.TryGetElement(
                visualState.SemanticElementId,
                out var element) &&
            element is not null)
        {
            typeId = element.TypeId;
            semanticProperties = element.Properties;
        }
        else if (document.SemanticModel.TryGetRelationship(
                     visualState.SemanticElementId,
                     out var relationship) &&
                 relationship is not null)
        {
            typeId = relationship.TypeId;
            semanticProperties = relationship.Properties;
            isConnector = true;
            sourceId = relationship.SourceId;
            targetId = relationship.TargetId;
            labelPlacement = ConnectorLabelPlacement.Resolve(visualState.Properties);
        }
        else
        {
            return false;
        }

        var dataFields = schemaCatalog.TryGetSchema(typeId, out var schema)
            ? schema.Fields
                .Select(field => CreateDataProperty(field, semanticProperties, isConnector))
                .ToImmutableArray()
            : [];

        snapshot = new DocumentCanvasPropertySnapshot(
            document.DocumentId,
            document.Revision,
            visualState.Id,
            visualState.SemanticElementId,
            typeId,
            dataFields,
            visualState.PlacementMode,
            new RectD(
                visualState.Position.X,
                visualState.Position.Y,
                visualState.Size.Width,
                visualState.Size.Height),
            isConnector,
            sourceId,
            targetId,
            labelPlacement,
            visualState.BoundaryAttachment is not null,
            interactionScopeId,
            targetSceneObjectId,
            spatialRegion,
            sessionGeneration,
            sourceScene);
        return true;
    }

    internal static bool TryCreateSemantic(
        DocumentSnapshot document,
        SemanticElementId semanticElementId,
        RectD presentationBounds,
        ElementPropertiesSchemaCatalog schemaCatalog,
        out DocumentCanvasPropertySnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentNullException.ThrowIfNull(schemaCatalog);
        snapshot = null;
        if (!document.SemanticModel.TryGetElement(semanticElementId, out var element) ||
            element is null)
        {
            return false;
        }

        var dataFields = schemaCatalog.TryGetSchema(element.TypeId, out var schema)
            ? schema.Fields
                .Select(field => CreateDataProperty(
                    field,
                    element.Properties,
                    isConnector: false,
                    allowMissingText: true))
                .ToImmutableArray()
            : [];
        snapshot = new DocumentCanvasPropertySnapshot(
            document.DocumentId,
            document.Revision,
            null,
            element.Id,
            element.TypeId,
            dataFields,
            VisualPlacementMode.Automatic,
            presentationBounds,
            IsConnector: false,
            SourceId: null,
            TargetId: null,
            LabelPlacement: null);
        return true;
    }

    internal bool TryGetDataField(
        ElementPropertyFieldId fieldId,
        out DocumentCanvasDataPropertySnapshot? field)
    {
        ArgumentNullException.ThrowIfNull(fieldId);
        field = DataFields.FirstOrDefault(candidate => candidate.FieldId == fieldId);
        return field is not null;
    }

    private static DocumentCanvasDataPropertySnapshot CreateDataProperty(
        ElementPropertyFieldDefinition definition,
        PropertyMap semanticProperties,
        bool isConnector,
        bool allowMissingText = false)
    {
        var expectedKind = definition.EditorKind switch
        {
            ElementPropertyEditorKind.SingleLineText or
                ElementPropertyEditorKind.MultilineText => PropertyValueKind.Text,
            ElementPropertyEditorKind.Integer => PropertyValueKind.Integer,
            ElementPropertyEditorKind.Boolean => PropertyValueKind.Boolean,
            _ => throw new ArgumentOutOfRangeException(
                nameof(definition),
                definition.EditorKind,
                "The element Properties editor kind is unsupported."),
        };
        var isAvailable = semanticProperties.TryGetValue(
                definition.SemanticPropertyKey,
                out var value) &&
            value is not null &&
            value.Kind == expectedKind &&
            (!isConnector ||
             definition.MutationKind == SemanticPropertyMutationKind.Property) &&
            (definition.MutationKind != SemanticPropertyMutationKind.Name ||
              !string.IsNullOrWhiteSpace(value.TextValue));
        if (!isAvailable && allowMissingText && !isConnector &&
            expectedKind == PropertyValueKind.Text &&
            definition.MutationKind != SemanticPropertyMutationKind.Name)
        {
            value = PropertyValue.FromText(string.Empty);
            isAvailable = true;
        }

        if (!isAvailable)
        {
            return new DocumentCanvasDataPropertySnapshot(
                definition,
                null,
                string.Empty,
                IsAvailable: false);
        }

        var editorValue = value!.Kind switch
        {
            PropertyValueKind.Text => value.TextValue,
            PropertyValueKind.Integer => value.IntegerValue.ToString(
                CultureInfo.InvariantCulture),
            PropertyValueKind.Boolean => value.BooleanValue ? "true" : "false",
            _ => throw new InvalidOperationException(
                "A supported element Properties field resolved an incompatible value kind."),
        };
        return new DocumentCanvasDataPropertySnapshot(
            definition,
            value,
            editorValue,
            IsAvailable: true);
    }
}

internal sealed class DocumentCanvasDataPropertyDraft
{
    internal DocumentCanvasDataPropertyDraft(
        DocumentCanvasDataPropertySnapshot authoritative)
    {
        ArgumentNullException.ThrowIfNull(authoritative);
        Authoritative = authoritative;
        EditorValue = authoritative.EditorValue;
    }

    internal DocumentCanvasDataPropertySnapshot Authoritative { get; }

    internal ElementPropertyFieldDefinition Definition => Authoritative.Definition;

    internal ElementPropertyFieldId FieldId => Authoritative.FieldId;

    internal bool CanEdit => Authoritative.CanEdit;

    internal string EditorValue { get; set; }

    internal bool IsDirty
    {
        get
        {
            if (!CanEdit)
            {
                return false;
            }

            return Definition.EditorKind switch
            {
                ElementPropertyEditorKind.SingleLineText or
                    ElementPropertyEditorKind.MultilineText =>
                    !StringComparer.Ordinal.Equals(
                        EditorValue,
                        Authoritative.Value!.TextValue),
                ElementPropertyEditorKind.Integer =>
                    !TryParseInteger(out var value) ||
                    value != Authoritative.Value!.IntegerValue,
                ElementPropertyEditorKind.Boolean =>
                    !TryParseBoolean(out var value) ||
                    value != Authoritative.Value!.BooleanValue,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(Definition),
                    Definition.EditorKind,
                    "The element Properties editor kind is unsupported."),
            };
        }
    }

    internal bool TryCreateTargetValue(out PropertyValue? value)
    {
        value = null;
        switch (Definition.EditorKind)
        {
            case ElementPropertyEditorKind.SingleLineText:
            case ElementPropertyEditorKind.MultilineText:
                value = PropertyValue.FromText(EditorValue);
                return true;
            case ElementPropertyEditorKind.Integer:
                if (!TryParseInteger(out var integer))
                {
                    return false;
                }

                value = PropertyValue.FromInteger(integer);
                return true;
            case ElementPropertyEditorKind.Boolean:
                if (!TryParseBoolean(out var boolean))
                {
                    return false;
                }

                value = PropertyValue.FromBoolean(boolean);
                return true;
            default:
                throw new InvalidOperationException(
                    "The element Properties editor kind is unsupported.");
        }
    }

    internal bool TryParseInteger(out long value) =>
        long.TryParse(
            EditorValue,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);

    internal bool TryParseBoolean(out bool value) =>
        bool.TryParse(EditorValue, out value);
}

internal sealed class DocumentCanvasPropertiesDraft
{
    internal DocumentCanvasPropertiesDraft(DocumentCanvasPropertySnapshot authoritative)
    {
        ArgumentNullException.ThrowIfNull(authoritative);
        Authoritative = authoritative;
        DataFields = authoritative.DataFields
            .Select(static field => new DocumentCanvasDataPropertyDraft(field))
            .ToImmutableArray();
        X = Format(authoritative.Bounds.X);
        Y = Format(authoritative.Bounds.Y);
        Width = Format(authoritative.Bounds.Width);
        Height = Format(authoritative.Bounds.Height);
    }

    internal DocumentCanvasPropertySnapshot Authoritative { get; }

    internal ImmutableArray<DocumentCanvasDataPropertyDraft> DataFields { get; }

    internal string X { get; set; }

    internal string Y { get; set; }

    internal string Width { get; set; }

    internal string Height { get; set; }

    internal bool IsStale { get; set; }

    internal string? Feedback { get; set; }

    internal Diagnostic? FeedbackDiagnostic { get; set; }

    internal bool IsSemanticDirty => DataFields.Any(static item => item.IsDirty);

    internal bool IsBoundsDirty => Authoritative.CanEditBounds &&
        (!TryParseBounds(out var bounds, out _) || bounds != Authoritative.Bounds);

    internal bool HasConflictingChanges =>
        SemanticDirtyFieldCount > 1 || IsSemanticDirty && IsBoundsDirty;

    internal bool IsDirty => IsSemanticDirty || IsBoundsDirty;

    internal bool IsValid => TryValidate(out _, out _);

    internal bool TryValidate(
        out RectD bounds,
        out ImmutableArray<string> validationMessages,
        IStringLocalizer<ModelerStrings>? text = null)
    {
        var boundsValid = TryParseBounds(out bounds, out var boundsMessages, text);
        var messages = boundsMessages.ToBuilder();
        foreach (var field in DataFields.Where(static field => field.IsDirty))
        {
            if (field.Definition.MutationKind == SemanticPropertyMutationKind.Name &&
                !Authoritative.IsConnector &&
                string.IsNullOrWhiteSpace(field.EditorValue))
            {
                messages.Add(
                    text is null
                        ? $"{field.Definition.DisplayName} must contain at least one non-whitespace character."
                        : text["Validation_Required", ModelerLabels.PropertyFieldLabel(text, field.Definition)]);
            }

            if (field.Definition.EditorKind == ElementPropertyEditorKind.Integer &&
                !field.TryParseInteger(out _))
            {
                messages.Add(
                    text is null
                        ? $"{field.Definition.DisplayName} must be an integer within the supported range."
                        : text["Validation_Integer", ModelerLabels.PropertyFieldLabel(text, field.Definition)]);
            }

            if (field.Definition.EditorKind == ElementPropertyEditorKind.Boolean &&
                !field.TryParseBoolean(out _))
            {
                messages.Add(text is null
                    ? $"{field.Definition.DisplayName} must be true or false."
                    : text["Validation_Boolean", ModelerLabels.PropertyFieldLabel(text, field.Definition)]);
            }
        }

        if (HasConflictingChanges)
        {
            messages.Add(
                text is null
                    ? "Apply one Data field or visual bounds before editing another property group."
                    : text["Validation_Conflicting"]);
        }

        validationMessages = messages.ToImmutable();
        return boundsValid && validationMessages.IsEmpty;
    }

    internal bool TryGetDataField(
        ElementPropertyFieldId fieldId,
        out DocumentCanvasDataPropertyDraft? field)
    {
        ArgumentNullException.ThrowIfNull(fieldId);
        field = DataFields.FirstOrDefault(candidate => candidate.FieldId == fieldId);
        return field is not null;
    }

    internal bool TryGetDirtyDataField(out DocumentCanvasDataPropertyDraft? field)
    {
        field = DataFields.FirstOrDefault(static candidate => candidate.IsDirty);
        return SemanticDirtyFieldCount == 1 && field is not null;
    }

    internal bool TryParseBounds(
        out RectD bounds,
        out ImmutableArray<string> validationMessages,
        IStringLocalizer<ModelerStrings>? text = null)
    {
        bounds = default;
        if (!Authoritative.CanEditBounds)
        {
            validationMessages = [];
            return true;
        }

        var messages = ImmutableArray.CreateBuilder<string>();
        var x = ParseFinite(X, "X", messages, text);
        var y = ParseFinite(Y, "Y", messages, text);
        var width = ParseFinite(Width, "Width", messages, text);
        var height = ParseFinite(Height, "Height", messages, text);
        if (width is < Canvas2DInteractionController.MinimumVisualExtent)
        {
            messages.Add(text is null
                ? FormattableString.Invariant($"Width must be at least {Canvas2DInteractionController.MinimumVisualExtent}.")
                : text["Validation_MinWidth", Canvas2DInteractionController.MinimumVisualExtent]);
        }

        if (height is < Canvas2DInteractionController.MinimumVisualExtent)
        {
            messages.Add(text is null
                ? FormattableString.Invariant($"Height must be at least {Canvas2DInteractionController.MinimumVisualExtent}.")
                : text["Validation_MinHeight", Canvas2DInteractionController.MinimumVisualExtent]);
        }

        if (messages.Count == 0)
        {
            try
            {
                bounds = new RectD(x!.Value, y!.Value, width!.Value, height!.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                messages.Add(text is null
                    ? "The requested bounds must be finite and renderable."
                    : text["Validation_Bounds"]);
            }
        }

        validationMessages = messages.ToImmutable();
        return validationMessages.IsEmpty;
    }

    private static double? ParseFinite(
        string input,
        string label,
        ImmutableArray<string>.Builder messages,
        IStringLocalizer<ModelerStrings>? text)
    {
        if (!double.TryParse(
                input,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value) ||
            !double.IsFinite(value))
        {
            messages.Add(text is null
                ? $"{label} must be a finite number."
                : text["Validation_Finite", text[$"Properties_{label}"].Value]);
            return null;
        }

        return value;
    }

    internal static string Format(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);

    private int SemanticDirtyFieldCount => DataFields.Count(static item => item.IsDirty);
}

internal enum DocumentCanvasPropertiesApplyStatus
{
    NoChange,
    Committed,
    ValidationFailed,
    Stale,
    Unavailable,
    Failed,
}

internal sealed record DocumentCanvasPropertiesApplyResult(
    DocumentCanvasPropertiesApplyStatus Status,
    DocumentCanvasPropertySnapshot? Authoritative,
    ImmutableArray<Diagnostic> Diagnostics,
    string? Message)
{
    internal bool Succeeded => Status is
        DocumentCanvasPropertiesApplyStatus.NoChange or
        DocumentCanvasPropertiesApplyStatus.Committed;
}
