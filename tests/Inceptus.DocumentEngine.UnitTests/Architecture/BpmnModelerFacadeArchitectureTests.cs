using System.Reflection;
using System.Runtime.CompilerServices;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class BpmnModelerFacadeArchitectureTests
{
    private const BindingFlags PublicDeclared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Type[] FacadePayloads =
    [
        typeof(BpmnModelerReadyEventArgs),
        typeof(BpmnModelerDocumentChangedEventArgs),
        typeof(BpmnModelerOperationFailedEventArgs),
        typeof(BpmnModelerDocumentResult),
        typeof(BpmnModelerFileResult),
        typeof(BpmnModelerFileArtifact),
    ];

    [Fact]
    public void RootParametersUseImmutableInitialInputAndOptionalHighLevelCallbacks()
    {
        var parameters = typeof(InceptusBpmnModeler).GetProperties(PublicDeclared)
            .Where(static property => property.IsDefined(typeof(ParameterAttribute)))
            .ToDictionary(static property => property.Name, static property => property);

        Assert.Equal(typeof(DocumentSnapshot), parameters[nameof(InceptusBpmnModeler.InitialDocument)].PropertyType);
        Assert.Equal(typeof(EventCallback<BpmnModelerReadyEventArgs>),
            parameters[nameof(InceptusBpmnModeler.Ready)].PropertyType);
        Assert.Equal(typeof(EventCallback<BpmnModelerDocumentChangedEventArgs>),
            parameters[nameof(InceptusBpmnModeler.DocumentChanged)].PropertyType);
        Assert.Equal(typeof(EventCallback<BpmnModelerOperationFailedEventArgs>),
            parameters[nameof(InceptusBpmnModeler.OperationFailed)].PropertyType);
        Assert.Equal(typeof(string), parameters[nameof(InceptusBpmnModeler.Class)].PropertyType);
        Assert.Equal(typeof(string), parameters[nameof(InceptusBpmnModeler.Style)].PropertyType);
        Assert.True(parameters[nameof(InceptusBpmnModeler.AdditionalAttributes)]
            .GetCustomAttribute<ParameterAttribute>()!.CaptureUnmatchedValues);
        Assert.All(parameters.Values,
            static property => Assert.False(property.IsDefined(typeof(EditorRequiredAttribute))));
        Assert.DoesNotContain("InitialDocumentChanged", parameters.Keys);
        Assert.DoesNotContain("InitialDocumentExpression", parameters.Keys);
        Assert.DoesNotContain("Document", parameters.Keys);
        Assert.DoesNotContain("DiagnosticsChanged", parameters.Keys);
    }

    [Fact]
    public void RequiredHandleMethodsExchangeSnapshotsAndArtifactsWithoutRuntimeOwnership()
    {
        AssertMethod(nameof(InceptusBpmnModeler.CaptureDocumentSnapshot), typeof(BpmnModelerDocumentResult));
        AssertMethod(nameof(InceptusBpmnModeler.NewDocumentAsync), typeof(ValueTask<BpmnModelerDocumentResult>),
            typeof(CancellationToken));
        AssertMethod(nameof(InceptusBpmnModeler.LoadDocumentAsync), typeof(ValueTask<BpmnModelerDocumentResult>),
            typeof(DocumentSnapshot), typeof(CancellationToken));
        AssertMethod(nameof(InceptusBpmnModeler.ImportNativeDocumentAsync),
            typeof(ValueTask<BpmnModelerDocumentResult>), typeof(ReadOnlyMemory<byte>), typeof(CancellationToken));
        AssertMethod(nameof(InceptusBpmnModeler.ExportNativeDocumentAsync), typeof(ValueTask<BpmnModelerFileResult>),
            typeof(CancellationToken));
        AssertMethod(nameof(InceptusBpmnModeler.PublishAsync), typeof(ValueTask<BpmnModelerFileResult>),
            typeof(CancellationToken));
    }

    [Fact]
    public void FacadeSignaturesAndNestedPayloadsExposeNoLiveRuntimeOrCompositionCatalogs()
    {
        var supportedTypes = FacadePayloads.Prepend(typeof(InceptusBpmnModeler));
        var signatureTypes = supportedTypes.SelectMany(PublicSignatureTypes);
        var visited = new HashSet<Type>();

        foreach (var signatureType in signatureTypes)
        {
            AssertFacadeBoundary(signatureType, visited);
        }

        Assert.Contains(typeof(DocumentSnapshot), visited);
        Assert.Contains(typeof(BpmnModelerFileArtifact), visited);
        Assert.Contains(typeof(BpmnModelerDocumentChangedEventArgs), visited);
        Assert.DoesNotContain(typeof(IBpmnModelerStartupDocumentProvider), visited);
    }

    [Fact]
    public void FacadeResultAndEventPayloadPropertiesAreReadOnly()
    {
        foreach (var type in FacadePayloads)
        {
            Assert.True(type.IsSealed);
            Assert.All(type.GetProperties(PublicDeclared), static property => Assert.Null(property.SetMethod));
        }

        Assert.Equal(typeof(DocumentSnapshot), typeof(BpmnModelerReadyEventArgs)
            .GetProperty(nameof(BpmnModelerReadyEventArgs.Snapshot))!.PropertyType);
        Assert.Equal(typeof(DocumentSnapshot), typeof(BpmnModelerDocumentChangedEventArgs)
            .GetProperty(nameof(BpmnModelerDocumentChangedEventArgs.Snapshot))!.PropertyType);
    }

    [Fact]
    public void RegistrationIsOneDiscoverableExtensionWithoutCompositionOptions()
    {
        var extensions = typeof(BpmnModelerServiceCollectionExtensions);
        Assert.Equal("Microsoft.Extensions.DependencyInjection", extensions.Namespace);
        var registration = Assert.Single(extensions.GetMethods(PublicDeclared),
            static method => method.Name == "AddInceptusBpmnModeler");
        Assert.True(registration.IsStatic);
        Assert.True(registration.IsDefined(typeof(ExtensionAttribute)));
        Assert.Equal(typeof(IServiceCollection), registration.ReturnType);
        var services = Assert.Single(registration.GetParameters());
        Assert.Equal(typeof(IServiceCollection), services.ParameterType);
    }

    [Fact]
    public void ApprovedStartupProviderRemainsTheExplicitSingleMethodOwnershipException()
    {
        // The accepted pre-1.0 provider transfers one fresh Document at startup. It is
        // deliberately excluded from the ordinary component handle and event payload graph.
        var method = Assert.Single(typeof(IBpmnModelerStartupDocumentProvider).GetMethods(PublicDeclared));
        Assert.Equal(nameof(IBpmnModelerStartupDocumentProvider.GetInitialDocumentAsync), method.Name);
        Assert.Equal(typeof(ValueTask<Document>), method.ReturnType);
        var cancellation = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(CancellationToken), cancellation.ParameterType);
        Assert.True(cancellation.HasDefaultValue);
    }

    private static void AssertMethod(string name, Type returnType, params Type[] parameterTypes)
    {
        var method = Assert.Single(typeof(InceptusBpmnModeler).GetMethods(PublicDeclared),
            method => method.Name == name);
        Assert.Equal(returnType, method.ReturnType);
        Assert.Equal(parameterTypes, method.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.All(method.GetParameters().Where(static parameter => parameter.ParameterType == typeof(CancellationToken)),
            static parameter => Assert.True(parameter.HasDefaultValue));
    }

    private static IEnumerable<Type> PublicSignatureTypes(Type type)
    {
        foreach (var property in type.GetProperties(PublicDeclared))
        {
            yield return property.PropertyType;
        }

        foreach (var method in type.GetMethods(PublicDeclared).Where(static method => !method.IsSpecialName))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var constructor in type.GetConstructors(PublicDeclared))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static void AssertFacadeBoundary(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return;
        }

        var typeNamespace = type.Namespace ?? string.Empty;
        Assert.False(typeNamespace.StartsWith("Inceptus.DocumentEngine.Runtime", StringComparison.Ordinal),
            $"The ordinary facade exposes runtime type {type.FullName}.");
        Assert.False(typeNamespace.StartsWith("Inceptus.DocumentEngine.Canvas2D", StringComparison.Ordinal),
            $"The ordinary facade exposes Canvas2D runtime type {type.FullName}.");
        Assert.False(typeNamespace.StartsWith("Inceptus.DocumentEngine.Bpmn.Blazor.Presentation", StringComparison.Ordinal),
            $"The ordinary facade exposes presentation implementation type {type.FullName}.");
        Assert.False(typeNamespace.StartsWith("Inceptus.DocumentEngine.Bpmn.Blazor.Composition", StringComparison.Ordinal),
            $"The ordinary facade exposes composition implementation type {type.FullName}.");
        Assert.False(typeNamespace.StartsWith("Inceptus.DocumentEngine", StringComparison.Ordinal) &&
            (type.Name.EndsWith("Catalog", StringComparison.Ordinal) ||
             type.Name.EndsWith("Registry", StringComparison.Ordinal) ||
             type.Name.EndsWith("Controller", StringComparison.Ordinal)),
            $"The ordinary facade exposes low-level integration type {type.FullName}.");

        if (type.HasElementType)
        {
            AssertFacadeBoundary(type.GetElementType()!, visited);
        }

        foreach (var argument in type.GetGenericArguments())
        {
            AssertFacadeBoundary(argument, visited);
        }

        if (type.Assembly == typeof(DocumentSnapshot).Assembly || FacadePayloads.Contains(type))
        {
            foreach (var property in type.GetProperties(PublicDeclared))
            {
                AssertFacadeBoundary(property.PropertyType, visited);
            }
        }
    }
}
