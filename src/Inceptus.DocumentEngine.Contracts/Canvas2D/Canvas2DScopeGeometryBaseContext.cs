using Inceptus.DocumentEngine.Contracts.Layout;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Text;

namespace Inceptus.DocumentEngine.Contracts.Canvas2D;

/// <summary>Immutable inputs for notation-owned node appearance and pre-route text requests.</summary>
public sealed class Canvas2DScopeGeometryBaseContext
{
    public Canvas2DScopeGeometryBaseContext(
        ScopeGeometryInputs inputs,
        ProjectedGraph projectedGraph,
        LayoutResult localLayout,
        Canvas2DSceneConfiguration configuration,
        Canvas2DSceneContributorDescriptor contributor,
        TextMeasurementRequest textConfiguration)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(projectedGraph);
        ArgumentNullException.ThrowIfNull(localLayout);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(contributor);
        ArgumentNullException.ThrowIfNull(textConfiguration);
        Inputs = inputs;
        ProjectedGraph = projectedGraph;
        LocalLayout = localLayout;
        Configuration = configuration;
        Contributor = contributor;
        TextConfiguration = textConfiguration;
    }

    public ScopeGeometryInputs Inputs { get; }
    public ProjectedGraph ProjectedGraph { get; }
    public LayoutResult LocalLayout { get; }
    public Canvas2DSceneConfiguration Configuration { get; }
    public Canvas2DSceneContributorDescriptor Contributor { get; }
    public TextMeasurementRequest TextConfiguration { get; }
}
