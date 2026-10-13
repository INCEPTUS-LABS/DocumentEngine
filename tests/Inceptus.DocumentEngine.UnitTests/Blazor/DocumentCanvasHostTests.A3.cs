using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A3PropertiesCancellationNotifiesHostThatDocumentActionsAreAvailable(bool modelView)
    {
        await using var fixture = new FacadeTestFixture(await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync());
        await fixture.InitializeAsync();
        var session = Session(fixture.Host);
        var document = fixture.Facade.GetDocument().Snapshot;
        var history = session.CaptureState().HistoryStatus;
        var availability = new List<bool>();
        fixture.Facade.ViewChanged += () => availability.Add(fixture.Facade.CaptureEditorContext()!.CanReplaceDocument);
        if (modelView)
        {
            Assert.NotNull(await OpenModelViewPropertiesAsync(fixture.Host));
        }
        else
        {
            await session.UpdateEditorStateAsync(new EditorStateSnapshot(selection: [BpmnDemoPipeline.TaskVisualId],
                viewport: session.CaptureState().EditorState.Viewport));
            Assert.NotNull(await fixture.Host.OpenPropertiesAsync(BpmnDemoPipeline.TaskVisualId));
        }
        Assert.False(fixture.Facade.CaptureEditorContext()!.CanReplaceDocument);
        Assert.Contains(false, availability);
        availability.Clear();
        Assert.True(modelView
            ? fixture.Host.UpdateModelViewPropertiesFormState(false, false)
            : fixture.Host.UpdatePropertiesFormState(false, null, false));
        Assert.True(Assert.Single(availability));
        Assert.False(fixture.Facade.CaptureEditorContext()!.HasOpenProperties);
        Assert.True(fixture.Facade.CaptureEditorContext()!.CanReplaceDocument);
        Assert.Same(document, fixture.Facade.GetDocument().Snapshot);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
    }

    [Fact]
    public async Task A3HostDisplaysBoundedValidationFailureAndClearsItAfterSuccessfulRetry()
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)));
        await host.InitializeAsync("a3-validation-canvas", "a3-validation-container");
        var rule = new A3FaultingValidationRule();
        host.ConfigureOperations(new ModelValidationCatalog([rule]));
        var original = host.GetDocument().Snapshot;
        var history = Session(host).CaptureState().HistoryStatus;
        var activator = new DemoPublishComponentActivator(host);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(new PublishDownloadRuntime())
            .AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<BpmnDemoHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(BpmnDemoHost.Editor)] = host })));
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await InvokePublishComponentAsync(activator.Component, "ValidateAsync");
            await activator.Component.RefreshAsync();
        });
        var failedMarkup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Contains("data-validation-diagnostic-code=\"CANVAS_MODEL_VALIDATION_FAILED\"", failedMarkup, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", failedMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("private plugin detail", failedMarkup, StringComparison.Ordinal);
        Assert.Null(host.Operations().ValidationSnapshot);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            InvokePublishComponent(activator.Component, "DismissOperationFeedback");
            await activator.Component.RefreshAsync();
        });
        Assert.Empty(host.Operations().Diagnostics);
        Assert.DoesNotContain("operation-feedback-panel", await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString), StringComparison.Ordinal);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await InvokePublishComponentAsync(activator.Component, "ValidateAsync");
            await activator.Component.RefreshAsync();
        });
        Assert.Contains("operation-feedback-panel", await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString), StringComparison.Ordinal);
        rule.Throw = false;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await InvokePublishComponentAsync(activator.Component, "ValidateAsync");
            await activator.Component.RefreshAsync();
        });
        Assert.DoesNotContain("data-validation-diagnostic-code", await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString), StringComparison.Ordinal);
        Assert.NotNull(host.Operations().ValidationSnapshot);
        Assert.Same(original, host.GetDocument().Snapshot);
        Assert.Equal(history, Session(host).CaptureState().HistoryStatus);
    }

    [Theory]
    [InlineData("NATIVE_DOCUMENT_JSON_MALFORMED", "Error_ImportMalformed")]
    [InlineData("CANVAS_NATIVE_DOCUMENT_EXPORT_FAILED", "Error_ExportFailed")]
    public async Task A3NativeFeedbackPanelCanBeDismissedWithoutChangingDocumentOrHistory(string code, string message)
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(900, 600, 1)));
        await host.InitializeAsync("a3-feedback-canvas", "a3-feedback-container");
        var original = host.GetDocument().Snapshot;
        var history = Session(host).CaptureState().HistoryStatus;
        var activator = new DemoPublishComponentActivator(host);
        using var services = PublishComponentServices(activator, new PublishDownloadRuntime());
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<BpmnDemoHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(BpmnDemoHost.Editor)] = host })));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                typeof(BpmnDemoHost).GetMethod("CompleteNativeDocumentOperation",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(activator.Component, [code, message]);
                await activator.Component.RefreshAsync();
            });
            var markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
            Assert.Contains("class=\"operation-feedback-panel\"", markup, StringComparison.Ordinal);
            Assert.Contains($"data-native-document-error-code=\"{code}\"", markup, StringComparison.Ordinal);
            Assert.Contains("role=\"alert\"", markup, StringComparison.Ordinal);
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                InvokePublishComponent(activator.Component, "DismissOperationFeedback");
                await activator.Component.RefreshAsync();
            });
            var dismissed = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
            Assert.DoesNotContain("operation-feedback-panel", dismissed, StringComparison.Ordinal);
            Assert.Contains("data-native-document-error-code=\"\"", dismissed, StringComparison.Ordinal);
        }
        Assert.Same(original, host.GetDocument().Snapshot);
        Assert.Equal(history, Session(host).CaptureState().HistoryStatus);
    }

    private sealed class A3FaultingValidationRule : IModelValidationRule
    {
        internal bool Throw { get; set; } = true;
        public ModelValidationRuleId RuleId { get; } = new("test:a3-validation-failure");
        public ImmutableArray<ModelValidationIssue> Validate(ModelValidationContext context) =>
            Throw ? throw new InvalidOperationException("private plugin detail") : [];
    }

    [Fact]
    public async Task A3PublicDocumentApiSupportsReplacementEditingHistoryAndHostWorkflows()
    {
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new Canvas2DSurfaceSize(1400, 900, 1)),
            compositionFactory: await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync(),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        await host.InitializeAsync("a3-active", "a3-standby", "a3-container");
