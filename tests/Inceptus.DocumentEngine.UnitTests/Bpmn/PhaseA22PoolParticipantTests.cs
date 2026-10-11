using Inceptus.DocumentEngine.Bpmn.Commands;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.Organizational.Commands;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Organizational.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Bpmn;

public sealed class PhaseA22PoolParticipantTests
{
    [Theory]
    [InlineData("sales", EffectiveParticipantKind.Participant, "company")]
    [InlineData("warehouse", EffectiveParticipantKind.Participant, "company")]
    [InlineData("supplier", EffectiveParticipantKind.StandalonePool, "supplier")]
    [InlineData("supplier2", EffectiveParticipantKind.StandalonePool, "supplier2")]
    [InlineData("sales-task", EffectiveParticipantKind.Participant, "company")]
    [InlineData("warehouse-task", EffectiveParticipantKind.Participant, "company")]
    [InlineData("supplier-task", EffectiveParticipantKind.StandalonePool, "supplier")]
    public void EffectiveParticipantUsesOnlyExplicitMembership(string endpoint, EffectiveParticipantKind kind, string owner)
    {
        var document = Snapshot();
        Assert.True(BpmnEffectiveParticipant.TryResolve(document, Id(endpoint), out var key, out var diagnostic));
        Assert.Null(diagnostic);
        Assert.Equal(new EffectiveParticipantKey(kind, Id(owner)), key);
        Assert.Empty(document.VisualModel.VisualStates);
        Assert.Single(document.SemanticModel.Elements, element => element.TypeId == BpmnSemanticTypes.Participant);
    }

