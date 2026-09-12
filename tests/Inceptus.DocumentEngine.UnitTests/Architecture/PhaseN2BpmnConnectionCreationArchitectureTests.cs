using Inceptus.DocumentEngine.Bpmn;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN2BpmnConnectionCreationArchitectureTests
{
    [Fact]
    public void HistoricalBpmnRegistrationsRemainConnectionCreationEmpty()
    {
        BpmnPluginRegistration[] historical =
        [
            BpmnPluginRegistration.M1,
            BpmnPluginRegistration.M2,
            BpmnPluginRegistration.M3,
            BpmnPluginRegistration.M31,
            BpmnPluginRegistration.M32,
            BpmnPluginRegistration.M321,
            BpmnPluginRegistration.M322,
            BpmnPluginRegistration.M323,
            BpmnPluginRegistration.M33,
            BpmnPluginRegistration.M34,
            BpmnPluginRegistration.N1,
        ];

        Assert.All(historical, static registration =>
            Assert.Empty(registration.AnchorConnectionCreationRegistrations));
        Assert.Single(BpmnPluginRegistration.N2.AnchorConnectionCreationRegistrations);
    }

    [Fact]
    public void BpmnPluginOwnsOnlyPureExistingSequenceFlowCommandConstruction()
    {
        var source = ReadProductionFiles(
            "Inceptus.DocumentEngine.Bpmn",
            "ConnectionCreation",
            "*.cs");

        Assert.Contains("new CreateBpmnSequenceFlowCommand(", source,
            StringComparison.Ordinal);
        Assert.Contains("BpmnSequenceFlowCreationValidation.Validate(", source,
            StringComparison.Ordinal);
        Assert.Contains("ConnectorAnchorOccupancy.IsOccupied(", source,
            StringComparison.Ordinal);
        string[] forbidden =
        [
            "AddConnectorAnchorCommand",
            "CommandProcessor",
            "EditingSession",
            "ExecuteAsync(",
            "HistoryManager",
            "ProjectionEngine",
            "LayoutEngine",
            "RoutingEngine",
            "BpmnRoutingAlgorithm",
            "Canvas2DSceneBuilder",
            "VisualModelSnapshot(",
            "SemanticModelSnapshot(",
            "new ConnectorAnchor(",
            "FindNearest",
            "NearestFree",
            "Reconnect",
            "Condition",
            "DefaultFlow",
            "Inceptus.Notation",
            "Simulation",
        ];
        Assert.All(forbidden, token =>
            Assert.DoesNotContain(token, source, StringComparison.Ordinal));
    }

    [Fact]
    public void SequenceFlowValidationUsesTheGenericOccupancyAuthorityAndDiagnostic()
    {
        var source = ReadProductionFiles(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnCreationValidation.cs");

        Assert.Contains(
            "ConnectorAnchorOccupancy.IsOccupied(document.VisualModel, anchorId)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "CommandExecutionDiagnosticCodes.ConnectorAnchorInUse",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnAnchorAlready", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nearest free", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new ConnectorAnchor(", source, StringComparison.Ordinal);
    }

    private static string ReadProductionFiles(
        string project,
        string directory,
        string pattern)
    {
        var path = Path.Combine(RepositoryRoot, "src", project, directory);
        return string.Concat(Directory
            .GetFiles(path, pattern, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the repository root.");
        }
    }
}
