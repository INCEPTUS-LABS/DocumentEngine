using Inceptus.DocumentEngine.Contracts.ContextMenus;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.ContextMenus;

public sealed class CanvasBackgroundActionCatalogTests
{
    [Fact]
    public void DefinitionsAreOrderedByGroupOrderAndStableIdentity()
    {
        var laterGroup = Definition("action:z", "View", order: 0);
        var laterAction = Definition("action:b", "Add", order: 20);
        var earlierAction = Definition("action:a", "Add", order: 10);

        var catalog = new CanvasBackgroundActionCatalog(
            [laterGroup, laterAction, earlierAction]);

        Assert.Equal(
            [earlierAction, laterAction, laterGroup],
            catalog.Definitions.AsEnumerable());
        Assert.True(catalog.TryGetDefinition(earlierAction.Id, out var resolved));
        Assert.Same(earlierAction, resolved);
    }

    [Fact]
    public void DuplicateStableIdentityIsRejectedBeforeDifferingSortKeysCanSeparateIt()
    {
        var duplicateId = new CanvasBackgroundActionId("action:duplicate");

        var exception = Assert.Throws<ArgumentException>(() =>
            new CanvasBackgroundActionCatalog(
            [
                Definition(duplicateId, "Add", order: 10),
                Definition("action:middle", "Other", order: 50),
                Definition(duplicateId, "View", order: 100),
            ]));

        Assert.Equal("definitions", exception.ParamName);
        Assert.Contains(duplicateId.Value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestCarriesGenericIdentityProviderWithoutPreallocatingAnIdentity()
    {
        var documentId = new DocumentId("context-menu:document");
        var revision = DocumentRevision.Zero;
        var semantic = new SemanticModelSnapshot(documentId, revision);
        var document = new DocumentSnapshot(
            semantic,
            new VisualModelSnapshot(documentId, revision),
            new DocumentMetadataSnapshot(documentId, revision));
        var identityProvider = new RecordingIdentityProvider();

        var request = new CanvasBackgroundActionRequest(
            document,
            semantic.RootScopeId,
            identityProvider);

        Assert.Same(identityProvider, request.IdentityProvider);
        Assert.Equal(0, identityProvider.CreateIdentityCount);
    }

    [Fact]
    public void ApplicabilityReceivesOnlyReadOnlyModelContext()
    {
        var documentId = new DocumentId("context-menu:applicability-document");
        var revision = DocumentRevision.Zero;
        var semantic = new SemanticModelSnapshot(documentId, revision);
        var document = new DocumentSnapshot(
            semantic,
            new VisualModelSnapshot(documentId, revision),
            new DocumentMetadataSnapshot(documentId, revision));
        var identityProvider = new RecordingIdentityProvider();
        CanvasBackgroundActionApplicabilityRequest? captured = null;
        var definition = new CanvasBackgroundActionDefinition(
            new CanvasBackgroundActionId("action:read-only-applicability"),
            "Add",
            "Read-only",
            static _ => throw new InvalidOperationException(
                "Applicability discovery must not create an execution plan."),
            applicability: request =>
            {
                captured = request;
                return true;
            });

        var applicable = definition.IsApplicable(new CanvasBackgroundActionRequest(
            document,
            semantic.RootScopeId,
            identityProvider));

        Assert.True(applicable);
        Assert.NotNull(captured);
        Assert.Same(document, captured.Document);
        Assert.Equal(semantic.RootScopeId, captured.ActiveScopeId);
        Assert.DoesNotContain(
            typeof(CanvasBackgroundActionApplicabilityRequest).GetProperties(),
            property => property.PropertyType == typeof(IDocumentCreationIdentityProvider) ||
                property.Name.Contains("IdentityProvider", StringComparison.Ordinal));
        Assert.All(
            typeof(CanvasBackgroundActionApplicabilityRequest).GetProperties(),
            static property => Assert.Null(property.SetMethod));
        Assert.Equal(0, identityProvider.CreateIdentityCount);
    }

    private static CanvasBackgroundActionDefinition Definition(
        string id,
        string group,
        int order) =>
        Definition(new CanvasBackgroundActionId(id), group, order);

    private static CanvasBackgroundActionDefinition Definition(
        CanvasBackgroundActionId id,
        string group,
        int order) =>
        new(
            id,
            group,
            id.Value,
            static _ => throw new InvalidOperationException("The catalog test does not execute actions."),
            order);

    private sealed class RecordingIdentityProvider : IDocumentCreationIdentityProvider
    {
        public int CreateIdentityCount { get; private set; }

        public DocumentCreationIdentity CreateIdentity()
        {
            CreateIdentityCount++;
            return new DocumentCreationIdentity(
                new SemanticElementId("context-menu:semantic"),
                new VisualStateId("context-menu:visual"));
        }
    }
}
