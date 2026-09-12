using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseICanvas2DRendererArchitectureTests
{
    private static readonly Assembly CanvasAssembly = typeof(Canvas2DRenderer).Assembly;
    private static readonly Assembly ContractsAssembly = typeof(DocumentSnapshot).Assembly;
    private static readonly Assembly RuntimeAssembly = typeof(Document).Assembly;

    [Fact]
    public void FrameworkOwnsExactlyOneConcreteNonReplaceableRenderer()
    {
        var renderer = typeof(Canvas2DRenderer);
        var rendererTypes = CanvasAssembly.GetExportedTypes()
            .Where(type => type.Name.EndsWith("Renderer", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal("Inceptus.DocumentEngine.Canvas2D.Rendering", renderer.Namespace);
        Assert.True(renderer.IsPublic);
        Assert.True(renderer.IsClass);
        Assert.True(renderer.IsSealed);
        Assert.False(renderer.IsAbstract);
        Assert.Equal([renderer], rendererTypes);
        Assert.DoesNotContain(ContractsAssembly.GetExportedTypes(), type =>
            type.Name is "IRenderer" or "ICanvas2DRenderer" ||
            type.Name.Contains("RenderingBackend", StringComparison.Ordinal));
        Assert.DoesNotContain(CanvasAssembly.GetExportedTypes(), type =>
            type.IsInterface && type.Name.Contains("Renderer", StringComparison.Ordinal));
    }

    [Fact]
    public void RendererPublicSurfaceConsumesSceneAndOwnPresentationValuesOnly()
    {
        var signatureTypes = GetPublicSignatureTypes(typeof(Canvas2DRenderer)).ToArray();
        Type[] forbidden =
        [
            typeof(Document),
            typeof(DocumentSnapshot),
            typeof(ISemanticModelView),
            typeof(IVisualModelView),
            typeof(EditorStateSnapshot),
            typeof(ProjectedGraph),
            typeof(LayoutResult),
            typeof(RoutingResult),
            typeof(ICommand),
            typeof(CommandProcessor),
            typeof(ICommandHistoryPolicy),
        ];

        Assert.DoesNotContain(signatureTypes, forbidden.Contains);
        var render = Assert.Single(typeof(Canvas2DRenderer).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.Name == "RenderAsync");
        Assert.Contains(render.GetParameters(), parameter =>
            parameter.ParameterType == typeof(Canvas2DScene));
    }

    [Fact]
    public void BrowserInteropIsContainedToRenderingAndDoesNotLeakIntoSceneOrUpstreamAssemblies()
    {
        var leaked = ContractsAssembly.GetExportedTypes()
            .Concat(RuntimeAssembly.GetExportedTypes())
            .Concat(CanvasAssembly.GetExportedTypes().Where(type =>
                type.Namespace == "Inceptus.DocumentEngine.Canvas2D.Scene"))
            .SelectMany(GetPublicSignatureTypes)
            .Where(IsBrowserInteropType)
            .Select(type => type.FullName ?? type.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(leaked);
        Assert.DoesNotContain(RuntimeAssembly.GetReferencedAssemblies(), reference =>
            reference.Name?.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void InternalExecutionSeamIsNotAPluginRendererOrBackendExtensionPoint()
    {
        var executionSeams = CanvasAssembly.GetTypes()
            .Where(type => type.Name.Contains("RenderExecution", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(executionSeams);
        Assert.All(executionSeams, type => Assert.False(type.IsPublic || type.IsNestedPublic));
        Assert.DoesNotContain(CanvasAssembly.GetExportedTypes(), type =>
            type.Name.Contains("Backend", StringComparison.Ordinal) ||
            type.Name.Contains("Registry", StringComparison.Ordinal) ||
            type.Name.Contains("Selector", StringComparison.Ordinal) ||
            type.Name.Contains("Capability", StringComparison.Ordinal));
    }

    [Fact]
    public void RendererImplementationDoesNotReferenceEditingOrPipelineExecutionTypes()
    {
        var implementationTypes = CanvasAssembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Canvas2D.Rendering",
                StringComparison.Ordinal) == true)
            .SelectMany(GetImplementationSignatureTypes)
            .Distinct()
            .ToArray();

        Assert.DoesNotContain(implementationTypes, IsForbiddenRenderingDependency);
    }

    [Fact]
    public void JavaScriptExecutionPathContainsNoAlternativeBackendOrDeferredOptimization()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Canvas2D",
            "wwwroot",
            "inceptus.canvas2d.js"));
        string[] forbidden =
        [
            "OffscreenCanvas",
            "WebGL",
            "THREE",
            "SVG",
            "RendererRegistry",
            "RenderingBackend",
            "Worker(",
        ];

        Assert.DoesNotContain(forbidden, fragment =>
            source.Contains(fragment, StringComparison.OrdinalIgnoreCase));
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

    private static IEnumerable<Type> GetImplementationSignatureTypes(Type type)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var field in type.GetFields(Flags))
        {
            foreach (var expanded in ExpandType(field.FieldType))
            {
                yield return expanded;
            }
        }

        foreach (var constructor in type.GetConstructors(Flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }

        foreach (var method in type.GetMethods(Flags))
        {
            foreach (var expanded in ExpandType(method.ReturnType))
            {
                yield return expanded;
            }

            foreach (var parameter in method.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }

        foreach (var property in type.GetProperties(Flags))
        {
            foreach (var expanded in ExpandType(property.PropertyType))
            {
                yield return expanded;
            }
        }

        foreach (var @event in type.GetEvents(Flags))
        {
            if (@event.EventHandlerType is null)
            {
                continue;
            }

            foreach (var expanded in ExpandType(@event.EventHandlerType))
            {
                yield return expanded;
            }
        }
    }

    private static bool IsForbiddenRenderingDependency(Type type)
    {
        if (type.Assembly == RuntimeAssembly ||
            type.Assembly.GetName().Name is "Inceptus.DocumentEngine.Bpmn" or
                "Inceptus.DocumentEngine.Blazor")
        {
            return true;
        }

        if (type.Assembly != ContractsAssembly)
        {
            return false;
        }

        return type.Namespace is not (
            "Inceptus.DocumentEngine.Contracts.Canvas2D" or
            "Inceptus.DocumentEngine.Contracts.Diagnostics" or
            "Inceptus.DocumentEngine.Contracts.Geometry" or
            "Inceptus.DocumentEngine.Contracts.Text");
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

    private static bool IsBrowserInteropType(Type type) =>
        type.Namespace?.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith(
            "System.Runtime.InteropServices.JavaScript",
            StringComparison.Ordinal) == true ||
        type.Name is "HTMLCanvasElement" or "CanvasRenderingContext2D" or "Path2D" or
            "ImageBitmap" or "CanvasGradient" or "CanvasPattern" or "ElementReference";

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
