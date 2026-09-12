using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Inceptus.DocumentEngine.Blazor.Demo;
using Inceptus.DocumentEngine.Bpmn.Publishing;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.EditingSession;
using Inceptus.DocumentEngine.Canvas2D.Publishing;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Canvas2D;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Diagnostics;
using Inceptus.DocumentEngine.Contracts.Documents;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Profiles;
using Inceptus.DocumentEngine.Contracts.Properties;
using Inceptus.DocumentEngine.Contracts.Publishing;
using Inceptus.DocumentEngine.Contracts.Semantics;
using Inceptus.DocumentEngine.Contracts.Text;
using Inceptus.DocumentEngine.Organizational.Profiles;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.IntegrationTests;

public sealed class PhaseN107PublishedDescriptionIntegrationTests
{
    private const string BootstrapPrefix =
        "globalThis.__INCEPTUS_PUBLISHED_PROCESS__ = ";
    private const string PublicationDescription =
        "Description of the whole published process.";
    private const string TaskDescription =
        "Operator kompletuje produkty zgodnie z listą kompletacyjną.";
    private const string HostileDescription =
        "</script><script>alert(1)</script> \"quoted\" \\\nDruga linia";

    [Fact]
    public async Task BpmnDescriptionsPublishBySemanticIdentityAndBootstrapMatchesJson()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        await using var renderer = CreateRenderer();
        Assert.True((await renderer.InitializeAsync(
            "phase-n107-data",
            new Canvas2DSurfaceSize(1400d, 900d, 1d))).Succeeded);
        var attached = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        await SavePublicationAsync(session, composition.Document);
        const string sameName = "Kompletacja zamówienia";
        await UpdateTextAsync(
            session,
            composition.Document,
            BpmnDemoPipeline.TaskId,
            BpmnSemanticProperties.Name,
            sameName);
        await UpdateTextAsync(
            session,
            composition.Document,
            BpmnDemoPipeline.ApprovedTaskId,
            BpmnSemanticProperties.Name,
            sameName);
        await UpdateTextAsync(
            session,
            composition.Document,
            BpmnDemoPipeline.TaskId,
            BpmnSemanticProperties.Description,
            TaskDescription);
        await UpdateTextAsync(
            session,
            composition.Document,
            BpmnDemoPipeline.ApprovedTaskId,
            BpmnSemanticProperties.Description,
            HostileDescription);
        await UpdateTextAsync(
            session,
            composition.Document,
            BpmnDemoPipeline.RejectedTaskId,
            BpmnSemanticProperties.Description,
            string.Empty);

        var recordingMapper = new RecordingPublishedNodeDataMapper();
        var package = await PublishAsync(session, recordingMapper);
        var repeated = await PublishAsync(session, new BpmnPublishedNodeDataMapper());

