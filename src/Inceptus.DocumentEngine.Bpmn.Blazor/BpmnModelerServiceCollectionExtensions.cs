using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Composition;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the supported BPMN modeler composition for independent component instances.
/// </summary>
public static class BpmnModelerServiceCollectionExtensions
{
    /// <summary>
    /// Adds the BPMN and Organizational modeler services. Repeated registration is idempotent.
    /// </summary>
    /// <remarks>
    /// Documents, editing sessions, History, renderers, and browser resources are owned by
    /// each component and are never registered as shared model state.
    /// </remarks>
    public static IServiceCollection AddInceptusBpmnModeler(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddLocalization();
        services.TryAddTransient(static provider => new BpmnModelerCompositionFactory(
            () => provider.GetServices<IBpmnModelerStartupDocumentProvider>()));
        return services;
    }
}
