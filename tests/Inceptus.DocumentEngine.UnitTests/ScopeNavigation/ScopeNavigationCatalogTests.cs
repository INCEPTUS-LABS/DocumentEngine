using System.Diagnostics.CodeAnalysis;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.ScopeNavigation;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.ScopeNavigation;

public sealed class ScopeNavigationCatalogTests
{
    [Fact]
    public void CatalogIsDeterministicAndProvidesUniqueSemanticTypeLookup()
    {
        var second = Registration("test:type/b", "Open B");
        var first = Registration("test:type/a", "Open A");

        var catalog = new ScopeNavigationCatalog([second, first]);

        Assert.Equal([first, second], catalog.Registrations.ToArray());
        Assert.True(catalog.TryGetRegistration(first.SemanticTypeId, out var resolved));
        Assert.Same(first, resolved);
        Assert.False(catalog.TryGetRegistration(new SemanticTypeId("test:type/missing"), out _));
        Assert.Empty(ScopeNavigationCatalog.Empty.Registrations);
    }

    [Fact]
    public void ContractsRejectInvalidAndDuplicateRegistrations()
    {
        var contribution = new StubContribution();
        var typeId = new SemanticTypeId("test:type");

        Assert.Throws<ArgumentNullException>(() =>
            new ScopeNavigationRegistration(null!, "Open", contribution));
        Assert.Throws<ArgumentException>(() =>
            new ScopeNavigationRegistration(typeId, " ", contribution));
        Assert.Throws<ArgumentNullException>(() =>
            new ScopeNavigationRegistration(typeId, "Open", null!));
        Assert.Throws<ArgumentException>(() => new ScopeNavigationCatalog([null!]));
        Assert.Throws<ArgumentException>(() => new ScopeNavigationCatalog(
        [
            new ScopeNavigationRegistration(typeId, "Open", contribution),
            new ScopeNavigationRegistration(typeId, "Open again", contribution),
        ]));
        Assert.Throws<ArgumentNullException>(() =>
            ScopeNavigationCatalog.Empty.TryGetRegistration(null!, out _));
    }

    private static ScopeNavigationRegistration Registration(string typeId, string label) =>
        new(new SemanticTypeId(typeId), label, new StubContribution());

    private sealed class StubContribution : IScopeNavigationContribution
    {
        public bool TryResolveTargetScope(
            DocumentSnapshot document,
            SemanticElementSnapshot ownerElement,
            [NotNullWhen(true)]
            out DocumentScopeId? targetScopeId)
        {
            targetScopeId = null;
            return false;
        }

        public string ResolveBreadcrumbLabel(
            DocumentSnapshot document,
            SemanticElementSnapshot ownerElement) => "Scope";
    }
}
