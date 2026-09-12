using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class CommandContractArchitectureTests
{
    [Fact]
    public void CommandInterfaceContainsOnlyImmutableDescriptionProperties()
    {
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(ICommand.TypeId)] = typeof(CommandTypeId),
            [nameof(ICommand.TargetDocumentId)] = typeof(DocumentId),
            [nameof(ICommand.ExpectedRevision)] = typeof(DocumentRevision),
            [nameof(ICommand.Category)] = typeof(CommandCategory),
            [nameof(ICommand.AffectedComponents)] = typeof(AuthoritativeDocumentComponent),
        };
        var actualProperties = typeof(ICommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(typeof(ICommand).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(
            typeof(ICommand).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => !method.IsSpecialName);
        Assert.Empty(typeof(ICommand).GetEvents());
    }

    [Fact]
    public void MoveCommandHasTheExactApprovedDataSurface()
    {
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(MoveVisualStateCommand.TypeId)] = typeof(CommandTypeId),
            [nameof(MoveVisualStateCommand.TargetDocumentId)] = typeof(DocumentId),
            [nameof(MoveVisualStateCommand.ExpectedRevision)] = typeof(DocumentRevision),
            [nameof(MoveVisualStateCommand.Category)] = typeof(CommandCategory),
            [nameof(MoveVisualStateCommand.AffectedComponents)] = typeof(AuthoritativeDocumentComponent),
            [nameof(MoveVisualStateCommand.TargetVisualStateId)] = typeof(VisualStateId),
            [nameof(MoveVisualStateCommand.TargetPosition)] = typeof(PointD),
            [nameof(MoveVisualStateCommand.RequestedPlacementMode)] = typeof(VisualPlacementMode?),
        };
        var actualProperties = typeof(MoveVisualStateCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        var knownTypeId = typeof(MoveVisualStateCommand)
            .GetProperty(nameof(MoveVisualStateCommand.KnownTypeId));
        var constructor = Assert.Single(typeof(MoveVisualStateCommand).GetConstructors());
        var parameters = constructor.GetParameters();

        Assert.True(typeof(MoveVisualStateCommand).IsSealed);
        Assert.Contains(typeof(ICommand), typeof(MoveVisualStateCommand).GetInterfaces());
        Assert.Equal(typeof(CommandTypeId), knownTypeId?.PropertyType);
        Assert.True(knownTypeId?.GetMethod?.IsStatic);
        Assert.Null(knownTypeId?.SetMethod);
        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(actualProperties.Keys, name =>
            Assert.Null(typeof(MoveVisualStateCommand).GetProperty(name)?.SetMethod));
        Assert.Equal(
            [
                typeof(DocumentId),
                typeof(DocumentRevision),
                typeof(VisualStateId),
                typeof(PointD),
                typeof(VisualPlacementMode?),
            ],
            parameters.Select(parameter => parameter.ParameterType));
        Assert.True(parameters[^1].IsOptional);
    }

    [Fact]
    public void MoveLabelCommandHasTheExactApprovedRouteRelativeDataSurface()
    {
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(MoveLabelCommand.TypeId)] = typeof(CommandTypeId),
            [nameof(MoveLabelCommand.TargetDocumentId)] = typeof(DocumentId),
            [nameof(MoveLabelCommand.ExpectedRevision)] = typeof(DocumentRevision),
            [nameof(MoveLabelCommand.Category)] = typeof(CommandCategory),
            [nameof(MoveLabelCommand.AffectedComponents)] =
                typeof(AuthoritativeDocumentComponent),
            [nameof(MoveLabelCommand.TargetVisualStateId)] = typeof(VisualStateId),
            [nameof(MoveLabelCommand.TargetPlacement)] = typeof(ConnectorLabelPlacement),
        };
        var actualProperties = typeof(MoveLabelCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(
                property => property.Name,
                property => property.PropertyType,
                StringComparer.Ordinal);
        var knownTypeId = typeof(MoveLabelCommand)
            .GetProperty(nameof(MoveLabelCommand.KnownTypeId));
        var constructor = Assert.Single(typeof(MoveLabelCommand).GetConstructors());

        Assert.True(typeof(MoveLabelCommand).IsSealed);
        Assert.Contains(typeof(ICommand), typeof(MoveLabelCommand).GetInterfaces());
        Assert.Equal(typeof(CommandTypeId), knownTypeId?.PropertyType);
        Assert.True(knownTypeId?.GetMethod?.IsStatic);
        Assert.Null(knownTypeId?.SetMethod);
        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(actualProperties.Keys, name =>
            Assert.Null(typeof(MoveLabelCommand).GetProperty(name)?.SetMethod));
        Assert.Equal(
            [
                typeof(DocumentId),
                typeof(DocumentRevision),
                typeof(VisualStateId),
                typeof(ConnectorLabelPlacement),
            ],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.All(constructor.GetParameters(), parameter => Assert.False(parameter.IsOptional));
    }

    [Fact]
    public void AtomicMoveCommandAndTargetHaveTheExactApprovedDataSurface()
    {
        var expectedCommandProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(MoveVisualStatesCommand.TypeId)] = typeof(CommandTypeId),
            [nameof(MoveVisualStatesCommand.TargetDocumentId)] = typeof(DocumentId),
            [nameof(MoveVisualStatesCommand.ExpectedRevision)] = typeof(DocumentRevision),
            [nameof(MoveVisualStatesCommand.Category)] = typeof(CommandCategory),
            [nameof(MoveVisualStatesCommand.AffectedComponents)] =
                typeof(AuthoritativeDocumentComponent),
            [nameof(MoveVisualStatesCommand.Moves)] = typeof(ImmutableArray<VisualStateMove>),
        };
        var expectedMoveProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(VisualStateMove.VisualStateId)] = typeof(VisualStateId),
            [nameof(VisualStateMove.TargetPosition)] = typeof(PointD),
            [nameof(VisualStateMove.RequestedPlacementMode)] = typeof(VisualPlacementMode?),
        };
        var commandProperties = typeof(MoveVisualStatesCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(
                property => property.Name,
                property => property.PropertyType,
                StringComparer.Ordinal);
        var moveProperties = typeof(VisualStateMove)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(
                property => property.Name,
                property => property.PropertyType,
                StringComparer.Ordinal);
        var knownTypeId = typeof(MoveVisualStatesCommand)
            .GetProperty(nameof(MoveVisualStatesCommand.KnownTypeId));
        var commandConstructor = Assert.Single(typeof(MoveVisualStatesCommand).GetConstructors());
        var moveConstructor = Assert.Single(typeof(VisualStateMove).GetConstructors());

        Assert.True(typeof(MoveVisualStatesCommand).IsSealed);
        Assert.True(typeof(VisualStateMove).IsSealed);
        Assert.Contains(typeof(ICommand), typeof(MoveVisualStatesCommand).GetInterfaces());
        Assert.Equal(typeof(CommandTypeId), knownTypeId?.PropertyType);
        Assert.True(knownTypeId?.GetMethod?.IsStatic);
        Assert.Null(knownTypeId?.SetMethod);
        Assert.Equal(
            expectedCommandProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            commandProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(
            expectedMoveProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            moveProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(commandProperties.Keys, name =>
            Assert.Null(typeof(MoveVisualStatesCommand).GetProperty(name)?.SetMethod));
        Assert.All(moveProperties.Keys, name =>
            Assert.Null(typeof(VisualStateMove).GetProperty(name)?.SetMethod));
        Assert.Equal(
            [typeof(DocumentId), typeof(DocumentRevision), typeof(IEnumerable<VisualStateMove>)],
            commandConstructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [typeof(VisualStateId), typeof(PointD), typeof(VisualPlacementMode?)],
            moveConstructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(moveConstructor.GetParameters()[^1].IsOptional);
    }

    [Fact]
    public void ResizeCommandHasTheExactApprovedDataSurface()
    {
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(ResizeVisualStateCommand.TypeId)] = typeof(CommandTypeId),
            [nameof(ResizeVisualStateCommand.TargetDocumentId)] = typeof(DocumentId),
            [nameof(ResizeVisualStateCommand.ExpectedRevision)] = typeof(DocumentRevision),
            [nameof(ResizeVisualStateCommand.Category)] = typeof(CommandCategory),
            [nameof(ResizeVisualStateCommand.AffectedComponents)] =
                typeof(AuthoritativeDocumentComponent),
            [nameof(ResizeVisualStateCommand.TargetVisualStateId)] = typeof(VisualStateId),
            [nameof(ResizeVisualStateCommand.TargetBounds)] = typeof(RectD),
            [nameof(ResizeVisualStateCommand.RequestedPlacementMode)] =
                typeof(VisualPlacementMode?),
        };
        var actualProperties = typeof(ResizeVisualStateCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(
                property => property.Name,
                property => property.PropertyType,
                StringComparer.Ordinal);
        var knownTypeId = typeof(ResizeVisualStateCommand)
            .GetProperty(nameof(ResizeVisualStateCommand.KnownTypeId));
        var constructor = Assert.Single(typeof(ResizeVisualStateCommand).GetConstructors());
        var parameters = constructor.GetParameters();

        Assert.True(typeof(ResizeVisualStateCommand).IsSealed);
        Assert.Contains(typeof(ICommand), typeof(ResizeVisualStateCommand).GetInterfaces());
        Assert.Equal(typeof(CommandTypeId), knownTypeId?.PropertyType);
        Assert.True(knownTypeId?.GetMethod?.IsStatic);
        Assert.Null(knownTypeId?.SetMethod);
        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(actualProperties.Keys, name =>
            Assert.Null(typeof(ResizeVisualStateCommand).GetProperty(name)?.SetMethod));
        Assert.Equal(
            [
                typeof(DocumentId),
                typeof(DocumentRevision),
                typeof(VisualStateId),
                typeof(RectD),
                typeof(VisualPlacementMode?),
            ],
            parameters.Select(parameter => parameter.ParameterType));
        Assert.True(parameters[^1].IsOptional);
    }

    [Fact]
    public void UpdateConnectionRouteCommandHasTheExactApprovedDataSurface()
    {
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(UpdateConnectionRouteCommand.TypeId)] = typeof(CommandTypeId),
            [nameof(UpdateConnectionRouteCommand.TargetDocumentId)] = typeof(DocumentId),
            [nameof(UpdateConnectionRouteCommand.ExpectedRevision)] = typeof(DocumentRevision),
            [nameof(UpdateConnectionRouteCommand.Category)] = typeof(CommandCategory),
            [nameof(UpdateConnectionRouteCommand.AffectedComponents)] =
                typeof(AuthoritativeDocumentComponent),
            [nameof(UpdateConnectionRouteCommand.TargetVisualStateId)] = typeof(VisualStateId),
            [nameof(UpdateConnectionRouteCommand.TargetRoute)] = typeof(ImmutableArray<PointD>),
        };
        var actualProperties = typeof(UpdateConnectionRouteCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(
                property => property.Name,
                property => property.PropertyType,
                StringComparer.Ordinal);
        var knownTypeId = typeof(UpdateConnectionRouteCommand)
            .GetProperty(nameof(UpdateConnectionRouteCommand.KnownTypeId));
        var constructor = Assert.Single(typeof(UpdateConnectionRouteCommand).GetConstructors());

        Assert.True(typeof(UpdateConnectionRouteCommand).IsSealed);
        Assert.Contains(typeof(ICommand), typeof(UpdateConnectionRouteCommand).GetInterfaces());
        Assert.Equal(typeof(CommandTypeId), knownTypeId?.PropertyType);
        Assert.True(knownTypeId?.GetMethod?.IsStatic);
        Assert.Null(knownTypeId?.SetMethod);
        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(actualProperties.Keys, name =>
            Assert.Null(typeof(UpdateConnectionRouteCommand).GetProperty(name)?.SetMethod));
        Assert.Equal(
            [
                typeof(DocumentId),
                typeof(DocumentRevision),
                typeof(VisualStateId),
                typeof(IEnumerable<PointD>),
            ],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.All(constructor.GetParameters(), parameter => Assert.False(parameter.IsOptional));
    }

    [Fact]
    public void CommandsOwnNoValidationExecutionUndoRedoTransactionOrEventOperation()
    {
        string[] forbiddenPrefixes =
        [
            "BeginTransaction",
            "Commit",
            "Execute",
            "Publish",
            "Redo",
            "Rollback",
            "Undo",
            "Validate",
        ];
        var commandTypes = typeof(ICommand).Assembly
            .GetExportedTypes()
            .Where(type => typeof(ICommand).IsAssignableFrom(type) && type != typeof(ICommand));

        foreach (var commandType in commandTypes)
        {
            var methodNames = commandType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance |
                    BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .ToArray();
            var events = commandType.GetEvents(
                BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly);

            Assert.DoesNotContain(methodNames, name =>
                forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
            Assert.Empty(events);
        }
    }

    [Fact]
    public void ValidatorAndRegistrationContractsHaveTheRestrictedShape()
    {
        var validate = Assert.Single(typeof(ICommandValidator).GetMethods());
        var registrationProperties = typeof(CommandValidatorRegistration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        var expectedRegistrationProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(CommandValidatorRegistration.TypeId)] = typeof(CommandTypeId),
            [nameof(CommandValidatorRegistration.ValidatorId)] = typeof(CommandValidatorId),
            [nameof(CommandValidatorRegistration.Validator)] = typeof(ICommandValidator),
        };

        Assert.Equal("Validate", validate.Name);
        Assert.Equal(typeof(ImmutableArray<Diagnostic>), validate.ReturnType);
        Assert.Equal(
            [typeof(ICommand), typeof(DocumentSnapshot)],
            validate.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            expectedRegistrationProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            registrationProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(registrationProperties.Keys, name =>
            Assert.Null(typeof(CommandValidatorRegistration).GetProperty(name)?.SetMethod));
        Assert.True(typeof(CommandValidatorRegistration).IsSealed);
    }

    [Fact]
    public void ValidationResultHasOnlyImmutableSnapshotBoundData()
    {
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(CommandValidationResult.DocumentId)] = typeof(DocumentId),
            [nameof(CommandValidationResult.Revision)] = typeof(DocumentRevision),
            [nameof(CommandValidationResult.CommandTypeId)] = typeof(CommandTypeId),
            [nameof(CommandValidationResult.Diagnostics)] = typeof(ImmutableArray<Diagnostic>),
            [nameof(CommandValidationResult.IsValid)] = typeof(bool),
        };
        var actualProperties = typeof(CommandValidationResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

        Assert.True(typeof(CommandValidationResult).IsSealed);
        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(actualProperties.Keys, name =>
            Assert.Null(typeof(CommandValidationResult).GetProperty(name)?.SetMethod));
    }

    [Fact]
    public void CategoryAndAffectedComponentEnumsAreClosedAndExplicit()
    {
        Assert.NotNull(typeof(AuthoritativeDocumentComponent).GetCustomAttribute<FlagsAttribute>());
        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["None"] = 0,
                ["SemanticModel"] = 1,
                ["VisualModel"] = 2,
                ["Metadata"] = 4,
                ["Publication"] = 8,
            }.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            Enum.GetValues<AuthoritativeDocumentComponent>()
                .ToDictionary(value => value.ToString(), value => (int)value, StringComparer.Ordinal)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(
            ["Semantic", "Visual", "Metadata", "Publication", "Document", "Compound"],
            Enum.GetNames<CommandCategory>());
    }

    [Fact]
    public void PhaseD1CommandsContainNoInstanceIdentityRestorationOrPublicTransactionType()
    {
        string[] forbiddenTypeFragments =
        [
            "CommandExecutionContext",
            "CommandId",
            "RestoreDocumentStateCommand",
            "Transaction",
        ];
        var exportedTypes = typeof(ICommand).Assembly.GetExportedTypes();

        Assert.DoesNotContain(exportedTypes, type =>
            forbiddenTypeFragments.Any(fragment =>
                type.Name.Contains(fragment, StringComparison.Ordinal)));
        Assert.DoesNotContain(
            exportedTypes.SelectMany(type => type.GetProperties()),
            property => property.Name == "CommandId");
    }

    [Fact]
    public void HandlerContractsReceiveOnlyImmutableCommandAndSnapshotData()
    {
        var handle = Assert.Single(typeof(ICommandHandler).GetMethods());
        var validateEnvelope = Assert.Single(typeof(ICommandEnvelopeValidator).GetMethods());
        var expectedRegistrationProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(CommandHandlerRegistration.TypeId)] = typeof(CommandTypeId),
            [nameof(CommandHandlerRegistration.EnvelopeValidator)] =
                typeof(ICommandEnvelopeValidator),
            [nameof(CommandHandlerRegistration.Handler)] = typeof(ICommandHandler),
        };
        var registrationProperties = typeof(CommandHandlerRegistration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

        Assert.Equal(nameof(ICommandHandler.HandleAsync), handle.Name);
        Assert.Equal(typeof(ValueTask<CommandHandlerResult>), handle.ReturnType);
        Assert.Equal(
            [typeof(ICommand), typeof(DocumentSnapshot), typeof(CancellationToken)],
            handle.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(nameof(ICommandEnvelopeValidator.Validate), validateEnvelope.Name);
        Assert.Equal(typeof(ImmutableArray<Diagnostic>), validateEnvelope.ReturnType);
        Assert.Equal(
            [typeof(ICommand)],
            validateEnvelope.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(
            expectedRegistrationProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            registrationProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(registrationProperties.Values, type => Assert.NotEqual(typeof(object), type));
        Assert.All(typeof(CommandHandlerRegistration).GetProperties(), property =>
            Assert.Null(property.SetMethod));
        var constructor = Assert.Single(typeof(CommandHandlerRegistration).GetConstructors());
        Assert.Equal(
            [typeof(CommandTypeId), typeof(ICommandEnvelopeValidator), typeof(ICommandHandler)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void CommittedEventCarriesAnExactImmutableSnapshotAndNoRuntimeDocument()
    {
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(DocumentChangedEvent.DocumentId)] = typeof(DocumentId),
            [nameof(DocumentChangedEvent.PreviousRevision)] = typeof(DocumentRevision),
            [nameof(DocumentChangedEvent.CommittedRevision)] = typeof(DocumentRevision),
            [nameof(DocumentChangedEvent.AffectedComponents)] = typeof(AuthoritativeDocumentComponent),
            [nameof(DocumentChangedEvent.CommandTypeId)] = typeof(CommandTypeId),
            [nameof(DocumentChangedEvent.CommittedSnapshot)] = typeof(DocumentSnapshot),
            [nameof(DocumentChangedEvent.PipelineInvalidation)] = typeof(PipelineInvalidation),
            [nameof(DocumentChangedEvent.NodeGeometryImpact)] =
                typeof(NodeGeometryPipelineImpact),
        };
        var actualProperties = typeof(DocumentChangedEvent)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);
        var subscribe = Assert.Single(typeof(IDocumentChangedSubscriber).GetMethods());

        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(actualProperties.Keys, propertyName =>
            Assert.Null(typeof(DocumentChangedEvent).GetProperty(propertyName)?.SetMethod));
        Assert.Equal(nameof(IDocumentChangedSubscriber.OnDocumentChangedAsync), subscribe.Name);
        Assert.Equal(typeof(ValueTask), subscribe.ReturnType);
        Assert.Equal(
            [typeof(DocumentChangedEvent)],
            subscribe.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(actualProperties.Values, type =>
            type.FullName == "Inceptus.DocumentEngine.Runtime.Documents.Document");
    }

    [Fact]
    public void ExecutionResultIsAnImmutableCommittedOrFailureDescription()
    {
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(CommandExecutionResult.DocumentId)] = typeof(DocumentId),
            [nameof(CommandExecutionResult.CommandTypeId)] = typeof(CommandTypeId),
            [nameof(CommandExecutionResult.Status)] = typeof(CommandExecutionStatus),
            [nameof(CommandExecutionResult.PreviousRevision)] = typeof(DocumentRevision),
            [nameof(CommandExecutionResult.CommittedRevision)] = typeof(DocumentRevision?),
            [nameof(CommandExecutionResult.AffectedComponents)] = typeof(AuthoritativeDocumentComponent),
            [nameof(CommandExecutionResult.Diagnostics)] = typeof(ImmutableArray<Diagnostic>),
            [nameof(CommandExecutionResult.CommittedEvent)] = typeof(DocumentChangedEvent),
            [nameof(CommandExecutionResult.CommittedSnapshot)] = typeof(DocumentSnapshot),
            [nameof(CommandExecutionResult.IsCommitted)] = typeof(bool),
        };
        var actualProperties = typeof(CommandExecutionResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

        Assert.True(typeof(CommandExecutionResult).IsSealed);
        Assert.Empty(typeof(CommandExecutionResult).GetConstructors());
        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(actualProperties.Keys, propertyName =>
            Assert.Null(typeof(CommandExecutionResult).GetProperty(propertyName)?.SetMethod));
        Assert.Equal(
            [
                "Committed",
                "EnvelopeValidationFailed",
                "DelegatedValidationFailed",
                "HandlerFailed",
                "ProposedStateValidationFailed",
                "Cancelled",
                "InternalFailure",
            ],
            Enum.GetNames<CommandExecutionStatus>());
    }

    [Fact]
    public void ConnectorAnchorCommandsHaveTheExactTypedVisualDataSurfaces()
    {
        var common = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(ICommand.TypeId)] = typeof(CommandTypeId),
            [nameof(ICommand.TargetDocumentId)] = typeof(DocumentId),
            [nameof(ICommand.ExpectedRevision)] = typeof(DocumentRevision),
            [nameof(ICommand.Category)] = typeof(CommandCategory),
            [nameof(ICommand.AffectedComponents)] = typeof(AuthoritativeDocumentComponent),
            [nameof(AddConnectorAnchorCommand.TargetVisualStateId)] = typeof(VisualStateId),
            [nameof(AddConnectorAnchorCommand.AnchorId)] = typeof(ConnectorAnchorId),
        };
        var expectedAdd = new Dictionary<string, Type>(common, StringComparer.Ordinal)
        {
            [nameof(AddConnectorAnchorCommand.Side)] = typeof(ConnectorAnchorSide),
            [nameof(AddConnectorAnchorCommand.Role)] = typeof(ConnectorAnchorRole),
            [nameof(AddConnectorAnchorCommand.InsertionIndex)] = typeof(int),
        };
        var expectedRemove = new Dictionary<string, Type>(common, StringComparer.Ordinal);

        AssertCommandSurface<AddConnectorAnchorCommand>(expectedAdd);
        AssertCommandSurface<RemoveConnectorAnchorCommand>(expectedRemove);
        Assert.Equal(
            [
                typeof(DocumentId),
                typeof(DocumentRevision),
                typeof(VisualStateId),
                typeof(ConnectorAnchorId),
                typeof(ConnectorAnchorSide),
                typeof(ConnectorAnchorRole),
                typeof(int),
            ],
            Assert.Single(typeof(AddConnectorAnchorCommand).GetConstructors())
                .GetParameters()
                .Select(parameter => parameter.ParameterType));
        Assert.Equal(
            [
                typeof(DocumentId),
                typeof(DocumentRevision),
                typeof(VisualStateId),
                typeof(ConnectorAnchorId),
            ],
            Assert.Single(typeof(RemoveConnectorAnchorCommand).GetConstructors())
                .GetParameters()
                .Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void CommandContractsExposeNoDelegatesServiceProvidersOrMutableCollections()
    {
        var commandContractTypes = typeof(ICommand).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == typeof(ICommand).Namespace)
            .ToArray();
        var signatureTypes = commandContractTypes
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance |
                    BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(property => ExpandType(property.PropertyType))
                .Concat(type
                    .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .SelectMany(constructor => constructor.GetParameters())
                    .SelectMany(parameter => ExpandType(parameter.ParameterType)))
                .Concat(type
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance |
                        BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(method => !method.IsSpecialName)
                    .SelectMany(method => method.GetParameters())
                    .SelectMany(parameter => ExpandType(parameter.ParameterType))))
            .ToArray();

        Assert.DoesNotContain(signatureTypes, type => typeof(Delegate).IsAssignableFrom(type));
        Assert.DoesNotContain(typeof(IServiceProvider), signatureTypes);
        Assert.DoesNotContain(signatureTypes, IsMutableCollectionContract);
    }

    private static bool IsMutableCollectionContract(Type type)
    {
        if (type.IsArray)
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        Type[] mutableDefinitions =
        [
            typeof(ICollection<>),
            typeof(IDictionary<,>),
            typeof(IList<>),
            typeof(Dictionary<,>),
            typeof(List<>),
        ];

        return mutableDefinitions.Contains(type.GetGenericTypeDefinition());
    }

    private static void AssertCommandSurface<TCommand>(
        IReadOnlyDictionary<string, Type> expected)
        where TCommand : ICommand
    {
        var type = typeof(TCommand);
        var actual = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

        Assert.True(type.IsSealed);
        Assert.Contains(typeof(ICommand), type.GetInterfaces());
        Assert.Equal(
            expected.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actual.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(actual.Keys, propertyName =>
            Assert.Null(type.GetProperty(propertyName)?.SetMethod));
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var expanded in ExpandType(argument))
            {
                yield return expanded;
            }
        }
    }
}
