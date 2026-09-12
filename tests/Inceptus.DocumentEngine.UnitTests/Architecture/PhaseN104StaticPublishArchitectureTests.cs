namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseN104StaticPublishArchitectureTests
{
    [Fact]
    public void PublishControlUsesCompactNativeDownloadBoundaryAndTransientState()
    {
        var component = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Components",
            "DocumentCanvas.razor");

        Assert.Contains("id=\"@DomId(\"publish\")\"", component,
            StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Publish\"", component, StringComparison.Ordinal);
        Assert.Contains("title=\"Publish\"", component, StringComparison.Ordinal);
        Assert.Contains("published-process.zip", component, StringComparison.Ordinal);
        Assert.Contains("application/zip", component, StringComparison.Ordinal);
        Assert.Contains("BrowserFileDownload.DownloadAsync(", component, StringComparison.Ordinal);
        Assert.Contains("_publishInProgress", component, StringComparison.Ordinal);
        Assert.Contains("_publishOperationErrorMessage", component, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishedFormatAndRendererStayNotationNeutralAndStaticBrowserOnly()
    {
        var builder = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Publishing",
            "PublishedProcessPackageBuilder.cs");
        var format = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Publishing",
            "PublishedProcessSnapshot.cs");
        var javascript = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Publishing",
            "Assets",
            "inceptus.publish.js");
        var html = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Publishing",
            "Assets",
            "index.html");

        Assert.Contains("Inceptus.PublishedProcess", builder, StringComparison.Ordinal);
        Assert.Contains("FormatVersion = 1", builder, StringComparison.Ordinal);
        Assert.Contains("index.html", builder, StringComparison.Ordinal);
        Assert.Contains("process.json", builder, StringComparison.Ordinal);
        Assert.Contains("process.data.js", builder, StringComparison.Ordinal);
        Assert.Contains("inceptus.publish.js", builder, StringComparison.Ordinal);
        Assert.Contains("styles.css", builder, StringComparison.Ordinal);
        Assert.Contains("CompressionLevel.NoCompression", builder, StringComparison.Ordinal);
        Assert.Contains("ArchiveTimestamp", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", builder + format + javascript, StringComparison.Ordinal);
        Assert.DoesNotContain("Organizational", builder + format + javascript,
            StringComparison.Ordinal);
        Assert.Contains("CreateDataBootstrap(processJson)", builder, StringComparison.Ordinal);
        Assert.Contains("JavaScriptEncoder.Default", builder, StringComparison.Ordinal);
        Assert.Contains("globalThis.__INCEPTUS_PUBLISHED_PROCESS__", builder,
            StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", javascript, StringComparison.Ordinal);
        Assert.Contains("requestAnimationFrame", javascript, StringComparison.Ordinal);
        Assert.Contains("fillText", javascript, StringComparison.Ordinal);
        Assert.Contains("<script src=\"./process.data.js\"></script>", html,
            StringComparison.Ordinal);
        Assert.Contains("<script src=\"./inceptus.publish.js\"></script>", html,
            StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"module\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            html.IndexOf("./process.data.js", StringComparison.Ordinal) <
            html.IndexOf("./inceptus.publish.js", StringComparison.Ordinal));

        var staticRuntime = javascript + html;
        string[] forbidden =
        [
            "Blazor",
            "WebAssembly",
            "_framework",
            ".dll",
            ".wasm",
            "EditingSession",
            "Projection",
            "LayoutEngine",
            "RoutingEngine",
            "SignalR",
            "fetch(",
            "XMLHttpRequest",
            "import(",
            "WebSocket",
            "EventSource",
            "localStorage",
            "document.cookie",
            "innerHTML",
            "eval(",
            "new Function",
            "http://",
            "https://",
        ];
        Assert.All(forbidden, token => Assert.DoesNotContain(
            token,
            staticRuntime,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublishCaptureIsOneReadOnlyCurrentTupleAndOrganizationalPolicyLivesAtHost()
    {
        var capture = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "EditingSession",
            "EditingSession.Publishing.cs");
        var host = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn.Blazor",
            "Presentation",
            "DocumentCanvasHost.Publishing.cs");
        var policy = ReadProductionFile(
            "Inceptus.DocumentEngine.Bpmn",
            "Publishing",
            "BpmnPublishedTokenRoleClassifier.cs");
        var contract = ReadProductionFile(
            "Inceptus.DocumentEngine.Contracts",
            "Publishing",
            "PublishedTokenRole.cs");

        Assert.Contains("_commandGate.WaitAsync", capture, StringComparison.Ordinal);
        Assert.Contains("artifacts.IsCompatibleWith", capture, StringComparison.Ordinal);
        Assert.Contains("currentScene.SourceRevision", capture, StringComparison.Ordinal);
        Assert.Contains("RebuildSceneAsync", capture, StringComparison.Ordinal);
        Assert.Contains("EditorStateSnapshot.Empty", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("session.ExecuteAsync(", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateModelProfileViewStateAsync", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateModelProfileElementViewStateAsync", capture,
            StringComparison.Ordinal);

        Assert.Contains("WithPreferredVisibility", host, StringComparison.Ordinal);
        Assert.Contains("OrganizationalModelProfile.Id", host, StringComparison.Ordinal);
        Assert.Contains("entry.ProfileId != OrganizationalModelProfile.Id", host,
            StringComparison.Ordinal);
        Assert.Contains("BpmnPublishedTokenRoleClassifier", host, StringComparison.Ordinal);
        Assert.Contains("BpmnSemanticTypes", policy, StringComparison.Ordinal);
        Assert.Contains("BpmnSemanticTypes.ParallelGateway", policy, StringComparison.Ordinal);
        Assert.Contains("PublishedTokenRole.ParallelSynchronize", policy,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Bpmn", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("Organizational", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewerInteractionSurfaceCannotEditPublishedGeometry()
    {
        var javascript = ReadProductionFile(
            "Inceptus.DocumentEngine.Canvas2D",
            "Publishing",
            "Assets",
            "inceptus.publish.js");

        Assert.Contains("this.viewport.pan(", javascript, StringComparison.Ordinal);
        Assert.Contains("this.viewport.zoomAt(", javascript, StringComparison.Ordinal);
        Assert.Contains("case \"parallelSynchronize\"", javascript,
            StringComparison.Ordinal);
        Assert.Contains("this.#waitAtParallel(", javascript, StringComparison.Ordinal);
        Assert.Contains("node.incomingConnectorIds.every", javascript,
            StringComparison.Ordinal);
        Assert.Contains("-deltaX * horizontalMultiplier", javascript,
            StringComparison.Ordinal);
        Assert.Contains("-deltaY * verticalMultiplier", javascript,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Math.exp(-event.deltaY", javascript,
            StringComparison.Ordinal);
        Assert.DoesNotContain("BPMN", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("this.runtime.start()", javascript, StringComparison.Ordinal);
        Assert.DoesNotContain("node.position =", javascript, StringComparison.Ordinal);
        Assert.DoesNotContain("node.bounds =", javascript, StringComparison.Ordinal);
        Assert.DoesNotContain("connector.points =", javascript, StringComparison.Ordinal);
        Assert.DoesNotContain("contextmenu", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("undo", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("redo", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("properties", javascript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("toolbox", javascript, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadProductionFile(params string[] path) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, "src", .. path]));

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
