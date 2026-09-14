using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseL5ContextPropertiesArchitectureTests
{
    [Fact]
    public void PropertiesUiIsAViewportFixedDataIdentityVisualFormOverlay()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var styles = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor.css");
        var canvasIndex = component.IndexOf("<canvas", StringComparison.Ordinal);
        var formIndex = component.IndexOf(
            "id=\"@DomId(\"properties-form\")\"",
            StringComparison.Ordinal);
        var dataIndex = component.IndexOf("data-property-group=\"data\"", StringComparison.Ordinal);
        var identityIndex = component.IndexOf(
            "data-property-group=\"identity\"",
            StringComparison.Ordinal);
        var visualIndex = component.IndexOf(
            "data-property-group=\"visual\"",
            StringComparison.Ordinal);

        Assert.True(canvasIndex >= 0 && formIndex > canvasIndex);
        Assert.Contains("<form id=\"@DomId(\"properties-form\")\"", component,
            StringComparison.Ordinal);
        Assert.Contains("class=\"properties-backdrop\"", component, StringComparison.Ordinal);
        Assert.Contains("<button id=\"@DomId(\"properties-cancel\")\"", component,
            StringComparison.Ordinal);
        Assert.Contains("<button id=\"@DomId(\"properties-apply\")\"", component,
            StringComparison.Ordinal);
        Assert.Contains("<button id=\"@DomId(\"properties-close\")\"", component,
            StringComparison.Ordinal);
        Assert.Contains("readonly", component, StringComparison.Ordinal);
        Assert.True(dataIndex > formIndex && identityIndex > dataIndex && visualIndex > identityIndex);
        Assert.Contains("<legend>@Text[\"Properties_Data\"]</legend>", component, StringComparison.Ordinal);
        var dataGroup = Between(
            component,
            "<fieldset data-property-group=\"data\">",
            "</fieldset>");
        Assert.Contains("@foreach (var field in draft.DataFields)", dataGroup,
            StringComparison.Ordinal);
        Assert.Contains("@PropertyFieldLabel(field.Definition)", dataGroup, StringComparison.Ordinal);
        Assert.Contains("@switch (field.Definition.EditorKind)", dataGroup,
            StringComparison.Ordinal);
        Assert.Contains("case ElementPropertyEditorKind.SingleLineText:", dataGroup,
            StringComparison.Ordinal);
        Assert.Contains("case ElementPropertyEditorKind.Integer:", dataGroup,
            StringComparison.Ordinal);
        Assert.Contains("case ElementPropertyEditorKind.MultilineText:", dataGroup,
            StringComparison.Ordinal);
        Assert.Contains("case ElementPropertyEditorKind.Boolean:", dataGroup,
            StringComparison.Ordinal);
        Assert.Contains("id=\"@PropertiesFieldControlId(field.FieldId)\"", dataGroup,
            StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(
            dataGroup,
            "data-property-field-id=\"@field.FieldId.Value\""));
        Assert.Equal(3, CountOccurrences(
            dataGroup,
            "readonly=\"@(!field.CanEdit)\""));
        Assert.Equal(3, CountOccurrences(
            dataGroup,
            "disabled=\"@_propertiesApplying\""));
        Assert.Contains("type=\"text\"", dataGroup, StringComparison.Ordinal);
        Assert.Contains("type=\"number\"", dataGroup, StringComparison.Ordinal);
        Assert.Contains("step=\"1\"", dataGroup, StringComparison.Ordinal);
        Assert.Contains("<textarea", dataGroup, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", dataGroup, StringComparison.Ordinal);
        Assert.DoesNotContain("demo:", dataGroup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bpmn", dataGroup, StringComparison.OrdinalIgnoreCase);

        foreach (var identityControlId in new[]
                 {
                     "properties-semantic-id",
                     "properties-visual-id",
                     "properties-type",
                 })
        {
            var identityControl = Between(
                component,
                $"id=\"@DomId(\"{identityControlId}\")\"",
                "/>");
            Assert.Contains("readonly", identityControl, StringComparison.Ordinal);
            Assert.DoesNotContain("@oninput", identityControl, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("properties-panel", component, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grid-template-columns: 1fr", styles,
            StringComparison.OrdinalIgnoreCase);

        var surfaceStyle = Between(styles, ".document-canvas-surface {", "}");
        var backdropStyle = Between(styles, ".properties-backdrop {", "}");
        var formStyle = Between(styles, ".properties-form {", "}");
        var toolbarStyle = Between(styles, ".editor-toolbar {", "}");
        Assert.Contains("position: relative", surfaceStyle, StringComparison.Ordinal);
        Assert.Contains("width: 100%", surfaceStyle, StringComparison.Ordinal);
        Assert.Contains("position: fixed", backdropStyle, StringComparison.Ordinal);
        Assert.Contains("inset: 0", backdropStyle, StringComparison.Ordinal);
        Assert.Contains("position: fixed", formStyle, StringComparison.Ordinal);
        Assert.Contains("top: 50%", formStyle, StringComparison.Ordinal);
        Assert.Contains("left: 50%", formStyle, StringComparison.Ordinal);
        Assert.Contains("100vw", formStyle, StringComparison.Ordinal);
        Assert.Contains("100vh", formStyle, StringComparison.Ordinal);
        Assert.Contains("overflow: auto", formStyle, StringComparison.Ordinal);
        Assert.Contains("transform: translate(-50%, -50%)", formStyle,
            StringComparison.Ordinal);
        Assert.Contains("z-index: 30", backdropStyle, StringComparison.Ordinal);
        Assert.Contains("position: relative", toolbarStyle, StringComparison.Ordinal);
        Assert.Contains("z-index: 31", toolbarStyle, StringComparison.Ordinal);
        Assert.Contains("z-index: 32", formStyle, StringComparison.Ordinal);
        var main = Between(component, "<main class=\"document-canvas-page\"", ">");
        var shell = Between(component, "<section class=\"document-canvas-shell\"", ">");
        Assert.Contains("@onclick=\"CloseContextMenu\"", main, StringComparison.Ordinal);
        Assert.DoesNotContain("@onclick=\"CloseContextMenu\"", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void AvailabilityAndFormTargetAreDerivedFromCanonicalSelectionMembership()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var availability = Between(
            component,
            "private bool PropertiesAvailable =>",
            "private string PropertiesAvailableValue");
        var reconciliation = Between(
            component,
            "private void ReconcilePropertiesForm()",
            "private Task UndoAsync()");
        var capture = Between(
            host,
            "private static bool TryCaptureSelectedProperties(",
            "private static DocumentCanvasPropertiesApplyResult CreateApplyFailure(");

        Assert.Contains("ContextMenu?.TargetVisualStateId", availability,
            StringComparison.Ordinal);
        Assert.Contains("EditorState.Selection.Contains(targetVisualStateId)", availability,
            StringComparison.Ordinal);
        Assert.Contains("IsCurrentPropertiesTarget(_state, draft.Authoritative)",
            reconciliation, StringComparison.Ordinal);
        Assert.Contains("new DocumentCanvasPropertiesDraft(replacement)", reconciliation,
            StringComparison.Ordinal);
        Assert.Contains("!state.EditorState.Selection.Contains(targetVisualStateId)", capture,
            StringComparison.Ordinal);
        Assert.Contains("state.EditorState.SemanticSceneSelection", capture,
            StringComparison.Ordinal);
        Assert.Contains("session.TryCaptureDocumentSnapshot", capture, StringComparison.Ordinal);
        Assert.Contains("ExecuteForSceneTargetAsync", host, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "requireSoleSelection: true",
            Between(
                host,
                "internal async ValueTask<DocumentCanvasPropertiesApplyResult> ApplyPropertiesAsync(",
                "internal DocumentCanvasHostState CaptureState()"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DraftTypingAndCancelAreTransientAndOnlyApplyExecutesOneCommand()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var sceneTargetGate = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.SceneTargets.cs");
        var properties = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasProperties.cs");
        var typing = Between(
            component,
            "private void UpdateDataDraftValue(",
            "private async Task ApplyPropertiesAsync()");
        var cancel = Between(
            component,
            "private void CancelProperties()",
            "private void UpdateDataDraftValue(");
        var apply = Between(
            host,
            "internal async ValueTask<DocumentCanvasPropertiesApplyResult> ApplyPropertiesAsync(",
            "internal async ValueTask<HistoryOperationResult?> ExecuteBackgroundContextActionAsync(");
        var componentApply = Between(
            component,
            "private async Task ApplyPropertiesAsync()",
            "private void ReconcilePropertiesForm()");
        var form = Between(
            component,
            "<form id=\"@DomId(\"properties-form\")\"",
            "</form>");
        var dataGroup = Between(
            form,
            "<fieldset data-property-group=\"data\">",
            "</fieldset>");

        Assert.Contains("DocumentCanvasPropertiesDraft", properties, StringComparison.Ordinal);
        Assert.Contains("DocumentCanvasDataPropertyDraft", properties, StringComparison.Ordinal);
        Assert.Contains("ImmutableArray<DocumentCanvasDataPropertyDraft> DataFields", properties,
            StringComparison.Ordinal);
        Assert.Contains("internal string EditorValue", properties, StringComparison.Ordinal);
        Assert.Contains("internal string X", properties, StringComparison.Ordinal);
        Assert.Contains("internal string Y", properties, StringComparison.Ordinal);
        Assert.Contains("internal string Width", properties, StringComparison.Ordinal);
        Assert.Contains("internal string Height", properties, StringComparison.Ordinal);
        Assert.DoesNotContain("ICommand", properties, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteAsync", properties, StringComparison.Ordinal);

        Assert.DoesNotContain("Command", typing, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteAsync", typing, StringComparison.Ordinal);
        Assert.DoesNotContain("hasConflictingDraft", typing, StringComparison.Ordinal);
        Assert.DoesNotContain("Command", cancel, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteAsync", cancel, StringComparison.Ordinal);
        Assert.Contains("_propertiesDraft = null", cancel, StringComparison.Ordinal);

        Assert.Contains("!dataChanged && !boundsChanged", apply,
            StringComparison.Ordinal);
        Assert.Contains("dataChanged && boundsChanged", apply, StringComparison.Ordinal);
        Assert.Contains("DocumentCanvasPropertiesApplyStatus.NoChange", apply,
            StringComparison.Ordinal);
        Assert.Contains("changedDataField.TryCreateTargetValue", apply,
            StringComparison.Ordinal);
        Assert.Contains("currentField.Definition.MutationKind switch", apply,
            StringComparison.Ordinal);
        Assert.Contains("new UpdateSemanticElementNameCommand(", apply, StringComparison.Ordinal);
        Assert.Contains("new UpdateSemanticElementPropertyCommand(", apply,
            StringComparison.Ordinal);
        Assert.Contains("PropertyValue.FromInteger", properties, StringComparison.Ordinal);
        Assert.Contains("PropertyValue.FromText(EditorValue)", properties,
            StringComparison.Ordinal);
        Assert.Contains("new MoveVisualStateCommand(", apply, StringComparison.Ordinal);
        Assert.Contains("new ResizeVisualStateCommand(", apply, StringComparison.Ordinal);
        Assert.Contains("VisualPlacementMode.Pinned", apply, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(apply, "session.ExecuteForSceneTargetAsync("));
        Assert.Equal(0, CountOccurrences(apply, "session.ExecuteAsync("));
        Assert.Equal(1, CountOccurrences(sceneTargetGate, "History.ExecuteAsync("));
        Assert.Contains("TryEnterCommandAsync", sceneTargetGate, StringComparison.Ordinal);
        Assert.DoesNotContain("History.ExecuteAsync(", host, StringComparison.Ordinal);
        Assert.Contains("session.WaitForIdleAsync", apply, StringComparison.Ordinal);
        Assert.Contains("TryCaptureSelectedProperties", apply, StringComparison.Ordinal);
        Assert.Contains(
            "catch (OperationCanceledException) when (_componentLifetime.IsCancellationRequested)",
            componentApply,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(form, "@onsubmit=\"ApplyPropertiesAsync\""));
        Assert.DoesNotContain("@onblur", dataGroup, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(dataGroup, "@onchange="));
        Assert.DoesNotContain("@onkeydown", dataGroup, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveVisualStateCommand" + Environment.NewLine +
            "            await", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void EditableFieldsUseFocusedVisualNameAndTypedSemanticPropertyCommands()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var properties = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasProperties.cs");
        var commandTypes = typeof(ICommand).Assembly.GetExportedTypes()
            .Where(type => !type.IsInterface && typeof(ICommand).IsAssignableFrom(type))
            .ToArray();

        Assert.Contains("@foreach (var field in draft.DataFields)", component,
            StringComparison.Ordinal);
        Assert.Contains("id=\"@PropertiesFieldControlId(field.FieldId)\"", component,
            StringComparison.Ordinal);
        Assert.Contains("data-property-field-id=\"@field.FieldId.Value\"", component,
            StringComparison.Ordinal);
        Assert.Contains("id=\"@DomId(\"properties-placement\")\"", component,
            StringComparison.Ordinal);
        Assert.Contains("id=\"@DomId(\"properties-source\")\"", component,
            StringComparison.Ordinal);
        Assert.Contains("id=\"@DomId(\"properties-target\")\"", component,
            StringComparison.Ordinal);
        Assert.Contains("value=\"@field.EditorValue\"", component, StringComparison.Ordinal);
        Assert.Contains("readonly=\"@(!field.CanEdit)\"", component,
            StringComparison.Ordinal);
        Assert.Contains("ElementPropertyEditorKind.SingleLineText", component,
            StringComparison.Ordinal);
        Assert.Contains("ElementPropertyEditorKind.Integer", component,
            StringComparison.Ordinal);
        Assert.Contains("ElementPropertyEditorKind.MultilineText", component,
            StringComparison.Ordinal);
        Assert.Contains("ElementPropertyEditorKind.Boolean", component,
            StringComparison.Ordinal);
        Assert.Contains("CanEditBounds", component, StringComparison.Ordinal);
        Assert.Contains("ElementPropertiesSchemaCatalog schemaCatalog", properties,
            StringComparison.Ordinal);
        Assert.Contains("schemaCatalog.TryGetSchema(typeId", properties,
            StringComparison.Ordinal);
        Assert.Contains("CreateDataProperty(field, semanticProperties, isConnector)", properties,
            StringComparison.Ordinal);
        Assert.Contains("Definition.IsEditable && IsAvailable", properties,
            StringComparison.Ordinal);
        Assert.Contains("IsConnector", properties, StringComparison.Ordinal);
        Assert.Contains("TryGetRelationship", properties, StringComparison.Ordinal);
        Assert.Contains("SourceId", properties, StringComparison.Ordinal);
        Assert.Contains("TargetId", properties, StringComparison.Ordinal);

        Assert.Equal(
            [
                "AddConnectorAnchorCommand",
                "CompoundDocumentCommand",
                "CreateTopLevelDocumentScopeCommand",
                "MoveLabelCommand",
                "MoveVisualStateCommand",
                "MoveVisualStatesCommand",
                "RemoveConnectorAnchorCommand",
                "ResizeVisualStateCommand",
                "SetModelProfileAvailabilityCommand",
                "UpdateBoundaryAttachmentCommand",
                "UpdateConnectionRouteCommand",
                "UpdateDocumentPublicationCommand",
                "UpdateNodeLabelVisualOverrideCommand",
                "UpdateSemanticElementNameCommand",
                "UpdateSemanticElementPropertyCommand",
            ],
            commandTypes.Select(static type => type.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TypedSemanticPropertyCommandHasOnlyImmutableSemanticDescriptionData()
    {
        var type = typeof(UpdateSemanticElementPropertyCommand);
        var expected = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(UpdateSemanticElementPropertyCommand.TypeId)] = typeof(CommandTypeId),
            [nameof(UpdateSemanticElementPropertyCommand.TargetDocumentId)] = typeof(DocumentId),
            [nameof(UpdateSemanticElementPropertyCommand.ExpectedRevision)] =
                typeof(DocumentRevision),
            [nameof(UpdateSemanticElementPropertyCommand.Category)] = typeof(CommandCategory),
            [nameof(UpdateSemanticElementPropertyCommand.AffectedComponents)] =
                typeof(AuthoritativeDocumentComponent),
            [nameof(UpdateSemanticElementPropertyCommand.TargetSemanticElementId)] =
                typeof(SemanticElementId),
            [nameof(UpdateSemanticElementPropertyCommand.PropertyKey)] = typeof(string),
            [nameof(UpdateSemanticElementPropertyCommand.TargetValue)] = typeof(PropertyValue),
        };
        var properties = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.True(type.IsSealed);
        Assert.Contains(typeof(ICommand), type.GetInterfaces());
        Assert.Equal(
            expected.OrderBy(static pair => pair.Key, StringComparer.Ordinal),
            properties.ToDictionary(
                    static property => property.Name,
                    static property => property.PropertyType,
                    StringComparer.Ordinal)
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal));
        Assert.All(properties, static property => Assert.Null(property.SetMethod));
        Assert.Equal(
            [
                typeof(DocumentId),
                typeof(DocumentRevision),
                typeof(SemanticElementId),
                typeof(string),
                typeof(PropertyValue),
            ],
            Assert.Single(type.GetConstructors()).GetParameters()
                .Select(static parameter => parameter.ParameterType));
        var command = new UpdateSemanticElementPropertyCommand(
            new DocumentId("test:l5-3"),
            DocumentRevision.Zero,
            new SemanticElementId("test:element"),
            "test:property",
            PropertyValue.FromInteger(7));
        Assert.Equal(CommandCategory.Semantic, command.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.SemanticModel,
            command.AffectedComponents);
    }

    [Fact]
    public void ContextMenuUsesOneHitTestPathAndJavaScriptOnlyTransportsScalarInput()
    {
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Interaction",
            "Canvas2DInteractionController.cs");
        var pointerContract = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "ICanvasPresentationPointerObserver.cs");
        var javaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));

        Assert.Contains("PointerContextMenuAsync", controller, StringComparison.Ordinal);
        Assert.Contains("ExecutePointInteractionAsync(cssPoint, InteractionKind.ContextMenu",
            controller, StringComparison.Ordinal);
        Assert.Contains("TryConvertPoint(observed, cssPoint", controller,
            StringComparison.Ordinal);
        Assert.Contains("_hitTestService.HitTest(observed.CurrentScene, documentPoint)",
            controller, StringComparison.Ordinal);
        Assert.Contains("ResolveSelectableVisualStateId", controller, StringComparison.Ordinal);
        Assert.Contains("Selection.Contains(selectedVisualStateId)", controller,
            StringComparison.Ordinal);
        Assert.Contains("ContextMenu", pointerContract, StringComparison.Ordinal);

        Assert.Contains("contextmenu", javaScript, StringComparison.Ordinal);
        Assert.Contains("preventDefault", javaScript, StringComparison.Ordinal);
        Assert.Contains("clientX", javaScript, StringComparison.Ordinal);
        Assert.Contains("clientY", javaScript, StringComparison.Ordinal);
        string[] forbiddenJavaScriptMeaning =
        [
            "Properties...",
            "propertiesForm",
            "demo:element-number",
            "demo:description",
            "ElementNumberPropertyKey",
            "DescriptionPropertyKey",
            "VisualStateId",
            "SemanticElement",
            "Selection.Count",
            "hitTest",
            "MoveVisualStateCommand",
            "ResizeVisualStateCommand",
            "UpdateSemanticElementNameCommand",
            "UpdateSemanticElementPropertyCommand",
        ];
        Assert.DoesNotContain(forbiddenJavaScriptMeaning, fragment =>
            javaScript.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SceneRendererAndPersistentModelsContainNoPropertiesUiSemantics()
    {
        var scene = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene");
        var renderer = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering");
        var contracts = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Contracts",
            "Canvas2D");
        string[] forbidden =
        [
            "DocumentCanvasProperties",
            "PropertiesFormOpen",
            "PropertiesApply",
            "PropertiesDraft",
            "inceptus-properties-form",
            "inceptus-properties-element-number",
            "inceptus-properties-description",
            "demo:element-number",
            "demo:description",
            "object-context-menu",
        ];

        Assert.DoesNotContain(forbidden, fragment =>
            scene.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(forbidden, fragment =>
            renderer.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(forbidden, fragment =>
            contracts.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(EditingSession).Assembly.GetExportedTypes(),
            static type => type.Name.Contains("PropertiesForm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PresentationReadsImmutableSnapshotsAndNeverMutatesModelsDirectly()
    {
        var sessionBoundary = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Document.cs");
        var properties = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasProperties.cs");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var method = Assert.Single(
            typeof(EditingSession).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            static candidate =>
                candidate.Name == nameof(EditingSession.TryCaptureDocumentSnapshot));

        Assert.Equal(typeof(bool), method.ReturnType);
        Assert.Equal(typeof(DocumentSnapshot).MakeByRefType(),
            Assert.Single(method.GetParameters()).ParameterType);
        Assert.Contains("_document.CaptureSnapshot()", sessionBoundary,
            StringComparison.Ordinal);
        Assert.Contains("_closing || _closed", sessionBoundary, StringComparison.Ordinal);
        Assert.Contains("DocumentSnapshot document", properties, StringComparison.Ordinal);
        Assert.Contains("TryGetVisualState", properties, StringComparison.Ordinal);
        Assert.Contains("TryGetElement", properties, StringComparison.Ordinal);
        Assert.Contains("TryGetRelationship", properties, StringComparison.Ordinal);
        Assert.Contains("session.ExecuteForSceneTargetAsync(", host,
            StringComparison.Ordinal);
        Assert.Contains("new UpdateSemanticElementNameCommand(", host, StringComparison.Ordinal);
        Assert.Contains("new UpdateSemanticElementPropertyCommand(", host,
            StringComparison.Ordinal);
        Assert.Contains("current.VisualStateId", host, StringComparison.Ordinal);
        Assert.Contains("currentField.Definition.SemanticPropertyKey", host,
            StringComparison.Ordinal);
        Assert.Contains("currentField.Definition.MutationKind switch", host,
            StringComparison.Ordinal);
        Assert.Contains("changedDataField.TryCreateTargetValue", host,
            StringComparison.Ordinal);

        string[] forbiddenMutation =
        [
            "TryInstallState",
            "DocumentFactory.Create",
            "DocumentReconstructor",
            "new SemanticModelSnapshot",
            "new VisualModelSnapshot",
            "new SemanticElementSnapshot",
            "new VisualStateSnapshot",
            ".SemanticModel =",
            ".VisualModel =",
        ];
        Assert.DoesNotContain(forbiddenMutation, fragment =>
            (properties + host).Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void PhaseL5AddsNoBpmnSerializationOrFrozenDocumentationChanges()
    {
        var phaseSources = ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "Presentation",
                "DocumentCanvasProperties.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "Presentation",
                "DocumentCanvasHost.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "Components",
                "DocumentCanvas.razor") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "EditingSession",
                "EditingSession.Document.cs");

        Assert.DoesNotContain("BpmnSemanticTypes", phaseSources, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", phaseSources, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmn", phaseSources, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", phaseSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Serialize", phaseSources, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            ChangedPaths().Where(static path =>
                !path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)),
            static path =>
                path.Contains("Serialization", StringComparison.OrdinalIgnoreCase) &&
                    !ApprovedN102Changes.IsApprovedSerializationPath(path) ||
                path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                    !path.Equals(
                        "src/Inceptus.DocumentEngine.Blazor/Demo/bpmn-demo.inceptus.json",
                        StringComparison.OrdinalIgnoreCase));
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate '{startMarker}'.");
        return source[start..end];
    }

    private static string ReadProductionDirectory(string project, string directory)
    {
        var path = Path.Combine(RepositoryRoot, "src", project, directory);
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static string[] ChangedPaths()
    {
        var output = RunGit("status --short");
        return output.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Length > 3
                ? line[3..].Trim().Replace('\\', '/')
                : line.Trim())
            .ToArray();
    }

    private static string RunGit(string arguments)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
