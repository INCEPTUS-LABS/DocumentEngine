using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Toolbox;

namespace Inceptus.DocumentEngine.Blazor.Demo;

/// <summary>
/// Application-only neutral Toolbox contribution. Its items deliberately have no creation
/// behavior until a later phase supplies the corresponding command and placement integration.
/// </summary>
internal static class NeutralDemoToolbox
{
    private static readonly ToolboxGroupId GenericGroupId = new("demo:toolbox:generic");

    internal static ToolboxCatalog Catalog { get; } = new(
    [
        new ToolboxContribution(
            groups:
            [
                new ToolboxGroupDefinition(GenericGroupId, "Generic", 0),
            ],
            items:
            [
                new ToolboxItemDefinition(
                    new ToolboxItemId("demo:toolbox:element"),
                    NeutralDemoPipeline.NeutralNodeTypeId,
                    GenericGroupId,
                    "Element",
                    0,
                    new ToolboxIconDescriptor("generic:element", "□")),
                new ToolboxItemDefinition(
                    new ToolboxItemId("demo:toolbox:element-variant"),
                    NeutralDemoPipeline.NeutralNodeTypeId,
                    GenericGroupId,
                    "Element variant",
                    1,
                    new ToolboxIconDescriptor("generic:element-variant", "○")),
                new ToolboxItemDefinition(
                    new ToolboxItemId("demo:toolbox:policy-element"),
                    NeutralDemoPipeline.NeutralMixedPolicyNodeTypeId,
                    GenericGroupId,
                    "Policy element",
                    2,
                    new ToolboxIconDescriptor("generic:policy-element", "◇")),
            ]),
    ]);
}
