namespace Inceptus.DocumentEngine.UnitTests.Architecture;

/// <summary>
/// Exact project files authorized by the N10.1 leaf-plugin gate. The production
/// reference graph remains asserted independently by SolutionArchitectureTests.
/// </summary>
internal static class ApprovedOrganizationalProjectChanges
{
    internal static bool IsApproved(string path) => path is
        "src/Inceptus.DocumentEngine.Blazor/Inceptus.DocumentEngine.Blazor.csproj" or
        "src/Inceptus.DocumentEngine.Organizational/Inceptus.DocumentEngine.Organizational.csproj" or
        "tests/Inceptus.DocumentEngine.UnitTests/Inceptus.DocumentEngine.UnitTests.csproj" or
        "tests/Inceptus.DocumentEngine.IntegrationTests/Inceptus.DocumentEngine.IntegrationTests.csproj";
}
