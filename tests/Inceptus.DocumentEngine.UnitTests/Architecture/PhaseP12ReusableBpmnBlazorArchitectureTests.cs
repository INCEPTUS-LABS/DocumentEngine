using System.Reflection;
using System.Xml.Linq;
using Inceptus.DocumentEngine.Bpmn.Blazor;
using Inceptus.DocumentEngine.Bpmn.Blazor.Components;
using Inceptus.DocumentEngine.Runtime.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class PhaseP12ReusableBpmnBlazorArchitectureTests
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    [Fact]
    public void SourceHostStartsWithInvariantDataCultureAndNeutralEnglishUICulture()
    {
        var hostRoot = Path.Combine(RepositoryRoot, "src", "Inceptus.DocumentEngine.Blazor");
        var program = File.ReadAllText(Path.Combine(hostRoot, "Program.cs"));
        var builderIndex = program.IndexOf("WebAssemblyHostBuilder.CreateDefault(args)", StringComparison.Ordinal);
        Assert.True(builderIndex >= 0);
        string[] cultureStatements =
        [
            "var uiCulture = CultureInfo.GetCultureInfo(\"en\");",
            "CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;",
            "CultureInfo.DefaultThreadCurrentUICulture = uiCulture;",
            "CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;",
            "CultureInfo.CurrentUICulture = uiCulture;",
        ];
        var previousIndex = -1;
        foreach (var statement in cultureStatements)
        {
            var index = program.IndexOf(statement, StringComparison.Ordinal);
            Assert.True(index > previousIndex && index < builderIndex,
                $"Source-host culture must be established before host creation: {statement}");
            previousIndex = index;
        }

        Assert.Contains("<html lang=\"en\">", File.ReadAllText(Path.Combine(hostRoot, "wwwroot", "index.html")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddingToolbarAndStatusRespondToWorkspaceWidthNotBrowserWidth()
    {
        var styles = File.ReadAllText(Path.Combine(RepositoryRoot, "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor", "Components", "DocumentCanvas.razor.css"));
        Assert.Contains("container: inceptus-workspace / inline-size;", styles, StringComparison.Ordinal);
        Assert.Contains("@container inceptus-workspace (max-width: 960px)", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("@media (max-width:", styles, StringComparison.Ordinal);
        var toolbarStart = styles.IndexOf(".editor-toolbar {", StringComparison.Ordinal);
        var toolbarEnd = styles.IndexOf('}', toolbarStart);
        var toolbar = styles[toolbarStart..toolbarEnd];
        Assert.Contains("flex-wrap: nowrap;", toolbar, StringComparison.Ordinal);
        Assert.Contains("flex: 0 0 auto;", toolbar, StringComparison.Ordinal);
        var commandsStart = styles.IndexOf(".editor-toolbar-commands {", StringComparison.Ordinal);
        var commands = styles[commandsStart..styles.IndexOf('}', commandsStart)];
        Assert.Contains("flex-wrap: wrap;", commands, StringComparison.Ordinal);
        Assert.Contains("min-width: 0;", commands, StringComparison.Ordinal);
        var narrow = styles[styles.IndexOf("@container inceptus-workspace", StringComparison.Ordinal)..];
        Assert.Contains("flex-wrap: nowrap;", narrow, StringComparison.Ordinal);
        Assert.Contains("overflow-x: auto;", narrow, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px;", narrow, StringComparison.Ordinal);
        Assert.Contains("min-width: 44px;", narrow, StringComparison.Ordinal);
        Assert.Contains(".editor-toolbar .layout-icon-action,", narrow, StringComparison.Ordinal);
        Assert.Contains(".editor-toolbar .display-mode-toggle {", narrow, StringComparison.Ordinal);
        var iconsStart = styles.IndexOf(".layout-controls .layout-icon-action,", StringComparison.Ordinal);
        Assert.True(iconsStart >= 0);
        var icons = styles[iconsStart..styles.IndexOf('}', iconsStart)];
        Assert.Contains(".editor-toolbar .display-mode-toggle {", icons, StringComparison.Ordinal);
        Assert.Contains("width: 2.25rem;", icons, StringComparison.Ordinal);
        Assert.Contains("height: 2.25rem;", icons, StringComparison.Ordinal);
        Assert.Contains("min-width: 2.25rem;", icons, StringComparison.Ordinal);
    }

    [Fact]
    public void ReusablePresentationAssetsHaveOnePackageOwner()
    {
        var rclRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor");
        var demoRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Blazor");
        var canvasRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Canvas2D");

        Assert.True(File.Exists(Path.Combine(rclRoot, "wwwroot", "inceptus.presentation.js")));
        Assert.True(File.Exists(Path.Combine(rclRoot, "wwwroot", "fonts", "DejaVuSans-2.37.ttf")));
        Assert.True(File.Exists(Path.Combine(rclRoot, "wwwroot", "fonts", "DejaVuSans-LICENSE.txt")));

        Assert.False(File.Exists(Path.Combine(demoRoot, "wwwroot", "inceptus.presentation.js")));
        Assert.False(File.Exists(Path.Combine(demoRoot, "wwwroot", "fonts", "DejaVuSans-2.37.ttf")));
        Assert.False(File.Exists(Path.Combine(demoRoot, "wwwroot", "fonts", "DejaVuSans-LICENSE.txt")));
        Assert.True(File.Exists(Path.Combine(demoRoot, "wwwroot", "index.html")));

        Assert.True(File.Exists(Path.Combine(canvasRoot, "wwwroot", "inceptus.canvas2d.js")));
        Assert.True(File.Exists(Path.Combine(canvasRoot, "Publishing", "Assets", "index.html")));
        Assert.True(File.Exists(Path.Combine(
            canvasRoot,
            "Publishing",
            "Assets",
            "inceptus.publish.js")));
        Assert.True(File.Exists(Path.Combine(canvasRoot, "Publishing", "Assets", "styles.css")));
    }

    [Fact]
    public void ProjectsIntroduceNoManualStaticAssetCopyTarget()
    {
        var projectFiles = Directory.GetFiles(
            RepositoryRoot,
            "*.csproj",
            SearchOption.AllDirectories);

        foreach (var projectFile in projectFiles)
        {
            var project = XDocument.Load(projectFile);
            Assert.DoesNotContain(project.Descendants(), element =>
                (element.Name.LocalName is "Target" or "Copy") &&
                (((string?)element.Attribute("Name"))?.Contains(
                     "asset",
                     StringComparison.OrdinalIgnoreCase) == true ||
                 ((string?)element.Attribute("SourceFiles"))?.Contains(
                     "wwwroot",
                     StringComparison.OrdinalIgnoreCase) == true));
        }
    }

    [Fact]
    public void StartupProviderRemainsNarrowAlongsideTheApprovedImmutableP13Facade()
    {
        var providerType = typeof(IBpmnModelerStartupDocumentProvider);
        var providerMethods = providerType.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var providerMethod = Assert.Single(providerMethods);

        Assert.Equal("GetInitialDocumentAsync", providerMethod.Name);
        Assert.Equal(typeof(ValueTask<Document>), providerMethod.ReturnType);
        var cancellationParameter = Assert.Single(providerMethod.GetParameters());
        Assert.Equal(typeof(CancellationToken), cancellationParameter.ParameterType);
        Assert.True(cancellationParameter.HasDefaultValue);

        var modelerType = typeof(InceptusBpmnModeler);
        var publicMembers = modelerType.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(static member => member.Name)
            .ToArray();
        string[] approvedMembers =
        [
            nameof(InceptusBpmnModeler.InitialDocument),
            nameof(InceptusBpmnModeler.DocumentChanged),
            nameof(InceptusBpmnModeler.Ready),
            nameof(InceptusBpmnModeler.OperationFailed),
            nameof(InceptusBpmnModeler.CaptureDocumentSnapshot),
            nameof(InceptusBpmnModeler.LoadDocumentAsync),
            nameof(InceptusBpmnModeler.NewDocumentAsync),
            nameof(InceptusBpmnModeler.GetDocument),
            nameof(InceptusBpmnModeler.GetDocumentAsync),
            nameof(InceptusBpmnModeler.SetDocumentAsync),
        ];
        Assert.All(approvedMembers, member => Assert.Contains(member, publicMembers));

        string[] deferredMembers =
        [
            "ImportNativeDocumentAsync",
            "ExportNativeDocumentAsync",
            "PublishAsync",
            "DiagnosticsChanged",
            "InitialDocumentChanged",
            "InitialDocumentExpression",
            "Document",
            "EditingSession",
            "Composition",
        ];

        Assert.DoesNotContain(publicMembers, deferredMembers.Contains);
    }

    [Fact]
    public void DemoOwnsItsNativeSampleAndRclContainsNoSampleOrApplicationShell()
    {
        var rclRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Bpmn.Blazor");
        var demoRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Inceptus.DocumentEngine.Blazor");

        Assert.True(File.Exists(Path.Combine(demoRoot, "Demo", "bpmn-demo.inceptus.json")));
        Assert.True(File.Exists(Path.Combine(demoRoot, "Program.cs")));
        Assert.True(File.Exists(Path.Combine(demoRoot, "App.razor")));
        Assert.True(File.Exists(Path.Combine(demoRoot, "wwwroot", "index.html")));

        Assert.Empty(Directory.GetFiles(rclRoot, "*.inceptus.json", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(rclRoot, "Program.cs")));
        Assert.False(File.Exists(Path.Combine(rclRoot, "App.razor")));
        Assert.False(File.Exists(Path.Combine(rclRoot, "wwwroot", "index.html")));
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Inceptus.DocumentEngine.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
