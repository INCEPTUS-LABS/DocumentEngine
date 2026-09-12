using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseC2ScopeArchitectureTests
{
    [Fact]
    public void PhaseHKeepsFinalSceneAndBuilderOutOfUpstreamAssemblies()
    {
        string[] deferredSubsystemFragments =
        [
            "EditingSession",
            "Renderer",
        ];
        var upstreamTypes = typeof(ICommand).Assembly.GetTypes()
            .Concat(typeof(Document).Assembly.GetTypes())
            .Where(type => !type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            .ToArray();

        Assert.DoesNotContain(upstreamTypes, type =>
            deferredSubsystemFragments.Any(fragment =>
                type.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(upstreamTypes, type =>
            type.Name is "Canvas2DScene" or "Canvas2DSceneBuilder");
    }

    [Fact]
    public void HandlerAndEventContractsDoNotLeakRuntimeBrowserOrHostTypes()
    {
        Type[] contractTypes =
        [
            typeof(ICommandHandler),
            typeof(ICommandEnvelopeValidator),
            typeof(CommandHandlerRegistration),
            typeof(CommandHandlerResult),
            typeof(IDocumentChangedSubscriber),
            typeof(DocumentChangedEvent),
            typeof(CommandExecutionResult),
        ];
        var signatureTypes = contractTypes
            .SelectMany(GetPublicSignatureTypes)
            .ToArray();
        string[] forbiddenNamespacePrefixes =
        [
            "Inceptus.DocumentEngine.Runtime",
            "Inceptus.DocumentEngine.Bpmn",
            "Inceptus.DocumentEngine.Canvas2D",
            "Inceptus.DocumentEngine.Blazor",
            "Microsoft.AspNetCore.Components",
            "Microsoft.JSInterop",
            "System.Runtime.InteropServices.JavaScript",
        ];

        Assert.DoesNotContain(signatureTypes, type =>
            forbiddenNamespacePrefixes.Any(prefix =>
                type.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true));
        Assert.DoesNotContain(signatureTypes, type => type == typeof(IServiceProvider));
        Assert.DoesNotContain(signatureTypes, type => typeof(Delegate).IsAssignableFrom(type));
    }

    [Fact]
    public void TransactionsAndDocumentStateReplacementAreNotPublicContracts()
    {
        var productionAssemblies = new[] { typeof(ICommand).Assembly, typeof(Document).Assembly };
        var exportedTypes = productionAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .ToArray();

        Assert.DoesNotContain(exportedTypes, type =>
            type.Name.Contains("Transaction", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("StateReplacement", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("ExecutionContext", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        yield return type;

        foreach (var property in type.GetProperties(
                     BindingFlags.Public | BindingFlags.Instance |
                     BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            foreach (var expanded in ExpandType(property.PropertyType))
            {
                yield return expanded;
            }
        }

        foreach (var method in type.GetMethods(
                     BindingFlags.Public | BindingFlags.Instance |
                     BindingFlags.Static | BindingFlags.DeclaredOnly))
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

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var expanded in ExpandType(argument))
            {
                yield return expanded;
            }
        }
    }
}
