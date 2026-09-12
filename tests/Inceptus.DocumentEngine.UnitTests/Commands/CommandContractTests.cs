using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class CommandContractTests
{
    [Fact]
    public void MoveCommandRetainsItsImmutableDescriptionAndFixedScope()
    {
        var command = new MoveVisualStateCommand(
            new DocumentId("test:document"),
            new DocumentRevision(8),
            new VisualStateId("test:visual"),
            new PointD(25d, 40d),
            VisualPlacementMode.Pinned);
        Assert.Equal("inceptus:command/move-visual-state", MoveVisualStateCommand.KnownTypeId.Value);
        Assert.Equal(MoveVisualStateCommand.KnownTypeId, command.TypeId);
        Assert.Equal(new DocumentId("test:document"), command.TargetDocumentId);
        Assert.Equal(new DocumentRevision(8), command.ExpectedRevision);
        Assert.Equal(CommandCategory.Visual, command.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            command.AffectedComponents);
        Assert.Equal(new VisualStateId("test:visual"), command.TargetVisualStateId);
        Assert.Equal(new PointD(25d, 40d), command.TargetPosition);
        Assert.Equal(VisualPlacementMode.Pinned, command.RequestedPlacementMode);
    }

    [Fact]
    public void OmittedPlacementModeRequestsThatPersistentModeBePreserved()
    {
        var command = new MoveVisualStateCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            new PointD(1d, 2d));

        Assert.Null(command.RequestedPlacementMode);
    }

    [Fact]
    public void MoveCommandsUseDeepStructuralEquality()
    {
        var first = Command(VisualPlacementMode.Manual);
        var same = Command(VisualPlacementMode.Manual);
        var differentPosition = new MoveVisualStateCommand(
            new DocumentId("test:document"),
            new DocumentRevision(8),
            new VisualStateId("test:visual"),
            new PointD(99d, 40d),
            VisualPlacementMode.Manual);
        var differentMode = Command(VisualPlacementMode.Pinned);

        Assert.Equal(first, same);
        Assert.True(first == same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, differentPosition);
        Assert.NotEqual(first, differentMode);
        Assert.True(first != differentMode);
    }

    [Fact]
    public void MoveCommandRejectsInvalidIdentityAndPlacementInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new MoveVisualStateCommand(
            null!,
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            new PointD(0d, 0d)));
        Assert.Throws<ArgumentNullException>(() => new MoveVisualStateCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            null!,
            new PointD(0d, 0d)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoveVisualStateCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            new PointD(0d, 0d),
            (VisualPlacementMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoveVisualStateCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            new PointD(double.NaN, 0d)));
    }

    [Fact]
    public void AtomicMoveCommandCopiesOrdersAndRetainsItsImmutableDescription()
    {
        var mutable = new List<VisualStateMove>
        {
            new(
                new VisualStateId("test:zulu"),
                new PointD(50d, 60d),
                VisualPlacementMode.Pinned),
            new(
                new VisualStateId("test:alpha"),
                new PointD(10d, 20d)),
        };
        var command = new MoveVisualStatesCommand(
            new DocumentId("test:document"),
            new DocumentRevision(8),
            mutable);
        mutable.Clear();

        Assert.Equal(
            "inceptus:command/move-visual-states",
            MoveVisualStatesCommand.KnownTypeId.Value);
        Assert.Equal(MoveVisualStatesCommand.KnownTypeId, command.TypeId);
        Assert.Equal(new DocumentId("test:document"), command.TargetDocumentId);
        Assert.Equal(new DocumentRevision(8), command.ExpectedRevision);
        Assert.Equal(CommandCategory.Visual, command.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            command.AffectedComponents);
        Assert.Equal(
            ["test:alpha", "test:zulu"],
            command.Moves.Select(move => move.VisualStateId.Value));
        Assert.Null(command.Moves[0].RequestedPlacementMode);
        Assert.Equal(VisualPlacementMode.Pinned, command.Moves[1].RequestedPlacementMode);
    }

    [Fact]
    public void AtomicMoveCommandsUseOrderIndependentDeepStructuralEquality()
    {
        var first = new MoveVisualStatesCommand(
            new DocumentId("test:document"),
            new DocumentRevision(8),
            [
                new VisualStateMove(
                    new VisualStateId("test:beta"),
                    new PointD(30d, 40d),
                    VisualPlacementMode.Pinned),
                new VisualStateMove(
                    new VisualStateId("test:alpha"),
                    new PointD(10d, 20d),
                    VisualPlacementMode.Manual),
            ]);
        var sameDifferentInputOrder = new MoveVisualStatesCommand(
            first.TargetDocumentId,
            first.ExpectedRevision,
            first.Moves.Reverse());
        var different = new MoveVisualStatesCommand(
            first.TargetDocumentId,
            first.ExpectedRevision,
            [
                new VisualStateMove(
                    new VisualStateId("test:alpha"),
                    new PointD(10d, 21d),
                    VisualPlacementMode.Manual),
                first.Moves[1],
            ]);

        Assert.Equal(first, sameDifferentInputOrder);
        Assert.True(first == sameDifferentInputOrder);
        Assert.Equal(first.GetHashCode(), sameDifferentInputOrder.GetHashCode());
        Assert.NotEqual(first, different);
        Assert.True(first != different);
        Assert.Equal(
            new VisualStateMove(
                new VisualStateId("test:alpha"),
                new PointD(10d, 20d),
                VisualPlacementMode.Manual),
            first.Moves[0]);
    }

    [Fact]
    public void AtomicMoveCommandRejectsInvalidOrDuplicateTargets()
    {
        var documentId = new DocumentId("test:document");
        var visualId = new VisualStateId("test:visual");
        var move = new VisualStateMove(visualId, new PointD(10d, 20d));

        Assert.Throws<ArgumentNullException>(() => new MoveVisualStatesCommand(
            null!,
            DocumentRevision.Zero,
            [move]));
        Assert.Throws<ArgumentNullException>(() => new MoveVisualStatesCommand(
            documentId,
            DocumentRevision.Zero,
            null!));
        Assert.Throws<ArgumentException>(() => new MoveVisualStatesCommand(
            documentId,
            DocumentRevision.Zero,
            []));
        Assert.Throws<ArgumentException>(() => new MoveVisualStatesCommand(
            documentId,
            DocumentRevision.Zero,
            [move, null!]));
        Assert.Throws<ArgumentException>(() => new MoveVisualStatesCommand(
            documentId,
            DocumentRevision.Zero,
            [move, new VisualStateMove(visualId, new PointD(30d, 40d))]));
        Assert.Throws<ArgumentNullException>(() => new VisualStateMove(
            null!,
            new PointD(10d, 20d)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VisualStateMove(
            visualId,
            new PointD(10d, 20d),
            (VisualPlacementMode)99));
    }

    [Fact]
    public void ResizeCommandRetainsItsImmutableDescriptionAndFixedScope()
    {
        var command = new ResizeVisualStateCommand(
            new DocumentId("test:document"),
            new DocumentRevision(8),
            new VisualStateId("test:visual"),
            new RectD(25d, 40d, 80d, 60d),
            VisualPlacementMode.Pinned);

        Assert.Equal(
            "inceptus:command/resize-visual-state",
            ResizeVisualStateCommand.KnownTypeId.Value);
        Assert.Equal(ResizeVisualStateCommand.KnownTypeId, command.TypeId);
        Assert.Equal(new DocumentId("test:document"), command.TargetDocumentId);
        Assert.Equal(new DocumentRevision(8), command.ExpectedRevision);
        Assert.Equal(CommandCategory.Visual, command.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            command.AffectedComponents);
        Assert.Equal(new VisualStateId("test:visual"), command.TargetVisualStateId);
        Assert.Equal(new RectD(25d, 40d, 80d, 60d), command.TargetBounds);
        Assert.Equal(VisualPlacementMode.Pinned, command.RequestedPlacementMode);
    }

    [Fact]
    public void ResizeCommandPreservesOptionalIntentAndUsesStructuralEquality()
    {
        var first = ResizeCommand(VisualPlacementMode.Manual);
        var same = ResizeCommand(VisualPlacementMode.Manual);
        var preserved = new ResizeVisualStateCommand(
            first.TargetDocumentId,
            first.ExpectedRevision,
            first.TargetVisualStateId,
            first.TargetBounds);
        var differentBounds = new ResizeVisualStateCommand(
            first.TargetDocumentId,
            first.ExpectedRevision,
            first.TargetVisualStateId,
            new RectD(25d, 40d, 81d, 60d),
            VisualPlacementMode.Manual);
        var differentMode = ResizeCommand(VisualPlacementMode.Pinned);

        Assert.Equal(first, same);
        Assert.True(first == same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.Null(preserved.RequestedPlacementMode);
        Assert.NotEqual(first, differentBounds);
        Assert.NotEqual(first, differentMode);
        Assert.True(first != differentMode);
    }

    [Fact]
    public void ResizeCommandUsesRectDGeometryRulesWithoutInventingAMinimumSize()
    {
        var zeroExtent = new ResizeVisualStateCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            new RectD(10d, 20d, 0d, 0d));

        Assert.Equal(new RectD(10d, 20d, 0d, 0d), zeroExtent.TargetBounds);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResizeVisualStateCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            new RectD(0d, 0d, -1d, 10d)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResizeVisualStateCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            new RectD(double.NaN, 0d, 10d, 10d)));
    }

    [Fact]
    public void ResizeCommandRejectsInvalidIdentityAndPlacementInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new ResizeVisualStateCommand(
            null!,
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            new RectD(0d, 0d, 10d, 10d)));
        Assert.Throws<ArgumentNullException>(() => new ResizeVisualStateCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            null!,
            new RectD(0d, 0d, 10d, 10d)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResizeVisualStateCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            new RectD(0d, 0d, 10d, 10d),
            (VisualPlacementMode)99));
    }

    [Fact]
    public void UpdateConnectionRouteCommandRetainsItsImmutableDescriptionAndFixedScope()
    {
        var route = new[]
        {
            new PointD(10d, 20d),
            new PointD(30d, 45d),
            new PointD(80d, 90d),
        };
        var command = new UpdateConnectionRouteCommand(
            new DocumentId("test:document"),
            new DocumentRevision(8),
            new VisualStateId("test:visual"),
            route);

        Assert.Equal(
            "inceptus:command/update-connection-route",
            UpdateConnectionRouteCommand.KnownTypeId.Value);
        Assert.Equal(UpdateConnectionRouteCommand.KnownTypeId, command.TypeId);
        Assert.Equal(new DocumentId("test:document"), command.TargetDocumentId);
        Assert.Equal(new DocumentRevision(8), command.ExpectedRevision);
        Assert.Equal(CommandCategory.Visual, command.Category);
        Assert.Equal(
            AuthoritativeDocumentComponent.VisualModel,
            command.AffectedComponents);
        Assert.Equal(new VisualStateId("test:visual"), command.TargetVisualStateId);
        Assert.Equal(route, command.TargetRoute.AsEnumerable());
    }

    [Fact]
    public void UpdateConnectionRouteCommandDefensivelyCopiesAndUsesDeepEquality()
    {
        var mutableRoute = new List<PointD>
        {
            new(10d, 20d),
            new(30d, 45d),
            new(80d, 90d),
        };
        var first = UpdateRouteCommand(mutableRoute);
        var same = UpdateRouteCommand([.. mutableRoute]);
        var different = UpdateRouteCommand(
            [new PointD(10d, 20d), new PointD(35d, 45d), new PointD(80d, 90d)]);

        mutableRoute[1] = new PointD(999d, 999d);
        mutableRoute.Add(new PointD(1000d, 1000d));

        Assert.Equal(
            new[]
            {
                new PointD(10d, 20d),
                new PointD(30d, 45d),
                new PointD(80d, 90d),
            },
            first.TargetRoute.AsEnumerable());
        Assert.Equal(first, same);
        Assert.True(first == same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, different);
        Assert.True(first != different);
    }

    [Fact]
    public void UpdateConnectionRouteCommandRequiresIdentityAndAnEmptyOrCompleteFiniteRoute()
    {
        Assert.Throws<ArgumentNullException>(() => new UpdateConnectionRouteCommand(
            null!,
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            [new PointD(0d, 0d), new PointD(10d, 10d)]));
        Assert.Throws<ArgumentNullException>(() => new UpdateConnectionRouteCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            null!,
            [new PointD(0d, 0d), new PointD(10d, 10d)]));
        Assert.Throws<ArgumentNullException>(() => new UpdateConnectionRouteCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            null!));
        var clear = new UpdateConnectionRouteCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            []);
        Assert.Empty(clear.TargetRoute);
        Assert.Throws<ArgumentException>(() => new UpdateConnectionRouteCommand(
            new DocumentId("test:document"),
            DocumentRevision.Zero,
            new VisualStateId("test:visual"),
            [new PointD(0d, 0d)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointD(double.NaN, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointD(0d, double.PositiveInfinity));
    }

    [Fact]
    public void RegistrationRetainsStableTypeValidatorAndImplementation()
    {
        var validator = new TestValidator();
        var registration = new CommandValidatorRegistration(
            new CommandTypeId("test:command"),
            new CommandValidatorId("test:validator"),
            validator);

        Assert.Equal(new CommandTypeId("test:command"), registration.TypeId);
        Assert.Equal(new CommandValidatorId("test:validator"), registration.ValidatorId);
        Assert.Same(validator, registration.Validator);
    }

    [Fact]
    public void RegistrationRequiresAllValues()
    {
        var typeId = new CommandTypeId("test:command");
        var validatorId = new CommandValidatorId("test:validator");
        var validator = new TestValidator();

        Assert.Throws<ArgumentNullException>(() =>
            new CommandValidatorRegistration(null!, validatorId, validator));
        Assert.Throws<ArgumentNullException>(() =>
            new CommandValidatorRegistration(typeId, null!, validator));
        Assert.Throws<ArgumentNullException>(() =>
            new CommandValidatorRegistration(typeId, validatorId, null!));
    }

    [Fact]
    public void CommandCategoriesAndAffectedComponentsHaveTheApprovedValues()
    {
        Assert.Equal(
            ["Semantic", "Visual", "Metadata", "Publication", "Document", "Compound"],
            Enum.GetNames<CommandCategory>());
        Assert.Equal(0, (int)AuthoritativeDocumentComponent.None);
        Assert.Equal(1, (int)AuthoritativeDocumentComponent.SemanticModel);
        Assert.Equal(2, (int)AuthoritativeDocumentComponent.VisualModel);
        Assert.Equal(4, (int)AuthoritativeDocumentComponent.Metadata);
        Assert.Equal(8, (int)AuthoritativeDocumentComponent.Publication);
        Assert.Equal(
            AuthoritativeDocumentComponent.SemanticModel |
            AuthoritativeDocumentComponent.VisualModel |
            AuthoritativeDocumentComponent.Metadata |
            AuthoritativeDocumentComponent.Publication,
            (AuthoritativeDocumentComponent)15);
    }

    private static MoveVisualStateCommand Command(VisualPlacementMode placementMode) =>
        new(
            new DocumentId("test:document"),
            new DocumentRevision(8),
            new VisualStateId("test:visual"),
            new PointD(25d, 40d),
            placementMode);

    private static ResizeVisualStateCommand ResizeCommand(
        VisualPlacementMode placementMode) =>
        new(
            new DocumentId("test:document"),
            new DocumentRevision(8),
            new VisualStateId("test:visual"),
            new RectD(25d, 40d, 80d, 60d),
            placementMode);

    private static UpdateConnectionRouteCommand UpdateRouteCommand(
        IEnumerable<PointD> route) =>
        new(
            new DocumentId("test:document"),
            new DocumentRevision(8),
            new VisualStateId("test:visual"),
            route);

    private sealed class TestValidator : ICommandValidator
    {
        public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document) =>
            [];
    }
}
