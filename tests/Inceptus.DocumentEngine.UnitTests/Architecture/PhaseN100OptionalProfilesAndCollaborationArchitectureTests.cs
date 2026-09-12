using System.Reflection;
using Inceptus.DocumentEngine.Bpmn;
using Inceptus.DocumentEngine.Bpmn.Profiles;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Bpmn.Validation;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Metadata;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Semantics;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN100OptionalProfilesAndCollaborationArchitectureTests
{
    [Fact]
    public void SemanticContainmentIsTypedAndLegacyElementsDefaultToScope()
    {
        Assert.Equal(
            [SemanticElementContainmentKind.Scope, SemanticElementContainmentKind.Document],
            Enum.GetValues<SemanticElementContainmentKind>());

        var parameter = typeof(SemanticElementSnapshot)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Single(candidate => candidate.Name == "containmentKind");

        Assert.True(parameter.HasDefaultValue);
        Assert.Equal(SemanticElementContainmentKind.Scope, parameter.DefaultValue);
        Assert.Equal(
            typeof(SemanticElementContainmentKind),
            typeof(SemanticElementSnapshot)
                .GetProperty(nameof(SemanticElementSnapshot.ContainmentKind))?.PropertyType);
    }

    [Fact]
    public void ProfileAvailabilityLivesOnlyInSemanticModelAndVisibilityIsViewState()
    {
        Assert.Equal(
            typeof(ModelProfileStateSnapshot),
            typeof(SemanticModelSnapshot)
                .GetProperty(nameof(SemanticModelSnapshot.ModelProfiles))?.PropertyType);
        Assert.DoesNotContain(
            typeof(DocumentSnapshot).GetProperties(),
            property => property.PropertyType == typeof(ModelProfileStateSnapshot));
        Assert.DoesNotContain(
            typeof(DocumentMetadataSnapshot).GetProperties(),
            property => property.PropertyType == typeof(ModelProfileStateSnapshot));

        Assert.Equal(
            typeof(ModelProfileViewStateSnapshot),
            typeof(EditingSessionState)
                .GetProperty(nameof(EditingSessionState.ModelProfileViewState))?.PropertyType);
        Assert.DoesNotContain(
            typeof(SemanticModelSnapshot).GetProperties(),
            property => property.PropertyType == typeof(ModelProfileViewStateSnapshot));
        Assert.DoesNotContain(
            typeof(DocumentSnapshot).GetProperties(),
            property => property.PropertyType == typeof(ModelProfileViewStateSnapshot));
        Assert.DoesNotContain(
            typeof(DocumentMetadataSnapshot).GetProperties(),
            property => property.PropertyType == typeof(ModelProfileViewStateSnapshot));
    }

    [Fact]
    public void DocumentRetainsExactlySemanticVisualAndMetadataAuthorities()
    {
        var componentProperties = typeof(DocumentSnapshot)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.Name is
                nameof(DocumentSnapshot.SemanticModel) or
                nameof(DocumentSnapshot.VisualModel) or
                nameof(DocumentSnapshot.Metadata))
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Metadata", "SemanticModel", "VisualModel"], componentProperties);
        var production = ReadAllProductionSource();
        Assert.DoesNotContain("DocumentSemanticStore", production, StringComparison.Ordinal);
        Assert.DoesNotContain("CollaborationSemanticStore", production, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnDocumentModel", production, StringComparison.Ordinal);
    }

    [Fact]
    public void BpmnCollaborationAndParticipantAreDocumentElementsNotFlowNodesOrScopes()
    {
        var collaboration = BpmnSemanticFactory.CreateCollaboration(
            new("architecture:n100:collaboration"));
        var participant = BpmnSemanticFactory.CreateParticipant(
            new("architecture:n100:participant"),
            collaboration.Id);

        Assert.Equal(SemanticElementContainmentKind.Document, collaboration.ContainmentKind);
        Assert.Equal(SemanticElementContainmentKind.Document, participant.ContainmentKind);
        Assert.False(BpmnSemanticTypes.IsFlowNode(BpmnSemanticTypes.Collaboration));
        Assert.False(BpmnSemanticTypes.IsFlowNode(BpmnSemanticTypes.Participant));
        Assert.Null(collaboration.AttachedToElementId);
        Assert.Null(participant.AttachedToElementId);
        Assert.DoesNotContain(
            typeof(BpmnSemanticProperties).GetFields(
                BindingFlags.Public | BindingFlags.Static),
            field => field.Name.Contains("ParticipantIds", StringComparison.Ordinal));
    }

    [Fact]
    public void N100AddsOnlyProfilesOrganizationalCommandsAndBackgroundActionToN91()
    {
        var prior = BpmnPluginRegistration.N91;
        var current = BpmnPluginRegistration.N100;

        Assert.Equal(prior.ProjectionRules, current.ProjectionRules);
        Assert.Equal(prior.LayoutAlgorithms, current.LayoutAlgorithms);
        Assert.Equal(prior.RoutingAlgorithms, current.RoutingAlgorithms);
        Assert.Equal(prior.SceneContributors, current.SceneContributors);
        Assert.Equal(prior.ToolboxContributions, current.ToolboxContributions);
        Assert.Equal(prior.ToolboxPlacementRegistrations,
            current.ToolboxPlacementRegistrations);
        Assert.Equal(prior.PropertiesSchemas, current.PropertiesSchemas);
        Assert.Equal(prior.ModelValidationRules, current.ModelValidationRules);
        Assert.Equal(prior.CommandHandlers.Length + 6, current.CommandHandlers.Length);
        Assert.Equal(prior.HistoryPolicies.Length + 6, current.HistoryPolicies.Length);
        Assert.Equal(BpmnModelProfiles.Definitions, current.ModelProfileDefinitions);
        Assert.Single(current.BackgroundActions);
        Assert.DoesNotContain(
            current.ModelValidationRules,
            rule => rule.RuleId == BpmnCollaborationStructuralValidator.KnownRuleId);
    }

    [Fact]
    public void GenericFrameworkAndPresentationContainNoProfileOrNotationSwitches()
    {
        string[] genericDirectories =
        [
            Path.Combine(RepositoryRoot, "src", "Inceptus.DocumentEngine.Contracts"),
            Path.Combine(RepositoryRoot, "src", "Inceptus.DocumentEngine.Runtime"),
            Path.Combine(RepositoryRoot, "src", "Inceptus.DocumentEngine.Canvas2D"),
            Path.Combine(RepositoryRoot, "src", "Inceptus.DocumentEngine.Bpmn.Blazor", "Presentation"),
            Path.Combine(RepositoryRoot, "src", "Inceptus.DocumentEngine.Bpmn.Blazor", "Components"),
        ];
        var genericSource = string.Join(
            Environment.NewLine,
            genericDirectories.SelectMany(ReadGenericSourceDirectory));

        Assert.DoesNotContain("BpmnSemanticTypes", genericSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BpmnPluginRegistration", genericSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CreateBpmn", genericSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OrganizationalProfile", genericSource,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StageProfile", genericSource,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void N100DoesNotIntroduceDeferredPoolLaneStageOrMessageFlowSemantics()
    {
        var semanticTypeNames = typeof(BpmnSemanticTypes)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(static property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Pool", semanticTypeNames);
        Assert.DoesNotContain("Lane", semanticTypeNames);
        Assert.DoesNotContain("Stage", semanticTypeNames);
        Assert.DoesNotContain("MessageFlow", semanticTypeNames);
    }

    private static string ReadAllProductionSource() => string.Join(
        Environment.NewLine,
        Directory.EnumerateDirectories(Path.Combine(RepositoryRoot, "src"))
            .SelectMany(ReadSourceDirectory));

    private static IEnumerable<string> ReadSourceDirectory(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText);

    private static IEnumerable<string> ReadGenericSourceDirectory(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(static path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) &&
                !ApprovedN104Changes.IsPublishingApplicationCompositionPath(path))
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText);

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName,
                        "Inceptus.DocumentEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }
}
