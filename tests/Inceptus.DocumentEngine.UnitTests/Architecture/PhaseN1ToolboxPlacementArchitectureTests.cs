using System.Reflection;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Creation;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN1ToolboxPlacementArchitectureTests
{
    [Fact]
    public void ToolboxDefinitionsRemainDataOnlyAndPlacementIsASeparateCapability()
    {
        var definitionMembers = typeof(ToolboxItemDefinition).GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(definitionMembers, static member =>
            MemberType(member) is { } type &&
            (typeof(ICommand).IsAssignableFrom(type) ||
             typeof(Delegate).IsAssignableFrom(type) ||
             typeof(IToolboxPlacementCommandFactory).IsAssignableFrom(type)));
        Assert.DoesNotContain(definitionMembers, static member =>
            member.Name.Contains("Placement", StringComparison.Ordinal) ||
            member.Name.Contains("Command", StringComparison.Ordinal));

        Assert.NotEqual(typeof(ToolboxCatalog), typeof(ToolboxPlacementCatalog));
        Assert.NotEqual(typeof(ToolboxContribution), typeof(ToolboxPlacementRegistration));
        Assert.Empty(BpmnPluginRegistration.M34.ToolboxPlacementRegistrations);
    }

    [Fact]
    public void GenericPlacementContractsAreNotationAndPresentationNeutral()
    {
        Type[] placementTypes =
        [
            typeof(IToolboxPlacementCommandFactory),
            typeof(IDocumentCreationIdentityProvider),
            typeof(DocumentCreationIdentity),
            typeof(ToolboxPlacementRequest),
            typeof(ToolboxPlacementPlan),
            typeof(ToolboxPlacementPlanResult),
            typeof(ToolboxPlacementRegistration),
            typeof(ToolboxPlacementCatalog),
        ];
        var contractsAssembly = typeof(ToolboxCatalog).Assembly;

        Assert.All(placementTypes, type =>
        {
            Assert.True(type.IsPublic);
            Assert.Same(contractsAssembly, type.Assembly);
        });

        var signatures = placementTypes
            .SelectMany(PublicSignatureTypes)
            .Distinct()
            .ToArray();
        Assert.DoesNotContain(signatures, static type =>
            type.Namespace?.Contains("Bpmn", StringComparison.OrdinalIgnoreCase) == true ||
            type.Namespace?.Contains("Blazor", StringComparison.OrdinalIgnoreCase) == true ||
            type.Namespace?.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith(
                "Microsoft.AspNetCore.Components",
                StringComparison.Ordinal) == true);

        var source = ReadProductionFiles(
            "Inceptus.DocumentEngine.Contracts",
            "Toolbox",
            "ToolboxPlacement*.cs") +
            ReadProductionFiles(
                "Inceptus.DocumentEngine.Contracts",
                "Creation",
                "*.cs") +
            ReadProductionFile(
                "Inceptus.DocumentEngine.Contracts",
                "Toolbox",
                "IToolboxPlacementCommandFactory.cs");
        string[] forbidden =
        [
            "BPMN.",
            "Bpmn",
            "Canvas2DRenderer",
            "RenderFragment",
            "Microsoft.JSInterop",
            "CreateBpmn",
            "AddConnectorAnchorCommand",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void N1RegistersEveryVisibleBpmnNodeExactlyOnceAndNoSequenceFlow()
    {
        var registration = BpmnPluginRegistration.N1;
        var toolbox = new ToolboxCatalog(registration.ToolboxContributions);
        var placements = registration.ToolboxPlacementRegistrations;

        Assert.Equal(6, toolbox.Items.Length);
        Assert.Equal(6, placements.Length);
        Assert.Equal(
            toolbox.Items.Select(static item => item.ItemId).OrderBy(static id => id.Value),
            placements.Select(static item => item.ToolboxItemId).OrderBy(static id => id.Value));
        Assert.Equal(6, placements.Select(static item => item.ToolboxItemId).Distinct().Count());
        Assert.DoesNotContain(toolbox.Items, static item =>
            item.DisplayName.Contains("Sequence Flow", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(placements, static item =>
            item.ToolboxItemId.Value.Contains("sequence-flow", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlacementExecutionUsesSessionAndKeepsBpmnOutOfGenericPresentation()
    {
        var controller = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "ToolboxPlacementController.cs");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var panel = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "ToolboxPanel.razor");
        var canvas = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var placement = ReadProductionFiles(
            "Inceptus.DocumentEngine.Bpmn",
            "Placement",
            "*.cs");

        Assert.Contains("session.ExecuteForSceneTargetAsync(", controller,
            StringComparison.Ordinal);
        Assert.Contains("session.UpdateEditorStateAsync(", controller, StringComparison.Ordinal);
        Assert.Contains("Canvas2DRenderer.ConvertCssToDocument(", controller + host,
            StringComparison.Ordinal);
        Assert.Contains("VisualPlacementMode.Pinned", placement, StringComparison.Ordinal);

        string[] forbiddenGeneric =
        [
            "BpmnSemanticTypes",
            "BpmnPluginRegistration",
            "CreateBpmn",
            "CommandProcessor",
            "BpmnElementCreationCommandHandler",
        ];
        Assert.All(forbiddenGeneric, token =>
            Assert.DoesNotContain(token, controller + host + panel + canvas,
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("AddConnectorAnchorCommand", controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmnSequenceFlowCommand", controller,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmnSequenceFlowCommand", placement,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AddConnectorAnchorCommand", placement,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectorAnchor", placement, StringComparison.Ordinal);
    }

    [Fact]
    public void N1IntroducesNoDragDropGhostOrCoreEnginePlacementMechanism()
    {
        var razor = ReadProductionFiles(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "*.razor");
        var javascript = ReadProductionFiles(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "wwwroot",
            "*.js");
        var runtime = ReadProductionFiles(
            "Inceptus.DocumentEngine.Runtime",
            string.Empty,
            "*.cs");
        var canvas2D = ReadProductionFiles(
            "Inceptus.DocumentEngine.Canvas2D",
            string.Empty,
            "*.cs");

        string[] forbiddenUi = ["@ondrag", "@ondrop", "draggable=", "dragstart"];
        Assert.All(forbiddenUi, token =>
            Assert.DoesNotContain(token, razor + javascript, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("ToolboxPlacement", runtime, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ToolboxPlacement", canvas2D, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlacementGhost", razor + javascript,
            StringComparison.OrdinalIgnoreCase);
    }

    private static Type? MemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        MethodInfo method => method.ReturnType,
        EventInfo @event => @event.EventHandlerType,
        _ => null,
    };

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

        foreach (var member in type.GetMembers(
                     BindingFlags.Public | BindingFlags.Instance |
                     BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (MemberType(member) is { } memberType)
            {
                yield return memberType;
            }

            if (member is MethodInfo method)
            {
                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType;
                }
            }
        }
    }

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

    private static string ReadProductionFiles(
        string project,
        string directory,
        string pattern)
    {
        var path = Path.Combine(RepositoryRoot, "src", project, directory);
        return Directory.Exists(path)
            ? string.Concat(Directory
                .GetFiles(path, pattern, SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText))
            : string.Empty;
    }

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
