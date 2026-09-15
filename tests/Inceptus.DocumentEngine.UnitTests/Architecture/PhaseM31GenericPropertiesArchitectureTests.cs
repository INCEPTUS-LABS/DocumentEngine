using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM31GenericPropertiesArchitectureTests
{
    [Fact]
    public void BlazorPropertiesPresentationIsSchemaDrivenWithExplicitBpmnEventExclusion()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var presentation = ReadProductionDirectory(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            ApprovedN104Changes.IsPublishingApplicationCompositionPath);
        var dataGroup = Between(
            component,
            "<fieldset data-property-group=\"data\">",
            "</fieldset>");

        const string eventExclusion = "!BpmnSemanticTypes.IsEvent(TypeId)";
        Assert.Equal(1, CountOccurrences(presentation, eventExclusion));
        Assert.DoesNotContain("BpmnSemanticTypes", component + presentation.Replace(eventExclusion, string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", component + presentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmn", component + presentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("NeutralDemoPipeline", component + presentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("NeutralDemoPropertiesSchemas", component + presentation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("demo:", component + presentation,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@foreach (var field in draft.DataFields)", dataGroup,
            StringComparison.Ordinal);
        Assert.Contains(
            "DomId($\"properties-data-field-{fieldId.Value}\")",
            component,
            StringComparison.Ordinal);
        Assert.Contains("@switch (field.Definition.EditorKind)", dataGroup,
            StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(dataGroup, "case ElementPropertyEditorKind."));
        Assert.Contains("case ElementPropertyEditorKind.SingleLineText:", dataGroup,
            StringComparison.Ordinal);
        Assert.Contains("case ElementPropertyEditorKind.Integer:", dataGroup,
            StringComparison.Ordinal);
        Assert.Contains("case ElementPropertyEditorKind.MultilineText:", dataGroup,
            StringComparison.Ordinal);
        Assert.Contains("case ElementPropertyEditorKind.Boolean:", dataGroup,
            StringComparison.Ordinal);
        Assert.DoesNotContain("SemanticTypeId", dataGroup, StringComparison.Ordinal);
        Assert.DoesNotContain("SemanticPropertyKey", dataGroup, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaCatalogRemainsACompositionConcernOutsideAuthoritativeEditorModels()
    {
        var composition = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasComposition.cs");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var forbiddenLayers = string.Join(
            Environment.NewLine,
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Documents"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Semantics"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Visuals"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Canvas2D", "EditingSession"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Canvas2D", "Scene"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Runtime", "Documents"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Runtime", "History"));

        Assert.Contains("ElementPropertiesSchemaCatalog propertiesSchemaCatalog", composition,
            StringComparison.Ordinal);
        Assert.Contains("PropertiesSchemaCatalog = propertiesSchemaCatalog", composition,
            StringComparison.Ordinal);
        Assert.Contains("composition.PropertiesSchemaCatalog", host,
            StringComparison.Ordinal);
        Assert.Contains("_propertiesSchemaCatalog", host, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementPropertiesSchemaCatalog", forbiddenLayers,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaContractsContainOnlyImmutableDeclarativeData()
    {
        Type[] contractTypes =
        [
            typeof(ElementPropertyFieldId),
            typeof(ElementPropertyFieldDefinition),
            typeof(ElementPropertiesSchema),
            typeof(ElementPropertiesSchemaCatalog),
        ];

        foreach (var contractType in contractTypes)
        {
            Assert.All(
                contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance),
                property =>
                {
                    Assert.Null(property.SetMethod);
                    Assert.False(IsExecutableType(property.PropertyType));
                    Assert.DoesNotContain("Command", property.Name,
                        StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("Callback", property.Name,
                        StringComparison.OrdinalIgnoreCase);
                });
            Assert.All(
                contractType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .SelectMany(static constructor => constructor.GetParameters()),
                parameter => Assert.False(IsExecutableType(parameter.ParameterType)));
        }
    }

    [Fact]
    public void InputOnlyUpdatesDraftAndApplyDispatchesExistingGenericCommands()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.cs");
        var dataGroup = Between(
            component,
            "<fieldset data-property-group=\"data\">",
            "</fieldset>");
        var inputHandler = Between(
            component,
            "private void UpdateDataDraftValue(",
            "private void UpdateVisualDraftValue(");
        var apply = Between(
            host,
            "internal async ValueTask<DocumentCanvasPropertiesApplyResult> ApplyPropertiesAsync(",
            "internal DocumentCanvasHostState CaptureState()");
        var propertiesApply = Between(
            host,
            "internal async ValueTask<DocumentCanvasPropertiesApplyResult> ApplyPropertiesAsync(",
            "internal async ValueTask<DocumentCanvasPropertySnapshot?> OpenPropertiesAsync(");

        Assert.Equal(3, CountOccurrences(dataGroup, "@oninput="));
        Assert.DoesNotContain("@onblur", dataGroup, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(dataGroup, "@onchange="));
        Assert.DoesNotContain("@onkeydown", dataGroup, StringComparison.Ordinal);
        Assert.DoesNotContain("Command", inputHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("Execute", inputHandler, StringComparison.Ordinal);
        Assert.Contains("field.EditorValue =", inputHandler, StringComparison.Ordinal);
        Assert.Contains("SemanticPropertyMutationKind.Name", apply, StringComparison.Ordinal);
        Assert.Contains("SemanticPropertyMutationKind.Property", apply,
            StringComparison.Ordinal);
        Assert.Contains("new UpdateSemanticElementNameCommand(", apply,
            StringComparison.Ordinal);
        Assert.Contains("new UpdateSemanticElementPropertyCommand(", apply,
            StringComparison.Ordinal);
        Assert.Contains("new MoveVisualStateCommand(", apply, StringComparison.Ordinal);
        Assert.Contains("new ResizeVisualStateCommand(", apply, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(apply, "session.ExecuteForSceneTargetAsync("));
        Assert.Equal(
            1,
            CountOccurrences(propertiesApply, "session.ExecuteForSceneTargetAsync("));
        Assert.Contains("current.VisualStateId is null", propertiesApply,
            StringComparison.Ordinal);
        Assert.Contains("current.SpatialRegion", propertiesApply,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", apply, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExecutableType(Type type) =>
        typeof(Delegate).IsAssignableFrom(type) ||
        typeof(ICommand).IsAssignableFrom(type) ||
        type.FullName?.Contains("RenderFragment", StringComparison.Ordinal) == true;

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not isolate '{startMarker}'.");
        return source[start..end];
    }

    private static string ReadProductionDirectory(
        string project,
        string directory,
        Func<string, bool>? excludedPath = null)
    {
        var path = Path.Combine(RepositoryRoot, "src", project, directory);
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(static path =>
                    path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                .Where(candidate => excludedPath is null || !excludedPath(candidate))
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
    }

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

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