        Assert.Equal(PublishedProcessPackageBuilder.Format, package.Snapshot.Format);
        Assert.Equal(1, package.Snapshot.FormatVersion);
        Assert.True(package.ProcessJson.AsSpan().SequenceEqual(repeated.ProcessJson.AsSpan()));
        Assert.True(package.Archive.AsSpan().SequenceEqual(repeated.Archive.AsSpan()));
        Assert.Equal(
            package.Snapshot.Presentation.Nodes.Select(static node => node.Id),
            recordingMapper.SemanticElementIds);
        Assert.Equal(
            recordingMapper.SemanticElementIds.Count,
            recordingMapper.SemanticElementIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(PublicationDescription, package.Snapshot.Publication.Description);
        var task = Node(package, BpmnDemoPipeline.TaskId.Value);
        var approved = Node(package, BpmnDemoPipeline.ApprovedTaskId.Value);
        var rejected = Node(package, BpmnDemoPipeline.RejectedTaskId.Value);
        Assert.Equal(sameName, task.Label);
        Assert.Equal(sameName, approved.Label);
        Assert.Equal(TaskDescription, task.Description);
        Assert.Equal(HostileDescription, approved.Description);
        Assert.Equal(string.Empty, rejected.Description);
        Assert.Equal(
            "Start both parallel branches.",
            Node(package, BpmnDemoPipeline.ParallelSplitGatewayId.Value).Description);
        Assert.Equal(
            "Open this SubProcess to edit its seeded child process.",
            Node(package, BpmnDemoPipeline.ProcessOrderSubProcessId.Value).Description);
        Assert.Equal(
            "A descriptive message intermediate catch event.",
            Node(package, BpmnDemoPipeline.MessageCatchEventId.Value).Description);
        Assert.Equal(
            "Continue after the modeled message catch event.",
            Node(package, BpmnDemoPipeline.ProcessMessageTaskId.Value).Description);
        Assert.Equal(
            string.Empty,
            Node(package, BpmnDemoPipeline.StartEventId.Value).Description);

        var processJson = Encoding.UTF8.GetString(package.ProcessJson.AsSpan());
        var processData = ArchiveText(package.Archive, "process.data.js");
        Assert.StartsWith(BootstrapPrefix, processData, StringComparison.Ordinal);
        Assert.EndsWith(";\n", processData, StringComparison.Ordinal);
        var bootstrappedJson = processData[BootstrapPrefix.Length..^2];
        Assert.Equal(processJson, bootstrappedJson);
        using var json = JsonDocument.Parse(processJson);
        using var bootstrap = JsonDocument.Parse(bootstrappedJson);
        Assert.True(JsonElement.DeepEquals(json.RootElement, bootstrap.RootElement));
        var jsonNodes = json.RootElement.GetProperty("presentation")
            .GetProperty("nodes")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(package.Snapshot.Presentation.Nodes.Length, jsonNodes.Length);
        Assert.All(jsonNodes, node =>
        {
            Assert.True(node.TryGetProperty("description", out var description));
            Assert.Equal(JsonValueKind.String, description.ValueKind);
        });
        Assert.Equal(
            TaskDescription,
            JsonNode(jsonNodes, BpmnDemoPipeline.TaskId.Value)
                .GetProperty("description")
                .GetString());
        Assert.Equal(
            HostileDescription,
            JsonNode(jsonNodes, BpmnDemoPipeline.ApprovedTaskId.Value)
                .GetProperty("description")
                .GetString());
        Assert.DoesNotContain("</script>", processData, StringComparison.OrdinalIgnoreCase);

        var runtimeJavaScript = ArchiveText(package.Archive, "inceptus.publish.js");
        Assert.DoesNotContain("eval(", runtimeJavaScript, StringComparison.Ordinal);
        Assert.DoesNotContain("new Function", runtimeJavaScript, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", runtimeJavaScript, StringComparison.Ordinal);

        var native = NativeDocumentSerializer.Export(composition.Document);
        var imported = NativeDocumentSerializer.Import(
            native.AsMemory(),
            composition.Configuration.ConnectorAnchorPolicyProvider);
        Assert.True(imported.Succeeded, Diagnostics(imported.Diagnostics));
        var importedTask = imported.Document!.SemanticModel.Elements.Single(element =>
            element.Id == BpmnDemoPipeline.TaskId);
        var importedApproved = imported.Document.SemanticModel.Elements.Single(element =>
            element.Id == BpmnDemoPipeline.ApprovedTaskId);
        Assert.Equal(
            TaskDescription,
            importedTask.Properties[BpmnSemanticProperties.Description].TextValue);
        Assert.Equal(
            HostileDescription,
            importedApproved.Properties[BpmnSemanticProperties.Description].TextValue);
    }

    [Fact]
    public async Task DescriptionOnlyEditChangesOnlyDescriptionAndPreservesPresentation()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        await using var renderer = CreateRenderer();
        Assert.True((await renderer.InitializeAsync(
            "phase-n107-stability",
            new Canvas2DSurfaceSize(1400d, 900d, 1d))).Succeeded);
        var attached = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        await SavePublicationAsync(session, composition.Document);
        var before = await PublishAsync(session, new BpmnPublishedNodeDataMapper());
        var stateBeforeEdit = session.CaptureState();
        const string changed =
            "Operator kompletuje zamówienie zgodnie z listą produktów.";

        await UpdateTextAsync(
            session,
            composition.Document,
            BpmnDemoPipeline.TaskId,
            BpmnSemanticProperties.Description,
            changed);
        var stateAfterEdit = session.CaptureState();
        var after = await PublishAsync(session, new BpmnPublishedNodeDataMapper());
        var stateAfterPublish = session.CaptureState();

        Assert.Equal(stateBeforeEdit.DocumentRevision.Increment(), stateAfterEdit.DocumentRevision);
        Assert.Equal(
            stateBeforeEdit.HistoryStatus.EntryCount + 1,
            stateAfterEdit.HistoryStatus.EntryCount);
        Assert.Equal(stateAfterEdit.DocumentRevision, stateAfterPublish.DocumentRevision);
        Assert.Equal(stateAfterEdit.HistoryStatus, stateAfterPublish.HistoryStatus);
        Assert.Equal(before.Snapshot.Publication, after.Snapshot.Publication);
        Assert.Equal(before.Snapshot.Runtime, after.Snapshot.Runtime);
        Assert.Equal(before.Snapshot.Source.DocumentId, after.Snapshot.Source.DocumentId);
        Assert.Equal(before.Snapshot.Source.ScopeId, after.Snapshot.Source.ScopeId);
        Assert.Equal(
            before.Snapshot.Source.Revision + 1,
            after.Snapshot.Source.Revision);
        Assert.Equal(
            "Review the incoming customer order.\nCheck completeness before approval.",
            Node(before, BpmnDemoPipeline.TaskId.Value).Description);
        Assert.Equal(changed, Node(after, BpmnDemoPipeline.TaskId.Value).Description);

        Assert.Equal(
            before.Snapshot.Presentation.ContentBounds,
            after.Snapshot.Presentation.ContentBounds);
        Assert.Equal(
            before.Snapshot.Presentation.Nodes.Length,
            after.Snapshot.Presentation.Nodes.Length);
        foreach (var expected in before.Snapshot.Presentation.Nodes)
        {
            var actual = Node(after, expected.Id);
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Descriptor, actual.Descriptor);
            Assert.Equal(expected.Bounds, actual.Bounds);
            Assert.Equal(expected.Label, actual.Label);
            if (!StringComparer.Ordinal.Equals(expected.Id, BpmnDemoPipeline.TaskId.Value))
            {
                Assert.Equal(expected.Description, actual.Description);
            }
        }

