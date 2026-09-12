using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Interaction;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class DocumentCanvasPropertiesTests
{
    private static readonly SemanticTypeId NodeTypeId = new("type:node");
    private static readonly SemanticTypeId ConnectorTypeId = new("type:connector");
    private static readonly ElementPropertyFieldId NameFieldId = new("name");
    private static readonly ElementPropertyFieldId ElementNumberFieldId =
        new("element-number");
    private static readonly ElementPropertyFieldId DescriptionFieldId = new("description");
    private static readonly ElementPropertyFieldId EnabledFieldId = new("enabled");
    private static readonly ElementPropertiesSchemaCatalog SchemaCatalog = new(
    [
        new ElementPropertiesSchema(
            NodeTypeId,
            [
                Field(
                    NameFieldId,
                    "Name",
                    "demo:label",
                    ElementPropertyEditorKind.SingleLineText,
                    SemanticPropertyMutationKind.Name,
                    0),
                Field(
                    ElementNumberFieldId,
                    "Element number",
                    "demo:element-number",
                    ElementPropertyEditorKind.Integer,
                    SemanticPropertyMutationKind.Property,
                    1),
                Field(
                    DescriptionFieldId,
                    "Description",
                    "demo:description",
                    ElementPropertyEditorKind.MultilineText,
                    SemanticPropertyMutationKind.Property,
                    2),
            ]),
        new ElementPropertiesSchema(
            ConnectorTypeId,
            [
                Field(
                    NameFieldId,
                    "Name",
                    "demo:label",
                    ElementPropertyEditorKind.SingleLineText,
                    SemanticPropertyMutationKind.Property,
                    0),
                Field(
                    DescriptionFieldId,
                    "Description",
                    "demo:description",
                    ElementPropertyEditorKind.MultilineText,
                    SemanticPropertyMutationKind.Property,
                    1),
            ]),
    ]);

    [Fact]
    public void ContextMenuPositionIsClampedInsideCurrentCssSurface()
    {
        var target = new VisualStateId("visual:node");
        var scene = CreateEmptyScene();

        var menu = DocumentCanvasContextMenuState.CreateClamped(
            new PointD(995d, 495d),
            new Canvas2DSurfaceSize(1000d, 500d, 2d),
            target,
            new SceneObjectId("scene:node"),
            connectorRouteAction: null,
            new DocumentRevision(3),
            new EditingSessionGeneration(4),
            scene);
        var reclamped = DocumentCanvasContextMenuState.CreateClamped(
            menu.CssPosition,
            new Canvas2DSurfaceSize(320d, 240d, 1.25d),
            target,
            new SceneObjectId("scene:node"),
            connectorRouteAction: null,
            menu.DocumentRevision,
            menu.SessionGeneration,
            scene);

        Assert.Equal(new PointD(752d, 448d), menu.CssPosition);
        Assert.Equal(new PointD(72d, 188d), reclamped.CssPosition);
        Assert.Equal(target, reclamped.TargetVisualStateId);
        Assert.Same(scene, reclamped.SourceScene);
    }

    [Fact]
    public void BackgroundContextMenuHasNoElementTargetAndReservesItsContributedActions()
    {
        var scene = CreateEmptyScene();
        var action = new CanvasBackgroundActionDefinition(
            new CanvasBackgroundActionId("test:background-action"),
            "Add",
            "Process",
            static _ => throw new InvalidOperationException("The menu-state test does not execute."));

        var menu = DocumentCanvasContextMenuState.CreateBackgroundClamped(
            new PointD(995d, 495d),
            new Canvas2DSurfaceSize(1000d, 500d, 2d),
            new DocumentRevision(3),
            new EditingSessionGeneration(4),
            scene,
            new DocumentScopeId("scope:root"),
            [action]);

        Assert.Equal(DocumentCanvasContextMenuKind.Background, menu.Kind);
        Assert.Null(menu.TargetVisualStateId);
        Assert.Equal(new PointD(752d, 376d), menu.CssPosition);
        Assert.Same(action, Assert.Single(menu.BackgroundActions));
        Assert.Null(menu.DeletionAction);
        Assert.Null(menu.ScopeNavigationAction);
    }

    [Fact]
    public void ConnectorAnchorAddMenuReservesViewportSpaceForThreeActions()
    {
        var target = new VisualStateId("visual:node");
        var scene = CreateEmptyScene();
        var action = new Canvas2DConnectorAnchorContextAction(
            Canvas2DConnectorAnchorContextActionKind.AddAnchor,
            target,
            new SceneObjectId("scene:edge-zone"),
            new SceneObjectId("scene:node"),
            ConnectorAnchorSide.Right,
            insertionIndex: 0,
            edgeParameter: 0.5d);

        var menu = DocumentCanvasContextMenuState.CreateClamped(
            new PointD(995d, 495d),
            new Canvas2DSurfaceSize(1000d, 500d, 2d),
            target,
            action.TargetSceneObjectId,
            connectorRouteAction: null,
            new DocumentRevision(3),
            new EditingSessionGeneration(4),
            scene,
            action);
        var reclamped = DocumentCanvasContextMenuState.CreateClamped(
            menu.CssPosition,
            new Canvas2DSurfaceSize(320d, 240d, 1.25d),
            target,
            action.TargetSceneObjectId,
            connectorRouteAction: null,
            menu.DocumentRevision,
            menu.SessionGeneration,
            scene,
            action);

        Assert.Equal(new PointD(752d, 376d), menu.CssPosition);
        Assert.Equal(new PointD(72d, 116d), reclamped.CssPosition);
        Assert.Same(action, reclamped.ConnectorAnchorAction);
    }

    [Theory]
    [InlineData(ConnectorAnchorRoleCapability.Source)]
    [InlineData(ConnectorAnchorRoleCapability.Target)]
    public void RoleRestrictedConnectorAnchorMenuReservesSpaceForOnlyLegalAction(
        ConnectorAnchorRoleCapability allowedRole)
    {
        var target = new VisualStateId("visual:node");
        var scene = CreateEmptyScene();
        var action = new Canvas2DConnectorAnchorContextAction(
            Canvas2DConnectorAnchorContextActionKind.AddAnchor,
            target,
            new SceneObjectId("scene:edge-zone"),
            new SceneObjectId("scene:node"),
            ConnectorAnchorSide.Right,
            insertionIndex: 0,
            edgeParameter: 0.5d,
            allowedRoles: allowedRole);

        var menu = DocumentCanvasContextMenuState.CreateClamped(
            new PointD(995d, 495d),
            new Canvas2DSurfaceSize(1000d, 500d, 2d),
            target,
            action.TargetSceneObjectId,
            connectorRouteAction: null,
            new DocumentRevision(3),
            new EditingSessionGeneration(4),
            scene,
            action);
        var reclamped = DocumentCanvasContextMenuState.CreateClamped(
            menu.CssPosition,
            new Canvas2DSurfaceSize(320d, 240d, 1.25d),
            target,
            action.TargetSceneObjectId,
            connectorRouteAction: null,
            menu.DocumentRevision,
            menu.SessionGeneration,
            scene,
            action);

        Assert.Equal(new PointD(752d, 412d), menu.CssPosition);
        Assert.Equal(new PointD(72d, 152d), reclamped.CssPosition);
        Assert.Same(action, reclamped.ConnectorAnchorAction);
    }

    [Fact]
    public void AuthoritativeSnapshotResolvesEditableExistingTextNameForSemanticElement()
    {
        var document = CreateDocument();

        var found = DocumentCanvasPropertySnapshot.TryCreate(
            document,
            new VisualStateId("visual:node"),
            SchemaCatalog,
            out var snapshot);

        Assert.True(found);
        Assert.NotNull(snapshot);
        Assert.Equal("semantic:node", snapshot.SemanticId.Value);
        Assert.Equal("type:node", snapshot.TypeId.Value);
        Assert.Equal(
            [NameFieldId, ElementNumberFieldId, DescriptionFieldId],
            snapshot.DataFields.Select(static item => item.FieldId));
        AssertDataField(snapshot, NameFieldId, "Node name", "demo:label", canEdit: true);
        AssertDataField(
            snapshot,
            ElementNumberFieldId,
            "10",
            "demo:element-number",
            canEdit: true);
        AssertDataField(
            snapshot,
            DescriptionFieldId,
            "Initial node description",
            "demo:description",
            canEdit: true);
        Assert.Equal(new RectD(10d, 20d, 100d, 60d), snapshot.Bounds);
        Assert.True(snapshot.CanEditBounds);
        Assert.False(snapshot.IsConnector);
    }

    [Fact]
    public void AuthoritativeSnapshotExposesConnectorDataAndRouteRelativePlacement()
    {
        var found = DocumentCanvasPropertySnapshot.TryCreate(
            CreateDocument(),
            new VisualStateId("visual:connector"),
            SchemaCatalog,
            out var snapshot);

        Assert.True(found);
        Assert.NotNull(snapshot);
        Assert.False(snapshot.CanEditBounds);
        Assert.True(snapshot.IsConnector);
        Assert.Equal(
            [NameFieldId, DescriptionFieldId],
            snapshot.DataFields.Select(static item => item.FieldId));
        AssertDataField(snapshot, NameFieldId, string.Empty, "demo:label", canEdit: true);
        AssertDataField(
            snapshot,
            DescriptionFieldId,
            "Initial connector description",
            "demo:description",
            canEdit: true);
        Assert.Equal("semantic:node", snapshot.SourceId?.Value);
        Assert.Equal("semantic:other", snapshot.TargetId?.Value);
        Assert.Equal(ConnectorLabelPlacement.Default, snapshot.LabelPlacement);

        var draft = new DocumentCanvasPropertiesDraft(snapshot);
        DraftField(draft, NameFieldId).EditorValue = "Approved";
        Assert.True(DraftField(draft, NameFieldId).IsDirty);
        Assert.True(draft.IsValid);

        DraftField(draft, NameFieldId).EditorValue = string.Empty;
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void SemanticElementWithoutExistingTextLabelKeepsNameReadOnly()
    {
        var found = DocumentCanvasPropertySnapshot.TryCreate(
            CreateDocument(),
            new VisualStateId("visual:other"),
            SchemaCatalog,
            out var snapshot);

        Assert.True(found);
        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot.DataFields.Length);
        Assert.All(snapshot.DataFields, static item => Assert.False(item.CanEdit));
        Assert.All(snapshot.DataFields, static item => Assert.False(item.IsAvailable));
        Assert.True(snapshot.CanEditBounds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void SemanticElementWithBlankExistingLabelKeepsNameReadOnly(string name)
    {
        var found = DocumentCanvasPropertySnapshot.TryCreate(
            CreateDocument(name),
            new VisualStateId("visual:node"),
            SchemaCatalog,
            out var snapshot);

        Assert.True(found);
        Assert.NotNull(snapshot);
        var field = DataField(snapshot, NameFieldId);
        Assert.Equal(string.Empty, field.EditorValue);
        Assert.Null(field.Value);
        Assert.False(field.IsAvailable);
        Assert.False(field.CanEdit);
    }

    [Fact]
    public void MissingSchemaKeepsIdentityAndVisualDataWithoutFallbackFields()
    {
        var found = DocumentCanvasPropertySnapshot.TryCreate(
            CreateDocument(),
            new VisualStateId("visual:node"),
            ElementPropertiesSchemaCatalog.Empty,
            out var snapshot);

        Assert.True(found);
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.DataFields);
        Assert.Equal(new SemanticElementId("semantic:node"), snapshot.SemanticId);
        Assert.Equal(NodeTypeId, snapshot.TypeId);
        Assert.Equal(new RectD(10d, 20d, 100d, 60d), snapshot.Bounds);
        Assert.True(snapshot.CanEditBounds);
    }

    [Fact]
    public void WrongKindSchemaPropertyIsEmptyUnavailableAndReadOnly()
    {
        var wrongKindCatalog = new ElementPropertiesSchemaCatalog(
        [
            new ElementPropertiesSchema(
                NodeTypeId,
                [
                    Field(
                        ElementNumberFieldId,
                        "Element number",
                        "demo:label",
                        ElementPropertyEditorKind.Integer,
                        SemanticPropertyMutationKind.Property,
                        0),
                ]),
        ]);

        Assert.True(DocumentCanvasPropertySnapshot.TryCreate(
            CreateDocument(),
            new VisualStateId("visual:node"),
            wrongKindCatalog,
            out var snapshot));
        var dataField = Assert.Single(Assert.IsType<DocumentCanvasPropertySnapshot>(snapshot)
            .DataFields);

        Assert.Equal(ElementNumberFieldId, dataField.FieldId);
        Assert.Equal(string.Empty, dataField.EditorValue);
        Assert.Null(dataField.Value);
        Assert.False(dataField.IsAvailable);
        Assert.False(dataField.CanEdit);
    }

    [Fact]
    public void NeutralDemoRegistersApplicationOwnedNodeAndConnectorSchemas()
    {
        Assert.True(NeutralDemoPropertiesSchemas.Catalog.TryGetSchema(
            NeutralDemoPipeline.NeutralNodeTypeId,
            out var nodeSchema));
        Assert.Equal(
            [NameFieldId, ElementNumberFieldId, DescriptionFieldId],
            nodeSchema.Fields.Select(static item => item.FieldId));
        Assert.Equal(
            SemanticPropertyMutationKind.Name,
            nodeSchema.Fields[0].MutationKind);

        Assert.True(NeutralDemoPropertiesSchemas.Catalog.TryGetSchema(
            NeutralDemoPipeline.NeutralEdgeTypeId,
            out var connectorSchema));
        Assert.Equal(
            [NameFieldId, DescriptionFieldId],
            connectorSchema.Fields.Select(static item => item.FieldId));
        Assert.All(
            connectorSchema.Fields,
            static item => Assert.Equal(
                SemanticPropertyMutationKind.Property,
                item.MutationKind));
    }

    [Fact]
    public void DraftUsesInvariantNumbersTracksDirtyStateAndEnforcesSharedMinimumExtent()
    {
        var authoritative = AssertSnapshot("visual:node");
        var draft = new DocumentCanvasPropertiesDraft(authoritative);

        Assert.Equal("10", draft.X);
        Assert.Equal("20", draft.Y);
        Assert.Equal("100", draft.Width);
        Assert.Equal("60", draft.Height);
        Assert.False(draft.IsDirty);
        Assert.All(draft.DataFields, static item => Assert.False(item.IsDirty));
        Assert.False(draft.IsSemanticDirty);
        Assert.False(draft.IsBoundsDirty);
        Assert.True(draft.TryParseBounds(out var original, out var originalErrors));
        Assert.Equal(authoritative.Bounds, original);
        Assert.Empty(originalErrors);

        draft.X = "12.5";
        draft.Width = (Canvas2DInteractionController.MinimumVisualExtent / 2d)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(draft.IsDirty);
        Assert.True(draft.IsBoundsDirty);
        Assert.False(draft.TryParseBounds(out _, out var errors));
        Assert.Contains(errors, message => message.Contains("Width", StringComparison.Ordinal));
    }

    [Fact]
    public void DraftTracksExactNameAndRejectsCombinedSemanticAndVisualChanges()
    {
        var draft = new DocumentCanvasPropertiesDraft(AssertSnapshot("visual:node"));

        var name = DraftField(draft, NameFieldId);
        Assert.Equal("Node name", name.EditorValue);
        name.EditorValue = "  Renamed node  ";

        Assert.True(name.IsDirty);
        Assert.False(draft.IsBoundsDirty);
        Assert.True(draft.IsDirty);
        Assert.True(draft.IsValid);

        draft.X = "12";

        Assert.True(draft.HasConflictingChanges);
        Assert.False(draft.IsValid);

        draft.X = "10";
        name.EditorValue = string.Empty;

        Assert.True(name.IsDirty);
        Assert.False(draft.HasConflictingChanges);
        Assert.False(draft.IsValid);
        Assert.False(draft.TryValidate(out _, out var errors));
        Assert.Contains(errors, message => message.Contains("Name", StringComparison.Ordinal));

        name.EditorValue = "Renamed node";

        Assert.True(draft.IsValid);
    }

    [Fact]
    public void DraftTracksTypedElementNumberAndExactMultilineDescription()
    {
        var draft = new DocumentCanvasPropertiesDraft(AssertSnapshot("visual:node"));

        var elementNumber = DraftField(draft, ElementNumberFieldId);
        var description = DraftField(draft, DescriptionFieldId);
        Assert.Equal("10", elementNumber.EditorValue);
        Assert.Equal("Initial node description", description.EditorValue);
        Assert.True(elementNumber.TryParseInteger(out var initialNumber));
        Assert.Equal(10L, initialNumber);

        elementNumber.EditorValue = "25";

        Assert.True(elementNumber.IsDirty);
        Assert.True(draft.IsSemanticDirty);
        Assert.True(draft.TryValidate(out _, out var numberErrors));
        Assert.True(elementNumber.TryCreateTargetValue(out var parsedNumber));
        Assert.Equal(25L, parsedNumber?.IntegerValue);
        Assert.Empty(numberErrors);

        elementNumber.EditorValue = "10";
        description.EditorValue = "First line\nSecond line\r\nThird line";

        Assert.False(elementNumber.IsDirty);
        Assert.True(description.IsDirty);
        Assert.Equal("First line\nSecond line\r\nThird line", description.EditorValue);
        Assert.True(draft.IsValid);
    }

    [Fact]
    public void BooleanDraftPreservesTypedValueAndCreatesTypedApplyTarget()
    {
        var booleanCatalog = new ElementPropertiesSchemaCatalog(
        [
            new ElementPropertiesSchema(
                NodeTypeId,
                [
                    Field(
                        EnabledFieldId,
                        "Enabled",
                        "demo:enabled",
                        ElementPropertyEditorKind.Boolean,
                        SemanticPropertyMutationKind.Property,
                        0),
                ]),
        ]);
        Assert.True(DocumentCanvasPropertySnapshot.TryCreate(
            CreateDocument(),
            new VisualStateId("visual:node"),
            booleanCatalog,
            out var snapshot));
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(snapshot);
        var authoritativeField = DataField(authoritative, EnabledFieldId);

        Assert.Equal(PropertyValueKind.Boolean, authoritativeField.Value?.Kind);
        Assert.True(authoritativeField.Value!.BooleanValue);
        Assert.Equal("true", authoritativeField.EditorValue);
        Assert.True(authoritativeField.CanEdit);

        var draft = new DocumentCanvasPropertiesDraft(authoritative);
        var enabled = DraftField(draft, EnabledFieldId);
        Assert.True(enabled.TryParseBoolean(out var initial));
        Assert.True(initial);
        Assert.False(enabled.IsDirty);

        enabled.EditorValue = "false";

        Assert.True(enabled.IsDirty);
        Assert.True(draft.IsSemanticDirty);
        Assert.True(draft.TryValidate(out _, out var errors));
        Assert.Empty(errors);
        Assert.True(enabled.TryCreateTargetValue(out var target));
        Assert.Equal(PropertyValueKind.Boolean, target?.Kind);
        Assert.False(target!.BooleanValue);

        enabled.EditorValue = "not-a-boolean";

        Assert.False(enabled.TryCreateTargetValue(out _));
        Assert.False(draft.TryValidate(out _, out errors));
        Assert.Contains(errors, message =>
            message.Contains("Enabled", StringComparison.Ordinal));
    }

    [Fact]
    public void BoundaryAttachedNodeIsFixedGeometryWithoutConnectorFields()
    {
        Assert.True(DocumentCanvasPropertySnapshot.TryCreate(
            CreateDocument(attachOtherToBoundary: true),
            new VisualStateId("visual:other"),
            SchemaCatalog,
            out var snapshot));
        var authoritative = Assert.IsType<DocumentCanvasPropertySnapshot>(snapshot);

        Assert.True(authoritative.IsBoundaryAttached);
        Assert.False(authoritative.CanEditBounds);
        Assert.False(authoritative.IsConnector);
        Assert.Null(authoritative.SourceId);
        Assert.Null(authoritative.TargetId);
        Assert.Null(authoritative.LabelPlacement);

        var draft = new DocumentCanvasPropertiesDraft(authoritative)
        {
            X = "not-a-number",
            Y = "not-a-number",
            Width = "not-a-number",
            Height = "not-a-number",
        };

        Assert.False(draft.IsBoundsDirty);
        Assert.True(draft.TryParseBounds(out var bounds, out var errors));
        Assert.Equal(default, bounds);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    public void DraftRejectsInvalidIntegerElementNumber(string invalid)
    {
        var draft = new DocumentCanvasPropertiesDraft(AssertSnapshot("visual:node"));
        var elementNumber = DraftField(draft, ElementNumberFieldId);
        elementNumber.EditorValue = invalid;

        Assert.True(elementNumber.IsDirty);
        Assert.False(draft.TryValidate(out _, out var errors));
        Assert.Contains(errors, message =>
            message.Contains("Element number", StringComparison.Ordinal));
    }

    [Fact]
    public void DraftRejectsMultipleDataFieldsInOneApply()
    {
        var draft = new DocumentCanvasPropertiesDraft(AssertSnapshot("visual:node"));
        DraftField(draft, NameFieldId).EditorValue = "Renamed node";
        DraftField(draft, DescriptionFieldId).EditorValue = "Changed description";

        Assert.True(draft.HasConflictingChanges);
        Assert.False(draft.IsValid);
        Assert.False(draft.TryValidate(out _, out var errors));
        Assert.Contains(errors, message =>
            message.Contains("one Data field", StringComparison.Ordinal));
    }

    [Fact]
    public void DraftRoundTripsHighPrecisionAuthoritativeBoundsWithoutBecomingDirty()
    {
        var source = AssertSnapshot("visual:node");
        var authoritative = source with
        {
            Bounds = new RectD(
                10.123456789012345d,
                -20.987654321098765d,
                100.00000000000003d,
                60.333333333333336d),
        };

        var draft = new DocumentCanvasPropertiesDraft(authoritative);

        Assert.False(draft.IsDirty);
        Assert.True(draft.TryParseBounds(out var roundTripped, out var errors));
        Assert.Empty(errors);
        Assert.Equal(authoritative.Bounds, roundTripped);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,5")]
    public void DraftRejectsNonFiniteOrNonInvariantNumbers(string invalid)
    {
        var draft = new DocumentCanvasPropertiesDraft(AssertSnapshot("visual:node"))
        {
            X = invalid,
        };

        Assert.False(draft.TryParseBounds(out _, out var errors));
        Assert.NotEmpty(errors);
    }

    private static DocumentCanvasPropertySnapshot AssertSnapshot(string visualStateId)
    {
        Assert.True(DocumentCanvasPropertySnapshot.TryCreate(
            CreateDocument(),
            new VisualStateId(visualStateId),
            SchemaCatalog,
            out var snapshot));
        return Assert.IsType<DocumentCanvasPropertySnapshot>(snapshot);
    }

    private static ElementPropertyFieldDefinition Field(
        ElementPropertyFieldId fieldId,
        string displayName,
        string semanticPropertyKey,
        ElementPropertyEditorKind editorKind,
        SemanticPropertyMutationKind mutationKind,
        int order) => new(
            fieldId,
            displayName,
            semanticPropertyKey,
            editorKind,
            mutationKind,
            isEditable: true,
            order);

    private static void AssertDataField(
        DocumentCanvasPropertySnapshot snapshot,
        ElementPropertyFieldId fieldId,
        string editorValue,
        string semanticPropertyKey,
        bool canEdit)
    {
        var dataField = DataField(snapshot, fieldId);

        Assert.Equal(editorValue, dataField.EditorValue);
        Assert.Equal(semanticPropertyKey, dataField.Definition.SemanticPropertyKey);
        Assert.Equal(canEdit, dataField.CanEdit);
        Assert.Equal(canEdit, dataField.IsAvailable);
    }

    private static DocumentCanvasDataPropertySnapshot DataField(
        DocumentCanvasPropertySnapshot snapshot,
        ElementPropertyFieldId fieldId)
    {
        Assert.True(snapshot.TryGetDataField(fieldId, out var dataField));
        return Assert.IsType<DocumentCanvasDataPropertySnapshot>(dataField);
    }

    private static DocumentCanvasDataPropertyDraft DraftField(
        DocumentCanvasPropertiesDraft draft,
        ElementPropertyFieldId fieldId)
    {
        Assert.True(draft.TryGetDataField(fieldId, out var dataField));
        return Assert.IsType<DocumentCanvasDataPropertyDraft>(dataField);
    }

    private static Canvas2DScene CreateEmptyScene() => new(
        new DocumentId("document:context-menu"),
        new DocumentRevision(3),
        new AlgorithmId("layout:test"),
        new AlgorithmId("routing:test"),
        Canvas2DSceneConfiguration.Default,
        [],
        ViewportSnapshot.Default,
        Matrix2D.Identity,
        activeToolId: null,
        focusTargetId: null,
        PropertyMap.Empty,
        PropertyMap.Empty,
        []);

    private static DocumentSnapshot CreateDocument(
        string nodeName = "Node name",
        bool attachOtherToBoundary = false)
    {
        var documentId = new DocumentId("document:properties");
        var revision = new DocumentRevision(3);
        var nodeId = new SemanticElementId("semantic:node");
        var otherId = new SemanticElementId("semantic:other");
        var connectorId = new SemanticElementId("semantic:connector");
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                documentId,
                revision,
                [
                    new SemanticElementSnapshot(
                        nodeId,
                        new SemanticTypeId("type:node"),
                        [
                            new("demo:label", PropertyValue.FromText(nodeName)),
                            new("demo:element-number", PropertyValue.FromInteger(10L)),
                            new("demo:enabled", PropertyValue.FromBoolean(true)),
                            new(
                                "demo:description",
                                PropertyValue.FromText("Initial node description")),
                        ]),
                    new SemanticElementSnapshot(otherId, new SemanticTypeId("type:node")),
                ],
                [new SemanticRelationshipSnapshot(
                    connectorId,
                    new SemanticTypeId("type:connector"),
                    nodeId,
                    otherId,
                    [
                        new("demo:label", PropertyValue.FromText(string.Empty)),
                        new(
                            "demo:description",
                            PropertyValue.FromText("Initial connector description")),
                    ])]),
            new VisualModelSnapshot(
                documentId,
                revision,
                [
                    new VisualStateSnapshot(
                        new VisualStateId("visual:node"),
                        nodeId,
                        new PointD(10d, 20d),
                        new SizeD(100d, 60d),
                        VisualPlacementMode.Manual),
                    new VisualStateSnapshot(
                        new VisualStateId("visual:other"),
                        otherId,
                        new PointD(200d, 20d),
                        new SizeD(100d, 60d),
                        VisualPlacementMode.Manual,
                        boundaryAttachment: attachOtherToBoundary
                            ? new BoundaryAttachmentPlacement(
                                BoundaryAttachmentSide.Right,
                                0.5d)
                            : null),
                    new VisualStateSnapshot(
                        new VisualStateId("visual:connector"),
                        connectorId,
                        new PointD(0d, 0d),
                        new SizeD(0d, 0d),
                        VisualPlacementMode.Manual,
                        route: [new PointD(10d, 10d), new PointD(50d, 50d)]),
                ]),
            new DocumentMetadataSnapshot(documentId, revision));
    }
}
