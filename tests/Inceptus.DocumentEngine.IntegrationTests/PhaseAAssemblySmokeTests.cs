using System.Reflection;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseAAssemblySmokeTests
{
    [Fact]
    public void AllProductionAssembliesCanBeResolvedTogether()
    {
        string[] assemblyNames =
        [
            "Inceptus.DocumentEngine.Contracts",
            "Inceptus.DocumentEngine.Runtime",
            "Inceptus.DocumentEngine.Canvas2D",
            "Inceptus.DocumentEngine.Bpmn",
            "Inceptus.DocumentEngine.Blazor",
        ];

        foreach (var assemblyName in assemblyNames)
        {
            var assembly = Assembly.Load(assemblyName);

            Assert.Equal(assemblyName, assembly.GetName().Name);
        }
    }
}

