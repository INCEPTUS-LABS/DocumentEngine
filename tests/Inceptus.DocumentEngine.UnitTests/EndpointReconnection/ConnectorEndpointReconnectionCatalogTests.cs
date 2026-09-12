using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.EndpointReconnection;

public sealed class ConnectorEndpointReconnectionCatalogTests
{
    [Fact]
    public void CatalogOrdersRegistrationsAndProvidesDeterministicLookup()
    {
        var zulu = Registration("test:reconnect:zulu");
        var alpha = Registration("test:reconnect:alpha");
        var catalog = new ConnectorEndpointReconnectionCatalog([zulu, alpha]);

        Assert.Equal(
            ["test:reconnect:alpha", "test:reconnect:zulu"],
            catalog.Registrations.Select(static registration =>
                registration.ReconnectionId.Value));
        Assert.True(catalog.TryGetRegistration(alpha.ReconnectionId, out var resolved));
        Assert.Same(alpha, resolved);
        Assert.False(catalog.TryGetRegistration(
            new ConnectorEndpointReconnectionId("test:reconnect:missing"),
            out resolved));
        Assert.Null(resolved);
        Assert.Throws<ArgumentNullException>(() =>
            catalog.TryGetRegistration(null!, out _));
    }

    [Fact]
    public void CatalogRejectsNullAndDuplicateRegistrations()
    {
        var first = Registration("test:reconnect:duplicate");
        var duplicate = Registration("test:reconnect:duplicate");

        Assert.Throws<ArgumentException>(() =>
            new ConnectorEndpointReconnectionCatalog([null!]));
        Assert.Throws<ArgumentException>(() =>
            new ConnectorEndpointReconnectionCatalog([first, duplicate]));
    }

    [Fact]
    public void CatalogDefensivelyCopiesAndExposesImmutableRegistrations()
    {
        var registration = Registration("test:reconnect:stable");
        var mutable = new List<ConnectorEndpointReconnectionRegistration>
        {
            registration,
        };
        var catalog = new ConnectorEndpointReconnectionCatalog(mutable);
        mutable.Clear();

        Assert.Same(registration, Assert.Single(catalog.Registrations));
        AssertReadOnly(catalog.Registrations);
    }

    [Fact]
    public void EmptyCatalogIsStableAndRegistrationRequiresAllInputs()
    {
        var request = StartRequest();

        Assert.Empty(ConnectorEndpointReconnectionCatalog.Empty.Registrations);
        Assert.Empty(new ConnectorEndpointReconnectionCatalog(null).Registrations);
        Assert.Empty(
            ConnectorEndpointReconnectionCatalog.Empty.GetMatchingRegistrations(request));
        Assert.False(ConnectorEndpointReconnectionCatalog.Empty.TryGetRegistration(
            new ConnectorEndpointReconnectionId("test:reconnect:missing"),
            out _));

        var id = new ConnectorEndpointReconnectionId("test:reconnect:valid");
        var factory = new MatchingFactory(matches: true);
        var registration = new ConnectorEndpointReconnectionRegistration(id, factory);
        Assert.Equal(id, registration.ReconnectionId);
        Assert.Same(factory, registration.CommandFactory);
        Assert.Throws<ArgumentNullException>(() =>
            new ConnectorEndpointReconnectionRegistration(null!, factory));
        Assert.Throws<ArgumentNullException>(() =>
            new ConnectorEndpointReconnectionRegistration(id, null!));
    }

    [Fact]
    public void MatchingReturnsZeroOneOrAllAmbiguousRegistrationsWithoutGuessing()
    {
        var request = StartRequest();
        var noMatch = Registration("test:reconnect:none", matches: false);
        var zuluMatch = Registration("test:reconnect:zulu", matches: true);
        var alphaMatch = Registration("test:reconnect:alpha", matches: true);

        var zero = new ConnectorEndpointReconnectionCatalog([noMatch])
            .GetMatchingRegistrations(request);
        var one = new ConnectorEndpointReconnectionCatalog([noMatch, alphaMatch])
            .GetMatchingRegistrations(request);
        var ambiguous = new ConnectorEndpointReconnectionCatalog(
                [zuluMatch, noMatch, alphaMatch])
            .GetMatchingRegistrations(request);

        Assert.Empty(zero);
        Assert.Same(alphaMatch, Assert.Single(one));
        Assert.Equal(
            ["test:reconnect:alpha", "test:reconnect:zulu"],
            ambiguous.Select(static registration => registration.ReconnectionId.Value));
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
    public void MatchingRejectsAMissingStartRequest()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ConnectorEndpointReconnectionCatalog.Empty.GetMatchingRegistrations(null!));
    }

    private static ConnectorEndpointReconnectionRegistration Registration(
        string id,
        bool matches = true) =>
        new(new ConnectorEndpointReconnectionId(id), new MatchingFactory(matches));

    private static ConnectorEndpointReconnectionStartRequest StartRequest()
    {
        var revision = new DocumentRevision(4);
        var documentId = new DocumentId("test:document");
        var document = new DocumentSnapshot(
            new SemanticModelSnapshot(documentId, revision),
            new VisualModelSnapshot(documentId, revision),
            new DocumentMetadataSnapshot(documentId, revision));
        return new ConnectorEndpointReconnectionStartRequest(
            document,
            revision,
            new SemanticElementId("test:semantic:relationship"),
            new VisualStateId("test:visual:connector"),
            ConnectorEndpointKind.Source,
            new SemanticElementId("test:semantic:source"),
            new ConnectorAnchorId("test:anchor:source"));
    }

    private static void AssertReadOnly<T>(ImmutableArray<T> values)
    {
        var mutableView = (IList<T>)values;
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
    }

    private sealed class MatchingFactory(bool matches) :
        IConnectorEndpointReconnectionCommandFactory
    {
        internal ConnectorEndpointReconnectionStartRequest? LastRequest
        { get; private set; }

        public bool CanStart(ConnectorEndpointReconnectionStartRequest request)
        {
            LastRequest = request;
            return matches;
        }

        public ConnectorEndpointReconnectionPlanResult CreatePlan(
            ConnectorEndpointReconnectionRequest request) =>
            throw new NotSupportedException();
    }
}
