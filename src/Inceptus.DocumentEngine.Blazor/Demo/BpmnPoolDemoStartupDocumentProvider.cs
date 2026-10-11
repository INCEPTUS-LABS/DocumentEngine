using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.Blazor.Demo;

/// <summary>The normal reference diagram: two Lanes of Company A and independent Suppliers.</summary>
internal sealed class BpmnPoolDemoStartupDocumentProvider : IBpmnModelerStartupDocumentProvider
{
    internal static BpmnPoolDemoStartupDocumentProvider Instance { get; } = new();

    public ValueTask<Document> GetInitialDocumentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var documentId = new DocumentId("demo:pools");
        var revision = DocumentRevision.Zero;
        var pools = new[] { Pool("sales", "Sales", true), Pool("warehouse", "Warehouse", true),
            Pool("supplier", "Supplier", false), Pool("supplier2", "Supplier B", false) };
        var nodes = new[]
        {
            BpmnSemanticFactory.CreateTask(Id("prepare"), "ORDER", "Prepare order", 1),
            BpmnSemanticFactory.CreateTask(Id("send"), BpmnSemanticTypes.SendTask, "SEND", "Send order", 2),
            BpmnSemanticFactory.CreateMessageThrowEvent(Id("throw"), "Send message"),
            BpmnSemanticFactory.CreateTask(Id("pack"), "PACK", "Pack order", 3),
            BpmnSemanticFactory.CreateTask(Id("receive"), BpmnSemanticTypes.ReceiveTask, "RECEIVE", "Receive order", 4),
            BpmnSemanticFactory.CreateMessageCatchEvent(Id("catch"), "Receive message"),
            BpmnSemanticFactory.CreateTask(Id("review"), "REVIEW", "Review order", 5),
        };
        var semantic = new SemanticModelSnapshot(documentId, revision, pools.Concat(nodes).Prepend(
            new SemanticElementSnapshot(Id("company-a"), BpmnSemanticTypes.Participant,
                [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Company A"))],
                containmentKind: SemanticElementContainmentKind.Document)),
            modelProfiles: new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]),
            profileAssignments: new[] { ("prepare", "sales"), ("send", "sales"), ("throw", "sales"),
                ("pack", "warehouse"), ("receive", "supplier"), ("catch", "supplier"), ("review", "supplier2") }
                .Select(pair => new ModelProfileElementAssignmentSnapshot(OrganizationalModelProfile.Id, Id(pair.Item1), Id(pair.Item2))));
        var visual = new VisualModelSnapshot(documentId, revision,
            [Node("prepare", 40, 25), Node("send", 270, 25), Node("throw", 480, 45, true), Node("pack", 40, 25),
                Node("receive", 40, 25), Node("catch", 280, 45, true), Node("review", 40, 25)],
            pools.Select((pool, index) => new ModelProfileElementPresentationSnapshot(OrganizationalModelProfile.Id, pool.Id, index)));
        var result = DocumentFactory.Create(new DocumentSnapshot(semantic, visual, new DocumentMetadataSnapshot(documentId, revision)),
            new ElementConnectorAnchorPolicyRegistry(BpmnPluginRegistration.N100.ConnectorAnchorPolicies));
        return ValueTask.FromResult(result.Document ?? throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(item => item.Message))));
    }

    private static SemanticElementSnapshot Pool(string id, string name, bool assigned) => new(Id(id), OrganizationalSemanticTypes.Pool,
        new[] { new KeyValuePair<string, PropertyValue>(OrganizationalSemanticProperties.Name, PropertyValue.FromText(name)) }
            .Concat(assigned ? [new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText(Id("company-a").Value))] : []));
    private static VisualStateSnapshot Node(string id, double x, double y, bool isEvent = false) =>
        new(new("demo:pools:visual:" + id), Id(id), new(x, y), isEvent ? new(40, 40) : new(140, 80), VisualPlacementMode.Pinned);
    private static SemanticElementId Id(string id) => new("demo:pools:" + id);
}