#pragma warning disable CA1859 // Exercise the public host boundary, not internal host overloads.
        IBpmnDocumentEditor editor = host;
#pragma warning restore CA1859
        var original = editor.GetDocument().Snapshot!;
        var oldSession = Session(host);
        var replacement = await editor.SetDocumentAsync(original);
        Assert.True(replacement.Succeeded);
        Assert.Equal(original, editor.GetDocument().Snapshot);
        Assert.NotSame(oldSession, Session(host));
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        var session = Session(host);
        var task = original.SemanticModel.Elements.First(element => element.TypeId == BpmnSemanticTypes.Task);
        Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(original.DocumentId,
            original.Revision, task.Id, BpmnSemanticProperties.Name, "Edited after SetDocument"))).IsCommitted);
        await session.WaitForIdleAsync();
        Assert.Equal(1, session.CaptureState().HistoryStatus.EntryCount);
        await host.UndoAsync();
        Assert.True(session.CaptureState().HistoryStatus.CanRedo);
        await host.RedoAsync();
        Assert.False(session.CaptureState().HistoryStatus.CanRedo);
        await SaveDefaultPublicationAsync(session);
        var source = editor.GetDocument().Snapshot!;
        var before = session.CaptureState();
        var operations = new DemoDocumentOperations(editor);
        var exported = await operations.ExportNativeDocumentAsync();
        Assert.True(exported.Succeeded);
        Assert.Equal(source, BpmnDocumentSerializer.Deserialize(exported.Payload.AsMemory()).Document);
        Assert.NotNull(await operations.ValidateAsync());
        var presentation = (await editor.CapturePresentationAsync())!.Capture!;
        var generated = new PublishingPackageGenerator().Generate(source, presentation);
        Assert.True(generated.Succeeded);
        var published = await operations.PublishProcessAsync();
        Assert.True(published.Succeeded);
        Assert.Equal(generated.Package!.Archive.ToArray(), published.Payload.ToArray());
        Assert.Equal(FacadeExpectedPublishEntries, ReadPublishedArchive(published.Payload).Keys);
        Assert.Same(source, editor.GetDocument().Snapshot);
        Assert.Equal(before.HistoryStatus, session.CaptureState().HistoryStatus);
        var imported = await operations.ImportNativeDocumentAsync(exported.Payload.AsMemory());
        Assert.True(imported.Succeeded);
        Assert.Equal(source, editor.GetDocument().Snapshot);
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        Assert.Null(operations.ValidationSnapshot);
    }
}
