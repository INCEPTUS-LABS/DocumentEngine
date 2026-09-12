using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Visuals;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN80ProcessScopeArchitectureTests
{
    [Fact]
    public void SemanticModelOwnsTheSinglePersistentContainmentAuthority()
    {
        Assert.Equal(
            typeof(DocumentScopeId),
            typeof(SemanticModelSnapshot)
                .GetProperty(nameof(SemanticModelSnapshot.RootScopeId))?.PropertyType);
        Assert.Equal(
            typeof(ImmutableArray<DocumentScopeSnapshot>),
            typeof(SemanticModelSnapshot)
                .GetProperty(nameof(SemanticModelSnapshot.NestedScopes))?.PropertyType);
        Assert.Equal(
            typeof(ImmutableArray<SemanticElementScopeMembershipSnapshot>),
            typeof(SemanticModelSnapshot)
                .GetProperty(nameof(SemanticModelSnapshot.ScopeMemberships))?.PropertyType);

        Assert.Equal(
            typeof(SemanticElementContainmentKind),
            typeof(SemanticElementSnapshot)
                .GetProperty(nameof(SemanticElementSnapshot.ContainmentKind))?.PropertyType);
        Assert.Single(
            typeof(SemanticElementSnapshot).GetProperties(),
            static property => ContainsContainmentName(property.Name));
        Assert.DoesNotContain(
            typeof(SemanticRelationshipSnapshot).GetProperties(),
            static property => ContainsContainmentName(property.Name));
        Assert.Equal(
            typeof(SemanticElementId),
            typeof(SemanticElementScopeMembershipSnapshot)
                .GetProperty(nameof(
                    SemanticElementScopeMembershipSnapshot.SemanticElementId))?.PropertyType);
        Assert.Equal(
            typeof(DocumentScopeId),
            typeof(SemanticElementScopeMembershipSnapshot)
                .GetProperty(nameof(
                    SemanticElementScopeMembershipSnapshot.ScopeId))?.PropertyType);
    }

    [Fact]
    public void VisualModelContainsNoIndependentScopeOrParentAuthority()
    {
        Type[] visualContractTypes =
        [
            typeof(VisualModelSnapshot),
            typeof(VisualStateSnapshot),
            typeof(IVisualModelView),
        ];

        foreach (var type in visualContractTypes)
        {
            Assert.DoesNotContain(
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly),
                static property => ContainsContainmentName(property.Name));
            Assert.DoesNotContain(
                type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .SelectMany(static constructor => constructor.GetParameters()),
                static parameter => ContainsContainmentName(parameter.Name ?? string.Empty));
        }
    }

    [Fact]
    public void RootScopeIsImplicitDocumentStructureRatherThanAFakeSemanticNode()
    {
        var documentId = new DocumentId("n8:root-only");
        var snapshot = new SemanticModelSnapshot(documentId, DocumentRevision.Zero);

        Assert.Equal(new DocumentScopeId(documentId.Value), snapshot.RootScopeId);
        Assert.Equal(snapshot.RootScopeId, snapshot.GetRootScope().Id);
        Assert.Empty(snapshot.NestedScopes);
        Assert.Empty(snapshot.Elements);
        Assert.DoesNotContain(
            typeof(SemanticModelSnapshot).Assembly.GetExportedTypes(),
            static type =>
                type.Name.Contains("RootProcess", StringComparison.OrdinalIgnoreCase) ||
                type.Name.Contains("ProcessRootNode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenericScopeContractsRemainNotationNeutral()
    {
        var genericSource = string.Join(
            Environment.NewLine,
            ReadProductionProject("Inceptus.DocumentEngine.Contracts")
                .Concat(ReadProductionProject("Inceptus.DocumentEngine.Runtime"))
                .Concat(ReadProductionProject("Inceptus.DocumentEngine.Canvas2D")));
        string[] forbiddenTokens =
        [
            "BPMN",
            "Bpmn",
            "SequenceFlow",
            "SubProcess",
        ];

        Assert.All(forbiddenTokens, token =>
            Assert.DoesNotContain(token, genericSource, StringComparison.Ordinal));
    }

    [Fact]
    public void ScopeContractsExposeOnlyImmutableReadAndQuerySurface()
    {
        Type[] scopeTypes =
        [
            typeof(DocumentScopeSnapshot),
            typeof(SemanticElementScopeMembershipSnapshot),
            typeof(SemanticModelSnapshot),
        ];

        foreach (var type in scopeTypes)
        {
            Assert.All(
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly),
                static property => Assert.Null(property.SetMethod));
            Assert.DoesNotContain(
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance |
                        BindingFlags.DeclaredOnly)
                    .Select(static property => property.PropertyType)
                    .Concat(type.GetMethods(
                            BindingFlags.Public | BindingFlags.Instance |
                            BindingFlags.DeclaredOnly)
                        .Where(static method => !method.IsSpecialName)
                        .Select(static method => method.ReturnType)),
                IsMutableCollectionContract);
        }

        string[] forbiddenMutationPrefixes =
        [
            "Add",
            "Clear",
            "Create",
            "Delete",
            "Insert",
            "Move",
            "Mutate",
            "Remove",
            "Reparent",
            "Replace",
            "Set",
            "Update",
        ];
        Assert.DoesNotContain(
            typeof(SemanticModelSnapshot).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => !method.IsSpecialName &&
                forbiddenMutationPrefixes.Any(prefix =>
                    method.Name.StartsWith(prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void RelationshipsDeriveScopeAndPersistNoSecondScopeField()
    {
        Assert.DoesNotContain(
            typeof(SemanticRelationshipSnapshot).GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            static property => property.Name.Contains("Scope", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            typeof(SemanticRelationshipSnapshot).GetConstructors()
                .SelectMany(static constructor => constructor.GetParameters()),
            static parameter =>
                parameter.Name?.Contains("scope", StringComparison.OrdinalIgnoreCase) == true);

        var semanticModelSource = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Semantics",
            "SemanticModelSnapshot.cs");
        Assert.Contains("return TryGetScope(relationship.SourceId, out scope);", semanticModelSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SameScopeSequenceFlowRuleAndRootGraphAnalysisKeepDistinctAuthorities()
    {
        var rules = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Semantics",
            "BpmnSequenceFlowConfigurationRules.cs");
        var creation = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnCreationValidation.cs");
        var reconnection = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Commands",
            "BpmnEndpointReconnectionValidation.cs");
        var structuralValidation = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Validation",
            "BpmnStructuralValidationRule.cs");
        const string sequenceFlowValidationType =
            "internal static class BpmnSequenceFlowCreationValidation";
        var sequenceFlowValidationStart = creation.IndexOf(
            sequenceFlowValidationType,
            StringComparison.Ordinal);
        Assert.True(sequenceFlowValidationStart >= 0);
        var sequenceFlowCreation = creation[sequenceFlowValidationStart..];

        Assert.Contains("SequenceFlowCrossesScope", rules, StringComparison.Ordinal);
        Assert.Contains("GetScope(", rules, StringComparison.Ordinal);
        Assert.Contains("BpmnSequenceFlowConfigurationRules.AnalyzeCandidate", creation,
            StringComparison.Ordinal);
        Assert.Contains("ValidateEndpointSemantics", reconnection,
            StringComparison.Ordinal);
        Assert.Contains(".Analyze(document)", structuralValidation,
            StringComparison.Ordinal);
        Assert.Contains(
            ".AnalyzeScope(document, activeScopeId)",
            structuralValidation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetScope(", sequenceFlowCreation, StringComparison.Ordinal);
        Assert.DoesNotContain("GetScope(", reconnection, StringComparison.Ordinal);
        Assert.Contains("document.SemanticModel.GetScope(node.Id).Id ==",
            structuralValidation, StringComparison.Ordinal);
        Assert.Contains("document.SemanticModel.IsTopLevelScope(activeScopeId)", structuralValidation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetScope(flow.SourceId)", structuralValidation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetScope(flow.TargetId)", structuralValidation,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ScopeId", sequenceFlowCreation, StringComparison.Ordinal);
        Assert.DoesNotContain("ScopeId", reconnection, StringComparison.Ordinal);
    }

    [Fact]
    public void N81AddsCompactSubProcessWithoutInlineExpansionOrNestedCoordinates()
    {
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(RepositoryRoot, "src"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(static path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));
        string[] forbiddenTokens =
        [
            "ChildBoundsInsideParent",
            "ContainerLocalCoordinates",
            "ExpandedSubProcess",
            "ExpandedSubProcessBounds",
            "MoveElementToScope",
            "NestedCanvasTransform",
        ];

        Assert.Contains("BPMN.SubProcess", source, StringComparison.Ordinal);
        Assert.All(forbiddenTokens, token =>
            Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScopeContractsContainNoGeometryOrRuntimeNavigationState()
    {
        Type[] scopeTypes =
        [
            typeof(DocumentScopeSnapshot),
            typeof(SemanticElementScopeMembershipSnapshot),
        ];
        var publicSignatureTypes = scopeTypes
            .SelectMany(static type =>
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance |
                        BindingFlags.DeclaredOnly)
                    .Select(static property => property.PropertyType)
                    .Concat(type.GetConstructors()
                        .SelectMany(static constructor => constructor.GetParameters())
                        .Select(static parameter => parameter.ParameterType)))
            .ToArray();

        Assert.DoesNotContain(publicSignatureTypes, static type =>
            type.Namespace?.Contains("Geometry", StringComparison.OrdinalIgnoreCase) == true ||
            type.Namespace?.Contains("EditorState", StringComparison.OrdinalIgnoreCase) == true ||
            type.Name.Contains("Viewport", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsContainmentName(string name) =>
        name.Contains("Contain", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Owner", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Parent", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Scope", StringComparison.OrdinalIgnoreCase);

    private static bool IsMutableCollectionContract(Type type)
    {
        if (type.IsArray || type == typeof(IList) || type == typeof(IDictionary) ||
            type == typeof(ICollection))
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        Type[] forbiddenDefinitions =
        [
            typeof(List<>),
            typeof(Dictionary<,>),
            typeof(IList<>),
            typeof(IDictionary<,>),
            typeof(ICollection<>),
        ];
        return forbiddenDefinitions.Contains(type.GetGenericTypeDefinition());
    }

    private static IEnumerable<string> ReadProductionProject(string project) =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", project),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(static path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText);

    private static string ReadProductionFile(
        string project,
        string directory,
        string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "src", project, directory, fileName));

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
