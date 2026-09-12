using System.Collections.Immutable;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Commands;

public sealed class CommandValidationServiceTests
{
    private static readonly DocumentId TestDocumentId = new("test:document");
    private static readonly DocumentRevision TestRevision = new(5);
    private static readonly CommandTypeId TestCommandType = new("test:command");

    [Fact]
    public void ServiceCreationDefensivelyCopiesAndOrdersRegistrationsByStableIds()
    {
        var calls = new List<string>();
        var registrations = new List<CommandValidatorRegistration>
        {
            Registration("test:command", "test:z", RecordingValidator("z", calls)),
            Registration("test:other", "test:a", RecordingValidator("other", calls)),
            Registration("test:command", "test:a", RecordingValidator("a", calls)),
        };
        var creation = CommandValidationService.Create(registrations);
        registrations.Clear();

        Assert.True(creation.Succeeded);
        Assert.Empty(creation.Diagnostics);
        var service = Assert.IsType<CommandValidationService>(creation.Service);
        var result = service.Validate(Command(), Snapshot());

        Assert.True(result.IsValid);
        Assert.Equal(["a", "z"], calls);
    }

    [Fact]
    public void DuplicateTypeAndValidatorRegistrationReturnsFailureWithoutService()
    {
        var creation = CommandValidationService.Create(
        [
            Registration("test:command", "test:validator", ValidValidator()),
            Registration("test:command", "test:validator", ValidValidator()),
            Registration("test:command", "test:validator", ValidValidator()),
        ]);

        Assert.False(creation.Succeeded);
        Assert.Null(creation.Service);
        var diagnostic = Assert.Single(creation.Diagnostics);
        Assert.Equal(
            CommandValidationDiagnosticCodes.DuplicateValidatorRegistration,
            diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("test:validator", diagnostic.SourceIdentity);
        Assert.Equal("test:command", diagnostic.Context["CommandTypeId"]);
        Assert.Equal("test:validator", diagnostic.Context["ValidatorId"]);
    }

    [Fact]
    public void ValidatorIdMayBeReusedForADifferentCommandType()
    {
        var creation = CommandValidationService.Create(
        [
            Registration("test:command-a", "test:validator", ValidValidator()),
            Registration("test:command-b", "test:validator", ValidValidator()),
        ]);

        Assert.True(creation.Succeeded);
        Assert.NotNull(creation.Service);
        Assert.Empty(creation.Diagnostics);
    }

    [Fact]
    public void ServiceCreationRejectsNullInputAndNullRegistration()
    {
        Assert.Throws<ArgumentNullException>(() => CommandValidationService.Create(null!));
        Assert.Throws<ArgumentException>(() => CommandValidationService.Create([null!]));
    }

    [Fact]
    public void ValidationResultIsBoundToTheActualSnapshotAndCommandType()
    {
        var snapshot = Snapshot();
        var service = Service(Registration(
            TestCommandType.Value,
            "test:validator",
            ValidValidator()));

        var result = service.Validate(Command(), snapshot);

        Assert.True(result.IsValid);
        Assert.Equal(snapshot.DocumentId, result.DocumentId);
        Assert.Equal(snapshot.Revision, result.Revision);
        Assert.Equal(TestCommandType, result.CommandTypeId);
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public void TargetDocumentMismatchShortCircuitsRegisteredValidators()
    {
        var callCount = 0;
        var service = Service(Registration(
            TestCommandType.Value,
            "test:validator",
            CountingValidator(() => callCount++)));
        var command = new TestCommand(
            TestCommandType,
            new DocumentId("test:other-document"),
            TestRevision,
            CommandCategory.Visual,
            AuthoritativeDocumentComponent.VisualModel);

        var result = service.Validate(command, Snapshot());

        Assert.False(result.IsValid);
        Assert.Equal(0, callCount);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(CommandValidationDiagnosticCodes.TargetDocumentMismatch, diagnostic.Code);
        Assert.Equal("test:document", diagnostic.Context["ActualDocumentId"]);
        Assert.Equal("test:other-document", diagnostic.Context["TargetDocumentId"]);
    }

    [Fact]
    public void StaleRevisionShortCircuitsRegisteredValidators()
    {
        var callCount = 0;
        var service = Service(Registration(
            TestCommandType.Value,
            "test:validator",
            CountingValidator(() => callCount++)));
        var command = new TestCommand(
            TestCommandType,
            TestDocumentId,
            TestRevision.Increment(),
            CommandCategory.Visual,
            AuthoritativeDocumentComponent.VisualModel);

        var result = service.Validate(command, Snapshot());

        Assert.False(result.IsValid);
        Assert.Equal(0, callCount);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(CommandValidationDiagnosticCodes.StaleRevision, diagnostic.Code);
        Assert.Equal("5", diagnostic.Context["ActualRevision"]);
        Assert.Equal("6", diagnostic.Context["ExpectedRevision"]);
    }

    [Fact]
    public void UnsupportedCommandTypeReturnsAnErrorWithoutInvokingOtherValidators()
    {
        var callCount = 0;
        var service = Service(Registration(
            "test:other-command",
            "test:validator",
            CountingValidator(() => callCount++)));

        var result = service.Validate(Command(), Snapshot());

        Assert.False(result.IsValid);
        Assert.Equal(0, callCount);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(CommandValidationDiagnosticCodes.UnsupportedCommandType, diagnostic.Code);
        Assert.Equal(TestCommandType.Value, diagnostic.Context["CommandTypeId"]);
    }

    [Fact]
    public void ApprovedCategoryAndAffectedComponentPairsAreValid()
    {
        (CommandCategory Category, AuthoritativeDocumentComponent Components)[] cases =
        [
            (CommandCategory.Semantic, AuthoritativeDocumentComponent.SemanticModel),
            (CommandCategory.Visual, AuthoritativeDocumentComponent.VisualModel),
            (CommandCategory.Metadata, AuthoritativeDocumentComponent.Metadata),
            (CommandCategory.Publication, AuthoritativeDocumentComponent.Publication),
            (CommandCategory.Document,
                AuthoritativeDocumentComponent.SemanticModel |
                AuthoritativeDocumentComponent.VisualModel),
            (CommandCategory.Compound,
                AuthoritativeDocumentComponent.VisualModel |
                AuthoritativeDocumentComponent.Metadata),
            (CommandCategory.Document,
                AuthoritativeDocumentComponent.SemanticModel |
                AuthoritativeDocumentComponent.VisualModel |
                AuthoritativeDocumentComponent.Metadata),
        ];
        var service = Service(Registration(
            TestCommandType.Value,
            "test:validator",
            ValidValidator()));

        foreach (var (category, components) in cases)
        {
            var result = service.Validate(
                new TestCommand(TestCommandType, TestDocumentId, TestRevision, category, components),
                Snapshot());

            Assert.True(result.IsValid);
        }
    }

    [Fact]
    public void InvalidCategoryAndAffectedComponentDeclarationsAreRejected()
    {
        (CommandCategory Category, AuthoritativeDocumentComponent Components)[] cases =
        [
            (CommandCategory.Visual, AuthoritativeDocumentComponent.SemanticModel),
            (CommandCategory.Document, AuthoritativeDocumentComponent.VisualModel),
            (CommandCategory.Compound, AuthoritativeDocumentComponent.Metadata),
            (CommandCategory.Semantic, AuthoritativeDocumentComponent.None),
            (CommandCategory.Visual, (AuthoritativeDocumentComponent)16),
            ((CommandCategory)99, AuthoritativeDocumentComponent.VisualModel),
        ];
        var callCount = 0;
        var service = Service(Registration(
            TestCommandType.Value,
            "test:validator",
            CountingValidator(() => callCount++)));

        foreach (var (category, components) in cases)
        {
            var result = service.Validate(
                new TestCommand(TestCommandType, TestDocumentId, TestRevision, category, components),
                Snapshot());

            Assert.False(result.IsValid);
            Assert.Contains(
                result.Diagnostics,
                diagnostic => diagnostic.Code ==
                    CommandValidationDiagnosticCodes.InvalidAffectedComponentDeclaration);
        }

        Assert.Equal(0, callCount);
    }

    [Fact]
    public void WarningDiagnosticsRemainValid()
    {
        var service = Service(Registration(
            TestCommandType.Value,
            "test:validator",
            new DelegateValidator((_, _) =>
            [
                new Diagnostic("TEST_WARNING", DiagnosticSeverity.Warning, "warning"),
            ])));

        var result = service.Validate(Command(), Snapshot());

        Assert.True(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("TEST_WARNING", diagnostic.Code);
    }

    [Fact]
    public void ValidatorErrorIsRetainedAndMarkedAsAValidatorFailure()
    {
        var service = Service(Registration(
            TestCommandType.Value,
            "test:validator",
            new DelegateValidator((_, _) =>
            [
                new Diagnostic("TEST_ERROR", DiagnosticSeverity.Error, "invalid"),
            ])));

        var result = service.Validate(Command(), Snapshot());

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "TEST_ERROR");
        var failure = Assert.Single(result.Diagnostics, diagnostic =>
            diagnostic.Code == CommandValidationDiagnosticCodes.ValidatorFailure);
        Assert.Equal("test:validator", failure.SourceIdentity);
        Assert.Equal(TestCommandType.Value, failure.Context["CommandTypeId"]);
        Assert.Equal("test:validator", failure.Context["ValidatorId"]);
        Assert.False(failure.Context.ContainsKey("ExceptionType"));
    }

    [Fact]
    public void AllMatchingValidatorsRunAfterErrorsAndResultsRemainDeterministic()
    {
        var calls = new List<string>();
        var service = Service(
            Registration(
                TestCommandType.Value,
                "test:z-validator",
                new DelegateValidator((_, _) =>
                {
                    calls.Add("z");
                    return
                    [
                        new Diagnostic("TEST_Z", DiagnosticSeverity.Error, "z failed"),
                    ];
                })),
            Registration(
                TestCommandType.Value,
                "test:a-validator",
                new DelegateValidator((_, _) =>
                {
                    calls.Add("a");
                    return
                    [
                        new Diagnostic("TEST_A", DiagnosticSeverity.Error, "a failed"),
                    ];
                })));

        var first = service.Validate(Command(), Snapshot());
        var second = service.Validate(Command(), Snapshot());

        Assert.Equal(["a", "z", "a", "z"], calls);
        Assert.Equal(first, second);
        Assert.False(first.IsValid);
        Assert.Equal(4, first.Diagnostics.Length);
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Code == "TEST_A");
        Assert.Contains(first.Diagnostics, diagnostic => diagnostic.Code == "TEST_Z");
        Assert.Equal(
            2,
            first.Diagnostics.Count(diagnostic =>
                diagnostic.Code == CommandValidationDiagnosticCodes.ValidatorFailure));
    }

    [Fact]
    public void InvalidValidatorOutputBecomesADeterministicFailure()
    {
        var defaultOutput = Service(Registration(
            TestCommandType.Value,
            "test:default-validator",
            new DelegateValidator((_, _) => default)));
        var nullOutput = Service(Registration(
            TestCommandType.Value,
            "test:null-validator",
            new DelegateValidator((_, _) =>
                ImmutableArray.CreateRange(new Diagnostic[] { null! }))));

        var defaultResult = defaultOutput.Validate(Command(), Snapshot());
        var nullResult = nullOutput.Validate(Command(), Snapshot());

        Assert.False(defaultResult.IsValid);
        Assert.Equal(
            CommandValidationDiagnosticCodes.ValidatorFailure,
            Assert.Single(defaultResult.Diagnostics).Code);
        Assert.False(nullResult.IsValid);
        Assert.Equal(
            CommandValidationDiagnosticCodes.ValidatorFailure,
            Assert.Single(nullResult.Diagnostics).Code);
    }

    [Fact]
    public void ValidatorExceptionIsSanitizedIntoADeterministicFailure()
    {
        var service = Service(Registration(
            TestCommandType.Value,
            "test:throwing-validator",
            new DelegateValidator((_, _) => throw new InvalidOperationException("sensitive text"))));

        var result = service.Validate(Command(), Snapshot());

        Assert.False(result.IsValid);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(CommandValidationDiagnosticCodes.ValidatorFailure, diagnostic.Code);
        Assert.Equal(typeof(InvalidOperationException).FullName,
            diagnostic.Context["ExceptionType"]);
        Assert.DoesNotContain("sensitive text", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive text", diagnostic.Context.Values);
    }

    [Fact]
    public async Task ValidationServiceSupportsConcurrentReadOnlyValidation()
    {
        var invocationCount = 0;
        var service = Service(Registration(
            TestCommandType.Value,
            "test:validator",
            CountingValidator(() => Interlocked.Increment(ref invocationCount))));
        var snapshot = Snapshot();
        var command = Command();

        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => service.Validate(command, snapshot))));

