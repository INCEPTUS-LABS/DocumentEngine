using System.Reflection;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Bpmn.Blazor.Presentation;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Canvas2D.Scene;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM05ToolboxArchitectureTests
{
    private static readonly Type[] ToolboxContractTypes =
    [
        typeof(ToolboxSectionId),
        typeof(ToolboxGroupId),
        typeof(ToolboxItemId),
        typeof(ToolboxIconDescriptor),
        typeof(ToolboxSectionDefinition),
        typeof(ToolboxGroupDefinition),
        typeof(ToolboxItemDefinition),
        typeof(ToolboxContribution),
        typeof(ToolboxCatalog),
    ];

    [Fact]
    public void PluginBoundaryIsPublicGenericDataOnlyAndUsesCanonicalSemanticTypeIdentity()
    {
        var contractsAssembly = typeof(ToolboxCatalog).Assembly;

        Assert.All(ToolboxContractTypes, type =>
        {
            Assert.True(type.IsPublic);
            Assert.Same(contractsAssembly, type.Assembly);
        });
        Assert.Equal(
            typeof(SemanticTypeId),
            typeof(ToolboxItemDefinition)
                .GetProperty(nameof(ToolboxItemDefinition.ElementTypeId))!
                .PropertyType);
        Assert.Equal(
            typeof(ToolboxItemId),
            typeof(ToolboxItemDefinition)
                .GetProperty(nameof(ToolboxItemDefinition.ItemId))!
                .PropertyType);
        Assert.NotEqual(
            typeof(ToolboxItemDefinition)
                .GetProperty(nameof(ToolboxItemDefinition.ItemId))!
                .PropertyType,
            typeof(ToolboxItemDefinition)
                .GetProperty(nameof(ToolboxItemDefinition.ElementTypeId))!
                .PropertyType);

        var signatureTypes = ToolboxContractTypes
            .SelectMany(GetPublicSignatureTypes)
            .Distinct()
            .ToArray();
        Assert.DoesNotContain(signatureTypes, static type =>
            type.Namespace?.StartsWith(
                "Microsoft.AspNetCore.Components",
                StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) == true ||
            typeof(Delegate).IsAssignableFrom(type));
        Assert.DoesNotContain(ToolboxContractTypes.SelectMany(PublicMembers), static member =>
            member.Name.Contains("RenderFragment", StringComparison.Ordinal) ||
            member.Name.Contains("CreateElement", StringComparison.Ordinal) ||
            member.Name.Contains("CommandFactory", StringComparison.Ordinal) ||
            member.Name.Contains("JavaScript", StringComparison.Ordinal));
    }

    [Fact]
    public void CatalogKeepsToolboxAndSemanticIdentitiesIndependentAndOrdersContributions()
    {
        var groupA = new ToolboxGroupDefinition(new ToolboxGroupId("test:a"), "A", 10);
        var groupB = new ToolboxGroupDefinition(new ToolboxGroupId("test:b"), "B", 10);
        var sharedType = new SemanticTypeId("test:shared-type");
        var itemA = Item("test:a:item-b", sharedType, groupA.GroupId, 20);
        var itemB = Item("test:a:item-a", sharedType, groupA.GroupId, 20);
        var itemC = Item("test:b:item", new SemanticTypeId("test:other-type"), groupB.GroupId, 0);
        var first = new ToolboxCatalog(
        [
            new ToolboxContribution([groupB], [itemC]),
            new ToolboxContribution([groupA], [itemA, itemB]),
        ]);
        var reversed = new ToolboxCatalog(
        [
            new ToolboxContribution([groupA], [itemB, itemA]),
            new ToolboxContribution([groupB], [itemC]),
        ]);

        Assert.Equal([groupA, groupB], first.Groups.ToArray());
        Assert.Equal([itemB, itemA, itemC], first.Items.ToArray());
        Assert.Equal(first.Groups.ToArray(), reversed.Groups.ToArray());
        Assert.Equal(first.Items.ToArray(), reversed.Items.ToArray());
        Assert.Equal(2, first.Items.Count(item => item.ElementTypeId == sharedType));
        Assert.Equal(2, first.Items
            .Where(item => item.ElementTypeId == sharedType)
            .Select(item => item.ItemId)
            .Distinct()
            .Count());
    }

    [Fact]
    public void ToolboxSelectionIsPresentationLocalAndAbsentFromDocumentEditorAndHistoryState()
    {
        Assert.Equal(
            "Inceptus.DocumentEngine.Bpmn.Blazor.Presentation",
            typeof(ToolboxSelectionState).Namespace);
        Assert.True(typeof(ToolboxPanel).Assembly == typeof(ToolboxSelectionState).Assembly);

        var persistentAndRuntimeStateTypes = new[]
        {
            typeof(DocumentSnapshot),
            typeof(EditorStateSnapshot),
            typeof(VisualStateSnapshot),
            typeof(HistoryStatus),
        };
        Assert.DoesNotContain(
            persistentAndRuntimeStateTypes.SelectMany(PublicMembers),
            static member => MemberType(member) == typeof(ToolboxItemId) ||
                member.Name.Contains("Toolbox", StringComparison.Ordinal));

        var selectionMembers = typeof(ToolboxSelectionState).GetMembers(
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.DoesNotContain(selectionMembers, static member =>
            MemberType(member) is { } memberType &&
            (typeof(ICommand).IsAssignableFrom(memberType) ||
             memberType.Namespace?.Contains("History", StringComparison.Ordinal) == true ||
             memberType.Name.Contains("EditingSession", StringComparison.Ordinal)));

        var commandTypes = typeof(ICommand).Assembly.GetExportedTypes()
            .Where(type => typeof(ICommand).IsAssignableFrom(type))
            .ToArray();
        Assert.DoesNotContain(commandTypes, static type =>
            type.Name.Contains("Toolbox", StringComparison.Ordinal));
    }

    [Fact]
    public void ToolboxRemainsDomUiAndIsAbsentFromCanvasRendererSceneAndJavaScript()
    {
        var renderer = ReadProductionFiles(
            "Inceptus.DocumentEngine.Canvas2D",
            "Rendering",
            "*.cs");
        var scene = ReadProductionFiles(
            "Inceptus.DocumentEngine.Canvas2D",
            "Scene",
            "*.cs");
        var javascript = ReadProductionFiles(
            "Inceptus.DocumentEngine.Canvas2D",
            "wwwroot",
            "*.js") +
            ReadProductionFiles(
                "Inceptus.DocumentEngine.Bpmn.Blazor",
                "wwwroot",
                "*.js");

        Assert.DoesNotContain("Toolbox", renderer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Toolbox", scene, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Toolbox", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            typeof(Canvas2DScene).GetProperties(),
            property => GetExpandedTypes(property.PropertyType)
                .Any(type => ToolboxContractTypes.Contains(type)));
        Assert.DoesNotContain(
            typeof(Canvas2DRenderer).GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly),
            member => member.Name.Contains("Toolbox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RazorContainsNoCreationDragTypeSwitchOrSessionMutationPath()
    {
        var panel = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "ToolboxPanel.razor");
        var allToolboxContracts = ReadProductionFiles(
            "Inceptus.DocumentEngine.Contracts",
            "Toolbox",
            "*.cs");
        var anchorRegistry = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Visuals",
            "ElementConnectorAnchorPolicyRegistry.cs");

        string[] forbiddenPanelTokens =
        [
            "@ondrag",
            "@ondrop",
            "draggable=",
            "Canvas2DScene",
            "DocumentCanvasHost",
            "EditingSession",
            "ExecuteAsync",
            "SemanticTypeId",
            "BpmnSemanticTypes",
            "BpmnPluginRegistration",
            "CreateBpmn",
            "CreateElement",
            "Placement",
        ];
        foreach (var token in forbiddenPanelTokens)
        {
            Assert.DoesNotContain(token, panel, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("Bpmn", allToolboxContracts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RenderFragment", allToolboxContracts, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementConnectorAnchorPolicy", allToolboxContracts,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Toolbox", anchorRegistry, StringComparison.Ordinal);
    }

    private static ToolboxItemDefinition Item(
        string itemId,
        SemanticTypeId typeId,
        ToolboxGroupId groupId,
        int order) =>
        new(
            new ToolboxItemId(itemId),
            typeId,
            groupId,
            itemId,
            order,
            new ToolboxIconDescriptor("test:icon", "□"));

    private static IEnumerable<MemberInfo> PublicMembers(Type type) =>
        type.GetMembers(
            BindingFlags.Public | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly);

    private static Type? MemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        MethodInfo method => method.ReturnType,
        EventInfo @event => @event.EventHandlerType,
        _ => null,
    };

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        yield return type;
        foreach (var constructor in type.GetConstructors())
        {
            foreach (var parameter in constructor.GetParameters())
            {
                foreach (var expanded in GetExpandedTypes(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }

        foreach (var member in PublicMembers(type))
        {
            if (MemberType(member) is not { } memberType)
            {
                continue;
            }

            foreach (var expanded in GetExpandedTypes(memberType))
            {
                yield return expanded;
            }

            if (member is not MethodInfo method)
            {
                continue;
            }

            foreach (var parameter in method.GetParameters())
            {
                foreach (var expanded in GetExpandedTypes(parameter.ParameterType))
                {
                    yield return expanded;
                }
            }
        }
    }

    private static IEnumerable<Type> GetExpandedTypes(Type type)
    {
        yield return type;
        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var expanded in GetExpandedTypes(elementType))
            {
                yield return expanded;
            }
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var expanded in GetExpandedTypes(argument))
            {
                yield return expanded;
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
        string searchPattern)
    {
        var root = Path.Combine(RepositoryRoot, "src", project, directory);
        return Directory.Exists(root)
            ? string.Concat(
                Directory.GetFiles(root, searchPattern, SearchOption.AllDirectories)
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
