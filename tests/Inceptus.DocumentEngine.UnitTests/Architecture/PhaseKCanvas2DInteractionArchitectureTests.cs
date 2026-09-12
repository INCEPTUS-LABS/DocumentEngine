using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseKCanvas2DInteractionArchitectureTests
{
    private const string HitTestingNamespace =
        "Inceptus.DocumentEngine.Canvas2D.HitTesting";
    private const string InteractionNamespace =
        "Inceptus.DocumentEngine.Canvas2D.Interaction";

    private static readonly Assembly CanvasAssembly = typeof(Canvas2DScene).Assembly;
    private static readonly Assembly BlazorAssembly = typeof(InceptusBpmnModeler).Assembly;
    private static readonly Assembly ContractsAssembly = typeof(DocumentSnapshot).Assembly;
    private static readonly Assembly RuntimeAssembly = typeof(Document).Assembly;

    [Fact]
    public void FrameworkOwnsOneConcreteSceneOnlyHitTestService()
    {
        var service = CanvasAssembly.GetType(
            $"{HitTestingNamespace}.Canvas2DSceneHitTestService",
            throwOnError: true)!;
        var hitTestTypes = CanvasAssembly.GetTypes()
            .Where(type => type.Namespace == HitTestingNamespace)
            .ToArray();

        Assert.True(service.IsPublic && service.IsClass && service.IsSealed);
        Assert.False(service.IsAbstract);
        Assert.DoesNotContain(hitTestTypes, type => type.IsPublic && type.IsInterface);
        Assert.DoesNotContain(hitTestTypes, type =>
            type.Name.Contains("Registry", StringComparison.Ordinal) ||
            type.Name.Contains("Selector", StringComparison.Ordinal) ||
            type.Name.Contains("Backend", StringComparison.Ordinal) ||
            type.Name.Contains("Provider", StringComparison.Ordinal));

        var hitTest = Assert.Single(
            service.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            static method => !method.IsSpecialName);
        Assert.Equal("HitTest", hitTest.Name);
        Assert.Equal(
            [typeof(Canvas2DScene), typeof(PointD)],
            hitTest.GetParameters().Select(static parameter => parameter.ParameterType));
        Assert.Equal(
            $"{HitTestingNamespace}.Canvas2DSceneHitTestResult",
            hitTest.ReturnType.FullName);
    }

    [Fact]
    public void HitTestingHasNoAuthoritativeModelRuntimeOrBrowserDependency()
    {
        var implementationTypes = CanvasAssembly.GetTypes()
            .Where(type => type.Namespace == HitTestingNamespace)
            .SelectMany(GetImplementationSignatureTypes)
            .Distinct()
            .ToArray();

        Assert.DoesNotContain(implementationTypes, type =>
            type.Assembly == RuntimeAssembly ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.Commands",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.EditorState",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.History",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.Layout",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.Projection",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.Routing",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.Semantics",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Inceptus.DocumentEngine.Contracts.Visuals",
                StringComparison.Ordinal) == true ||
            IsBrowserType(type));

        var source = ReadProductionDirectory("Inceptus.DocumentEngine.Canvas2D", "HitTesting");
        string[] forbidden =
        [
            "CanvasRenderingContext2D",
            "CommandProcessor",
            "DocumentEngine.Runtime",
            "HistoryManager",
            "LayoutResult",
            "Path2D",
            "ProjectedGraph",
            "RoutingResult",
            "SemanticModel",
            "VisualModel",
            "isPointInPath",
            "isPointInStroke",
        ];

        Assert.DoesNotContain(forbidden, fragment =>
            source.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void InteractionRemainsOneConcreteSessionCoordinatorWithoutTransientCommands()
    {
        var controller = CanvasAssembly.GetType(
            $"{InteractionNamespace}.Canvas2DInteractionController",
            throwOnError: true)!;
        var interactionTypes = CanvasAssembly.GetTypes()
            .Where(type => type.Namespace == InteractionNamespace)
            .ToArray();

        Assert.True(controller.IsPublic && controller.IsClass && controller.IsSealed);
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(controller));
        Assert.DoesNotContain(interactionTypes, type => type.IsPublic && type.IsInterface);
        Assert.DoesNotContain(interactionTypes, type =>
            ContainsAny(
                type.Name,
                "EventBus",
                "GestureFramework",
                "InputDevice",
                "Registry",
                "Selector",
                "ToolManager"));

        var source = ReadProductionDirectory("Inceptus.DocumentEngine.Canvas2D", "Interaction") +
            Environment.NewLine +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Canvas2D",
                "EditingSession",
                "EditingSession.Interaction.cs");
        string[] forbidden =
        [
            "CommandProcessor",
            "HistoryManager",
            "LayoutEngine",
            "ProjectionEngine",
            "RoutingEngine",
            "SemanticModel",
            "SelectElementCommand",
            "HoverElementCommand",
            "ClearSelectionCommand",
            "SetViewportCommand",
        ];

        Assert.DoesNotContain(forbidden, fragment =>
            source.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void BrowserPointerCaptureIsInternalScalarOnlyAndContainsNoSceneMeaning()
    {
        var pointerTypes = BlazorAssembly.GetTypes()
            .Where(type => type.Name.Contains("Pointer", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(pointerTypes);
        Assert.All(pointerTypes, type => Assert.False(type.IsPublic || type.IsNestedPublic));
        Assert.DoesNotContain(pointerTypes.SelectMany(GetImplementationSignatureTypes), IsLiveDomOrCanvasType);

        var callback = Assert.Single(pointerTypes, type =>
            type.Name == "CanvasPresentationPointerCallback");
        var callbackMethods = callback.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var callbackMethod = Assert.Single(callbackMethods, method =>
            method.Name == "OnCanvasPointerInput");
        Assert.Equal("OnCanvasPointerInput", callbackMethod.Name);
        Assert.Equal(14, callbackMethod.GetParameters().Length);
        Assert.All(
            callbackMethod.GetParameters(),
            parameter => Assert.True(
                parameter.ParameterType.IsPrimitive || parameter.ParameterType.IsEnum,
                $"Pointer callback parameter '{parameter.Name}' is not scalar."));

        var javaScript = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "inceptus.presentation.js"));
        Assert.Contains("pointerdown", javaScript, StringComparison.Ordinal);
        Assert.Contains("pointermove", javaScript, StringComparison.Ordinal);
        Assert.Contains("pointerup", javaScript, StringComparison.Ordinal);
        Assert.Contains("pointercancel", javaScript, StringComparison.Ordinal);
        Assert.Contains("pointerleave", javaScript, StringComparison.Ordinal);
        Assert.Contains("setPointerCapture", javaScript, StringComparison.Ordinal);
        Assert.Contains("releasePointerCapture", javaScript, StringComparison.Ordinal);
        string[] forbidden =
        [
            "CanvasRenderingContext2D",
            "CommandProcessor",
            "SceneObjectId",
            "getContext(",
            "isPointInPath",
            "isPointInStroke",
        ];
        Assert.DoesNotContain(forbidden, fragment =>
            javaScript.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact]
    public void PhaseKAddsNoTransientCommandsOrUpstreamInteractionRuntime()
    {
        string[] forbiddenCommandNames =
        [
            "ClearSelectionCommand",
            "HoverElementCommand",
            "SelectElementCommand",
            "SetViewportCommand",
        ];
        var upstreamTypes = ContractsAssembly.GetTypes().Concat(RuntimeAssembly.GetTypes()).ToArray();

        Assert.DoesNotContain(upstreamTypes, type =>
            type.Namespace?.StartsWith(InteractionNamespace, StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(HitTestingNamespace, StringComparison.Ordinal) == true);
        Assert.DoesNotContain(upstreamTypes, type =>
            forbiddenCommandNames.Contains(type.Name, StringComparer.Ordinal));
        Assert.DoesNotContain(CanvasAssembly.GetTypes(),
            type => type.Namespace?.Contains("Bpmn", StringComparison.Ordinal) == true ||
                forbiddenCommandNames.Contains(type.Name, StringComparer.Ordinal));
        Assert.DoesNotContain(BlazorAssembly.GetTypes(), type =>
            forbiddenCommandNames.Contains(type.Name, StringComparer.Ordinal));
    }

    private static IEnumerable<Type> GetImplementationSignatureTypes(Type type)
    {
        yield return type;
        foreach (var field in type.GetFields(
                     BindingFlags.Public | BindingFlags.NonPublic |
                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            foreach (var expanded in ExpandType(field.FieldType))
            {
                yield return expanded;
            }
        }

        foreach (var constructor in type.GetConstructors(
                     BindingFlags.Public | BindingFlags.NonPublic |
                     BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }

        foreach (var method in type.GetMethods(
                     BindingFlags.Public | BindingFlags.NonPublic |
                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
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

    private static string ReadProductionDirectory(string project, string directory)
    {
        var path = Path.Combine(RepositoryRoot, "src", project, directory);
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static bool IsBrowserType(Type type) =>
        type.Namespace?.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith(
            "System.Runtime.InteropServices.JavaScript",
            StringComparison.Ordinal) == true ||
        type.Name is "CanvasRenderingContext2D" or "ElementReference" or
            "HTMLCanvasElement" or "Path2D" or "PointerEvent";

    private static bool IsLiveDomOrCanvasType(Type type) =>
        type.Namespace?.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal) == true ||
        type.Namespace?.StartsWith(
            "System.Runtime.InteropServices.JavaScript",
            StringComparison.Ordinal) == true ||
        type.Name is "CanvasRenderingContext2D" or "ElementReference" or
            "HTMLCanvasElement" or "Path2D" or "PointerEvent";

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

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
