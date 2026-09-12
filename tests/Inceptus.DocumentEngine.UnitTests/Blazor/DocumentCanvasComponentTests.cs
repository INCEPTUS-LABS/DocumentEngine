using System.Reflection;
using System.Text.RegularExpressions;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Validation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class DocumentCanvasComponentTests
{
    [Theory]
    [InlineData("form", "properties-form")]
    [InlineData("type", "properties-type")]
    [InlineData("semantic-id", "properties-semantic-id")]
    [InlineData("visual-id", "properties-visual-id")]
    [InlineData("x", "properties-x")]
    [InlineData("apply", "properties-apply")]
    [InlineData("close", "properties-close")]
    public void DataFieldControlsUseADisjointDomIdentityNamespace(
        string fieldId,
        string frameworkOwnedId)
    {
        var component = new DocumentCanvas();
        var method = typeof(DocumentCanvas).GetMethod(
            "PropertiesFieldControlId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var prefix = Assert.IsType<string>(typeof(DocumentCanvas)
            .GetField("_domIdPrefix", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(component));

        var actual = Assert.IsType<string>(method!.Invoke(
            component,
            [new ElementPropertyFieldId(fieldId)]));

        Assert.Equal($"{prefix}-properties-data-field-{fieldId}", actual);
        Assert.NotEqual($"{prefix}-{frameworkOwnedId}", actual);
    }

    [Fact]
    public async Task RepeatedStaticRendersContainOneActiveCanvasAndOneReplacementBuffer()
    {
        var jsRuntime = new RecordingJsRuntime();
        var services = new ServiceCollection()
            .AddSingleton<IJSRuntime>(jsRuntime)
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        var component = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<DocumentCanvas>());
        var initial = await renderer.Dispatcher.InvokeAsync(component.ToHtmlString);
        var repeatedComponent = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<DocumentCanvas>(ParameterView.Empty));
        var rerendered = await renderer.Dispatcher.InvokeAsync(repeatedComponent.ToHtmlString);
        var prefix = ExtractDomIdPrefix(initial);

        AssertDoubleBufferedCanvas(initial);
        AssertDoubleBufferedCanvas(rerendered);
        Assert.Contains("data-presentation-status=\"uninitialized\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-document-id=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-document-revision=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-active-scope-id=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-scope-breadcrumb=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-history-entry-count=\"0\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-can-undo=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-can-redo=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-viewport-zoom=\"1\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-zoom-percentage=\"100\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-can-zoom-out=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-can-set-zoom-100=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-can-zoom-in=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-context-menu-open=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-context-menu-kind=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-context-target-visual-state-id=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-open-scope-available=\"false\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-add-connector-point-available=\"false\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-delete-connector-point-available=\"false\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-properties-available=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-properties-form-open=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-properties-target-visual-state-id=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-properties-dirty=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-properties-apply-status=\"idle\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-properties-data-field-count=\"0\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-properties-data-field-ids=\"\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-model-view-properties-form-open=\"false\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-model-view-properties-dirty=\"false\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-model-view-properties-apply-status=\"idle\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-graphical-interaction-enabled=\"false\"", initial, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-selection-count=\"0\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-selected-visual-state-ids=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-selected-scene-object-id=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-hovered-scene-object-id=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-selected-semantic-element-id=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-hovered-semantic-element-id=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-selected-visual-state-id=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-selected-document-bounds=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-selected-route=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-active-gesture-kind=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-canvas-cursor=\"default\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-interaction-diagnostic-code=\"\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-validation-state=\"not-validated\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-validation-issue-count=\"0\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-validation-error-count=\"0\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-validation-warning-count=\"0\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-validation-info-count=\"0\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-can-validate=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("data-issues-panel-open=\"false\"", initial,
            StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-undo\"", initial, StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-redo\"", initial, StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-import\"", initial, StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-new-diagram\"", initial, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"New diagram\"", initial, StringComparison.Ordinal);
        Assert.Contains("title=\"New diagram\"", initial, StringComparison.Ordinal);
        Assert.Matches(
            $"<button id=\"{prefix}-new-diagram\"[\\s\\S]*?type=\"button\"",
            initial);
        Assert.Contains($"id=\"{prefix}-import-file\"", initial, StringComparison.Ordinal);
        Assert.Contains("accept=\".inceptus.json,application/json\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("title=\"Import native Document (maximum 16 MiB)\"", initial,
            StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-export\"", initial, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Export native Document\"", initial,
            StringComparison.Ordinal);
        Assert.Matches(
            "<span class=\"native-document-action-icon\"[^>]*aria-hidden=\"true\"[^>]*>⇩</span>",
            initial);
        Assert.Matches(
            "<span class=\"native-document-action-icon\"[^>]*aria-hidden=\"true\"[^>]*>⇧</span>",
            initial);
        Assert.DoesNotMatch(">\\s*Import\\s*<", initial);
        Assert.DoesNotMatch(">\\s*Export\\s*<", initial);
        Assert.DoesNotContain("Inceptus Document Engine", initial, StringComparison.Ordinal);
        Assert.DoesNotContain("Persistent visual editing pipeline", initial,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Move, resize, and route-edit diagram objects through transient previews",
            initial,
            StringComparison.Ordinal);
        Assert.Contains("data-native-document-operation=\"idle\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-native-document-error-code=\"\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-new-diagram-operation=\"idle\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-new-diagram-error-code=\"\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-new-diagram-confirmation=\"closed\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-publication-dialog=\"closed\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-publication-draft-valid=\"false\"", initial,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"id=\"{prefix}-new-diagram-dialog\"", initial,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"id=\"{prefix}-publication-dialog\"", initial,
            StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-publish\"", initial, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Publish\"", initial, StringComparison.Ordinal);
        Assert.Contains($"data-active-canvas-id=\"{prefix}-document-canvas\"", initial,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"id=\"{prefix}-native-document-error\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("role=\"toolbar\"", initial, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Editor toolbar\"", initial, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Process scope\"", initial, StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-zoom-out\"", initial, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Zoom out\"", initial, StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-zoom-100\"", initial, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Set zoom to 100%\"", initial, StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-zoom-in\"", initial, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Zoom in\"", initial, StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-validate\"", initial, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Model validation\"", initial,
            StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-editor-status\"", initial,
            StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-issues-toggle\"", initial,
            StringComparison.Ordinal);
        Assert.Contains($"aria-controls=\"{prefix}-issues-panel\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("Errors 0", initial, StringComparison.Ordinal);
        Assert.Contains("Warnings 0", initial, StringComparison.Ordinal);
        Assert.Contains("Info 0", initial, StringComparison.Ordinal);
        Assert.Contains("Not validated", initial, StringComparison.Ordinal);
        Assert.DoesNotContain($"id=\"{prefix}-issues-panel\"", initial,
            StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"issues-list\"", initial, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"presentation-status\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("class=\"document-editor-layout\"", initial, StringComparison.Ordinal);
        Assert.Contains("class=\"document-editor-workspace\"", initial, StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-toolbox\"", initial, StringComparison.Ordinal);
        Assert.Contains($"aria-labelledby=\"{prefix}-toolbox-heading\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-selected-toolbox-item-id=\"\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-toolbox-item-id=\"bpmn:toolbox:start-event\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-toolbox-item-id=\"bpmn:toolbox:user-task\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("data-toolbox-item-id=\"bpmn:toolbox:sub-process\"", initial,
            StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"false\"", initial, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", initial, StringComparison.Ordinal);
        Assert.DoesNotContain($"id=\"{prefix}-object-context-menu\"", initial, StringComparison.Ordinal);
        Assert.DoesNotContain($"id=\"{prefix}-model-context-menu\"", initial,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"id=\"{prefix}-properties-form\"", initial, StringComparison.Ordinal);
        Assert.DoesNotContain($"id=\"{prefix}-model-view-properties-form\"", initial,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"id=\"{prefix}-properties-apply\"", initial, StringComparison.Ordinal);
        Assert.True(
            initial.IndexOf($"id=\"{prefix}-toolbox\"", StringComparison.Ordinal) <
            initial.IndexOf("role=\"toolbar\"", StringComparison.Ordinal) &&
            initial.IndexOf("role=\"toolbar\"", StringComparison.Ordinal) <
            initial.IndexOf("<canvas", StringComparison.Ordinal));
        Assert.DoesNotContain("@onclick", initial, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", initial, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(
            initial,
            "role=\"status\"",
            RegexOptions.CultureInvariant));
        Assert.DoesNotContain("role=\"alert\"", initial, StringComparison.Ordinal);
        Assert.Equal(0, jsRuntime.InvocationCount);
    }

    [Fact]
    public async Task TwoModelerInstancesRenderDisjointStableDomIdentitySets()
    {
        var services = new ServiceCollection()
            .AddSingleton<IJSRuntime>(new RecordingJsRuntime())
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        var first = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<InceptusBpmnModeler>());
        var second = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<InceptusBpmnModeler>());
        var firstMarkup = await renderer.Dispatcher.InvokeAsync(first.ToHtmlString);
        var secondMarkup = await renderer.Dispatcher.InvokeAsync(second.ToHtmlString);
        var firstIds = ExtractElementIds(firstMarkup);
        var secondIds = ExtractElementIds(secondMarkup);

        Assert.NotEmpty(firstIds);
        Assert.NotEmpty(secondIds);
        Assert.NotEqual(ExtractDomIdPrefix(firstMarkup), ExtractDomIdPrefix(secondMarkup));
        Assert.Empty(firstIds.Intersect(secondIds, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("NATIVE_DOCUMENT_JSON_MALFORMED", "not valid native Document JSON")]
    [InlineData("NATIVE_DOCUMENT_FORMAT_INVALID", "not an Inceptus native Document")]
    [InlineData("NATIVE_DOCUMENT_VERSION_UNSUPPORTED", "version is not supported")]
    [InlineData("NATIVE_DOCUMENT_STRUCTURE_INVALID", "structurally valid native Document")]
    [InlineData("CANVAS_NATIVE_DOCUMENT_REPLACEMENT_FAILED", "could not be opened")]
    public void NativeImportDiagnosticsMapToBoundedOperationMessages(
        string code,
        string expectedMessageFragment)
    {
        var result = NativeDocumentHostImportResult.Rejected(
        [
            new Diagnostic(
                code,
                DiagnosticSeverity.Error,
                "Raw implementation detail that must not reach the user.",
                "System.InvalidOperationException"),
        ]);

        var (actualCode, actualMessage) = DocumentCanvas.NativeDocumentImportMessage(result);

        Assert.Equal(code, actualCode);
        Assert.Contains(expectedMessageFragment, actualMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("implementation detail", actualMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.", actualMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulNativeImportClearsAStaleTransientOperationError()
    {
        var component = new DocumentCanvas();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var complete = typeof(DocumentCanvas).GetMethod(
            "CompleteNativeDocumentOperation",
            flags)!;
        var clear = typeof(DocumentCanvas).GetMethod(
            "ClearNativeDocumentOperationError",
            flags)!;
        var errorCode = typeof(DocumentCanvas).GetField(
            "_nativeDocumentOperationErrorCode",
            flags)!;
        var errorMessage = typeof(DocumentCanvas).GetField(
            "_nativeDocumentOperationErrorMessage",
            flags)!;

        complete.Invoke(component, ["NATIVE_DOCUMENT_JSON_MALFORMED", "Invalid file."]);
        Assert.Equal("NATIVE_DOCUMENT_JSON_MALFORMED", errorCode.GetValue(component));
        Assert.Equal("Invalid file.", errorMessage.GetValue(component));

        clear.Invoke(component, null);

        Assert.Null(errorCode.GetValue(component));
        Assert.Null(errorMessage.GetValue(component));
    }

    [Fact]
    public void NativeDocumentBrowserPolicyUsesTheDeterministicFallbackAndHostLocalLimit()
    {
        Assert.Equal("application/json", DocumentCanvas.NativeDocumentContentType);
        Assert.Equal("document.inceptus.json", DocumentCanvas.NativeDocumentFileName);
        Assert.EndsWith(".inceptus.json", DocumentCanvas.NativeDocumentFileName,
            StringComparison.Ordinal);
        Assert.Equal(16L * 1024L * 1024L, DocumentCanvas.MaximumNativeDocumentFileSize);
    }

    [Fact]
    public async Task EscapeClearsToolboxSelectionBeforeBrowserHostInitialization()
    {
        var component = new DocumentCanvas();
        var selection = Assert.IsType<ToolboxSelectionState>(typeof(DocumentCanvas)
            .GetField("_toolboxSelection", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(component));
        var itemId = new ToolboxItemId("test:item");
        Assert.True(selection.Select(itemId));
        var handler = typeof(DocumentCanvas).GetMethod(
            "HandleEditorKeyDownAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        var result = Assert.IsAssignableFrom<Task>(handler!.Invoke(
            component,
            [new KeyboardEventArgs { Key = "Escape" }]));
        await result;

        Assert.Null(selection.SelectedItemId);
    }

    [Fact]
    public async Task EscapeCancelsNewDiagramConfirmationBeforeAnyHostOperation()
    {
        var component = new DocumentCanvas();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var confirmation = typeof(DocumentCanvas).GetField(
            "_newDiagramConfirmationOpen",
            flags)!;
        var operation = typeof(DocumentCanvas).GetField(
            "_newDiagramOperationStatus",
            flags)!;
        confirmation.SetValue(component, true);
        operation.SetValue(component, "confirming");
        var handler = typeof(DocumentCanvas).GetMethod("HandleEditorKeyDownAsync", flags)!;

        await Assert.IsAssignableFrom<Task>(handler.Invoke(
            component,
            [new KeyboardEventArgs { Key = "Escape" }]));

        Assert.False(Assert.IsType<bool>(confirmation.GetValue(component)));
        Assert.Equal("idle", operation.GetValue(component));
    }

    [Fact]
    public void CancelButtonClosesNewDiagramConfirmationWithoutInvokingAHost()
    {
        var component = new DocumentCanvas();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var confirmation = typeof(DocumentCanvas).GetField(
            "_newDiagramConfirmationOpen",
            flags)!;
        var operation = typeof(DocumentCanvas).GetField(
            "_newDiagramOperationStatus",
            flags)!;
        confirmation.SetValue(component, true);
        operation.SetValue(component, "confirming");
        var cancel = typeof(DocumentCanvas).GetMethod(
            "CancelNewDiagramConfirmation",
            flags)!;

        cancel.Invoke(component, null);

        Assert.False(Assert.IsType<bool>(confirmation.GetValue(component)));
        Assert.Equal("idle", operation.GetValue(component));
    }

    [Fact]
    public void PublicationDraftValidationAndCancelRemainTransient()
    {
        var component = new DocumentCanvas();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var open = typeof(DocumentCanvas).GetField("_publicationDialogOpen", flags)!;
        var code = typeof(DocumentCanvas).GetField("_publicationCodeDraft", flags)!;
        var title = typeof(DocumentCanvas).GetField("_publicationTitleDraft", flags)!;
        var description = typeof(DocumentCanvas).GetField(
            "_publicationDescriptionDraft",
            flags)!;
        var status = typeof(DocumentCanvas).GetField("_publishOperationStatus", flags)!;
        var valid = typeof(DocumentCanvas).GetProperty("PublicationDraftIsValid", flags)!;
        var cancel = typeof(DocumentCanvas).GetMethod("CancelPublicationDialog", flags)!;

        open.SetValue(component, true);
        status.SetValue(component, "editing");
        code.SetValue(component, "Invalid Code");
        title.SetValue(component, "Title");
        Assert.False(Assert.IsType<bool>(valid.GetValue(component)));
        code.SetValue(component, " process-a ");
        title.SetValue(component, " Process A ");
        description.SetValue(component, " Draft only ");
        Assert.True(Assert.IsType<bool>(valid.GetValue(component)));

        cancel.Invoke(component, null);

        Assert.False(Assert.IsType<bool>(open.GetValue(component)));
        Assert.Equal(string.Empty, code.GetValue(component));
        Assert.Equal(string.Empty, title.GetValue(component));
        Assert.Equal(string.Empty, description.GetValue(component));
        Assert.Equal("idle", status.GetValue(component));
    }

    [Fact]
    public async Task EscapeCancelsPublicationDialogBeforeAnyHostOperation()
    {
        var component = new DocumentCanvas();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var open = typeof(DocumentCanvas).GetField("_publicationDialogOpen", flags)!;
        var code = typeof(DocumentCanvas).GetField("_publicationCodeDraft", flags)!;
        var status = typeof(DocumentCanvas).GetField("_publishOperationStatus", flags)!;
        open.SetValue(component, true);
        code.SetValue(component, "draft");
        status.SetValue(component, "editing");
        var handler = typeof(DocumentCanvas).GetMethod("HandleEditorKeyDownAsync", flags)!;

        await Assert.IsAssignableFrom<Task>(handler.Invoke(
            component,
            [new KeyboardEventArgs { Key = "Escape" }]));

        Assert.False(Assert.IsType<bool>(open.GetValue(component)));
        Assert.Equal(string.Empty, code.GetValue(component));
        Assert.Equal("idle", status.GetValue(component));
    }

    [Fact]
    public void OpeningPublicationDialogUsesLatestAuthoritativePublicationAndFreshDraft()
    {
        var component = new DocumentCanvas();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var documentId = new DocumentId("test:n10.6:dialog");
        var revision = new DocumentRevision(12);
        var scopeId = new DocumentScopeId(documentId.Value);
        var validation = new ValidationSnapshot(documentId, scopeId, revision);
        var open = typeof(DocumentCanvas).GetMethod(
            "InitializePublicationDialogDraft",
            flags)!;
        var cancel = typeof(DocumentCanvas).GetMethod("CancelPublicationDialog", flags)!;
        var code = typeof(DocumentCanvas).GetField("_publicationCodeDraft", flags)!;
        var title = typeof(DocumentCanvas).GetField("_publicationTitleDraft", flags)!;
        var description = typeof(DocumentCanvas).GetField(
            "_publicationDescriptionDraft",
            flags)!;
        var emptyState = HostState(
            SessionState(documentId, revision, scopeId),
            validation,
            publication: null,
            documentSessionVersion: 4);

        open.Invoke(component, [emptyState, emptyState.Session]);

        Assert.Equal(string.Empty, code.GetValue(component));
        Assert.Equal(string.Empty, title.GetValue(component));
        Assert.Equal(string.Empty, description.GetValue(component));
        cancel.Invoke(component, null);
        var first = new DocumentPublicationSnapshot(
            "process-a",
            "Process A",
            "Description A");
        var firstState = HostState(
            SessionState(documentId, revision, scopeId),
            validation,
            first,
            documentSessionVersion: 4);

        open.Invoke(component, [firstState, firstState.Session]);

        Assert.Equal(first.Code, code.GetValue(component));
        Assert.Equal(first.Title, title.GetValue(component));
        Assert.Equal(first.Description, description.GetValue(component));
        code.SetValue(component, "stale-draft");
        cancel.Invoke(component, null);
        var second = new DocumentPublicationSnapshot(
            "process-b",
            "Process B",
            "Description B");
        var secondState = HostState(
            SessionState(documentId, revision, scopeId),
            validation,
            second,
            documentSessionVersion: 4);

        open.Invoke(component, [secondState, secondState.Session]);

        Assert.Equal(second.Code, code.GetValue(component));
        Assert.Equal(second.Title, title.GetValue(component));
        Assert.Equal(second.Description, description.GetValue(component));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedSaveAndPublishDiscardDialogDraftBeforeHostWork(bool publish)
    {
        var component = new DocumentCanvas();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var dialogOpen = typeof(DocumentCanvas).GetField("_publicationDialogOpen", flags)!;
        var code = typeof(DocumentCanvas).GetField("_publicationCodeDraft", flags)!;
        var title = typeof(DocumentCanvas).GetField("_publicationTitleDraft", flags)!;
        var description = typeof(DocumentCanvas).GetField(
            "_publicationDescriptionDraft",
            flags)!;
        dialogOpen.SetValue(component, true);
        code.SetValue(component, "process-a");
        title.SetValue(component, "Process A");
        description.SetValue(component, "Description A");
        typeof(DocumentCanvas).GetField("_publicationSourceDocumentId", flags)!.SetValue(
            component,
            new DocumentId("test:n10.6:dialog-action"));
        typeof(DocumentCanvas).GetField("_publicationSourceRevision", flags)!.SetValue(
            component,
            new DocumentRevision(7));
        var action = typeof(DocumentCanvas).GetMethod(
            "ExecutePublicationDialogActionAsync",
            flags)!;

        await Assert.IsAssignableFrom<Task>(action.Invoke(component, [publish]));

        Assert.False(Assert.IsType<bool>(dialogOpen.GetValue(component)));
        Assert.Equal(string.Empty, code.GetValue(component));
        Assert.Equal(string.Empty, title.GetValue(component));
        Assert.Equal(string.Empty, description.GetValue(component));
    }

    [Fact]
    public void OpenIssuesPanelKeepsItsPositionAndShowsNotValidatedForAnotherScope()
    {
        var component = new DocumentCanvas();
        var issuesPanel = Assert.IsType<FloatingIssuesPanelState>(typeof(DocumentCanvas)
            .GetField("_issuesPanel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(component));
        var stateField = typeof(DocumentCanvas).GetField(
            "_state",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var validationState = typeof(DocumentCanvas).GetProperty(
            "ValidationStateValue",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var issuesPanelOpen = typeof(DocumentCanvas).GetProperty(
            "IssuesPanelOpenValue",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var documentId = new DocumentId("test:n82:issues-document");
        var revision = new DocumentRevision(12);
        var rootScopeId = new DocumentScopeId(documentId.Value);
        var childScopeId = new DocumentScopeId("test:n82:issues-child");
        var rootValidation = new ValidationSnapshot(documentId, rootScopeId, revision);

        issuesPanel.Open(900d, 600d);
        var openPosition = (issuesPanel.Left, issuesPanel.Top);
        stateField.SetValue(component, HostState(
            SessionState(documentId, revision, rootScopeId),
            rootValidation));
        Assert.Equal("true", issuesPanelOpen.GetValue(component));
        Assert.Equal("current", validationState.GetValue(component));

        stateField.SetValue(component, HostState(
            SessionState(documentId, revision, childScopeId),
            rootValidation));

        Assert.True(issuesPanel.IsOpen);
        Assert.Equal(openPosition, (issuesPanel.Left, issuesPanel.Top));
        Assert.Equal("true", issuesPanelOpen.GetValue(component));
        Assert.Equal("not-validated", validationState.GetValue(component));
    }

    private static void AssertDoubleBufferedCanvas(string markup)
    {
        var prefix = ExtractDomIdPrefix(markup);
        Assert.Equal(
            2,
            Regex.Count(markup, "<canvas\\b", RegexOptions.CultureInvariant));
        Assert.Contains($"id=\"{prefix}-document-canvas\"", markup, StringComparison.Ordinal);
        Assert.Contains($"id=\"{prefix}-document-canvas-standby\"", markup,
            StringComparison.Ordinal);
        Assert.Single(Regex.Matches(
            markup,
            "document-canvas-buffer-active",
            RegexOptions.CultureInvariant));
        Assert.Single(Regex.Matches(
            markup,
            "<canvas[^>]*tabindex=\"0\"",
            RegexOptions.CultureInvariant));
        Assert.Single(Regex.Matches(
            markup,
            "<canvas[^>]*tabindex=\"-1\"",
            RegexOptions.CultureInvariant));
        Assert.Contains(
            $"id=\"{prefix}-document-canvas-container\"",
            markup,
            StringComparison.Ordinal);
    }

    private static string ExtractDomIdPrefix(string markup)
    {
        var match = Regex.Match(
            markup,
            "data-active-canvas-id=\"(?<prefix>inceptus-[0-9a-f]{32})-document-canvas\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "The modeler DOM identity prefix was not rendered.");
        return match.Groups["prefix"].Value;
    }

    private static string[] ExtractElementIds(string markup) =>
        Regex.Matches(markup, "(?<=\\s)id=\"(?<id>[^\"]+)\"", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["id"].Value)
            .ToArray();

    private static EditingSessionState SessionState(
        DocumentId documentId,
        DocumentRevision revision,
        DocumentScopeId scopeId) => new(
            documentId,
            revision,
            scopeId,
            EditingSessionStatus.RuntimeFaulted,
            new EditingSessionGeneration(1),
            currentScene: null,
            lastKnownGoodScene: null,
            projectedGraph: null,
            layoutResult: null,
            routingResult: null,
            EditorStateSnapshot.Empty,
            new HistoryStatus(0, canUndo: false, canRedo: false),
            runtimeDiagnostics: null,
            presentationDiagnostics: null,
            isClosed: false);

    private static DocumentCanvasHostState HostState(
        EditingSessionState session,
        ValidationSnapshot validation,
        DocumentPublicationSnapshot? publication = null,
        long documentSessionVersion = 0) => new(
            session,
            SurfaceSize: null,
            HostDiagnostics: [],
            InteractionDiagnostics: [],
            PipelineCounters: null,
            InitializationAttempted: true,
            IsInitialized: true,
            IsDisposed: false,
            SuccessfulRenderCount: 1,
            LatestPresentationSucceeded: true,
            CssCursor: "default",
            ContextMenu: null,
            PropertiesTargetVisualStateId: null,
            PropertiesFormOpen: false,
            PropertiesFormDirty: false,
            PropertiesApplyInFlight: false,
            ValidationSnapshot: validation,
            ValidationInFlight: false,
            ScopeBreadcrumb: [],
            Publication: publication,
            DocumentSessionVersion: documentSessionVersion);

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        internal int InvocationCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            object?[]? args)
        {
            InvocationCount++;
            return ValueTask.FromException<TValue>(
                new InvalidOperationException("Static HTML rendering must not invoke browser interop."));
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            InvocationCount++;
            return ValueTask.FromException<TValue>(
                new InvalidOperationException("Static HTML rendering must not invoke browser interop."));
        }
    }
}
