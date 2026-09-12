namespace Inceptus.DocumentEngine.UnitTests.Architecture;

/// <summary>
/// Identifies the exact production project files whose package metadata is approved by Phase P1.1.
/// Package identities, packability, dependencies, and the source reference graph remain asserted
/// independently by <see cref="SolutionArchitectureTests"/>.
/// </summary>
internal static class ApprovedP11PackageFoundationChanges
{
    internal static bool IsApprovedProjectPath(string path) => path is
        "src/Inceptus.DocumentEngine.Blazor/Inceptus.DocumentEngine.Blazor.csproj" or
        "src/Inceptus.DocumentEngine.Bpmn/Inceptus.DocumentEngine.Bpmn.csproj" or
        "src/Inceptus.DocumentEngine.Canvas2D/Inceptus.DocumentEngine.Canvas2D.csproj" or
        "src/Inceptus.DocumentEngine.Contracts/Inceptus.DocumentEngine.Contracts.csproj" or
        "src/Inceptus.DocumentEngine.Organizational/Inceptus.DocumentEngine.Organizational.csproj" or
        "src/Inceptus.DocumentEngine.Runtime/Inceptus.DocumentEngine.Runtime.csproj";
}
