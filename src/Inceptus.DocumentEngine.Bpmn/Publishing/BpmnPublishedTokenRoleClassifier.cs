using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Contracts.Publishing;

namespace Inceptus.DocumentEngine.Bpmn.Publishing;

/// <summary>
/// Maps the supported BPMN semantic family and unambiguous active-scope topology into the
/// intentionally small Publish animation vocabulary.
/// </summary>
public sealed class BpmnPublishedTokenRoleClassifier : IPublishedTokenRoleClassifier
{
    public PublishedTokenRoleClassification Classify(
        PublishedTokenRoleClassificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var typeId = request.SemanticTypeId;
        if (typeId == BpmnSemanticTypes.StartEvent)
        {
            return PublishedTokenRoleClassification.Success(PublishedTokenRole.Start);
        }

        if (typeId == BpmnSemanticTypes.EndEvent)
        {
            return PublishedTokenRoleClassification.Success(PublishedTokenRole.End);
        }

        if (BpmnActivitySemanticTypes.IsActivity(typeId))
        {
            return PublishedTokenRoleClassification.Success(PublishedTokenRole.ActivityDelay);
        }

        if (typeId == BpmnSemanticTypes.ParallelGateway)
        {
            if (request.IncomingConnectorCount < 1 || request.OutgoingConnectorCount < 1)
            {
                return PublishedTokenRoleClassification.Failure(
                    "PUBLISH_TOKEN_PARALLEL_GATEWAY_INVALID",
                    $"Parallel Gateway '{request.SemanticElementId}' requires at least one " +
                    "incoming and one outgoing connector for Publish.");
            }

            return PublishedTokenRoleClassification.Success(
                PublishedTokenRole.ParallelSynchronize);
        }

        if (BpmnSemanticTypes.IsGateway(typeId))
        {
            if (request.IncomingConnectorCount >= 2 && request.OutgoingConnectorCount == 1)
            {
                return PublishedTokenRoleClassification.Success(
                    PublishedTokenRole.MergeInvariant);
            }

            if (request.IncomingConnectorCount == 1 && request.OutgoingConnectorCount >= 2)
            {
                return PublishedTokenRoleClassification.Success(
                    PublishedTokenRole.SplitInvariant);
            }

            if (request.IncomingConnectorCount == 1 && request.OutgoingConnectorCount == 1)
            {
                return PublishedTokenRoleClassification.Success(PublishedTokenRole.PassThrough);
            }

            return PublishedTokenRoleClassification.Failure(
                "PUBLISH_TOKEN_GATEWAY_AMBIGUOUS",
                $"Gateway '{request.SemanticElementId}' does not have an unambiguous " +
                "Publish split, merge, or pass-through topology.");
        }

        if (BpmnSemanticTypes.IsEvent(typeId))
        {
            return PublishedTokenRoleClassification.Success(PublishedTokenRole.PassThrough);
        }

        return PublishedTokenRoleClassification.Failure(
            "PUBLISH_TOKEN_NODE_UNSUPPORTED",
            $"Element '{request.SemanticElementId}' is not supported by the Publish token runtime.");
    }
}
