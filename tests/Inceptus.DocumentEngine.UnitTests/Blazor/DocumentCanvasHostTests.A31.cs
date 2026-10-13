using System.Collections.Immutable;
using System.Text;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Bpmn.Semantics;
using Inceptus.DocumentEngine.Canvas2D.Rendering;
using Inceptus.DocumentEngine.Contracts.Commands;
using Inceptus.DocumentEngine.Contracts.Primitives;
using Inceptus.DocumentEngine.Contracts.Validation;
using Inceptus.DocumentEngine.UnitTests.Bpmn;

namespace Inceptus.DocumentEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A31TransfersDoNotValidateAndExplicitValidationStillReportsFindings(bool standard)
    {
        await using var host = CreateHost(new RecordingRenderExecution(),
            new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync(),
            replacementRendererFactory: () => CreateRenderer(new RecordingRenderExecution()));
        await host.InitializeAsync("a31-active", "a31-standby", "a31-container");
        await using var measurement = new Canvas2DRenderer(new RecordingRenderExecution(), DemoBpmnImportPreparation.RendererConfiguration);
        Assert.True((await measurement.InitializeAsync("a31-measurement", new(1, 1, 1))).Succeeded);
        var preparer = DemoBpmnImportPreparation.Create(measurement);
        var source = BpmnStandardConverter.Import(BpmnStandardConverterTests.Xml(A31Diagram));
        BpmnStandardConverterTests.Success(source);
        Assert.True((await host.SetDocumentAsync(source.Document)).Succeeded);
        var session = Session(host);
        var initial = host.GetDocument().Snapshot!;
        Assert.True((await session.ExecuteAsync(new UpdateSemanticElementNameCommand(initial.DocumentId, initial.Revision,
            new SemanticElementId("task"), BpmnSemanticProperties.Name, "Edited with validation findings"))).IsCommitted);
        await session.WaitForIdleAsync();
        var document = host.GetDocument().Snapshot!;
        var nativeBytes = BpmnDocumentSerializer.Serialize(document);
        var history = session.CaptureState().HistoryStatus;
        var save = host.NativeSaveStatus!;
        var rule = new A31CountingValidationRule();
        var operations = new DemoDocumentOperations(host, new BpmnDocumentValidator(new ModelValidationCatalog([rule])));

        // Neither export nor import may execute even an advisory validation contribution.
        var exported = standard ? await operations.ExportStandardDocumentAsync() : await operations.ExportNativeDocumentAsync();
        Assert.True(exported.Succeeded, string.Join("; ", exported.Diagnostics.Select(d => d.Message)));
        Assert.Equal(0, rule.Calls);
        Assert.Null(operations.DocumentValidation);
        Assert.Null(operations.ValidationSnapshot);
        Assert.Same(document, host.GetDocument().Snapshot);
        Assert.Equal(history, session.CaptureState().HistoryStatus);
        Assert.Equal(save.SavedCheckpoint, host.NativeSaveStatus!.SavedCheckpoint);
        Assert.Equal(save.IsDirty, host.NativeSaveStatus.IsDirty);
        if (standard)
        {
            Assert.Equal("document.xml", exported.Artifact!.FileName);
            Assert.Equal("application/xml;charset=utf-8", exported.Artifact.ContentType);
            Assert.Null(exported.SaveCheckpoint);
            Assert.NotEmpty(operations.ConversionLog);
            Assert.All(operations.ConversionLog, entry => Assert.Equal(BpmnConversionDisposition.Omitted, entry.Disposition));
        }
        else
        {
            Assert.Equal(nativeBytes.ToArray(), exported.Payload.ToArray());
            Assert.Equal(document, BpmnDocumentSerializer.Deserialize(exported.Payload.AsMemory()).Document);
            Assert.NotNull(exported.SaveCheckpoint);
        }

        var imported = standard
            ? await operations.ImportStandardDocumentAsync(Encoding.UTF8.GetString(exported.Payload.AsSpan()), preparer)
            : await operations.ImportNativeDocumentAsync(exported.Payload.AsMemory());
        Assert.True(imported.Succeeded, string.Join("; ", imported.Diagnostics.Select(d => d.Message)));
        Assert.Equal(0, rule.Calls);
        Assert.Null(operations.DocumentValidation);
        Assert.Null(operations.ValidationSnapshot);
        Assert.Equal(0, Session(host).CaptureState().HistoryStatus.EntryCount);
        if (!standard) Assert.Equal(nativeBytes.ToArray(), BpmnDocumentSerializer.Serialize(host.GetDocument().Snapshot!).ToArray());

        Assert.NotNull(await operations.ValidateAsync());
        Assert.True(rule.Calls > 0);
        // The real unchanged rules still find both errors and warnings, after successful transfer.
        var explicitOperations = new DemoDocumentOperations(host);
        var report = await explicitOperations.ValidateAsync();
        Assert.NotNull(report);
        Assert.Contains(report.Issues, issue => issue.Severity == ModelValidationSeverity.Error);
        Assert.Contains(report.Issues, issue => issue.Severity == ModelValidationSeverity.Warning);
        Assert.Contains(report.Issues, issue => issue.Code == "BPMN_EVENT_BASED_GATEWAY_INVALID_TARGET");
        var validationCodes = report.Issues.Select(issue => issue.Code).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(operations.ConversionLog, entry => validationCodes.Contains(entry.Code));
        Assert.DoesNotContain(imported.Diagnostics.Concat(exported.Diagnostics), diagnostic => validationCodes.Contains(diagnostic.Code));
    }

    [Fact]
    public async Task A31AsyncImportReportsOnlyUnsupportedSourceInformationAsLoss()
    {
        var imported = await BpmnStandardConverter.ImportAsync(BpmnStandardConverterTests.Xml(A31Diagram), BpmnStandardConverterTests.Preparer());
        BpmnStandardConverterTests.Success(imported);
        Assert.True(imported.HasLosses);
        Assert.All(imported.ConversionLog, entry => Assert.Equal(BpmnConversionDisposition.Ignored, entry.Disposition));
        Assert.Contains(imported.ConversionLog, entry => entry.SourceIdentity == "unsupported" && entry.Property!.Contains("dataObject", StringComparison.Ordinal));
        var validation = new BpmnDocumentValidator().Validate(imported.Document!);
        Assert.False(validation.IsValid);
        Assert.True(validation.IsStructurallyValid);
        var validationCodes = validation.Issues.Select(issue => issue.Code).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(imported.ConversionLog, entry => validationCodes.Contains(entry.Code));
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => validationCodes.Contains(diagnostic.Code));
        Assert.DoesNotContain(imported.Diagnostics, diagnostic => diagnostic.Context.GetValueOrDefault("disposition") == "Ignored");
    }

    private const string A31Diagram = """
        <process id="findings">
          <eventBasedGateway id="gateway" name="Wait"/>
          <task id="task" name="Work"/>
          <sequenceFlow id="flow" sourceRef="gateway" targetRef="task"/>
          <dataObject id="unsupported" name="Order data"/>
        </process>
        """;

    private sealed class A31CountingValidationRule : IModelValidationRule
    {
        internal int Calls { get; private set; }
        public ModelValidationRuleId RuleId { get; } = new("test:a31-count-validation");
        public ImmutableArray<ModelValidationIssue> Validate(ModelValidationContext context)
        {
            Calls++;
            return [];
        }
    }
}
