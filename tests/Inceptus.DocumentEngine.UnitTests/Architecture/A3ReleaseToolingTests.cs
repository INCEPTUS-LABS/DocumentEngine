using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Inceptus.Bpmn.Integration;
using Inceptus.DocumentEngine.Contracts.Documents;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class A3ReleaseToolingTests
{
    private static readonly string[] Family =
    [
        "Inceptus.DocumentEngine.Contracts", "Inceptus.DocumentEngine.Runtime", "Inceptus.DocumentEngine.Bpmn",
        "Inceptus.DocumentEngine.Organizational", "Inceptus.DocumentEngine.Canvas2D",
        "Inceptus.DocumentEngine.Bpmn.Blazor", "Inceptus.Bpmn.Integration",
    ];

    [Theory]
    [InlineData("complete", true)]
    [InlineData("missing-package", false)]
    [InlineData("missing-symbol", false)]
    [InlineData("extra-package", false)]
    [InlineData("extra-symbol", false)]
    [InlineData("duplicate", false)]
    [InlineData("mixed-version", false)]
    [InlineData("wrong-integration-id", false)]
    public async Task ArchiveInventoryRequiresTheExactSevenPairs(string scenario, bool accepted)
    {
        using var fixture = new Fixture();
        foreach (var id in Family)
        {
            File.WriteAllText(Path.Combine(fixture.Directory, id + ".0.1.10.nupkg"), string.Empty);
            File.WriteAllText(Path.Combine(fixture.Directory, id + ".0.1.10.snupkg"), string.Empty);
        }

        var integration = Path.Combine(fixture.Directory, "Inceptus.Bpmn.Integration.0.1.10");
        switch (scenario)
        {
            case "missing-package": File.Delete(integration + ".nupkg"); break;
            case "missing-symbol": File.Delete(integration + ".snupkg"); break;
            case "extra-package": File.WriteAllText(Path.Combine(fixture.Directory, "Unexpected.0.1.10.nupkg"), string.Empty); break;
            case "extra-symbol": File.WriteAllText(Path.Combine(fixture.Directory, "Unexpected.0.1.10.snupkg"), string.Empty); break;
            case "duplicate": File.Copy(integration + ".nupkg", integration + ".copy.nupkg"); break;
            case "mixed-version": File.Move(integration + ".nupkg", Path.Combine(fixture.Directory, "Inceptus.Bpmn.Integration.0.1.9.nupkg")); break;
            case "wrong-integration-id": File.Move(integration + ".nupkg", Path.Combine(fixture.Directory, "Inceptus.DocumentEngine.Integration.0.1.10.nupkg")); break;
        }

        var result = await RunInspectorFunctionsAsync($"Assert-PackageInventory {Quote(fixture.Directory)} '0.1.10'");
        Assert.Equal(accepted, result.ExitCode == 0);
        if (!accepted) Assert.Contains("Expected exactly the seven", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing", false)]
    [InlineData("unexpected", false)]
    [InlineData("duplicate", false)]
    [InlineData("version", false)]
    [InlineData("framework", false)]
    [InlineData("exact-runtime", false)]
    public async Task DependencyChecksRetainAllExistingEdgesAndIncludeIntegration(string scenario, bool accepted)
    {
        foreach (var id in Family)
        {
            var project = XDocument.Load(Path.Combine(RepositoryRoot, "src", id, id + ".csproj"));
            var dependencies = project.Descendants("ProjectReference").Select(reference =>
            {
                var dependency = Path.GetFileNameWithoutExtension((string)reference.Attribute("Include")!);
                return new XElement("dependency", new XAttribute("id", dependency), new XAttribute("version",
                    id == "Inceptus.DocumentEngine.Canvas2D" && dependency == "Inceptus.DocumentEngine.Runtime" ? "[0.1.10]" : "0.1.10"));
            }).Concat(project.Descendants("PackageReference").Select(reference =>
                new XElement("dependency", new XAttribute("id", (string)reference.Attribute("Include")!), new XAttribute("version", "10.0.5")))).ToList();
            var group = new XElement("group", new XAttribute("targetFramework", "net10.0"), dependencies);
            var mutated = id == "Inceptus.Bpmn.Integration" && scenario != "exact-runtime";
            if (mutated)
            {
                switch (scenario)
                {
                    case "missing": group.Elements().First().Remove(); break;
                    case "unexpected": group.Add(new XElement("dependency", new XAttribute("id", "Unexpected"), new XAttribute("version", "0.1.10"))); break;
                    case "duplicate": group.Add(new XElement(group.Elements().First())); break;
                    case "version": group.Elements().First().SetAttributeValue("version", "0.1.9"); break;
                    case "framework": group.SetAttributeValue("targetFramework", "net9.0"); break;
                }
            }

            if (id == "Inceptus.DocumentEngine.Canvas2D" && scenario == "exact-runtime")
            {
                group.Elements().Single(element => ((string)element.Attribute("id")!).EndsWith(".Runtime", StringComparison.Ordinal))
                    .SetAttributeValue("version", "0.1.10");
                mutated = true;
            }

            var metadata = new XElement("metadata", new XElement("dependencies", group));
            var result = await RunInspectorFunctionsAsync($"[xml]$spec = {Quote(metadata.ToString())}; Assert-PackageDependencies '{id}' $spec.metadata '0.1.10'");
            Assert.True(result.ExitCode == 0 == (!mutated || accepted), result.Output);
        }
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing", false)]
    [InlineData("changed", false)]
    public async Task IntegrationResourcesAreReadFromAssemblyAndComparedToSource(string scenario, bool accepted)
    {
        using var fixture = new Fixture();
        var assembly = scenario == "missing" ? typeof(DocumentSnapshot).Assembly : typeof(BpmnImportProfile).Assembly;
        var bytes = File.ReadAllBytes(assembly.Location);
        if (scenario == "changed")
        {
            var marker = Encoding.UTF8.GetBytes(BpmnImportProfile.Namespace);
            var index = bytes.AsSpan().IndexOf(marker);
            Assert.True(index >= 0);
            bytes[index] = (byte)'X';
        }

        var path = Path.Combine(fixture.Directory, "resource-fixture.dll");
        File.WriteAllBytes(path, bytes);
        var result = await RunInspectorFunctionsAsync($"Assert-IntegrationResources ([IO.File]::ReadAllBytes({Quote(path)}))");
        Assert.Equal(accepted, result.ExitCode == 0);
        if (scenario == "changed") Assert.Contains("Embedded Integration resource:", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkflowsInspectorAndPackableProjectsUseTheSameExplicitFamily()
    {
        var result = await RunInspectorFunctionsAsync("$family | ConvertTo-Json -Compress");
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(Family, JsonSerializer.Deserialize<string[]>(result.Output.Trim()));
        var packable = Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(XDocument.Load).Where(project => project.Descendants("IsPackable").Any(value => value.Value == "true"))
            .Select(project => project.Descendants("PackageId").Single().Value).Order(StringComparer.Ordinal);
        Assert.Equal(Family.Order(StringComparer.Ordinal), packable);
        foreach (var workflow in new[] { "validate.yml", "publish.yml" })
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot, ".github/workflows", workflow));
            var loop = Regex.Match(source, @"foreach \(\$id in @\(([^\r\n]+)\)\)", RegexOptions.CultureInvariant);
            Assert.True(loop.Success);
            Assert.Equal(Family, Regex.Matches(loop.Groups[1].Value, "'([^']+)'", RegexOptions.CultureInvariant).Select(match => match.Groups[1].Value));
            Assert.Contains("dotnet pack \"src/$id/$id.csproj\" -c Release --no-build --no-restore --output $output", source, StringComparison.Ordinal);
            Assert.Contains("-RequireSourceLink", source, StringComparison.Ordinal);
        }
    }

    private static async Task<(int ExitCode, string Output)> RunInspectorFunctionsAsync(string exercise)
    {
        // Exercise the actual inspector functions, without packing releases or adding a gate-bypass switch.
        var script = File.ReadAllText(Path.Combine(RepositoryRoot, ".github/scripts/Inspect-Packages.ps1"));
        var start = script.IndexOf("$ErrorActionPreference = 'Stop'", StringComparison.Ordinal);
        var end = script.IndexOf("Require ($Commit -cmatch", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Directory, "exercise.ps1");
        File.WriteAllText(path, script[start..end] + Environment.NewLine + exercise);
        var info = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", path }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("PowerShell could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(1));
            return (process.ExitCode, await output + await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Inceptus.DocumentEngine.sln"))) return directory.FullName;
            throw new InvalidOperationException("The canonical source tree was not found.");
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "inceptus-release-tooling-" + Guid.NewGuid().ToString("N"));
        public Fixture() => System.IO.Directory.CreateDirectory(Directory);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
