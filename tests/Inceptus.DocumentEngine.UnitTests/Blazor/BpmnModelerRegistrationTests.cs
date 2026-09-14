using System.Collections.Immutable;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed class BpmnModelerRegistrationTests
{
    [Fact]
    public async Task RepeatedRegistrationResolvesIndependentCompleteModelerFactories()
    {
        var services = new ServiceCollection();
        Assert.Same(services, services.AddInceptusBpmnModeler());
        var initial = services.ToArray();

        services.AddInceptusBpmnModeler();
        services.AddInceptusBpmnModeler();

        Assert.Equal(initial, services.ToArray());
        var registration = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(BpmnModelerCompositionFactory));
        Assert.Equal(typeof(BpmnModelerCompositionFactory), registration.ServiceType);
        Assert.Equal(ServiceLifetime.Transient, registration.Lifetime);
        using var provider = services.BuildServiceProvider();
        var firstFactory = provider.GetRequiredService<BpmnModelerCompositionFactory>();
        var secondFactory = provider.GetRequiredService<BpmnModelerCompositionFactory>();
        Assert.NotSame(firstFactory, secondFactory);

        var first = await firstFactory.CreateAsync();
        var second = await secondFactory.CreateAsync();

        Assert.NotSame(first.Document, second.Document);
        Assert.NotEqual(first.Document.DocumentId, second.Document.DocumentId);
        Assert.NotSame(first.Configuration, second.Configuration);
        Assert.Equal(DocumentRevision.Zero, first.Document.Revision);
        Assert.Empty(first.Document.SemanticModel.Elements);
        Assert.Empty(first.Document.SemanticModel.Relationships);
        Assert.Empty(first.Document.VisualModel.VisualStates);
        Assert.NotNull(first.ToolboxPlacementCatalog);
        Assert.NotNull(first.PropertiesSchemaCatalog);
        Assert.NotNull(first.ModelValidationCatalog);
        Assert.NotNull(first.BackgroundActionCatalog);
        var policy = first.Configuration.ConnectorAnchorPolicyProvider.Resolve(
            BpmnSemanticTypes.StartEvent);
        Assert.Equal(ConnectorAnchorRoleCapability.Source, policy.Right.AllowedRoles);
    }

    [Fact]
    public void RegistrationContainsNoDemoHostOrMutableRuntimeServices()
    {
        var services = new ServiceCollection().AddInceptusBpmnModeler();

        var standardLocalization = new ServiceCollection().AddLocalization();
        Assert.All(services.Where(descriptor => descriptor.Lifetime == ServiceLifetime.Singleton),
            descriptor => Assert.Contains(standardLocalization, standard =>
                standard.ServiceType == descriptor.ServiceType &&
                standard.ImplementationType == descriptor.ImplementationType &&
                standard.Lifetime == descriptor.Lifetime));
        Assert.DoesNotContain(services, static descriptor =>
            descriptor.ServiceType == typeof(Document) ||
            descriptor.ServiceType.Namespace?.Contains("Demo", StringComparison.Ordinal) == true ||
            descriptor.ServiceType.FullName?.Contains("WebAssembly", StringComparison.Ordinal) ==
            true);
        Assert.DoesNotContain(services, static descriptor =>
            descriptor.ServiceType == typeof(IBpmnModelerStartupDocumentProvider));
        var references = typeof(BpmnModelerServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies();
        Assert.DoesNotContain(references, static reference =>
            reference.Name is "Inceptus.DocumentEngine.Blazor" ||
            reference.Name?.Contains("WebAssembly", StringComparison.Ordinal) == true ||
            reference.Name?.Contains("DevServer", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExplicitSnapshotWinsOverEveryProviderRegistrationCount(int providerCount)
    {
        var initial = CreateSnapshot();
        var startup = new CountingProvider();
        var services = new ServiceCollection().AddInceptusBpmnModeler();
        for (var index = 0; index < providerCount; index++)
        {
            services.AddSingleton<IBpmnModelerStartupDocumentProvider>(startup);
        }

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BpmnModelerCompositionFactory>()
            .WithInitialDocument(initial);

        var composition = await factory.CreateAsync();

        Assert.Equal(0, startup.InvocationCount);
        Assert.Equal(initial, composition.Document.CaptureSnapshot());
        Assert.Equal(new DocumentId("test:p13:initial"), composition.Document.DocumentId);
        Assert.Equal(new DocumentRevision(7), composition.Document.Revision);
        Assert.Single(composition.Document.SemanticModel.Elements);
        Assert.Single(composition.Document.VisualModel.VisualStates);
        Assert.Equal(initial.Publication, composition.Document.Publication);
    }

    [Fact]
    public async Task InitialSnapshotPreventsEvenConstructionOfUnusedProviderServices()
    {
        var constructionCount = 0;
        var services = new ServiceCollection().AddInceptusBpmnModeler();
        services.AddTransient<IBpmnModelerStartupDocumentProvider>(_ =>
        {
            constructionCount++;
            throw new InvalidOperationException("The fallback provider cannot be constructed.");
        });
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BpmnModelerCompositionFactory>();
        var snapshot = CreateSnapshot();

        var explicitComposition = await factory.WithInitialDocument(snapshot).CreateAsync();

        Assert.Equal(snapshot, explicitComposition.Document.CaptureSnapshot());
        Assert.Equal(0, constructionCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => factory.CreateAsync().AsTask());
        Assert.Equal(1, constructionCount);
    }

    [Fact]
    public async Task DeferredProviderResolutionRejectsNullEntriesWithoutAnExplicitSnapshot()
    {
        var factory = new BpmnModelerCompositionFactory(
            () => [null!]);

        await Assert.ThrowsAsync<ArgumentException>(() => factory.CreateAsync().AsTask());
        var snapshot = CreateSnapshot();
        var explicitComposition = await factory.WithInitialDocument(snapshot).CreateAsync();
        Assert.Equal(snapshot, explicitComposition.Document.CaptureSnapshot());
    }

    [Fact]
    public async Task SameInitialSnapshotCreatesIndependentLiveDocumentsAndComposition()
    {
        var snapshot = CreateSnapshot();
        var factory = new BpmnModelerCompositionFactory(initialDocument: snapshot);

        var first = await factory.CreateAsync();
        var second = await factory.CreateAsync();

        Assert.NotSame(first.Document, second.Document);
        Assert.NotSame(first.Configuration, second.Configuration);
        Assert.Equal(snapshot, first.Document.CaptureSnapshot());
        Assert.Equal(snapshot, second.Document.CaptureSnapshot());
        Assert.Equal(first.Document.DocumentId, second.Document.DocumentId);
    }

    [Fact]
    public async Task ProviderIsResolvedAfterRegistrationAndRemainsAdvancedFallback()
    {
        var startup = new CountingProvider();
        var services = new ServiceCollection().AddInceptusBpmnModeler();
        services.AddSingleton<IBpmnModelerStartupDocumentProvider>(startup);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<BpmnModelerCompositionFactory>();

        var composition = await factory.WithInitialDocument(null).CreateAsync();

        Assert.Equal(1, startup.InvocationCount);
        Assert.Same(startup.LastDocument, composition.Document);
    }

    [Fact]
    public async Task MultipleProvidersWithoutInitialSnapshotRemainAmbiguous()
    {
        var startup = new CountingProvider();
        var factory = new BpmnModelerCompositionFactory([startup, startup]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateAsync().AsTask());

        Assert.Equal(0, startup.InvocationCount);
    }

    [Fact]
    public async Task CancelledStartupDoesNotInvokeProvider()
    {
        var startup = new CountingProvider();
        var factory = new BpmnModelerCompositionFactory([startup]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => factory.CreateAsync(cancellation.Token).AsTask());

        Assert.Equal(0, startup.InvocationCount);
    }

    [Fact]
    public async Task ExplicitSnapshotUsesCanonicalBpmnAnchorPolicy()
    {
        var snapshot = CreateSnapshot(
            BpmnSemanticTypes.StartEvent,
            [new ConnectorAnchor(
                new ConnectorAnchorId("test:p13:invalid-start-target"),
                ConnectorAnchorSide.Right,
                ConnectorAnchorRole.Target,
                0)]);
        Assert.True(DocumentReconstructor.Reconstruct(snapshot).Succeeded);
        var startup = new CountingProvider();
        var factory = new BpmnModelerCompositionFactory([startup], snapshot);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateAsync().AsTask());

        Assert.Equal(0, startup.InvocationCount);
        Assert.False(BpmnModelerComposition.ReconstructDocument(snapshot).Succeeded);
    }

    [Fact]
    public void FileArtifactCopiesMutableBytesAndPreservesFileIdentity()
    {
        byte[] bytes = [1, 2, 3];
        var native = new BpmnModelerFileArtifact(
            "document.inceptus.json",
            "application/json",
            bytes);
        var publish = new BpmnModelerFileArtifact(
            "published-process.zip",
            "application/zip",
            bytes);

        bytes[0] = 99;

        Assert.Equal(new byte[] { 1, 2, 3 }, native.Content);
        Assert.Equal(new byte[] { 1, 2, 3 }, publish.Content);
        Assert.Equal("document.inceptus.json", native.FileName);
        Assert.Equal("application/json", native.ContentType);
        Assert.Equal("published-process.zip", publish.FileName);
        Assert.Equal("application/zip", publish.ContentType);
        Assert.Throws<ArgumentException>(() =>
            new BpmnModelerFileArtifact(" ", "application/json", bytes));
        Assert.Throws<ArgumentException>(() =>
            new BpmnModelerFileArtifact("document.inceptus.json", " ", bytes));
    }

    [Fact]
    public void FacadeResultsExposeImmutableSuccessOrBoundedFailureData()
    {
        var snapshot = CreateSnapshot();
        var successful = new BpmnModelerDocumentResult(
            BpmnModelerOperationStatus.Succeeded,
            snapshot,
            default);
        var diagnostic = new Diagnostic("TEST_REJECTED", DiagnosticSeverity.Error, "Rejected.");
        var failed = new BpmnModelerFileResult(
            BpmnModelerOperationStatus.Rejected,
            null,
            [diagnostic]);
        var ready = new BpmnModelerReadyEventArgs(snapshot);
        var changed = new BpmnModelerDocumentChangedEventArgs(
            snapshot,
            BpmnModelerDocumentChangeKind.DocumentReplacement);
        var failure = new BpmnModelerOperationFailedEventArgs(
            BpmnModelerOperation.Import,
            failed.Status,
            failed.Diagnostics);

        Assert.True(successful.Succeeded);
        Assert.Same(snapshot, successful.Snapshot);
        Assert.Empty(successful.Diagnostics);
        Assert.False(failed.Succeeded);
        Assert.Null(failed.Artifact);
        Assert.Same(diagnostic, Assert.Single(failed.Diagnostics));
        Assert.Same(snapshot, ready.Snapshot);
        Assert.Same(snapshot, changed.Snapshot);
        Assert.Equal(BpmnModelerDocumentChangeKind.DocumentReplacement, changed.Kind);
        Assert.Equal(BpmnModelerOperation.Import, failure.Operation);
        Assert.Equal(BpmnModelerOperationStatus.Rejected, failure.Status);
        Assert.Equal(failed.Diagnostics, failure.Diagnostics);
        Assert.Throws<ArgumentException>(() => new BpmnModelerDocumentResult(
            BpmnModelerOperationStatus.Succeeded,
            null,
            ImmutableArray<Diagnostic>.Empty));
    }

    private static DocumentSnapshot CreateSnapshot(
        SemanticTypeId? typeId = null,
        IEnumerable<ConnectorAnchor>? connectorAnchors = null)
    {
        var documentId = new DocumentId("test:p13:initial");
        var revision = new DocumentRevision(7);
        var elementId = new SemanticElementId("test:p13:task");
        return new DocumentSnapshot(
            new SemanticModelSnapshot(
                documentId,
                revision,
                [new SemanticElementSnapshot(elementId, typeId ?? BpmnSemanticTypes.Task)]),
            new VisualModelSnapshot(
                documentId,
                revision,
                [new VisualStateSnapshot(
                    new VisualStateId("test:p13:task-visual"),
                    elementId,
                    new PointD(40d, 60d),
                    new SizeD(100d, 80d),
                    VisualPlacementMode.Manual,
                    connectorAnchors: connectorAnchors)]),
            new DocumentMetadataSnapshot(documentId, revision),
            new DocumentPublicationSnapshot("p13-initial", "Initial fixture", "Fixture details."));
    }

    private sealed class CountingProvider : IBpmnModelerStartupDocumentProvider
    {
        internal int InvocationCount { get; private set; }

        internal Document? LastDocument { get; private set; }

        public ValueTask<Document> GetInitialDocumentAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            LastDocument = BpmnModelerComposition.CreateEmptyDocument();
            return ValueTask.FromResult(LastDocument);
        }
    }
}
