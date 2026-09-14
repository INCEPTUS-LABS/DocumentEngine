using System.Collections.Concurrent;
using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class InceptusBpmnModelerFacadeComponentTests
{
    [Fact]
    public async Task RootSupportsOptionalParametersAndPreservesContainerAttributes()
    {
        var jsRuntime = new UnexpectedJsRuntime();
        using var services = CreateServices(jsRuntime);
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        var defaultRoot = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<InceptusBpmnModeler>());
        var defaultMarkup = await renderer.Dispatcher.InvokeAsync(defaultRoot.ToHtmlString);
        var styledRoot = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<InceptusBpmnModeler>(Parameters(
                (nameof(InceptusBpmnModeler.Class), "consumer-modeler"),
                (nameof(InceptusBpmnModeler.Style), "height: 24rem; width: 40rem;"),
                (nameof(InceptusBpmnModeler.AdditionalAttributes),
                    new Dictionary<string, object>
                    {
                        ["data-consumer"] = "facade-host",
                        ["aria-label"] = "Consumer process editor",
                    }))));
        var styledMarkup = await renderer.Dispatcher.InvokeAsync(styledRoot.ToHtmlString);

        Assert.Contains("class=\"inceptus-bpmn-modeler\"", defaultMarkup, StringComparison.Ordinal);
        Assert.Contains("<canvas", defaultMarkup, StringComparison.Ordinal);
        Assert.Contains("class=\"inceptus-bpmn-modeler consumer-modeler\"", styledMarkup,
            StringComparison.Ordinal);
        Assert.Contains("style=\"height: 24rem; width: 40rem;\"", styledMarkup, StringComparison.Ordinal);
        Assert.Contains("data-consumer=\"facade-host\"", styledMarkup, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Consumer process editor\"", styledMarkup, StringComparison.Ordinal);
        Assert.Equal(0, jsRuntime.InvocationCount);

        using var uninitialized = new InceptusBpmnModeler();
        Assert.Null(uninitialized.InitialDocument);
        Assert.False(uninitialized.Ready.HasDelegate);
        Assert.False(uninitialized.DocumentChanged.HasDelegate);
        Assert.False(uninitialized.OperationFailed.HasDelegate);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealRootWithInitialDocumentDoesNotConstructUnusedStartupProvider(bool registerFacade)
    {
        var constructionCount = 0;
        var jsRuntime = new UnexpectedJsRuntime();
        var registrations = new ServiceCollection()
            .AddLogging()
            .AddLocalization()
            .AddSingleton<IJSRuntime>(jsRuntime)
            .AddTransient<IBpmnModelerStartupDocumentProvider>(_ =>
            {
                constructionCount++;
                throw new InvalidOperationException("The unused startup provider cannot be constructed.");
            });
        if (registerFacade)
        {
            registrations.AddLogging().AddInceptusBpmnModeler();
        }

        using var services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var snapshot = CreateSnapshot("test:facade-unused-provider", 6);

        var root = await renderer.Dispatcher.InvokeAsync(
            () => renderer.RenderComponentAsync<InceptusBpmnModeler>(Parameters(
                (nameof(InceptusBpmnModeler.InitialDocument), snapshot))));
        var markup = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);

        Assert.Contains("class=\"inceptus-bpmn-modeler\"", markup, StringComparison.Ordinal);
        Assert.Contains("<canvas", markup, StringComparison.Ordinal);
        Assert.Equal(0, constructionCount);
        Assert.Equal(0, jsRuntime.InvocationCount);
    }

    [Fact]
    public async Task PublicOperationsBeforeComponentInitializationReturnBoundedResultsWithoutRenderer()
    {
        using var component = new InceptusBpmnModeler();
        var failures = new ConcurrentQueue<BpmnModelerOperationFailedEventArgs>();
        Parameters((nameof(InceptusBpmnModeler.OperationFailed),
            EventCallback.Factory.Create<BpmnModelerOperationFailedEventArgs>(new object(),
                (Action<BpmnModelerOperationFailedEventArgs>)(args => failures.Enqueue(args)))))
            .SetParameterProperties(component);

        await AssertPublicOperationsUnavailableAsync(component);
        await GetFacade(component).Notifications.Pending.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(failures);
        Assert.True(GetFacade(component).Notifications.Pending.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AttachedRootBeforeModelerStartupReportsBoundedFailuresThroughRealDispatcher()
    {
        using var services = CreateServices();
        await using var renderer = new ExceptionRecordingRenderer(services);
        var notifications = new ConcurrentQueue<object>();
        var receiver = new object();
        await renderer.InitializeAsync(Parameters(
            (nameof(InceptusBpmnModeler.Ready),
                EventCallback.Factory.Create<BpmnModelerReadyEventArgs>(receiver,
                    (Action<BpmnModelerReadyEventArgs>)(args => notifications.Enqueue(args)))),
            (nameof(InceptusBpmnModeler.DocumentChanged),
                EventCallback.Factory.Create<BpmnModelerDocumentChangedEventArgs>(receiver,
                    (Action<BpmnModelerDocumentChangedEventArgs>)(args => notifications.Enqueue(args)))),
            (nameof(InceptusBpmnModeler.OperationFailed),
                EventCallback.Factory.Create<BpmnModelerOperationFailedEventArgs>(receiver,
                    (Action<BpmnModelerOperationFailedEventArgs>)(args => notifications.Enqueue(args))))));

        await renderer.Dispatcher.InvokeAsync(
            () => AssertPublicOperationsUnavailableAsync(renderer.Component));

        Assert.Equal(
            new[]
            {
                BpmnModelerOperation.Capture,
                BpmnModelerOperation.New,
                BpmnModelerOperation.Load,
                BpmnModelerOperation.Import,
                BpmnModelerOperation.Export,
                BpmnModelerOperation.Publish,
            },
            notifications.Select(notification =>
            {
                var failure = Assert.IsType<BpmnModelerOperationFailedEventArgs>(notification);
                Assert.Equal(BpmnModelerOperationStatus.Unavailable, failure.Status);
                return failure.Operation;
            }));
        Assert.Empty(renderer.Exceptions);
    }

    [Fact]
    public async Task InitialDocumentIsCapturedOnceAcrossParameterRenders()
    {
        using var services = CreateServices();
        await using var renderer = new ExceptionRecordingRenderer(services);
        var initial = CreateSnapshot("test:facade-initial", 6);
        var replacementParameter = CreateSnapshot("test:facade-rerender", 12);
        await renderer.InitializeAsync(Parameters((nameof(InceptusBpmnModeler.InitialDocument), initial)));
        var initialFacade = GetFacade(renderer.Component);
        var original = await initialFacade.CompositionFactory.CreateAsync();

        await renderer.SetParametersAsync(Parameters(
            (nameof(InceptusBpmnModeler.InitialDocument), replacementParameter),
            (nameof(InceptusBpmnModeler.Class), "updated-class")));
        var retainedFacade = GetFacade(renderer.Component);
        var afterParameterChange = await retainedFacade.CompositionFactory.CreateAsync();
        await renderer.SetParametersAsync(Parameters(
            (nameof(InceptusBpmnModeler.InitialDocument), null)));
        var afterNullParameter = await GetFacade(renderer.Component).CompositionFactory.CreateAsync();

        Assert.Same(initialFacade, retainedFacade);
        Assert.Same(initialFacade, GetFacade(renderer.Component));
        Assert.Equal(initial, original.Document.CaptureSnapshot());
        Assert.Equal(initial, afterParameterChange.Document.CaptureSnapshot());
        Assert.Equal(initial, afterNullParameter.Document.CaptureSnapshot());
        Assert.NotSame(original.Document, afterParameterChange.Document);
        Assert.Empty(renderer.Exceptions);
    }

    [Fact]
    public async Task OptionalCallbacksAcceptNotificationsWithoutErrors()
    {
        using var services = CreateServices();
        await using var renderer = new ExceptionRecordingRenderer(services);
        await renderer.InitializeAsync(ParameterView.Empty);
        var snapshot = CreateSnapshot("test:facade-optional", 0);

        await InvokeNotificationAsync(renderer.Component, new BpmnModelerReadyEventArgs(snapshot));
        await InvokeNotificationAsync(renderer.Component, Change(snapshot));
        await InvokeNotificationAsync(renderer.Component, Failure());

        Assert.Empty(renderer.Exceptions);
    }

    [Fact]
    public async Task EachNotificationInvokesOnlyItsMatchingCallbackOnceWithExactPayload()
    {
        using var services = CreateServices();
        await using var renderer = new ExceptionRecordingRenderer(services);
        var observed = new ConcurrentQueue<object>();
        var receiver = new object();
        await renderer.InitializeAsync(Parameters(
            (nameof(InceptusBpmnModeler.Ready),
                EventCallback.Factory.Create<BpmnModelerReadyEventArgs>(receiver,
                    (Action<BpmnModelerReadyEventArgs>)(args => observed.Enqueue(args)))),
            (nameof(InceptusBpmnModeler.DocumentChanged),
                EventCallback.Factory.Create<BpmnModelerDocumentChangedEventArgs>(receiver,
                    (Action<BpmnModelerDocumentChangedEventArgs>)(args => observed.Enqueue(args)))),
            (nameof(InceptusBpmnModeler.OperationFailed),
                EventCallback.Factory.Create<BpmnModelerOperationFailedEventArgs>(receiver,
                    (Action<BpmnModelerOperationFailedEventArgs>)(args => observed.Enqueue(args))))));
        var snapshot = CreateSnapshot("test:facade-payload", 1);
        var ready = new BpmnModelerReadyEventArgs(snapshot);
        var changed = Change(snapshot);
        var failed = Failure();
        using var notifications = new BpmnModelerNotifications(
            notification => InvokeNotificationAsync(renderer.Component, notification));

        _ = notifications.Enqueue(ready);
        _ = notifications.Enqueue(changed);
        await notifications.Enqueue(failed).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Collection(observed,
            notification => Assert.Same(ready, notification),
            notification => Assert.Same(changed, notification),
            notification => Assert.Same(failed, notification));
        Assert.Empty(renderer.Exceptions);
    }

    [Fact]
    public async Task AsynchronousHostWorkDoesNotBlockTheNextCallbackInvocation()
    {
        using var services = CreateServices();
        await using var renderer = new ExceptionRecordingRenderer(services);
        var releaseFirst = NewCompletion();
        var firstCompleted = NewCompletion();
        var observed = new ConcurrentQueue<DocumentRevision>();
        await renderer.InitializeAsync(Parameters((nameof(InceptusBpmnModeler.DocumentChanged),
            EventCallback.Factory.Create<BpmnModelerDocumentChangedEventArgs>(new object(),
                async args =>
                {
                    observed.Enqueue(args.Snapshot.Revision);
                    if (args.Snapshot.Revision.Value == 1)
                    {
                        await releaseFirst.Task;
                        firstCompleted.SetResult();
                    }
                }))));
        using var notifications = new BpmnModelerNotifications(
            notification => InvokeNotificationAsync(renderer.Component, notification));
        try
        {
            await notifications.Enqueue(Change(CreateSnapshot("test:facade-async", 1)))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(firstCompleted.Task.IsCompleted);
            await notifications.Enqueue(Change(CreateSnapshot("test:facade-async", 2)))
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new[] { new DocumentRevision(1), new DocumentRevision(2) }, observed);
            Assert.False(firstCompleted.Task.IsCompleted);
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        await firstCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(renderer.Exceptions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackFailuresReachRendererWithoutPoisoningLaterInvocations(bool asynchronous)
    {
        using var services = CreateServices();
        await using var renderer = new ExceptionRecordingRenderer(services);
        var expected = new InvalidOperationException("Consumer callback failed.");
        var asynchronousCallback = NewCompletion();
        var receiver = new object();
        var callback = asynchronous
            ? EventCallback.Factory.Create<BpmnModelerDocumentChangedEventArgs>(receiver,
                (Func<BpmnModelerDocumentChangedEventArgs, Task>)(_ => asynchronousCallback.Task))
            : EventCallback.Factory.Create<BpmnModelerDocumentChangedEventArgs>(receiver,
                (Action<BpmnModelerDocumentChangedEventArgs>)(_ => throw expected));
        await renderer.InitializeAsync(Parameters((nameof(InceptusBpmnModeler.DocumentChanged), callback)));
        using var notifications = new BpmnModelerNotifications(
            notification => InvokeNotificationAsync(renderer.Component, notification));

        await notifications.Enqueue(Change(CreateSnapshot("test:facade-failure", 1)))
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (asynchronous)
        {
            Assert.Empty(renderer.Exceptions);
            asynchronousCallback.SetException(expected);
        }
        var observedError = await renderer.FirstException.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(expected, observedError);
        var observedChanges = new ConcurrentQueue<BpmnModelerDocumentChangedEventArgs>();
        await renderer.SetParametersAsync(Parameters((nameof(InceptusBpmnModeler.DocumentChanged),
            EventCallback.Factory.Create<BpmnModelerDocumentChangedEventArgs>(receiver,
                (Action<BpmnModelerDocumentChangedEventArgs>)(args => observedChanges.Enqueue(args))))));
        var nextChange = Change(CreateSnapshot("test:facade-failure", 2));
        await notifications.Enqueue(nextChange).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(nextChange, Assert.Single(observedChanges));
        Assert.Same(expected, Assert.Single(renderer.Exceptions));
        Assert.True(notifications.Pending.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DisposedComponentSuppressesAllLaterCallbackKinds()
    {
        using var services = CreateServices();
        await using var renderer = new ExceptionRecordingRenderer(services);
        var observed = new ConcurrentQueue<object>();
        var receiver = new object();
        await renderer.InitializeAsync(Parameters(
            (nameof(InceptusBpmnModeler.Ready),
                EventCallback.Factory.Create<BpmnModelerReadyEventArgs>(receiver,
                    (Action<BpmnModelerReadyEventArgs>)(args => observed.Enqueue(args)))),
            (nameof(InceptusBpmnModeler.DocumentChanged),
                EventCallback.Factory.Create<BpmnModelerDocumentChangedEventArgs>(receiver,
                    (Action<BpmnModelerDocumentChangedEventArgs>)(args => observed.Enqueue(args)))),
            (nameof(InceptusBpmnModeler.OperationFailed),
                EventCallback.Factory.Create<BpmnModelerOperationFailedEventArgs>(receiver,
                    (Action<BpmnModelerOperationFailedEventArgs>)(args => observed.Enqueue(args))))));
        await renderer.Dispatcher.InvokeAsync(renderer.Component.Dispose);
        var snapshot = CreateSnapshot("test:facade-disposed", 0);

        await InvokeNotificationAsync(renderer.Component, new BpmnModelerReadyEventArgs(snapshot));
        await InvokeNotificationAsync(renderer.Component, Change(snapshot));
        await InvokeNotificationAsync(renderer.Component, Failure());

        Assert.Empty(observed);
        Assert.Empty(renderer.Exceptions);
    }

    private static async Task AssertPublicOperationsUnavailableAsync(InceptusBpmnModeler component)
    {
        var capture = component.CaptureDocumentSnapshot();
        var created = await component.NewDocumentAsync();
        var loaded = await component.LoadDocumentAsync(null);
        var imported = await component.ImportNativeDocumentAsync(ReadOnlyMemory<byte>.Empty);
        var exported = await component.ExportNativeDocumentAsync();
        var published = await component.PublishAsync();

        Assert.All(new[] { capture, created, loaded, imported }, static result =>
        {
            Assert.Equal(BpmnModelerOperationStatus.Unavailable, result.Status);
            Assert.Null(result.Snapshot);
            Assert.Single(result.Diagnostics);
        });
        Assert.All(new[] { exported, published }, static result =>
        {
            Assert.Equal(BpmnModelerOperationStatus.Unavailable, result.Status);
            Assert.Null(result.Artifact);
            Assert.Single(result.Diagnostics);
        });
    }

    private static BpmnModelerFacade GetFacade(InceptusBpmnModeler component) =>
        Assert.IsType<BpmnModelerFacade>(typeof(InceptusBpmnModeler)
            .GetField("_facade", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(component));

    private static Task InvokeNotificationAsync(InceptusBpmnModeler component, object notification) =>
        Assert.IsAssignableFrom<Task>(typeof(InceptusBpmnModeler)
            .GetMethod("InvokeNotificationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, [notification]));

    private static ParameterView Parameters(params (string Name, object? Value)[] parameters) =>
        ParameterView.FromDictionary(parameters.ToDictionary(static parameter => parameter.Name,
            static parameter => parameter.Value));

    private static ServiceProvider CreateServices(IJSRuntime? jsRuntime = null) =>
        new ServiceCollection()
            .AddSingleton(jsRuntime ?? new UnexpectedJsRuntime())
            .AddLogging().AddInceptusBpmnModeler()
            .BuildServiceProvider();

    private static DocumentSnapshot CreateSnapshot(string documentName, ulong revision)
    {
        var documentId = new DocumentId(documentName);
        var version = new DocumentRevision(revision);
        return new DocumentSnapshot(
            new SemanticModelSnapshot(documentId, version),
            new VisualModelSnapshot(documentId, version),
            new DocumentMetadataSnapshot(documentId, version));
    }

    private static BpmnModelerDocumentChangedEventArgs Change(DocumentSnapshot snapshot) =>
        new(snapshot, BpmnModelerDocumentChangeKind.PersistentMutation);

    private static BpmnModelerOperationFailedEventArgs Failure() =>
        new(BpmnModelerOperation.Import, BpmnModelerOperationStatus.Rejected, []);

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class UnexpectedJsRuntime : IJSRuntime
    {
        internal int InvocationCount { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            InvocationCount++;
            return ValueTask.FromException<TValue>(
                new InvalidOperationException("Static facade rendering must not invoke browser interop."));
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class FacadeTestComponent : InceptusBpmnModeler
    {
        public FacadeTestComponent()
        {
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            // Exercise the real root lifecycle/dispatcher without attaching browser resources.
        }
    }

#pragma warning disable BL0006 // This test-only renderer captures Blazor's real exception-dispatch boundary.
    private sealed class ExceptionRecordingRenderer(IServiceProvider services)
        : Renderer(services, NullLoggerFactory.Instance)
    {
        private readonly TaskCompletionSource<Exception> _firstException =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _componentId;

        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        internal FacadeTestComponent Component { get; private set; } = null!;

        internal ConcurrentQueue<Exception> Exceptions { get; } = new();

        internal Task<Exception> FirstException => _firstException.Task;

        internal Task InitializeAsync(ParameterView parameters) => Dispatcher.InvokeAsync(async () =>
        {
            Component = (FacadeTestComponent)InstantiateComponent(typeof(FacadeTestComponent));
            _componentId = AssignRootComponentId(Component);
            await RenderRootComponentAsync(_componentId, parameters);
        });

        internal Task SetParametersAsync(ParameterView parameters) =>
            Dispatcher.InvokeAsync(() => RenderRootComponentAsync(_componentId, parameters));

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

        protected override void HandleException(Exception exception)
        {
            Exceptions.Enqueue(exception);
            _firstException.TrySetResult(exception);
        }
    }
#pragma warning restore BL0006
}