        Assert.All(results, result => Assert.True(result.IsValid));
        Assert.Equal(64, invocationCount);
        Assert.Equal(snapshot, Snapshot());
    }

    [Fact]
    public void ValidationRejectsNullCommandSnapshotAndCommandIdentity()
    {
        var service = Service(Registration(
            TestCommandType.Value,
            "test:validator",
            ValidValidator()));

        Assert.Throws<ArgumentNullException>(() => service.Validate(null!, Snapshot()));
        Assert.Throws<ArgumentNullException>(() => service.Validate(Command(), null!));
        Assert.Throws<ArgumentNullException>(() => service.Validate(
            new TestCommand(
                null!,
                TestDocumentId,
                TestRevision,
                CommandCategory.Visual,
                AuthoritativeDocumentComponent.VisualModel),
            Snapshot()));
        Assert.Throws<ArgumentNullException>(() => service.Validate(
            new TestCommand(
                TestCommandType,
                null!,
                TestRevision,
                CommandCategory.Visual,
                AuthoritativeDocumentComponent.VisualModel),
            Snapshot()));
    }

    private static CommandValidationService Service(
        params CommandValidatorRegistration[] registrations)
    {
        var creation = CommandValidationService.Create(registrations);
        Assert.True(creation.Succeeded);
        Assert.Empty(creation.Diagnostics);
        return Assert.IsType<CommandValidationService>(creation.Service);
    }

    private static CommandValidatorRegistration Registration(
        string typeId,
        string validatorId,
        ICommandValidator validator) =>
        new(new CommandTypeId(typeId), new CommandValidatorId(validatorId), validator);

    private static DelegateValidator ValidValidator() =>
        new DelegateValidator((_, _) => []);

    private static DelegateValidator CountingValidator(Action count) =>
        new DelegateValidator((_, _) =>
        {
            count();
            return [];
        });

    private static DelegateValidator RecordingValidator(string value, List<string> calls) =>
        new DelegateValidator((_, _) =>
        {
            calls.Add(value);
            return [];
        });

    private static TestCommand Command() =>
        new(
            TestCommandType,
            TestDocumentId,
            TestRevision,
            CommandCategory.Visual,
            AuthoritativeDocumentComponent.VisualModel);

    private static DocumentSnapshot Snapshot()
    {
        var semantic = new SemanticModelSnapshot(
            TestDocumentId,
            TestRevision,
            [
                new SemanticElementSnapshot(
                    new SemanticElementId("test:semantic"),
                    new SemanticTypeId("test:node-type")),
            ]);
        var visual = new VisualModelSnapshot(
            TestDocumentId,
            TestRevision,
            [
                new VisualStateSnapshot(
                    new VisualStateId("test:visual"),
                    new SemanticElementId("test:semantic"),
                    new PointD(10d, 20d),
                    new SizeD(100d, 60d),
                    VisualPlacementMode.Manual),
            ]);
        var metadata = new DocumentMetadataSnapshot(TestDocumentId, TestRevision);
        return new DocumentSnapshot(semantic, visual, metadata);
    }

    private sealed class DelegateValidator(
        Func<ICommand, DocumentSnapshot, ImmutableArray<Diagnostic>> validate) : ICommandValidator
    {
        public ImmutableArray<Diagnostic> Validate(ICommand command, DocumentSnapshot document) =>
            validate(command, document);
    }

    private sealed class TestCommand(
        CommandTypeId typeId,
        DocumentId targetDocumentId,
        DocumentRevision expectedRevision,
        CommandCategory category,
        AuthoritativeDocumentComponent affectedComponents) : ICommand
    {
        public CommandTypeId TypeId { get; } = typeId;

        public DocumentId TargetDocumentId { get; } = targetDocumentId;

        public DocumentRevision ExpectedRevision { get; } = expectedRevision;

        public CommandCategory Category { get; } = category;

        public AuthoritativeDocumentComponent AffectedComponents { get; } = affectedComponents;
    }
}
