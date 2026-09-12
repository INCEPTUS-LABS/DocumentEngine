using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.ConnectionCreation;

public sealed class AnchorConnectionCreationCatalogTests
{
    [Fact]
    public void CatalogOrdersRegistrationsAndProvidesDeterministicLookup()
    {
        var zulu = Registration("test:create:zulu");
        var alpha = Registration("test:create:alpha");
        var catalog = new AnchorConnectionCreationCatalog([zulu, alpha]);

        Assert.Equal(
            ["test:create:alpha", "test:create:zulu"],
            catalog.Registrations.Select(static registration =>
                registration.CreationId.Value));
        Assert.True(catalog.TryGetRegistration(alpha.CreationId, out var resolved));
        Assert.Same(alpha, resolved);
        Assert.False(catalog.TryGetRegistration(
            new AnchorConnectionCreationId("test:create:missing"),
            out resolved));
        Assert.Null(resolved);
        Assert.Throws<ArgumentNullException>(() =>
            catalog.TryGetRegistration(null!, out _));
    }

    [Fact]
    public void CatalogRejectsNullAndDuplicateRegistrations()
    {
        var first = Registration("test:create:duplicate");
        var duplicate = Registration("test:create:duplicate");

        Assert.Throws<ArgumentException>(() =>
            new AnchorConnectionCreationCatalog([null!]));
        Assert.Throws<ArgumentException>(() =>
            new AnchorConnectionCreationCatalog([first, duplicate]));
    }

    [Fact]
    public void CatalogDefensivelyCopiesAndExposesImmutableRegistrations()
    {
        var registration = Registration("test:create:stable");
        var mutable = new List<AnchorConnectionCreationRegistration> { registration };
        var catalog = new AnchorConnectionCreationCatalog(mutable);
        mutable.Clear();

        Assert.Same(registration, Assert.Single(catalog.Registrations));
        AssertReadOnly(catalog.Registrations);
    }

    [Fact]
    public void EmptyCatalogIsStableAndRegistrationRequiresAllInputs()
    {
        var request = SourceRequest();

        Assert.Empty(AnchorConnectionCreationCatalog.Empty.Registrations);
        Assert.Empty(new AnchorConnectionCreationCatalog(null).Registrations);
        Assert.Empty(
            AnchorConnectionCreationCatalog.Empty.GetMatchingRegistrations(request));
        Assert.False(AnchorConnectionCreationCatalog.Empty.TryGetRegistration(
            new AnchorConnectionCreationId("test:create:missing"),
            out _));

        var id = new AnchorConnectionCreationId("test:create:valid");
        var factory = new MatchingFactory(matches: true);
        var registration = new AnchorConnectionCreationRegistration(id, factory);
        Assert.Equal(id, registration.CreationId);
        Assert.Same(factory, registration.CommandFactory);
        Assert.Throws<ArgumentNullException>(() =>
            new AnchorConnectionCreationRegistration(null!, factory));
        Assert.Throws<ArgumentNullException>(() =>
            new AnchorConnectionCreationRegistration(id, null!));
    }

    [Fact]
    public void MatchingReturnsZeroOneOrAllAmbiguousRegistrationsWithoutGuessing()
    {
        var request = SourceRequest();
        var noMatch = Registration("test:create:none", matches: false);
        var zuluMatch = Registration("test:create:zulu", matches: true);
        var alphaMatch = Registration("test:create:alpha", matches: true);

        var zero = new AnchorConnectionCreationCatalog([noMatch])
            .GetMatchingRegistrations(request);
        var one = new AnchorConnectionCreationCatalog([noMatch, alphaMatch])
            .GetMatchingRegistrations(request);
        var ambiguous = new AnchorConnectionCreationCatalog(
                [zuluMatch, noMatch, alphaMatch])
            .GetMatchingRegistrations(request);

        Assert.Empty(zero);
        Assert.Same(alphaMatch, Assert.Single(one));
        Assert.Equal(
            ["test:create:alpha", "test:create:zulu"],
            ambiguous.Select(static registration => registration.CreationId.Value));
        AssertReadOnly(zero);
        AssertReadOnly(one);
        AssertReadOnly(ambiguous);
        Assert.All(
            new[] { noMatch, alphaMatch, zuluMatch },
            registration => Assert.Same(
                request,
                Assert.IsType<MatchingFactory>(registration.CommandFactory).LastRequest));
    }

    [Fact]
    public void MatchingRejectsAMissingSourceRequest()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AnchorConnectionCreationCatalog.Empty.GetMatchingRegistrations(null!));
    }

    private static AnchorConnectionCreationRegistration Registration(
        string id,
        bool matches = true) =>
        new(new AnchorConnectionCreationId(id), new MatchingFactory(matches));

    private static AnchorConnectionCreationSourceRequest SourceRequest()
    {
        var revision = new DocumentRevision(4);
        var documentId = new DocumentId("test:document");
        var document = new DocumentSnapshot(
            new SemanticModelSnapshot(documentId, revision),
            new VisualModelSnapshot(documentId, revision),
            new DocumentMetadataSnapshot(documentId, revision));
        return new AnchorConnectionCreationSourceRequest(
            document,
            revision,
            new SemanticElementId("test:semantic:source"),
            new VisualStateId("test:visual:source"),
            new ConnectorAnchorId("test:anchor:source"));
    }

    private static void AssertReadOnly<T>(ImmutableArray<T> values)
    {
        var mutableView = (IList<T>)values;
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
    }

    private sealed class MatchingFactory(bool matches) :
        IAnchorConnectionCreationCommandFactory
    {
        internal AnchorConnectionCreationSourceRequest? LastRequest { get; private set; }

        public bool CanStart(AnchorConnectionCreationSourceRequest request)
        {
            LastRequest = request;
            return matches;
        }

        public AnchorConnectionCreationPlanResult CreatePlan(
            AnchorConnectionCreationRequest request) =>
            throw new NotSupportedException();
    }
}
