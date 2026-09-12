using System.Collections.Immutable;
using System.Reflection;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.EditorState;
using Inceptus.DocumentEngine.Contracts.Geometry;
using Inceptus.DocumentEngine.Contracts.History;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Runtime.Commands;
using Inceptus.DocumentEngine.Runtime.Documents;
using Inceptus.DocumentEngine.Runtime.EditorState;
using Inceptus.DocumentEngine.Runtime.History;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseD1ArchitectureTests
{
    private static readonly BindingFlags DeclaredPublicMembers =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void DocumentSnapshotContainsNeitherEditorStateNorHistory()
    {
        var signatureTypes = GetPublicSignatureTypes(typeof(DocumentSnapshot)).ToArray();

        Assert.DoesNotContain(signatureTypes, IsEditorStateOrHistoryType);
        Assert.DoesNotContain(
            typeof(DocumentSnapshot).GetProperties(),
            property => ContainsAny(property.Name, "EditorState", "History"));
    }

    [Fact]
    public void CommandHandlerAndValidatorContractsContainNoHistoryBoundary()
    {
        Type[] commandBoundary =
        [
            typeof(ICommand),
            typeof(ICommandHandler),
            typeof(ICommandEnvelopeValidator),
            typeof(ICommandValidator),
            typeof(CommandHandlerRegistration),
            typeof(CommandValidatorRegistration),
        ];

        var signatureTypes = commandBoundary.SelectMany(GetPublicSignatureTypes).ToArray();

        Assert.DoesNotContain(signatureTypes, type =>
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Contracts.History", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Runtime.History", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            commandBoundary.SelectMany(type => type.GetMembers(DeclaredPublicMembers)),
            member => member.Name.Contains("History", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HistoryEntriesAndStorageAreNotPubliclyExposed()
    {
        Assert.False(typeof(HistoryEntry).IsPublic);
        Assert.False(typeof(HistoryStore).IsPublic);
        Assert.False(typeof(HistoryState).IsPublic);
        Assert.False(typeof(PreparedHistoryMutation).IsPublic);
        Assert.False(typeof(HistoryCoordinator).IsPublic);

        var runtimeExports = typeof(Document).Assembly.GetExportedTypes();
        Assert.DoesNotContain(runtimeExports, type =>
            ContainsAny(
                type.Name,
                "HistoryEntry",
                "HistoryStore",
                "HistoryState",
                "PreparedHistoryMutation",
                "HistoryCoordinator"));

        var managerSignatures = GetPublicSignatureTypes(typeof(HistoryManager)).ToArray();
        Assert.DoesNotContain(managerSignatures, type =>
            type == typeof(HistoryEntry) ||
            type == typeof(HistoryStore) ||
            type == typeof(HistoryState) ||
            type == typeof(PreparedHistoryMutation));
    }

    [Fact]
    public void UndoAndRedoUseOnlyHistoryManagerAndCommandProcessorBoundaries()
    {
        var managerMethods = typeof(HistoryManager).GetMethods(DeclaredPublicMembers);
        var undo = Assert.Single(managerMethods, method => method.Name == nameof(HistoryManager.UndoAsync));
        var redo = Assert.Single(managerMethods, method => method.Name == nameof(HistoryManager.RedoAsync));

        AssertHistoryOperationShape(undo);
        AssertHistoryOperationShape(redo);

        var undoRedoSignatures = new[] { undo, redo }
            .SelectMany(GetSignatureTypes)
            .ToArray();
        Assert.DoesNotContain(undoRedoSignatures, IsForbiddenUndoRedoDependency);

        var publicUndoRedoMembers = typeof(Document).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMembers(DeclaredPublicMembers))
            .Where(member => ContainsAny(member.Name, "Undo", "Redo"))
            .ToArray();
        Assert.DoesNotContain(publicUndoRedoMembers, member =>
            ContainsAny(member.Name, "Replace", "RestoreState", "InstallState", "SetState"));
    }

    [Fact]
    public void SerializationShapedContractsContainNeitherEditorStateNorHistory()
    {
        var productionTypes = typeof(ICommand).Assembly.GetTypes()
            .Concat(typeof(Document).Assembly.GetTypes())
            .Where(type =>
                ContainsAny(type.Namespace ?? string.Empty, "Serialization", "Persistence", "Json") ||
                ContainsAny(type.Name, "Serializer", "SerializedDocument", "PersistedDocument"))
            .ToArray();

        var leakedTypes = productionTypes
            .SelectMany(GetPublicSignatureTypes)
            .Where(IsEditorStateOrHistoryType)
            .ToArray();

        Assert.Empty(leakedTypes);
    }

    [Fact]
    public void EditorStateStoreExposesOnlyImmutableSnapshotReplacement()
    {
        Assert.True(typeof(EditorStateStore).IsSealed);
        Assert.Empty(typeof(EditorStateStore).GetProperties(DeclaredPublicMembers));
        Assert.Empty(typeof(EditorStateStore).GetEvents(DeclaredPublicMembers));
        Assert.Empty(typeof(EditorStateStore).GetFields(DeclaredPublicMembers));

        var methods = typeof(EditorStateStore).GetMethods(DeclaredPublicMembers);
        Assert.Equal(
            [nameof(EditorStateStore.CaptureSnapshot), nameof(EditorStateStore.TryUpdate)],
            methods.Select(method => method.Name).Order(StringComparer.Ordinal));

        var capture = Assert.Single(methods, method => method.Name == nameof(EditorStateStore.CaptureSnapshot));
        Assert.Equal(typeof(EditorStateSnapshot), capture.ReturnType);
        Assert.Empty(capture.GetParameters());

        var update = Assert.Single(methods, method => method.Name == nameof(EditorStateStore.TryUpdate));
        Assert.Equal(typeof(bool), update.ReturnType);
        Assert.Equal(
            [typeof(EditorStateSnapshot), typeof(EditorStateSnapshot)],
            update.GetParameters().Select(parameter => parameter.ParameterType));

        Assert.DoesNotContain(
            GetPublicSignatureTypes(typeof(EditorStateStore)),
            type => typeof(Delegate).IsAssignableFrom(type) || IsMutableCollectionType(type));
    }

    [Fact]
    public void PluginHistoryPoliciesAndFactoriesAreContractsOnly()
    {
        Type[] pluginHistoryContracts =
        [
            typeof(ICommandHistoryPolicy),
            typeof(IHistoryCommandFactory),
            typeof(CommandHistoryPolicyRegistration),
            typeof(CommandHistoryPreparationResult),
        ];

        Assert.All(pluginHistoryContracts, type =>
            Assert.Same(typeof(ICommand).Assembly, type.Assembly));

        var signatureTypes = pluginHistoryContracts.SelectMany(GetPublicSignatureTypes).ToArray();
        Assert.DoesNotContain(signatureTypes, type =>
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Runtime", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Canvas2D", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Bpmn", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Blazor", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void EditorStateContractsHaveExactImmutableShapes()
    {
        AssertImmutableShape(
            typeof(ViewportSnapshot),
            new Dictionary<string, Type>
            {
                [nameof(ViewportSnapshot.Default)] = typeof(ViewportSnapshot),
                [nameof(ViewportSnapshot.Zoom)] = typeof(double),
                [nameof(ViewportSnapshot.Pan)] = typeof(VectorD),
                [nameof(ViewportSnapshot.VisibleDocumentRegion)] = typeof(RectD?),
            });
        AssertImmutableShape(
            typeof(EditorGestureSnapshot),
            new Dictionary<string, Type>
            {
                [nameof(EditorGestureSnapshot.Id)] = typeof(string),
                [nameof(EditorGestureSnapshot.Kind)] = typeof(string),
                [nameof(EditorGestureSnapshot.Origin)] = typeof(PointD),
                [nameof(EditorGestureSnapshot.Current)] = typeof(PointD),
                [nameof(EditorGestureSnapshot.Properties)] = typeof(PropertyMap),
            });
        AssertImmutableShape(
            typeof(EditorFeedbackSnapshot),
            new Dictionary<string, Type>
            {
                [nameof(EditorFeedbackSnapshot.Id)] = typeof(string),
                [nameof(EditorFeedbackSnapshot.Kind)] = typeof(string),
                [nameof(EditorFeedbackSnapshot.Bounds)] = typeof(RectD?),
                [nameof(EditorFeedbackSnapshot.Points)] = typeof(ImmutableArray<PointD>),
                [nameof(EditorFeedbackSnapshot.Properties)] = typeof(PropertyMap),
                [nameof(EditorFeedbackSnapshot.PresentationMode)] =
                    typeof(EditorFeedbackPresentationMode),
            });
        AssertImmutableShape(
            typeof(EditorStateSnapshot),
            new Dictionary<string, Type>
            {
                [nameof(EditorStateSnapshot.Empty)] = typeof(EditorStateSnapshot),
                [nameof(EditorStateSnapshot.Selection)] = typeof(ImmutableArray<VisualStateId>),
                [nameof(EditorStateSnapshot.SemanticSceneSelection)] =
                    typeof(SemanticElementId),
                [nameof(EditorStateSnapshot.HoveredObjectId)] = typeof(SceneObjectId),
                [nameof(EditorStateSnapshot.ActiveToolId)] = typeof(string),
                [nameof(EditorStateSnapshot.FocusTargetId)] = typeof(string),
                [nameof(EditorStateSnapshot.Viewport)] = typeof(ViewportSnapshot),
                [nameof(EditorStateSnapshot.ActiveGesture)] = typeof(EditorGestureSnapshot),
                [nameof(EditorStateSnapshot.TemporaryFeedback)] = typeof(ImmutableArray<EditorFeedbackSnapshot>),
                [nameof(EditorStateSnapshot.ToolState)] = typeof(PropertyMap),
            });

        AssertSingleConstructor(
            typeof(ViewportSnapshot),
            typeof(double),
            typeof(VectorD),
            typeof(RectD?));
        AssertSingleConstructor(
            typeof(EditorGestureSnapshot),
            typeof(string),
            typeof(string),
            typeof(PointD),
            typeof(PointD),
            typeof(IEnumerable<KeyValuePair<string, PropertyValue>>));
        AssertSingleConstructor(
            typeof(EditorFeedbackSnapshot),
            typeof(string),
            typeof(string),
            typeof(RectD?),
            typeof(IEnumerable<PointD>),
            typeof(IEnumerable<KeyValuePair<string, PropertyValue>>),
            typeof(EditorFeedbackPresentationMode));
        AssertSingleConstructor(
            typeof(EditorStateSnapshot),
            typeof(IEnumerable<VisualStateId>),
            typeof(SceneObjectId),
            typeof(string),
            typeof(string),
            typeof(ViewportSnapshot),
            typeof(EditorGestureSnapshot),
            typeof(IEnumerable<EditorFeedbackSnapshot>),
            typeof(IEnumerable<KeyValuePair<string, PropertyValue>>),
            typeof(SemanticElementId));

        Type[] editorStateTypes =
        [
            typeof(ViewportSnapshot),
            typeof(EditorGestureSnapshot),
            typeof(EditorFeedbackSnapshot),
            typeof(EditorStateSnapshot),
        ];
        Assert.All(editorStateTypes, AssertEqualityOnlyMethods);
        var signatureTypes = editorStateTypes.SelectMany(GetPublicSignatureTypes).ToArray();
        Assert.DoesNotContain(signatureTypes, IsMutableCollectionType);
        Assert.DoesNotContain(signatureTypes, type =>
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Contracts.History", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Runtime", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Canvas2D", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Inceptus.DocumentEngine.Bpmn", StringComparison.Ordinal) == true ||
            type.Namespace?.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void EditorStateIsIndependentOfSceneObjectsAndRuntimeSessionStatus()
    {
        Type[] editorStateTypes =
        [
            typeof(ViewportSnapshot),
            typeof(EditorGestureSnapshot),
            typeof(EditorFeedbackSnapshot),
            typeof(EditorStateSnapshot),
            typeof(EditorStateStore),
        ];
        var signatureTypes = editorStateTypes.SelectMany(GetPublicSignatureTypes).ToArray();

        Assert.DoesNotContain(signatureTypes, type =>
            type != typeof(SceneObjectId) &&
            type.Name.Contains("Scene", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(signatureTypes, type =>
            ContainsAny(type.FullName ?? type.Name, "RuntimeFaulted", "Ready", "EditingSessionStatus"));
        Assert.DoesNotContain(
            editorStateTypes.SelectMany(type => type.GetMembers(DeclaredPublicMembers)),
            member => ContainsAny(member.Name, "RuntimeFaulted", "Ready") ||
                      (member.Name.Contains("Scene", StringComparison.OrdinalIgnoreCase) &&
                       !member.Name.EndsWith(
                           nameof(EditorStateSnapshot.SemanticSceneSelection),
                           StringComparison.Ordinal)));
    }

    private static void AssertHistoryOperationShape(MethodInfo method)
    {
        Assert.Equal(typeof(ValueTask<HistoryOperationResult>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(CommandProcessor), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
    }

    private static void AssertImmutableShape(Type type, IReadOnlyDictionary<string, Type> expectedProperties)
    {
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetFields(DeclaredPublicMembers));
        Assert.Empty(type.GetEvents(DeclaredPublicMembers));

        var properties = type.GetProperties(DeclaredPublicMembers);
        Assert.Equal(
            expectedProperties.Keys.Order(StringComparer.Ordinal),
            properties.Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, property =>
        {
            Assert.Null(property.SetMethod);
            Assert.Equal(expectedProperties[property.Name], property.PropertyType);
        });
    }

    private static void AssertSingleConstructor(Type type, params Type[] parameterTypes)
    {
        var constructor = Assert.Single(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Equal(parameterTypes, constructor.GetParameters().Select(parameter => parameter.ParameterType));
    }

    private static void AssertEqualityOnlyMethods(Type type)
    {
        var methods = type.GetMethods(DeclaredPublicMembers)
            .Where(method => !method.IsSpecialName)
            .Select(method =>
                $"{method.Name}({string.Join(',', method.GetParameters().Select(parameter => parameter.ParameterType.FullName))})")
            .Order(StringComparer.Ordinal);
        var typedEquality = $"Equals({type.FullName})";

        Assert.Equal(
            [typedEquality, "Equals(System.Object)", "GetHashCode()"],
            methods);
    }

    private static bool IsForbiddenUndoRedoDependency(Type type)
    {
        var fullName = type.FullName ?? type.Name;
        return ContainsAny(
            fullName,
            "Scene",
            "Renderer",
            "Canvas",
            "Browser",
            "Bpmn",
            "Json",
            "Serialization",
            "DocumentStateReplacement");
    }

    private static bool IsEditorStateOrHistoryType(Type type) =>
        type.Namespace?.Contains(".EditorState", StringComparison.Ordinal) == true ||
        type.Namespace?.Contains(".History", StringComparison.Ordinal) == true ||
        ContainsAny(type.Name, "EditorState", "HistoryEntry", "HistoryStore");

    private static bool IsMutableCollectionType(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(List<>) ||
               definition == typeof(Dictionary<,>) ||
               definition == typeof(IList<>) ||
               definition == typeof(ICollection<>) ||
               definition == typeof(IDictionary<,>);
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Type> GetPublicSignatureTypes(Type type)
    {
        yield return type;

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var signatureType in GetSignatureTypes(constructor))
            {
                yield return signatureType;
            }
        }

        foreach (var property in type.GetProperties(DeclaredPublicMembers))
        {
            foreach (var expanded in ExpandType(property.PropertyType))
            {
                yield return expanded;
            }
        }

        foreach (var method in type.GetMethods(DeclaredPublicMembers))
        {
            foreach (var signatureType in GetSignatureTypes(method))
            {
                yield return signatureType;
            }
        }
    }

    private static IEnumerable<Type> GetSignatureTypes(MethodBase method)
    {
        if (method is MethodInfo methodInfo)
        {
            foreach (var expanded in ExpandType(methodInfo.ReturnType))
            {
                yield return expanded;
            }
        }

        foreach (var parameter in method.GetParameters())
        {
            foreach (var expanded in ExpandType(parameter.ParameterType))
            {
                yield return expanded;
            }
        }
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
