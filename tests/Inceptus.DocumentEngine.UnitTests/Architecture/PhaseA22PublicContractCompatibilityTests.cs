using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.ConnectionCreation;
using Inceptus.DocumentEngine.Contracts.EndpointReconnection;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Projection;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Routing;
using Inceptus.DocumentEngine.Contracts.Toolbox;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Projection;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseA22PublicContractCompatibilityTests
{
    [Fact]
    public void ExistingToolboxAndConnectionRegistrationSignaturesRemainCallable()
    {
        Assert.NotNull(typeof(ToolboxContribution).GetConstructor(
            [typeof(IEnumerable<ToolboxGroupDefinition>), typeof(IEnumerable<ToolboxItemDefinition>), typeof(IEnumerable<ToolboxSectionDefinition>)]));
        Assert.NotNull(typeof(AnchorConnectionCreationRegistration).GetConstructor(
            [typeof(AnchorConnectionCreationId), typeof(IAnchorConnectionCreationCommandFactory), typeof(IAnchorConnectionTargetEligibility)]));
        Assert.NotNull(typeof(ConnectorEndpointReconnectionRegistration).GetConstructor(
            [typeof(ConnectorEndpointReconnectionId), typeof(IConnectorEndpointReconnectionCommandFactory)]));
        Assert.NotNull(typeof(ConnectorEndpointReconnectionStartRequest).GetConstructor(
            [typeof(DocumentSnapshot), typeof(DocumentRevision), typeof(SemanticElementId), typeof(VisualStateId),
                typeof(ConnectorEndpointKind), typeof(SemanticElementId), typeof(ConnectorAnchorId)]));
        Assert.NotNull(typeof(ConnectorEndpointReconnectionRequest).GetConstructor(
            [typeof(DocumentSnapshot), typeof(DocumentRevision), typeof(SemanticElementId), typeof(VisualStateId),
                typeof(ConnectorEndpointKind), typeof(SemanticElementId), typeof(ConnectorAnchorId),
                typeof(SemanticElementId), typeof(VisualStateId), typeof(ConnectorAnchorId)]));
        Assert.NotNull(typeof(AnchorConnectionCreationSourceRequest).GetConstructor(
            [typeof(DocumentSnapshot), typeof(DocumentRevision), typeof(SemanticElementId), typeof(VisualStateId), typeof(ConnectorAnchorId)]));
        Assert.NotNull(typeof(AnchorConnectionCreationCatalog).GetMethod(
            nameof(AnchorConnectionCreationCatalog.GetMatchingRegistrations), [typeof(AnchorConnectionCreationSourceRequest)]));
        Assert.Empty(new ToolboxContribution().ConnectionItems);
        Assert.Empty(new ToolboxCatalog().ConnectionItems);
    }

    [Fact]
    public void ExistingRoutingProjectionAndSceneSignaturesRemainCallableByCompiledConsumers()
    {
        Assert.NotNull(typeof(VisualModelSnapshot).GetConstructor(
        [
            typeof(DocumentId), typeof(DocumentRevision), typeof(IEnumerable<VisualStateSnapshot>),
            typeof(IEnumerable<ModelProfileElementPresentationSnapshot>),
        ]));
        Assert.NotNull(typeof(VisualModelSnapshot).GetConstructor(
        [
            typeof(DocumentId), typeof(DocumentRevision), typeof(IEnumerable<VisualStateSnapshot>),
            typeof(IEnumerable<ModelProfileElementPresentationSnapshot>), typeof(IEnumerable<ScopeRoutingSnapshot>),
        ]));
        Assert.NotNull(typeof(ConnectorRoutingStatePreparationResult).GetMethod(
            nameof(ConnectorRoutingStatePreparationResult.Success),
            [typeof(IEnumerable<ScopeRoutingSnapshot>), typeof(IEnumerable<Diagnostic>)]));
        Assert.NotNull(typeof(ProjectionEngine).GetConstructor([typeof(IEnumerable<ProjectionRuleRegistration>)]));
        Assert.NotNull(typeof(Canvas2DSceneItem).GetConstructor(
        [
            typeof(SceneObjectId), typeof(Canvas2DSceneLayer), typeof(int), typeof(Canvas2DSceneGeometry),
            typeof(Canvas2DSceneOriginTrace), typeof(Matrix2D?), typeof(RectD?), typeof(Canvas2DSceneStyle),
            typeof(bool), typeof(Canvas2DHitTestPolicy), typeof(IEnumerable<KeyValuePair<string, PropertyValue>>),
            typeof(IEnumerable<KeyValuePair<string, PropertyValue>>), typeof(RectD?), typeof(Canvas2DSpatialRegion),
            typeof(Canvas2DConnectorPresentationMapping),
        ]));

        // These original default calls must continue resolving without an overload ambiguity.
        Assert.NotNull(new ProjectionEngine());
        Assert.True(ConnectorRoutingStatePreparationResult.Success([]).Succeeded);
        Assert.Empty(new VisualModelSnapshot(new DocumentId("compatibility"), DocumentRevision.Zero).DocumentRoutingStates);
    }

    [Fact]
    public void ExistingProcessValidationAndPresentationConstructorsRetainExactSignatures()
    {
        Assert.NotNull(typeof(ModelValidationContext).GetConstructor([typeof(DocumentSnapshot)]));
        Assert.NotNull(typeof(ModelValidationContext).GetConstructor([typeof(DocumentSnapshot), typeof(DocumentScopeId)]));
        Assert.NotNull(typeof(ValidationSnapshot).GetConstructor(
            [typeof(DocumentId), typeof(DocumentRevision), typeof(IEnumerable<ModelValidationIssue>), typeof(long?)]));
        Assert.NotNull(typeof(ValidationSnapshot).GetConstructor(
            [typeof(DocumentId), typeof(DocumentScopeId), typeof(DocumentRevision), typeof(IEnumerable<ModelValidationIssue>), typeof(long?)]));
        Assert.NotNull(typeof(Canvas2DScenePresentationContext).GetConstructor(
        [
            typeof(DocumentSnapshot), typeof(DocumentScopeId), typeof(ModelProfileViewStateSnapshot), typeof(IEnumerable<Canvas2DSceneItem>),
        ]));
        Assert.NotNull(typeof(Canvas2DScenePresentationContext).GetConstructor(
        [
            typeof(DocumentSnapshot), typeof(DocumentScopeId), typeof(ModelProfileViewStateSnapshot),
            typeof(ModelProfileElementViewStateSnapshot), typeof(IEnumerable<Canvas2DSceneItem>),
        ]));
    }
}
