using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.UnitTests.Bpmn;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task MainDiagramScopeNavigationNotifiesHostAfterPresentationIsReady()
    {
        await using var fixture = new FacadeTestFixture(await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync());
        await fixture.InitializeAsync();
        var imported = BpmnStandardConverter.Import(BpmnStandardConverterTests.Xml(
            "<process id='nested'><subProcess id='sub'><task id='child'/></subProcess></process>"));
        Assert.True(imported.Succeeded);
        var installed = await fixture.Facade.SetDocumentAsync(imported.Document!);
        Assert.True(installed.Succeeded, string.Join("; ", installed.Diagnostics.Select(diagnostic => diagnostic.Message)));
        var document = fixture.Facade.GetDocument().Snapshot!;
        var child = Assert.Single(document.SemanticModel.NestedScopes).Id;
        foreach (var scope in new[] { child, document.SemanticModel.RootScopeId })
        {
            var observed = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var ready = new TaskCompletionSource<BpmnEditorContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnViewChanged()
            {
                var captured = fixture.Facade.CaptureEditorContext();
                observed.Enqueue(captured is null ? "null" : $"{captured.Presentation.ActiveScopeId} {captured.Presentation.Status} replace:{captured.CanReplaceDocument} scene:{captured.Presentation.CurrentScene is not null}");
                if (captured is { CanReplaceDocument: true } context &&
                    context.Presentation.ActiveScopeId == scope && context.Presentation.CurrentScene is not null &&
                    context.Presentation.ProjectedGraph is not null && context.Presentation.LayoutResult is not null &&
                    context.Presentation.RoutingResult is not null)
                    ready.TrySetResult(context);
            }
            fixture.Facade.ViewChanged += OnViewChanged;
            BpmnEditorContext last;
            try
            {
                Assert.True((await fixture.Host.NavigateToScopeAsync(scope))?.Succeeded);
                last = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                Assert.Fail($"No ready context for {scope}; events: {string.Join(", ", observed)}; final: {fixture.Facade.CaptureEditorContext()?.Presentation.Status}");
                throw;
            }
            finally { fixture.Facade.ViewChanged -= OnViewChanged; }
            Assert.True(last.CanReplaceDocument);
            Assert.Equal(scope, last.Presentation.ActiveScopeId);
            Assert.NotNull(last.Presentation.CurrentScene);
            Assert.NotNull(last.Presentation.ProjectedGraph);
            Assert.NotNull(last.Presentation.LayoutResult);
            Assert.NotNull(last.Presentation.RoutingResult);
        }
    }

    [Theory]
    [InlineData("processes")]
    [InlineData("collaborations")]
    [InlineData("references")]
    public async Task MainDiagramRejectedImportPreservesEditedDocumentSessionHistoryAndSaveState(string kind)
    {
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1200, 800, 1)),
            compositionFactory: await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync());
        await host.InitializeAsync("main-atomic", "main-container");
        var session = Session(host);
        var initial = host.GetDocument().Snapshot!;
        var task = initial.SemanticModel.Elements.First(element => BpmnTaskSemanticTypes.IsTask(element.TypeId));
        Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(initial.DocumentId, initial.Revision,
            task.Id, BpmnSemanticProperties.Name, "Unsaved edit"))).IsCommitted);
        await session.WaitForIdleAsync();
        var document = host.GetDocument().Snapshot;
        var history = session.CaptureState().HistoryStatus;
        var save = host.NativeSaveStatus!;
        var xml = BpmnStandardConverterTests.Xml(kind switch
        {
            "processes" => "<process id='a'/><process id='b'/>",
            "collaborations" => "<collaboration id='a'/><collaboration id='b'/>",
            _ => "<process id='p'><task id='a'/><sequenceFlow id='f' sourceRef='a' targetRef='missing'/></process>",
        });
        var operations = new DemoDocumentOperations(host);
        var result = await operations.ImportStandardDocumentAsync(xml, BpmnStandardConverterTests.Preparer());
        Assert.False(result.Succeeded);
        Assert.Same(document, host.GetDocument().Snapshot);
        Assert.Same(session, Session(host));
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.Equal(save.SavedCheckpoint, host.NativeSaveStatus!.SavedCheckpoint);
        Assert.Equal(save.IsDirty, host.NativeSaveStatus.IsDirty);
        Assert.Null(operations.ValidationSnapshot);
        Assert.DoesNotContain(operations.ConversionLog, entry => entry.Code.StartsWith("BPMN_IMPORT_", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MainDiagramValidationSummaryReplacesNotValidatedAndOpensExistingIssuesPanel(bool emptyRules)
    {
        using var culture = new ModelerCultureScope("en");
        await using var host = CreateHost(new RecordingRenderExecution(), new RecordingSurfaceObserver(new(1200, 800, 1)),
            compositionFactory: await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync());
        await host.InitializeAsync("main-validation", "main-container");
        var operations = new DemoDocumentOperations(host, emptyRules ? new BpmnDocumentValidator(new ModelValidationCatalog([])) : null);
        var activator = new DemoPublishComponentActivator(host);
        using var services = new ServiceCollection().AddLogging().AddInceptusBpmnModeler()
            .AddSingleton<IJSRuntime>(new StandardDownloadRuntime(false)).AddSingleton<IComponentActivator>(activator).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<BpmnDemoHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(BpmnDemoHost.Editor)] = host })));
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            activator.Component.AttachEditor(host, operations);
            await activator.Component.RefreshAsync();
        });
        var markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Contains("Not validated", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-severity=", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("issues-toggle", markup, StringComparison.Ordinal);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await InvokePublishComponentAsync(activator.Component, "ValidateAsync");
            await activator.Component.RefreshAsync();
        });
        var report = Assert.IsType<ValidationSnapshot>(operations.ValidationSnapshot);
        if (emptyRules) Assert.Empty(report.Issues);
        markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.DoesNotContain("Not validated", markup, StringComparison.Ordinal);
        Assert.Contains("class=\"issues-toggle issues-counts status-issue-counts\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<span>Issues</span>", markup, StringComparison.Ordinal);
        Assert.Contains($"Errors {report.Issues.Count(issue => issue.Severity == ModelValidationSeverity.Error)}", markup, StringComparison.Ordinal);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            InvokePublishComponent(activator.Component, "ToggleIssuesPanel");
            await activator.Component.RefreshAsync();
        });
        markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Contains("class=\"issues-floating-panel\"", markup, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"true\"", markup, StringComparison.Ordinal);
        if (emptyRules) Assert.Contains("No issues", markup, StringComparison.Ordinal);
        var initial = host.GetDocument().Snapshot!;
        var task = initial.SemanticModel.Elements.First(element => BpmnTaskSemanticTypes.IsTask(element.TypeId));
        Assert.True((await Session(host).ExecuteAsync(new UpdateSemanticElementNameCommand(initial.DocumentId, initial.Revision,
            task.Id, BpmnSemanticProperties.Name, "Changed after validation"))).IsCommitted);
        await Session(host).WaitForIdleAsync();
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            InvokePublishComponent(activator.Component, "OnViewChanged");
            await activator.Component.RefreshAsync();
        });
        markup = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
        Assert.Contains("Not validated", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-severity=", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("issues-toggle", markup, StringComparison.Ordinal);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            InvokePublishComponent(activator.Component, "CloseIssuesPanel");
            await activator.Component.RefreshAsync();
        });
        Assert.DoesNotContain("class=\"issues-floating-panel\"", await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString), StringComparison.Ordinal);
    }
}
