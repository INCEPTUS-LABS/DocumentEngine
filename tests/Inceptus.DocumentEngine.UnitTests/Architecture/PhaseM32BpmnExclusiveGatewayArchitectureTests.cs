using System.Reflection;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn.Layout;
using Inceptus.DocumentEngine.Bpmn.Routing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Properties;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseM32BpmnExclusiveGatewayArchitectureTests
{
    [Fact]
    public void GenericFrameworkAndPresentationContainNoExclusiveGatewayBranch()
    {
        var genericSource = string.Concat(
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Contracts"), "*.cs"),
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Runtime"), "*.cs"),
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Canvas2D"), "*.cs"),
            ReadSources(Path.Combine("src", "Inceptus.DocumentEngine.Canvas2D"), "*.js"),
            ReadSources(
                Path.Combine("src", "Inceptus.DocumentEngine.Bpmn.Blazor", "Components"),
                "*.razor"),
            ReadSources(
                Path.Combine("src", "Inceptus.DocumentEngine.Bpmn.Blazor", "Presentation"),
                "*.cs"),
            ReadSources(
                Path.Combine("src", "Inceptus.DocumentEngine.Bpmn.Blazor", "wwwroot"),
                "*.js"));

        Assert.DoesNotContain("BPMN.ExclusiveGateway", genericSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(CreateBpmnExclusiveGatewayCommand),
            genericSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void M32ReusesExistingAlgorithmsSceneContributionAndGenericPropertiesContract()
    {
        var m31 = BpmnPluginRegistration.M31;
        var m32 = BpmnPluginRegistration.M32;

        Assert.Equal(m31.LayoutAlgorithms.AsEnumerable(), m32.LayoutAlgorithms.AsEnumerable());
        Assert.Equal(m31.RoutingAlgorithms.AsEnumerable(), m32.RoutingAlgorithms.AsEnumerable());
        Assert.Equal(m31.SceneContributors.AsEnumerable(), m32.SceneContributors.AsEnumerable());
        Assert.IsType<BpmnLayoutAlgorithm>(Assert.Single(m32.LayoutAlgorithms).Algorithm);
        Assert.IsType<BpmnRoutingAlgorithm>(Assert.Single(m32.RoutingAlgorithms).Algorithm);

        var gatewaySchema = Assert.Single(
            m32.PropertiesSchemas,
            schema => schema.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);
        Assert.IsType<ElementPropertiesSchema>(gatewaySchema);
        Assert.Same(typeof(ElementPropertiesSchema).Assembly, gatewaySchema.GetType().Assembly);
        Assert.All(gatewaySchema.Fields, field => Assert.All(
            field.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance),
            static property => Assert.False(
                typeof(Delegate).IsAssignableFrom(property.PropertyType))));

        var bpmnTypes = typeof(BpmnPluginRegistration).Assembly.GetTypes();
        string[] forbiddenTypeNames =
        [
            "BpmnGatewayLayoutAlgorithm",
            "BpmnGatewayRoutingAlgorithm",
            "GatewayCommandProcessor",
            "GatewayHistory",
            "GatewayTransaction",
            "RenameExclusiveGatewayCommand",
            "SetGatewayDescriptionCommand",
            "SetGatewayCodeCommand",
        ];
        Assert.All(forbiddenTypeNames, name => Assert.DoesNotContain(
            bpmnTypes,
            type => StringComparer.Ordinal.Equals(type.Name, name)));
    }

    [Fact]
    public void GatewayKeepsCodeAndTechnicalIdentitySeparateWithoutElementNumber()
    {
        Assert.Null(typeof(CreateBpmnExclusiveGatewayCommand).GetProperty("ElementNumber"));
        Assert.NotEqual(BpmnSemanticProperties.Code, BpmnSemanticProperties.ElementNumber);

        var schema = Assert.Single(
            BpmnPluginRegistration.M32.PropertiesSchemas,
            candidate => candidate.SemanticTypeId == BpmnSemanticTypes.ExclusiveGateway);
        Assert.DoesNotContain(schema.Fields, field =>
            StringComparer.Ordinal.Equals(
                field.SemanticPropertyKey,
                BpmnSemanticProperties.ElementNumber));
    }

    [Fact]
    public void BpmnProductionRemainsSimulationAndTransformationNeutral()
    {
        var source = string.Concat(ReadSources(
            Path.Combine("src", "Inceptus.DocumentEngine.Bpmn"),
            "*.cs"));
        string[] forbidden =
        [
            "Probability",
            "BranchWeight",
            "SimulationDecision",
            "QueueCapacity",
            "DurationDistribution",
            "ResourceCapacity",
            "TransformationMapping",
            "SourceBpmnId",
            "OriginalGatewayId",
            "WasTransformed",
            "Inceptus.Decision",
        ];

        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ReadSources(string relativeDirectory, string pattern) =>
        Directory.Exists(Path.Combine(RepositoryRoot, relativeDirectory))
            ? Directory
                .GetFiles(
                    Path.Combine(RepositoryRoot, relativeDirectory),
                    pattern,
                    SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText)
            : [];

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