    [Theory]
    [InlineData("sales-task", "warehouse-task", true)]
    [InlineData("sales-task", "supplier-task", false)]
    [InlineData("supplier-task", "supplier2-task", false)]
    [InlineData("supplier-task", "unpooled", true)]
    [InlineData("unpooled", "supplier-task", true)]
    [InlineData("sales-task", "unpooled", true)]
    [InlineData("unpooled", "sales-task", true)]
    [InlineData("unpooled", "unpooled2", true)]
    public void SequenceFlowUsesEffectiveParticipantAndRetainsUnpooledCompatibility(string source, string target, bool valid)
    {
        Assert.Equal(valid, BpmnEffectiveParticipant.ValidateSequenceEndpoints(Snapshot(), Id(source), Id(target)).IsEmpty);
        var document = Snapshot(BpmnSemanticFactory.CreateSequenceFlow(Id("flow"), Id(source), Id(target)));
        Assert.Equal(valid, new BpmnPoolFlowValidationRule().Validate(new ModelValidationContext(document)).IsEmpty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnassignedSequenceEndpointDoesNotMaskInvalidPoolParticipant(bool reverse)
    {
        var before = Snapshot();
        var model = before.SemanticModel;
        var invalid = new DocumentSnapshot(new SemanticModelSnapshot(model.DocumentId, model.Revision,
            model.Elements.Select(element => element.Id == Id("sales")
                ? new SemanticElementSnapshot(element.Id, element.TypeId,
                    [new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText("missing"))]) : element),
            model.Relationships, model.NestedScopes, model.ScopeMemberships, model.ModelProfiles, model.ProfileAssignments),
            before.VisualModel, before.Metadata);
        var diagnostics = BpmnEffectiveParticipant.ValidateSequenceEndpoints(invalid,
            Id(reverse ? "unpooled" : "sales-task"), Id(reverse ? "sales-task" : "unpooled"));
        Assert.Equal(BpmnEffectiveParticipant.AssignmentInvalid, Assert.Single(diagnostics).Code);
    }

    [Theory]
    [InlineData("scope", false, BpmnCommandDiagnosticCodes.SequenceFlowCrossesScope)]
    [InlineData("scope", true, BpmnCommandDiagnosticCodes.SequenceFlowCrossesScope)]
    [InlineData("start", false, BpmnCommandDiagnosticCodes.IncomingStartEvent)]
    [InlineData("end", true, BpmnCommandDiagnosticCodes.OutgoingEndEvent)]
    public void UnassignedSequenceEndpointRetainsScopeAndDirectionRules(string scenario, bool reverse, string code)
    {
        var before = Snapshot();
        var model = before.SemanticModel;
        var scope = new DocumentScopeId("another-process");
        var document = new DocumentSnapshot(new SemanticModelSnapshot(model.DocumentId, model.Revision,
            model.Elements.Select(element => element.Id == Id("unpooled") && scenario != "scope"
                ? new SemanticElementSnapshot(element.Id, scenario == "start" ? BpmnSemanticTypes.StartEvent : BpmnSemanticTypes.EndEvent)
                : element), model.Relationships,
            scenario == "scope" ? [new DocumentScopeSnapshot(scope)] : model.NestedScopes,
            scenario == "scope" ? [new SemanticElementScopeMembershipSnapshot(Id("unpooled"), scope)] : model.ScopeMemberships,
            model.ModelProfiles, model.ProfileAssignments), before.VisualModel, before.Metadata);
        var command = new CreateBpmnSequenceFlowCommand(document.DocumentId, document.Revision, Id("flow"), new("flow-visual"),
            Id(reverse ? "unpooled" : "sales-task"), Id(reverse ? "sales-task" : "unpooled"), new("source"), new("target"));
        Assert.Contains(BpmnPluginRegistration.N100.CommandValidators.Where(registration => registration.TypeId == command.TypeId)
            .SelectMany(registration => registration.Validator.Validate(command, document)), diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("sales-task", "warehouse-task", false)]
    [InlineData("sales-task", "supplier-task", true)]
    [InlineData("supplier-task", "supplier2-task", true)]
    [InlineData("supplier", "supplier2", true)]
    [InlineData("company", "supplier", true)]
    [InlineData("sales", "supplier", false)]
    [InlineData("unpooled", "supplier", false)]
    public void MessageFlowUsesDifferentEffectiveParticipantsAndRejectsDirectLane(string source, string target, bool valid)
    {
        Assert.Equal(valid, BpmnMessageFlowSemantics.ValidateEndpoints(Snapshot(), Id(source), Id(target)).IsEmpty);
    }

    [Fact]
    public async Task PoolAssignmentKeepsIndependentNamesAndCreatesNoParticipant()
    {
        var before = Snapshot();
        var handler = Inceptus.DocumentEngine.Bpmn.BpmnPluginRegistration.N100.CommandHandlers
            .Single(registration => registration.TypeId == SetBpmnPoolParticipantCommand.KnownTypeId).Handler;
        var assigned = await handler.HandleAsync(new SetBpmnPoolParticipantCommand(before.DocumentId, before.Revision,
            Id("supplier"), Id("company")), before, default);
        Assert.True(assigned.Succeeded);
        var after = assigned.ProposedDocument!;
        Assert.Equal("supplier", after.SemanticModel.Elements.Single(element => element.Id == Id("supplier"))
            .Properties[OrganizationalSemanticProperties.Name].TextValue);
        Assert.Equal(before.SemanticModel.Elements.Single(element => element.Id == Id("company")),
            after.SemanticModel.Elements.Single(element => element.Id == Id("company")));
        Assert.True(BpmnEffectiveParticipant.TryResolve(after, Id("supplier-task"), out var key, out _));
        Assert.Equal(new EffectiveParticipantKey(EffectiveParticipantKind.Participant, Id("company")), key);
        var unassigned = await handler.HandleAsync(new SetBpmnPoolParticipantCommand(after.DocumentId, after.Revision,
            Id("supplier"), null), after, default);
        Assert.True(unassigned.Succeeded);
        Assert.Equal(before, unassigned.ProposedDocument);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReassignmentRejectsIncidentFlowInvalidation(bool message)
    {
        var before = Snapshot(message
            ? BpmnSemanticFactory.CreateMessageFlow(Id("flow"), Id("sales-task"), Id("supplier-task"))
            : BpmnSemanticFactory.CreateSequenceFlow(Id("flow"), Id("sales-task"), Id("warehouse-task")));
        var validator = Inceptus.DocumentEngine.Bpmn.BpmnPluginRegistration.N100.CommandValidators
            .Single(registration => registration.TypeId == AssignOrganizationalElementCommand.KnownTypeId).Validator;
        var command = new AssignOrganizationalElementCommand(before.DocumentId, before.Revision,
            Id(message ? "supplier-task" : "warehouse-task"), Id(message ? "sales" : "supplier"));
        Assert.NotEmpty(validator.Validate(command, before));
        Assert.Equal(message ? Id("supplier") : Id("warehouse"),
            OrganizationalSemantics.GetOwningPoolId(before.SemanticModel, command.SemanticElementId));
    }

    [Fact]
    public void DeletingLaneAllowsSurvivingSequenceFlowToUnassignedElement()
    {
        var before = Snapshot(BpmnSemanticFactory.CreateSequenceFlow(Id("flow"), Id("sales-task"), Id("warehouse-task")));
        var validator = BpmnPluginRegistration.N100.CommandValidators.Single(registration =>
            registration.TypeId == DeleteOrganizationalPoolCommand.KnownTypeId).Validator;
        Assert.Empty(validator.Validate(new DeleteOrganizationalPoolCommand(before.DocumentId, before.Revision, Id("warehouse")), before));
    }

    [Fact]
    public void GenericPropertyCommandCannotBypassParticipantAssignmentValidation()
    {
        var before = Snapshot();
        var command = new Inceptus.DocumentEngine.Contracts.Commands.UpdateSemanticElementPropertyCommand(
            before.DocumentId, before.Revision, Id("sales"), OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText("missing"));
        Assert.Contains(BpmnPluginRegistration.N100.CommandValidators.Where(registration => registration.TypeId == command.TypeId)
            .SelectMany(registration => registration.Validator.Validate(command, before)),
            diagnostic => diagnostic.Code == BpmnEffectiveParticipant.AssignmentInvalid);
    }

    [Theory]
    [InlineData("supplier")]
    [InlineData("company")]
    public void DirectMessageEndpointPreventsRemovingRequiredPresentation(string source)
    {
        var before = Snapshot(BpmnSemanticFactory.CreateMessageFlow(Id("flow"), Id(source), Id("supplier2")));
        var command = new Inceptus.DocumentEngine.Contracts.Commands.SetModelProfileAvailabilityCommand(before.DocumentId, before.Revision,
            [new(OrganizationalModelProfile.Id, false)]);
        Assert.Contains(BpmnPluginRegistration.N100.CommandValidators.Where(registration => registration.TypeId == command.TypeId)
            .SelectMany(registration => registration.Validator.Validate(command, before)),
            diagnostic => diagnostic.Code == BpmnMessageFlowSemantics.PresentationUnavailable);
    }

    private static SemanticElementId Id(string name) => new(name);
    internal static DocumentSnapshot Snapshot(params SemanticRelationshipSnapshot[] flows)
    {
        var id = new DocumentId("pool-participant");
        var names = new[] { "sales", "warehouse", "supplier", "supplier2" };
        var pools = names.Select(name => new SemanticElementSnapshot(Id(name), OrganizationalSemanticTypes.Pool,
            new[] { new KeyValuePair<string, PropertyValue>(OrganizationalSemanticProperties.Name, PropertyValue.FromText(name)) }
                .Concat(name is "sales" or "warehouse"
                    ? [new(OrganizationalSemanticProperties.ParticipantId, PropertyValue.FromText("company"))] : [])));
        var tasks = names.Select(name => BpmnSemanticFactory.CreateTask(Id(name + "-task"), name, name, 1))
            .Append(BpmnSemanticFactory.CreateTask(Id("unpooled"), "U1", "Unpooled", 2))
            .Append(BpmnSemanticFactory.CreateTask(Id("unpooled2"), "U2", "Unpooled 2", 3));
        var participant = new SemanticElementSnapshot(Id("company"), BpmnSemanticTypes.Participant,
            [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Company A"))], containmentKind: SemanticElementContainmentKind.Document);
        return new(new SemanticModelSnapshot(id, DocumentRevision.Zero, pools.Concat(tasks).Append(participant), flows,
            modelProfiles: new ModelProfileStateSnapshot([OrganizationalModelProfile.Id]),
            profileAssignments: names.Select(name => new ModelProfileElementAssignmentSnapshot(OrganizationalModelProfile.Id, Id(name + "-task"), Id(name)))),
            new VisualModelSnapshot(id, DocumentRevision.Zero), new DocumentMetadataSnapshot(id, DocumentRevision.Zero));
    }
}
