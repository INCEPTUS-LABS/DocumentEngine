using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Canvas2D;

public sealed class Canvas2DSceneContributorContractTests
{
    [Fact]
    public void ContributorIdentityAndDescriptorAreExplicitStableValues()
    {
        var first = new Canvas2DSceneContributorId("test:scene-contributor");
        var second = new Canvas2DSceneContributorId("test:scene-contributor");
        var descriptor = new Canvas2DSceneContributorDescriptor(first, "1.0");

        Assert.Equal(first, second);
        Assert.Equal("test:scene-contributor", first.ToString());
        Assert.Equal(first, descriptor.ContributorId);
        Assert.Equal("1.0", descriptor.Version);
        Assert.Throws<ArgumentException>(() => new Canvas2DSceneContributorId(" "));
        Assert.Throws<ArgumentException>(() =>
            new Canvas2DSceneContributorDescriptor(first, ""));
    }

    [Fact]
    public void ContributorContractAcceptsOnlyOneImmutableContributionContext()
    {
        var method = Assert.Single(typeof(ICanvas2DSceneContributor).GetMethods());

        Assert.Equal("Contribute", method.Name);
        Assert.Equal(typeof(Canvas2DSceneContributionResult), method.ReturnType);
        Assert.Equal(
            [typeof(Canvas2DSceneContributionContext)],
            method.GetParameters().Select(static parameter => parameter.ParameterType));

        var contextProperties = typeof(Canvas2DSceneContributionContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(static property => property.Name, static property => property.PropertyType);
        var expected = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(Canvas2DSceneContributionContext.ProjectedGraph)] = typeof(ProjectedGraph),
            [nameof(Canvas2DSceneContributionContext.LayoutResult)] = typeof(LayoutResult),
            [nameof(Canvas2DSceneContributionContext.RoutingResult)] = typeof(RoutingResult),
            [nameof(Canvas2DSceneContributionContext.VisualModel)] =
                typeof(Inceptus.DocumentEngine.Contracts.Visuals.VisualModelSnapshot),
            [nameof(Canvas2DSceneContributionContext.EditorState)] = typeof(EditorStateSnapshot),
            [nameof(Canvas2DSceneContributionContext.Configuration)] =
                typeof(Canvas2DSceneConfiguration),
            [nameof(Canvas2DSceneContributionContext.Contributor)] =
                typeof(Canvas2DSceneContributorDescriptor),
            [nameof(Canvas2DSceneContributionContext.Presentation)] =
                typeof(Canvas2DScenePresentationContext),
        };

        Assert.Equal(
            expected.OrderBy(static pair => pair.Key, StringComparer.Ordinal),
            contextProperties.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
        Assert.DoesNotContain(contextProperties.Values, type =>
            type == typeof(DocumentSnapshot) ||
            type == typeof(SemanticModelSnapshot) ||
            type == typeof(DocumentMetadataSnapshot) ||
            type == typeof(IServiceProvider));
        Assert.All(contextProperties.Values, type =>
            Assert.DoesNotContain("Runtime", type.Namespace ?? string.Empty, StringComparison.Ordinal));

        var presentationProperties = typeof(Canvas2DScenePresentationContext)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(static property => property.Name, static property => property.PropertyType);
        Assert.Equal(
            new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                [nameof(Canvas2DScenePresentationContext.Document)] = typeof(DocumentSnapshot),
                [nameof(Canvas2DScenePresentationContext.ActiveScopeId)] =
                    typeof(DocumentScopeId),
                [nameof(Canvas2DScenePresentationContext.ModelProfileViewState)] =
                    typeof(ModelProfileViewStateSnapshot),
                [nameof(Canvas2DScenePresentationContext.ModelProfileElementViewState)] =
                    typeof(ModelProfileElementViewStateSnapshot),
                [nameof(Canvas2DScenePresentationContext.BaseSceneItems)] =
                    typeof(ImmutableArray<Canvas2DSceneItem>),
            }.OrderBy(static pair => pair.Key, StringComparer.Ordinal),
            presentationProperties.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
        Assert.All(
            typeof(Canvas2DScenePresentationContext).GetProperties(),
            property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(
            typeof(Canvas2DScenePresentationContext).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name is "Inceptus.DocumentEngine.Canvas2D" or
                "Inceptus.DocumentEngine.Runtime");
    }

    [Fact]
    public void RegistrationPreservesExplicitDescriptorAndContributor()
    {
        var contributor = new EmptyContributor();
        var descriptor = new Canvas2DSceneContributorDescriptor(
            new Canvas2DSceneContributorId("test:contributor"),
            "2");
        var registration = new Canvas2DSceneContributorRegistration(descriptor, contributor);

        Assert.Same(descriptor, registration.Descriptor);
        Assert.Same(contributor, registration.Contributor);
        Assert.Equal(Canvas2DSceneContributionStage.Canonical, registration.Stage);
        Assert.All(
            typeof(Canvas2DSceneContributorRegistration).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void ContributionResultIsImmutableAndRequiresConsistentDiagnostics()
    {
        var warning = new Diagnostic("SCENE_TEST_WARNING", DiagnosticSeverity.Warning, "warning");
        var error = new Diagnostic("SCENE_TEST_ERROR", DiagnosticSeverity.Error, "error");
        var contribution = new Canvas2DSceneContribution();
        var success = Canvas2DSceneContributionResult.Success(contribution, [warning]);
        var failure = Canvas2DSceneContributionResult.Failure([error]);

        Assert.True(success.Succeeded);
        Assert.Same(contribution, success.Contribution);
        Assert.False(failure.Succeeded);
        Assert.Null(failure.Contribution);
        Assert.Equal(error.Code, Assert.Single(failure.Diagnostics).Code);
        Assert.Throws<ArgumentException>(() =>
            Canvas2DSceneContributionResult.Success(contribution, [error]));
        Assert.Throws<ArgumentException>(() =>
            Canvas2DSceneContributionResult.Failure([warning]));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Diagnostic>)failure.Diagnostics).Add(error));
    }

