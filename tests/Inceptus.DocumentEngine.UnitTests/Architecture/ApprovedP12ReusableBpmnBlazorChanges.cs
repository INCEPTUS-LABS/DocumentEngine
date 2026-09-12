namespace Inceptus.DocumentEngine.UnitTests.Architecture;

/// <summary>
/// Identifies the narrowly bounded project-configuration changes approved by Phase P1.2.
/// The resulting dependency graph, package identity, and packability remain asserted
/// independently by <see cref="SolutionArchitectureTests"/>.
/// </summary>
internal static class ApprovedP12ReusableBpmnBlazorChanges
{
    internal static bool IsApprovedProjectPath(string path) => path is
        "src/Inceptus.DocumentEngine.Blazor/Inceptus.DocumentEngine.Blazor.csproj" or
        "src/Inceptus.DocumentEngine.Bpmn.Blazor/Inceptus.DocumentEngine.Bpmn.Blazor.csproj" or
        "tests/Inceptus.DocumentEngine.IntegrationTests/Inceptus.DocumentEngine.IntegrationTests.csproj" or
        "tests/Inceptus.DocumentEngine.UnitTests/Inceptus.DocumentEngine.UnitTests.csproj";
}
