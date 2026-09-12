using System.Reflection;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Runtime.Validation;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN5ModelValidationArchitectureTests
{
    [Fact]
    public void N5IsOneValidationContributionLayerOverN4()
    {
        var n4 = BpmnPluginRegistration.N4;
        var n5 = BpmnPluginRegistration.N5;

        Assert.Equal(n4.CommandHandlers, n5.CommandHandlers);
        Assert.Equal(n4.CommandValidators, n5.CommandValidators);
        Assert.Equal(n4.HistoryPolicies, n5.HistoryPolicies);
        Assert.Equal(n4.ProjectionRules, n5.ProjectionRules);
        Assert.Equal(n4.LayoutAlgorithms, n5.LayoutAlgorithms);
        Assert.Equal(n4.RoutingAlgorithms, n5.RoutingAlgorithms);
        Assert.Equal(n4.SceneContributors, n5.SceneContributors);
        Assert.Empty(n4.ModelValidationRules);
        Assert.IsType<BpmnStructuralValidationRule>(Assert.Single(n5.ModelValidationRules));
    }

    [Fact]
    public void GenericValidationContractsAndRuntimeContainNoNotationPolicy()
    {
        var generic = string.Concat(
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Validation"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Runtime", "Validation"));
        string[] forbidden =
        [
            "Bpmn",
            "SequenceFlow",
            "StartEvent",
            "EndEvent",
            "EventBasedGateway",
            "MessageCatchEvent",
            "TimerCatchEvent",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, generic, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BpmnValidationRuleIsReadOnlyAndDoesNotInvokePipelineOrPresentation()
    {
        var source = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Validation",
            "BpmnStructuralValidationRule.cs");
        string[] forbidden =
        [
            "ICommand",
            "CommandProcessor",
            "ProjectionEngine",
            "LayoutEngine",
            "RoutingEngine",
            "SceneBuilder",
            "EditorState",
            "JavaScript",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, source, StringComparison.Ordinal));
    }

    [Fact]
    public void HumanReadableFormattingIsBpmnOwnedWithoutPollutingGenericContracts()
    {
        var formatter = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Validation",
            "BpmnValidationTargetFormatter.cs");
        Assert.Contains("BpmnSemanticProperties.Code", formatter, StringComparison.Ordinal);
        Assert.Contains("BpmnSemanticProperties.Name", formatter, StringComparison.Ordinal);
        Assert.Contains("BpmnSemanticProperties.ElementNumber", formatter,
            StringComparison.Ordinal);

        var generic = string.Concat(
            ReadProductionDirectory("Inceptus.DocumentEngine.Contracts", "Validation"),
            ReadProductionDirectory("Inceptus.DocumentEngine.Runtime", "Validation"));
        Assert.DoesNotContain("BpmnSemanticProperties", generic, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementNumber", generic, StringComparison.Ordinal);

        string[] forbiddenIssueProperties =
        [
            "ElementCode",
            "ElementName",
            "ElementNumber",
            "BpmnTypeName",
        ];
        var publicIssueProperties = typeof(ModelValidationIssue).GetProperties()
            .Select(static property => property.Name);
        Assert.DoesNotContain(publicIssueProperties,
            name => forbiddenIssueProperties.Contains(name, StringComparer.Ordinal));
    }

    [Fact]
    public void MessageIsPresentationOnlyAndRoutingValidationRemainsNotationNeutral()
    {
        var first = new ModelValidationIssue(
            BpmnStructuralValidationRule.KnownRuleId,
            ModelValidationSeverity.Warning,
            BpmnModelValidationCodes.FlowNodeIsolated,
            "First readable wording",
            ModelValidationTarget.ForSemanticElement(new SemanticElementId("element:1")));
        var changed = new ModelValidationIssue(
            BpmnStructuralValidationRule.KnownRuleId,
            ModelValidationSeverity.Warning,
            BpmnModelValidationCodes.FlowNodeIsolated,
            "Changed readable wording",
            ModelValidationTarget.ForSemanticElement(new SemanticElementId("element:1")));
        Assert.Equal(first.Id, changed.Id);

        var routing = ReadProductionFile(
            "Inceptus.DocumentEngine.Runtime",
            "Validation",
            "RoutingModelValidationIssueProvider.cs");
        string[] forbidden =
        [
            "Bpmn",
            "ElementNumber",
            "BPMN.Code",
            "BPMN.Name",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, routing, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IssueMessagesUseNormalEncodingAndCanWrapLongCanonicalIds()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");
        var styles = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor.css");

        Assert.Contains("@issue.Message", component, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkupString", component, StringComparison.Ordinal);
        Assert.Contains(".issue-content > span", styles, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationSnapshotIsTransientAndAbsentFromAuthoritativeModelsAndHistory()
    {
        const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var contractsAssembly = typeof(ValidationSnapshot).Assembly;
        var runtimeAssembly = typeof(ModelValidationEngine).Assembly;
        var authoritativeTypes = contractsAssembly.GetTypes()
            .Where(static type => IsAuthoritativeNamespace(type.Namespace))
            .Concat(runtimeAssembly.GetTypes().Where(static type =>
                type.Namespace?.StartsWith(
                    "Inceptus.DocumentEngine.Runtime.History",
                    StringComparison.Ordinal) == true));

        Assert.DoesNotContain(
            authoritativeTypes.SelectMany(type => type.GetMembers(members)),
            member => member.Name.Contains("ValidationSnapshot", StringComparison.Ordinal));
    }

    private static bool IsAuthoritativeNamespace(string? namespaceName) =>
        namespaceName?.StartsWith(
            "Inceptus.DocumentEngine.Contracts.Documents",
            StringComparison.Ordinal) == true ||
        namespaceName?.StartsWith(
            "Inceptus.DocumentEngine.Contracts.Semantics",
            StringComparison.Ordinal) == true ||
        namespaceName?.StartsWith(
            "Inceptus.DocumentEngine.Contracts.Visuals",
            StringComparison.Ordinal) == true ||
        namespaceName?.StartsWith(
            "Inceptus.DocumentEngine.Contracts.Metadata",
            StringComparison.Ordinal) == true ||
        namespaceName?.StartsWith(
            "Inceptus.DocumentEngine.Contracts.History",
            StringComparison.Ordinal) == true;

    private static string ReadProductionDirectory(string project, string directory) =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src", project, directory),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

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
                if (File.Exists(Path.Combine(directory.FullName,
                        "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