    [Fact]
    public void CanonicalVisualOverrideIsImmutableVisualDataOnly()
    {
        var targetId = new SceneObjectId("test:canonical-item");
        var geometry = Canvas2DSceneGeometry.Ellipse(new RectD(0d, 0d, 10d, 10d));
        var style = new Canvas2DSceneStyle("#ffffff", "#000000", 2d);
        var visualOverride = new Canvas2DCanonicalSceneItemVisualOverride(
            targetId,
            geometry,
            style);

        Assert.Same(targetId, visualOverride.TargetSceneObjectId);
        Assert.Same(geometry, visualOverride.Geometry);
        Assert.Same(style, visualOverride.Style);
        Assert.All(
            typeof(Canvas2DCanonicalSceneItemVisualOverride).GetProperties(),
            property => Assert.Null(property.SetMethod));
        var constructor = Assert.Single(
            typeof(Canvas2DCanonicalSceneItemVisualOverride).GetConstructors());
        Assert.Equal(
            [
                typeof(SceneObjectId),
                typeof(Canvas2DSceneGeometry),
                typeof(Canvas2DSceneStyle),
            ],
            constructor.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.Equal(
            [
                nameof(Canvas2DCanonicalSceneItemVisualOverride.Geometry),
                nameof(Canvas2DCanonicalSceneItemVisualOverride.Style),
                nameof(Canvas2DCanonicalSceneItemVisualOverride.TargetSceneObjectId),
            ],
            typeof(Canvas2DCanonicalSceneItemVisualOverride).GetProperties()
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ContributionOrdersVisualOverridesAndRejectsDuplicateTargets()
    {
        var first = VisualOverride("test:a");
        var second = VisualOverride("test:b");
        var contribution = new Canvas2DSceneContribution(
            canonicalItemVisualOverrides: [second, first]);

        Assert.Equal(
            ["test:a", "test:b"],
            contribution.CanonicalItemVisualOverrides.Select(
                static item => item.TargetSceneObjectId.Value));
        var exception = Assert.Throws<ArgumentException>(() =>
            new Canvas2DSceneContribution(
                canonicalItemVisualOverrides: [first, VisualOverride("test:a")]));
        Assert.Contains("test:a", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ContributorPublicSurfaceContainsNoMutationBrowserOrHostCapability()
    {
        var contractTypes = new[]
        {
            typeof(ICanvas2DSceneContributor),
            typeof(Canvas2DSceneContributionContext),
            typeof(Canvas2DSceneContribution),
            typeof(Canvas2DCanonicalSceneItemVisualOverride),
            typeof(Canvas2DSceneContributionResult),
            typeof(Canvas2DSceneContributorRegistration),
        };
        var publicTypes = contractTypes.SelectMany(GetSignatureTypes).ToArray();
        string[] forbiddenFragments =
        [
            "CanvasRenderingContext2D",
            "CommandProcessor",
            "DocumentEngine.Runtime",
            "DocumentMetadataSnapshot",
            "History",
            "HTMLCanvasElement",
            "ImageBitmap",
            "JavaScript",
            "Path2D",
            "Renderer",
            "SemanticModelSnapshot",
        ];

        Assert.DoesNotContain(publicTypes, type =>
            forbiddenFragments.Any(fragment =>
                (type.FullName ?? type.Name).Contains(fragment, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(publicTypes, type => typeof(Delegate).IsAssignableFrom(type));
        Assert.DoesNotContain(publicTypes, type => type == typeof(IServiceProvider));
    }

    private static IEnumerable<Type> GetSignatureTypes(Type type)
    {
        yield return type;
        foreach (var memberType in type.GetProperties().Select(static property => property.PropertyType)
                     .Concat(type.GetMethods().Select(static method => method.ReturnType))
                     .Concat(type.GetMethods().SelectMany(static method =>
                         method.GetParameters().Select(static parameter => parameter.ParameterType))))
        {
            yield return memberType;
            foreach (var argument in memberType.GetGenericArguments())
            {
                yield return argument;
            }
        }
    }

    private static Canvas2DCanonicalSceneItemVisualOverride VisualOverride(string targetId) =>
        new(
            new SceneObjectId(targetId),
            Canvas2DSceneGeometry.Ellipse(new RectD(0d, 0d, 1d, 1d)),
            Canvas2DSceneStyle.Default);

    private sealed class EmptyContributor : ICanvas2DSceneContributor
    {
        public Canvas2DSceneContributionResult Contribute(
            Canvas2DSceneContributionContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return Canvas2DSceneContributionResult.Success(new Canvas2DSceneContribution());
        }
    }
}
