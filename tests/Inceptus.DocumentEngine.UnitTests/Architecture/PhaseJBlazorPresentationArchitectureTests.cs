using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseJBlazorPresentationArchitectureTests
{
    private static readonly Assembly BlazorAssembly = typeof(InceptusBpmnModeler).Assembly;
    private static readonly Assembly CanvasAssembly = typeof(Canvas2DScene).Assembly;
    private static readonly Assembly ContractsAssembly = typeof(DocumentSnapshot).Assembly;
    private static readonly Assembly RuntimeAssembly = typeof(Document).Assembly;

    [Fact]
    public void PresentationOrchestrationExistsOnlyInBlazorBoundary()
    {
        var presentationTypes = BlazorAssembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Bpmn.Blazor.Presentation",
                StringComparison.Ordinal) == true)
            .ToArray();

        Assert.NotEmpty(presentationTypes);
        Assert.DoesNotContain(
            ContractsAssembly.GetTypes().Concat(RuntimeAssembly.GetTypes()).Concat(CanvasAssembly.GetTypes()),
            type => type.Name.Contains("DocumentCanvas", StringComparison.Ordinal) ||
                type.Name.Contains("PresentationSurfaceObserver", StringComparison.Ordinal) ||
                type.Name.Contains("NeutralDemoPipeline", StringComparison.Ordinal));
    }

    [Fact]
    public void PresentationIntroducesNoGenericRendererBackendOrSelectionSurface()
    {
        string[] forbiddenFragments =
        [
            "IRenderer",
            "RenderingBackend",
            "RenderBackend",
            "RendererRegistry",
            "RendererSelector",
            "RendererSelection",
            "CapabilityNegotiation",
            "IProcessingPipeline",
            "PipelineRegistry",
            "PipelineSelector",
        ];
        string[] forbiddenMembers =
        [
            "RegisterRenderer",
            "ResolveRenderer",
            "SelectRenderer",
            "RegisterRenderingBackend",
            "ResolveRenderingBackend",
            "SelectRenderingBackend",
        ];

        foreach (var type in BlazorAssembly.GetTypes())
        {
            Assert.DoesNotContain(forbiddenFragments, fragment =>
                type.Name.Contains(fragment, StringComparison.Ordinal));
            var members = type.GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.DoesNotContain(members, member => forbiddenMembers.Contains(member.Name, StringComparer.Ordinal));
        }

        var renderers = BlazorAssembly.GetTypes()
            .Concat(CanvasAssembly.GetTypes())
            .Where(type => type.Name.EndsWith("Renderer", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal([typeof(Canvas2DRenderer)], renderers);
    }

    [Fact]
    public void PresentationPublicSurfaceExposesNoLiveBrowserGraphicsResources()
    {
        var leaked = BlazorAssembly.GetExportedTypes()
            .SelectMany(GetPublicSignatureTypes)
            .Where(IsLiveBrowserResource)
            .Select(type => type.FullName ?? type.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(leaked);
    }

    [Fact]
    public void BrowserAndBlazorTypesRemainAbsentFromUpstreamPublicSurfaces()
    {
        foreach (var assembly in new[] { ContractsAssembly, RuntimeAssembly, CanvasAssembly })
        {
            var leaked = assembly.GetExportedTypes()
                .SelectMany(GetPublicSignatureTypes)
                .Where(type => IsBlazorType(type) || IsDomType(type))
                .ToArray();

            Assert.Empty(leaked);
        }
    }

    [Fact]
    public void PresentationContainsNoLowerLayerInteractionHitTestingOrSerializationImplementation()
    {
        string[] forbiddenNamespaces =
        [
            "Inceptus.DocumentEngine.Bpmn.Commands",
            "Inceptus.DocumentEngine.Bpmn.Semantics",
            "Inceptus.DocumentEngine.Bpmn.Validation",
            "Inceptus.DocumentEngine.Canvas2D.Interaction",
            "Inceptus.DocumentEngine.Canvas2D.HitTesting",
            "Inceptus.DocumentEngine.Runtime.Serialization",
        ];

        Assert.DoesNotContain(BlazorAssembly.GetTypes(), type =>
            forbiddenNamespaces.Any(fragment =>
                type.Namespace?.StartsWith(fragment, StringComparison.Ordinal) == true));
        var genericPresentationTypes = BlazorAssembly.GetTypes()
            .Where(type =>
                type.Namespace?.StartsWith(
                    "Inceptus.DocumentEngine.Bpmn.Blazor.Presentation",
                    StringComparison.Ordinal) == true ||
                type.Namespace?.StartsWith(
                    "Inceptus.DocumentEngine.Bpmn.Blazor.Components",
                    StringComparison.Ordinal) == true)
            .ToArray();
        Assert.DoesNotContain(genericPresentationTypes, type =>
            type.Name.Contains("HitTest", StringComparison.Ordinal) ||
            type.Name.Contains("Serializer", StringComparison.Ordinal));
    }

    [Fact]
    public void DocumentCanvasIsOneConcreteThinHostWithoutPublicConfigurationSurface()
    {
        Assert.True(typeof(ComponentBase).IsAssignableFrom(typeof(DocumentCanvas)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(DocumentCanvas)));
        Assert.Empty(typeof(DocumentCanvas).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        var documentCanvasTypes = BlazorAssembly.GetTypes()
            .Where(type => type.Name is nameof(DocumentCanvas) or "DocumentCanvasHost")
            .ToArray();
        Assert.DoesNotContain(documentCanvasTypes, static type => type.IsInterface);
        Assert.DoesNotContain(documentCanvasTypes, static type =>
            type.Name.Contains("Registry", StringComparison.Ordinal) ||
            type.Name.Contains("Selector", StringComparison.Ordinal));
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        yield return type;
        foreach (var constructor in type.GetConstructors())
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }

        foreach (var member in type.GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            IEnumerable<Type> memberTypes = member switch
            {
                MethodInfo method => [method.ReturnType, .. method.GetParameters()
                    .Select(static parameter => parameter.ParameterType)],
                PropertyInfo property => [property.PropertyType],
                FieldInfo field => [field.FieldType],
                EventInfo @event when @event.EventHandlerType is not null => [@event.EventHandlerType],
                _ => [],
            };
            foreach (var memberType in memberTypes)
            {
                foreach (var expanded in ExpandType(memberType))
                {
                    yield return expanded;
                }
            }
        }
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var expanded in ExpandType(element))
            {
                yield return expanded;
            }
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var expanded in ExpandType(argument))
            {
                yield return expanded;
            }
        }
    }

    private static bool IsLiveBrowserResource(Type type) =>
        type.Name is "HTMLCanvasElement" or "CanvasRenderingContext2D" or "Path2D" or
            "ImageBitmap" or "CanvasGradient" or "CanvasPattern" or "ElementReference";

    private static bool IsDomType(Type type) => IsLiveBrowserResource(type) ||
        type.Namespace?.StartsWith("Microsoft.AspNetCore.Components.Web", StringComparison.Ordinal) == true;

    private static bool IsBlazorType(Type type) =>
        type.Namespace?.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal) == true;
}
