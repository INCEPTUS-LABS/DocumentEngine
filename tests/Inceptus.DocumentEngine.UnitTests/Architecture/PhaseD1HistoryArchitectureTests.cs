using System.Reflection;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseD1HistoryArchitectureTests
{
    [Fact]
    public void HistorySurfaceHasNoDeferredTechnologyOrSerializationDependency()
    {
        var contractsAssembly = typeof(HistoryStatus).Assembly;
        var runtimeAssembly = typeof(HistoryManager).Assembly;
        var historyTypes = contractsAssembly.GetExportedTypes()
            .Concat(runtimeAssembly.GetExportedTypes())
            .Where(type => type.Namespace?.Contains(
                ".History",
                StringComparison.Ordinal) == true)
            .ToArray();
        string[] forbiddenAssemblyPrefixes =
        [
            "Inceptus.DocumentEngine.Bpmn",
            "Inceptus.DocumentEngine.Blazor",
            "Inceptus.DocumentEngine.Canvas2D",
            "Microsoft.AspNetCore.Components",
            "Microsoft.JSInterop",
            "System.Runtime.InteropServices.JavaScript",
        ];
        string[] forbiddenSignatureNamespacePrefixes =
        [
            .. forbiddenAssemblyPrefixes,
            "System.Text.Json",
        ];
        string[] forbiddenNameFragments =
        [
            "Canvas",
            "EditorState",
            "Json",
            "Layout",
            "Renderer",
            "Routing",
            "Scene",
            "Serialization",
        ];

        Assert.DoesNotContain(
            contractsAssembly.GetReferencedAssemblies()
                .Concat(runtimeAssembly.GetReferencedAssemblies()),
            reference => forbiddenAssemblyPrefixes.Any(prefix =>
                reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true));

        foreach (var type in historyTypes)
        {
            Assert.DoesNotContain(forbiddenNameFragments, fragment =>
                type.FullName?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true);
            Assert.DoesNotContain(
                PublicSignatureTypes(type),
                signatureType => forbiddenSignatureNamespacePrefixes.Any(prefix =>
                    signatureType.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true));
        }
    }

    [Fact]
    public void PublicHistoryApiExposesNoEntryCursorOrDirectMutationOperation()
    {
        var publicHistoryTypes = typeof(HistoryStatus).Assembly.GetExportedTypes()
            .Concat(typeof(HistoryManager).Assembly.GetExportedTypes())
            .Where(type => type.Namespace?.Contains(
                ".History",
                StringComparison.Ordinal) == true)
            .ToArray();
        string[] forbiddenMembers =
        [
            "Add",
            "Append",
            "Clear",
            "Cursor",
            "Entries",
            "Install",
            "Mutate",
            "Pop",
            "Push",
            "Record",
            "Remove",
            "Replace",
            "Set",
        ];

        Assert.DoesNotContain(publicHistoryTypes, type =>
            type.Name.Contains("Store", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Entry", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Contains("Mutation", StringComparison.OrdinalIgnoreCase));

        foreach (var type in publicHistoryTypes)
        {
            var members = type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.DoesNotContain(members, member => forbiddenMembers.Any(forbidden =>
                member.Name.Equals(forbidden, StringComparison.OrdinalIgnoreCase) ||
                member.Name.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase)));
            Assert.All(
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance |
                    BindingFlags.Static | BindingFlags.DeclaredOnly),
                property => Assert.Null(property.SetMethod));
        }
    }

    private static IEnumerable<Type> PublicSignatureTypes(Type type)
    {
        yield return type;

        foreach (var constructor in type.GetConstructors())
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var property in type.GetProperties(
                     BindingFlags.Public | BindingFlags.Instance |
                     BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            yield return property.PropertyType;
        }

        foreach (var method in type.GetMethods(
                     BindingFlags.Public | BindingFlags.Instance |
                     BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }
}