        AssertConnectorsEqual(
            before.Snapshot.Presentation.Connectors,
            after.Snapshot.Presentation.Connectors);
        AssertPresentationItemsEqual(
            before.Snapshot.Presentation.Items,
            after.Snapshot.Presentation.Items);
        AssertTokenGraphEqual(before.Snapshot.TokenGraph.Nodes, after.Snapshot.TokenGraph.Nodes);
    }

    [Fact]
    public async Task BackwardCompatibleDefaultMapperPublishesExplicitEmptyDescriptions()
    {
        var composition = await BpmnModelerTestComposition.DemoFactory.CreateAsync();
        await using var renderer = CreateRenderer();
        Assert.True((await renderer.InitializeAsync(
            "phase-n107-default",
            new Canvas2DSurfaceSize(1400d, 900d, 1d))).Succeeded);
        var attached = await EditingSession.AttachAsync(
            composition.Document,
            renderer,
            composition.Configuration);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        await SavePublicationAsync(session, composition.Document);

        var package = await PublishAsync(session, mapper: null);

        Assert.Equal(1, package.Snapshot.FormatVersion);
        Assert.All(package.Snapshot.Presentation.Nodes, node =>
            Assert.Equal(string.Empty, node.Description));
        using var json = JsonDocument.Parse(package.ProcessJson.AsMemory());
        Assert.All(
            json.RootElement.GetProperty("presentation").GetProperty("nodes")
                .EnumerateArray(),
            node => Assert.Equal(
                string.Empty,
                node.GetProperty("description").GetString()));
    }

    private static async Task SavePublicationAsync(
        EditingSession session,
        Document document)
    {
        var result = await session.ExecuteAsync(new UpdateDocumentPublicationCommand(
            document.DocumentId,
            document.Revision,
            "phase-n107",
            "Published element descriptions",
            PublicationDescription));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync();
    }

    private static async Task UpdateTextAsync(
        EditingSession session,
        Document document,
        SemanticElementId elementId,
        string key,
        string value)
    {
        var result = await session.ExecuteAsync(new UpdateSemanticElementPropertyCommand(
            document.DocumentId,
            document.Revision,
            elementId,
            key,
            PropertyValue.FromText(value)));
        Assert.True(result.IsCommitted, Diagnostics(result.Diagnostics));
        await session.WaitForIdleAsync();
    }

    private static async Task<PublishedProcessPackage> PublishAsync(
        EditingSession session,
        IPublishedNodeDataMapper? mapper)
    {
        var state = session.CaptureState();
        var publishProfiles = state.ModelProfileViewState.WithPreferredVisibility(
            OrganizationalModelProfile.Id,
            isVisible: false);
        var publishElements = new ModelProfileElementViewStateSnapshot(
            state.ModelProfileElementViewState.CollapsedElements.Where(entry =>
                entry.ProfileId != OrganizationalModelProfile.Id));
        var capture = await session.CapturePresentationAsync(publishProfiles, publishElements);
        Assert.True(capture.Succeeded, Diagnostics(capture.Diagnostics));
        var builder = mapper is null
            ? new PublishedProcessPackageBuilder(new BpmnPublishedTokenRoleClassifier())
            : new PublishedProcessPackageBuilder(
                new BpmnPublishedTokenRoleClassifier(),
                mapper);
        var built = builder.Build(capture.Capture!);
        Assert.True(built.Succeeded, Diagnostics(built.Diagnostics));
        return Assert.IsType<PublishedProcessPackage>(built.Package);
    }

    private static PublishedPresentationNode Node(
        PublishedProcessPackage package,
        string id) =>
        package.Snapshot.Presentation.Nodes.Single(node =>
            StringComparer.Ordinal.Equals(node.Id, id));

    private static JsonElement JsonNode(JsonElement[] nodes, string id) =>
        nodes.Single(node => StringComparer.Ordinal.Equals(
            node.GetProperty("id").GetString(),
            id));

    private static void AssertConnectorsEqual(
        ImmutableArray<PublishedPresentationConnector> expected,
        ImmutableArray<PublishedPresentationConnector> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Id, actual[index].Id);
            Assert.Equal(expected[index].SourceElementId, actual[index].SourceElementId);
            Assert.Equal(expected[index].TargetElementId, actual[index].TargetElementId);
            Assert.Equal(expected[index].Points.ToArray(), actual[index].Points.ToArray());
        }
    }

    private static void AssertPresentationItemsEqual(
        ImmutableArray<PublishedPresentationItem> expected,
        ImmutableArray<PublishedPresentationItem> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Id, actual[index].Id);
            Assert.Equal(expected[index].Layer, actual[index].Layer);
            Assert.Equal(expected[index].ZIndex, actual[index].ZIndex);
            Assert.Equal(expected[index].GeometryKind, actual[index].GeometryKind);
            Assert.Equal(expected[index].GeometryBounds, actual[index].GeometryBounds);
            Assert.Equal(expected[index].Points.ToArray(), actual[index].Points.ToArray());
            Assert.Equal(expected[index].Content, actual[index].Content);
            Assert.Equal(expected[index].IsClosed, actual[index].IsClosed);
            Assert.Equal(expected[index].TextAnchor, actual[index].TextAnchor);
            Assert.Equal(expected[index].TextAlignment, actual[index].TextAlignment);
            Assert.Equal(expected[index].TextBaseline, actual[index].TextBaseline);
            Assert.Equal(expected[index].Transform, actual[index].Transform);
            Assert.Equal(expected[index].Clip, actual[index].Clip);
            Assert.Equal(expected[index].Fill, actual[index].Fill);
            Assert.Equal(expected[index].Stroke, actual[index].Stroke);
            Assert.Equal(expected[index].StrokeWidth, actual[index].StrokeWidth);
            Assert.Equal(
                expected[index].DashPattern.ToArray(),
                actual[index].DashPattern.ToArray());
            Assert.Equal(expected[index].Opacity, actual[index].Opacity);
            Assert.Equal(expected[index].FontFamily, actual[index].FontFamily);
            Assert.Equal(expected[index].FontSize, actual[index].FontSize);
        }
    }

    private static void AssertTokenGraphEqual(
        ImmutableArray<PublishedTokenNode> expected,
        ImmutableArray<PublishedTokenNode> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Equal(expected[index].Id, actual[index].Id);
            Assert.Equal(expected[index].Role, actual[index].Role);
            Assert.Equal(
                expected[index].IncomingConnectorIds.ToArray(),
                actual[index].IncomingConnectorIds.ToArray());
            Assert.Equal(
                expected[index].OutgoingConnectorIds.ToArray(),
                actual[index].OutgoingConnectorIds.ToArray());
        }
    }

    private static string ArchiveText(ImmutableArray<byte> payload, string name)
    {
        using var input = new MemoryStream(payload.ToArray(), writable: false);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException(
            $"Archive entry '{name}' was not found.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static Canvas2DRenderer CreateRenderer() =>
        new(
            new PhaseM31BpmnPropertiesIntegrationTests.RecordingRenderExecution(),
            new Canvas2DRendererConfiguration(
                fontResources:
                [
                    new Canvas2DFontResource(
                        "org.dejavu.DejaVuSans",
                        "2.37",
                        "DejaVu Sans",
                        "fonts/DejaVuSans-2.37.ttf",
                        400,
                        TextFontStyle.Normal),
                ],
                defaultFontFamily: "DejaVu Sans"));

    private static string Diagnostics(
        IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
            $"{diagnostic.Code}: {diagnostic.Message}"));

    private sealed class RecordingPublishedNodeDataMapper : IPublishedNodeDataMapper
    {
        private readonly BpmnPublishedNodeDataMapper _inner = new();

        internal List<string> SemanticElementIds { get; } = [];

        public PublishedNodeData Map(SemanticElementSnapshot semanticElement)
        {
            SemanticElementIds.Add(semanticElement.Id.Value);
            return _inner.Map(semanticElement);
        }
    }
}
