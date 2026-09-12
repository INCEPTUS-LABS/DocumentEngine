using System.Collections;
using System.Reflection;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseD2EditingSessionArchitectureTests
{
    private const string SessionNamespace = "Inceptus.DocumentEngine.Canvas2D.EditingSession";

    private static readonly Assembly CanvasAssembly = typeof(Canvas2DScene).Assembly;
    private static readonly Assembly ContractsAssembly = typeof(DocumentSnapshot).Assembly;
    private static readonly Assembly RuntimeAssembly = typeof(Document).Assembly;

    [Fact]
    public void EditingSessionIsOneFrameworkOwnedCanvas2DBoundary()
    {
        var sessionTypes = SessionTypes().ToArray();
        var editingSession = Assert.Single(
            sessionTypes,
            type => type.FullName == $"{SessionNamespace}.EditingSession");

        Assert.True(editingSession.IsPublic);
        Assert.True(editingSession.IsClass);
        Assert.True(editingSession.IsSealed);
        Assert.False(editingSession.IsAbstract);
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(editingSession));
        Assert.DoesNotContain(sessionTypes, type =>
            type.IsPublic && type.IsInterface);
        Assert.DoesNotContain(sessionTypes, type =>
            ContainsAny(
                type.Name,
                "EditingSessionRegistry",
                "EditingSessionSelector",
                "IProcessingPipeline",
                "PipelineRegistry",
                "PipelineSelector"));

        var upstreamTypes = ContractsAssembly.GetTypes().Concat(RuntimeAssembly.GetTypes());
        Assert.DoesNotContain(upstreamTypes, type =>
            type.Namespace?.StartsWith(SessionNamespace, StringComparison.Ordinal) == true ||
            type.Name.Contains("EditingSession", StringComparison.Ordinal));
    }

    [Fact]
    public void EditingSessionStatusContainsExactlyTheThreeApprovedActiveStates()
    {
        var status = CanvasAssembly.GetType(
            $"{SessionNamespace}.EditingSessionStatus",
            throwOnError: true)!;

        Assert.True(status.IsPublic);
        Assert.True(status.IsEnum);
        Assert.Equal(
            ["Ready", "Rebuilding", "RuntimeFaulted"],
            Enum.GetNames(status).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PublicEditingSessionSurfaceIsImmutableAndContainsNoHostOrBrowserResources()
    {
        var exportedSessionTypes = CanvasAssembly.GetExportedTypes()
            .Where(IsSessionType)
            .ToArray();

        Assert.NotEmpty(exportedSessionTypes);
        foreach (var type in exportedSessionTypes)
        {
            if (!type.IsEnum)
            {
                Assert.DoesNotContain(
                    type.GetFields(
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.DeclaredOnly),
                    field => !field.IsLiteral && !field.IsInitOnly);
            }
            Assert.All(
                type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly),
                property => Assert.Null(property.SetMethod));

            var signatureTypes = PublicDataAndOperationSignatureTypes(type).ToArray();
            Assert.DoesNotContain(signatureTypes, IsMutableCollectionType);
            Assert.DoesNotContain(signatureTypes, signatureType =>
                signatureType == typeof(IServiceProvider) ||
                typeof(Delegate).IsAssignableFrom(signatureType) ||
                IsForbiddenHostOrBrowserType(signatureType));

            var events = type.GetEvents(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly);
            Assert.All(events, @event =>
            {
                Assert.Equal("StateChanged", @event.Name);
                Assert.Equal(
                    typeof(EventHandler<>).MakeGenericType(
                        CanvasAssembly.GetType(
                            $"{SessionNamespace}.EditingSessionStateChangedEventArgs",
                            throwOnError: true)!),
                    @event.EventHandlerType);
            });
        }
    }

    [Fact]
    public void AuthoritativeCommandHistoryAndEditorContractsContainNoSessionState()
    {
        Type[] upstreamBoundaries =
        [
            typeof(DocumentSnapshot),
            typeof(IDocumentView),
            typeof(ICommand),
            typeof(ICommandHandler),
            typeof(ICommandValidator),
            typeof(DocumentChangedEvent),
            typeof(HistoryStatus),
            typeof(HistoryOperationResult),
            typeof(EditorStateSnapshot),
            typeof(HistoryManager),
        ];

        foreach (var type in upstreamBoundaries)
        {
            Assert.DoesNotContain(
                PublicSignatureTypes(type),
                signatureType => IsSessionType(signatureType) ||
                    signatureType.Name.Contains("EditingSession", StringComparison.Ordinal));
            Assert.DoesNotContain(
                type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly),
                member => ContainsAny(
                    member.Name,
                    "EditingSession",
                    "CurrentCanvas2DScene",
                    "LastKnownGoodCanvas2DScene",
                    "RuntimeFaulted"));
        }
    }

    [Fact]
    public void EditingSessionExposesNoTransactionStateReplacementOrPersistentSceneOperation()
    {
        string[] forbiddenMemberFragments =
        [
            "AdvanceRevision",
            "BeginTransaction",
            "CommitState",
            "InstallDocumentState",
            "PublishDocumentChanged",
            "ReplaceDocumentState",
            "Serialize",
            "SetRevision",
        ];

        foreach (var type in CanvasAssembly.GetExportedTypes().Where(IsSessionType))
        {
            var members = type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly);
            Assert.DoesNotContain(members, member =>
                forbiddenMemberFragments.Any(fragment =>
                    member.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void InternalSessionOrchestrationCannotBecomeAPluginReplaceableEngine()
    {
        var sessionTypes = SessionTypes().ToArray();

        Assert.DoesNotContain(sessionTypes, type =>
            type.IsInterface && type.IsPublic);
        Assert.DoesNotContain(sessionTypes, type =>
            type.IsPublic &&
            ContainsAny(type.Name, "Pipeline", "Runner", "Scheduler", "CoordinatorFactory"));
        Assert.DoesNotContain(sessionTypes, type =>
            type.IsPublic && type.GetInterfaces().Any(@interface =>
                ContainsAny(@interface.Name, "Engine", "Pipeline", "RendererBackend")));
    }

    private static IEnumerable<Type> SessionTypes() =>
        CanvasAssembly.GetTypes().Where(IsSessionType);

    private static bool IsSessionType(Type type) =>
        type.Namespace?.StartsWith(SessionNamespace, StringComparison.Ordinal) == true;

    private static IEnumerable<Type> PublicSignatureTypes(Type type)
    {
        yield return type;
        foreach (var constructor in type.GetConstructors(
                     BindingFlags.Public | BindingFlags.Instance))
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
                     BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                     BindingFlags.DeclaredOnly))
        {
            IEnumerable<Type> memberTypes = member switch
            {
                MethodInfo method =>
                [method.ReturnType, .. method.GetParameters()
                    .Select(static parameter => parameter.ParameterType)],
                PropertyInfo property => [property.PropertyType],
                EventInfo @event when @event.EventHandlerType is not null =>
                    [@event.EventHandlerType],
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

    private static IEnumerable<Type> PublicDataAndOperationSignatureTypes(Type type)
    {
        yield return type;
        foreach (var constructor in type.GetConstructors(
                     BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var expanded in ExpandType(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }

        foreach (var property in type.GetProperties(
                     BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                     BindingFlags.DeclaredOnly))
        {
            foreach (var expanded in ExpandType(property.PropertyType))
            {
                yield return expanded;
            }
        }

        foreach (var method in type.GetMethods(
                     BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                     BindingFlags.DeclaredOnly).Where(static method => !method.IsSpecialName))
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

    private static bool IsMutableCollectionType(Type type)
    {
        if (type == typeof(IList) || type == typeof(IDictionary))
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>) ||
            definition == typeof(Dictionary<,>) ||
            definition == typeof(IList<>) ||
            definition == typeof(ICollection<>) ||
            definition == typeof(IDictionary<,>);
    }

    private static bool IsForbiddenHostOrBrowserType(Type type)
    {
        var namespaceName = type.Namespace ?? string.Empty;
        return namespaceName.StartsWith("Inceptus.DocumentEngine.Bpmn", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Inceptus.DocumentEngine.Blazor", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal) ||
            namespaceName.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) ||
            namespaceName.StartsWith("System.Runtime.InteropServices.JavaScript", StringComparison.Ordinal) ||
            namespaceName.StartsWith("System.Text.Json", StringComparison.Ordinal) ||
            type.Name is "HTMLCanvasElement" or "CanvasRenderingContext2D" or "Path2D" or
                "ImageBitmap" or "CanvasGradient" or "CanvasPattern" or "ElementReference";
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment =>
            value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
