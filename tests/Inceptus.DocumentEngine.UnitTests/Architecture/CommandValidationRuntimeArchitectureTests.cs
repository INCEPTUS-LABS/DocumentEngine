using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Runtime.Commands;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class CommandValidationRuntimeArchitectureTests
{
    [Fact]
    public void GenericValidationServiceDoesNotHardCodeFrameworkCommandShapes()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Runtime",
            "Commands",
            "CommandValidationService.cs"));

        Assert.DoesNotContain(nameof(MoveVisualStateCommand), source, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(MoveVisualStatesCommand), source, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ResizeVisualStateCommand), source, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(MoveLabelCommand), source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(UpdateConnectionRouteCommand),
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(UpdateSemanticElementNameCommand),
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(UpdateSemanticElementPropertyCommand),
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(MoveVisualStateCommandEnvelopeValidator),
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(MoveVisualStatesCommandEnvelopeValidator),
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(ResizeVisualStateCommandEnvelopeValidator),
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(MoveLabelCommandEnvelopeValidator),
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(UpdateConnectionRouteCommandEnvelopeValidator),
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(UpdateSemanticElementNameCommandEnvelopeValidator),
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            nameof(UpdateSemanticElementPropertyCommandEnvelopeValidator),
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            MoveVisualStateCommand.KnownTypeId.Value,
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            MoveVisualStatesCommand.KnownTypeId.Value,
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ResizeVisualStateCommand.KnownTypeId.Value,
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            MoveLabelCommand.KnownTypeId.Value,
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            UpdateConnectionRouteCommand.KnownTypeId.Value,
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            UpdateSemanticElementNameCommand.KnownTypeId.Value,
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            UpdateSemanticElementPropertyCommand.KnownTypeId.Value,
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationServiceSeparatesEnvelopeAndDelegatedReadOnlyValidation()
    {
        var internalMethods = typeof(CommandValidationService)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.IsAssembly)
            .ToArray();
        var create = Assert.Single(internalMethods, method => method.Name == "Create");
        var validate = Assert.Single(internalMethods, method => method.Name == "Validate");
        var validateEnvelope = Assert.Single(internalMethods, method =>
            method.Name == "ValidateEnvelope");
        var validateDelegated = Assert.Single(internalMethods, method =>
            method.Name == "ValidateDelegated");

        Assert.True(typeof(CommandValidationService).IsSealed);
        Assert.False(typeof(CommandValidationService).IsPublic);
        Assert.Empty(typeof(CommandValidationService).GetConstructors());
        Assert.Equal(
            ["Create", "HasValidators", "Validate", "ValidateDelegated", "ValidateEnvelope"],
            internalMethods.Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Create", create.Name);
        Assert.Equal(typeof(CommandValidationServiceCreationResult), create.ReturnType);
        Assert.Equal(
            [typeof(IEnumerable<CommandValidatorRegistration>)],
            create.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal("Validate", validate.Name);
        Assert.Equal(typeof(CommandValidationResult), validate.ReturnType);
        Assert.Equal(
            [typeof(ICommand), typeof(DocumentSnapshot)],
            validate.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(CommandValidationResult), validateEnvelope.ReturnType);
        Assert.Equal(
            [typeof(ICommand), typeof(DocumentSnapshot), typeof(CommandHandlerRegistration)],
            validateEnvelope.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(CommandValidationResult), validateDelegated.ReturnType);
        Assert.Equal(
            [typeof(ICommand), typeof(DocumentSnapshot), typeof(CancellationToken)],
            validateDelegated.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Empty(typeof(CommandValidationService).GetEvents(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly));
        Assert.Empty(typeof(CommandValidationService).GetProperties(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.DeclaredOnly));
    }

    [Fact]
    public void ServiceCreationResultHasAnImmutableControlledShape()
    {
        var expectedProperties = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(CommandValidationServiceCreationResult.Succeeded)] = typeof(bool),
            [nameof(CommandValidationServiceCreationResult.Service)] =
                typeof(CommandValidationService),
            [nameof(CommandValidationServiceCreationResult.Diagnostics)] =
                typeof(ImmutableArray<Diagnostic>),
        };
        var actualProperties = typeof(CommandValidationServiceCreationResult)
            .GetProperties(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

        Assert.True(typeof(CommandValidationServiceCreationResult).IsSealed);
        Assert.False(typeof(CommandValidationServiceCreationResult).IsPublic);
        Assert.Empty(typeof(CommandValidationServiceCreationResult).GetConstructors());
        Assert.Equal(
            expectedProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualProperties.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.All(actualProperties.Keys, name =>
            Assert.Null(typeof(CommandValidationServiceCreationResult).GetProperty(
                name,
                BindingFlags.NonPublic | BindingFlags.Instance)?.SetMethod));
        Assert.DoesNotContain(
            typeof(CommandValidationServiceCreationResult).GetMethods(
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => !method.IsSpecialName);
    }

    [Fact]
    public void RegistryAndValidationMechanismsRemainInternal()
    {
        var runtimeAssembly = typeof(CommandValidationService).Assembly;
        Type[] internalValidationTypes =
        [
            typeof(CommandValidationService),
            typeof(CommandValidationServiceCreationResult),
            runtimeAssembly.GetType(
                "Inceptus.DocumentEngine.Runtime.Commands.CommandValidatorRegistry",
                throwOnError: true)!,
        ];
        var friends = runtimeAssembly
            .GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName)
            .Order(StringComparer.Ordinal);

        Assert.All(internalValidationTypes, type => Assert.False(type.IsPublic));
        Assert.Equal(
            [
                "Inceptus.DocumentEngine.Canvas2D",
                "Inceptus.DocumentEngine.IntegrationTests",
                "Inceptus.DocumentEngine.UnitTests",
            ],
            friends);
    }

    [Fact]
    public void ValidationRuntimeExposesNoExecutionHistoryEventOrRuntimeEngineSurface()
    {
        string[] forbiddenFragments =
        [
            "BeginTransaction",
            "Canvas2D",
            "CommandHandler",
            "CommandProcessor",
            "Commit",
            "EditorState",
            "EditingSession",
            "Execute",
            "History",
            "Layout",
            "Pipeline",
            "ProjectedGraph",
            "ProjectionEngine",
            "Publish",
            "Renderer",
            "Routing",
            "Scene",
            "Undo",
        ];
        Type[] phaseC1Types =
        [
            typeof(CommandValidationService),
            typeof(CommandValidationServiceCreationResult),
        ];

        foreach (var type in phaseC1Types)
        {
            Assert.DoesNotContain(forbiddenFragments, fragment =>
                type.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

            foreach (var member in type.GetMembers(
                         BindingFlags.Public | BindingFlags.Instance |
                         BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(forbiddenFragments, fragment =>
                    member.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));
            }

            Assert.Empty(type.GetEvents(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly));
        }
    }

    [Fact]
    public void RuntimeContainsNoPostPhaseGSubsystemImplementation()
    {
        string[] forbiddenTypeFragments =
        [
            "Canvas2D",
            "EditingSession",
            "Pipeline",
            "Renderer",
            "Scene",
        ];
        var runtimeTypes = typeof(CommandValidationService).Assembly.GetTypes();

        Assert.DoesNotContain(runtimeTypes, type =>
            forbiddenTypeFragments.Any(fragment =>
                type.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(runtimeTypes, type =>
            type.Namespace?.Contains("Bpmn", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
