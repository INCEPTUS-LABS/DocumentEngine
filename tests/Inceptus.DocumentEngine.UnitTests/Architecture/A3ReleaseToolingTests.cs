using System.Diagnostics;
using System.IO.Compression;
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
        var baselineFamily = await RunInspectorFunctionsAsync("$documentEnginePackageIds | ConvertTo-Json -Compress");
        Assert.True(baselineFamily.ExitCode == 0, baselineFamily.Output);
        Assert.Equal(Family, JsonSerializer.Deserialize<string[]>(baselineFamily.Output.Trim()));
        var targets = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.targets"));
        var taskCode = targets.Descendants("Code").Single().Value;
        var taskFamily = Regex.Match(taskCode, @"var family = new\[\] \{(.+?)\};", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(taskFamily.Success);
        Assert.Equal(Family, Regex.Matches(taskFamily.Groups[1].Value, "\"([^\"]+)\"", RegexOptions.CultureInvariant).Select(match => match.Groups[1].Value));
        Assert.DoesNotContain("Update-PublicationBaseline", targets.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-RestMethod", targets.ToString(), StringComparison.Ordinal);
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

    [Theory]
    [InlineData("selected", true)]
    [InlineData("crlf", true)]
    [InlineData("last", true)]
    [InlineData("unused-gap", true)]
    [InlineData("subheading", true)]
    [InlineData("limit", true)]
    [InlineData("missing", false)]
    [InlineData("prefix-only", false)]
    [InlineData("duplicate-target", false)]
    [InlineData("duplicate-intermediate", false)]
    [InlineData("empty-target", false)]
    [InlineData("empty-intermediate", false)]
    [InlineData("oversized", false)]
    [InlineData("oversized-unicode", false)]
    [InlineData("invalid-heading", false)]
    [InlineData("baseline-invalid", false)]
    [InlineData("baseline-equal", false)]
    [InlineData("baseline-ahead", false)]
    [InlineData("baseline-missing-package", false)]
    [InlineData("baseline-duplicate-package", false)]
    [InlineData("baseline-wrong-package", false)]
    [InlineData("baseline-inconsistent", false)]
    public async Task PackAndInspectorValidateCumulativeNotes(string scenario, bool accepted)
    {
        using var fixture = new Fixture();
        const string version = "0.1.17";
        var baseline = scenario switch { "baseline-invalid" => "invalid", "baseline-equal" => version, "baseline-ahead" => "0.1.18", _ => "0.1.14" };
        var body = "- Selected notes; accents: żółć. <tag> & \"quotes\". $(Version) @(Items) %3B";
        if (scenario == "subheading") body += "\n\n## Compatibility\n\n### Details\n- Keep this content.";
        var middle = scenario == "unused-gap" ? string.Empty : "## 0.1.16\n\n- Intermediate correction.\n\n";
        const string oldest = "## 0.1.15\n\n- Unpublished features.\n\n";
        if (scenario == "limit") body = new string('x', 35000 - $"## {version}\n\n\n\n{middle}{oldest.TrimEnd()}".Length);
        if (scenario == "oversized") { body = new string('x', 18000); middle = "## 0.1.16\n\n" + new string('x', 18000) + "\n\n"; }
        if (scenario == "oversized-unicode") body = string.Concat(Enumerable.Repeat("😀", 17501));
        if (scenario == "empty-target") body = " \t\n";
        if (scenario == "empty-intermediate") middle = "## 0.1.16\n\n \t\n\n";
        if (scenario == "invalid-heading") middle = "## 0.1.invalid\n\n- Invalid version.\n\n";
        var heading = scenario == "missing" ? "999.0.1" : scenario == "prefix-only" ? "0.1.170" : version;
        var expected = $"## {heading}\n\n{body}\n\n{middle}{oldest}";
        if (scenario == "duplicate-target") expected += $"## {version}\n- Duplicate\n";
        if (scenario == "duplicate-intermediate") expected += "## 0.1.16\n- Duplicate\n";
        var notes = "# Release notes\n\n## 999.0.0\n- Future\n\n" + expected;
        if (scenario != "last") notes += "## 0.1.14\n- Already published\n\n## 0.1.12 candidate\n- Older\n";
        if (scenario == "crlf") notes = notes.Replace("\n", "\r\n", StringComparison.Ordinal);
        var notesFile = Path.Combine(fixture.Directory, "RELEASE-NOTES.md");
        File.WriteAllText(notesFile, notes);
        var properties = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        properties.Descendants("Version").Single().Value = version;
        properties.Descendants("PreviousPublishedVersion").Single().Value = baseline;
        var records = properties.Descendants("PublicationBaselinePackage").ToArray();
        foreach (var record in records) record.SetAttributeValue("PublishedVersion", baseline);
        switch (scenario)
        {
            case "baseline-missing-package": records[^1].Remove(); break;
            case "baseline-duplicate-package": records[^1].SetAttributeValue("Include", Family[0]); break;
            case "baseline-wrong-package": records[^1].SetAttributeValue("Include", "Unexpected.Package"); break;
            case "baseline-inconsistent": records[^1].SetAttributeValue("PublishedVersion", "0.1.12"); break;
        }
        var propertiesFile = Path.Combine(fixture.Directory, "Directory.Build.props");
        properties.Save(propertiesFile);
        foreach (var file in new[] { "Directory.Build.targets", "README.md", "LICENSE", "THIRD-PARTY-NOTICES.md", "branding/inceptus-nuget-icon.png" })
        {
            var destination = Path.Combine(fixture.Directory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(RepositoryRoot, file), destination);
        }
        // Real offline NuGet pack with the repository targets; no production assemblies or release artifacts are replaced.
        var project = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("IsPackable", "true"), new XElement("PackageId", Family[0]),
                new XElement("IncludeBuildOutput", "false"), new XElement("IncludeSymbols", "false"),
                new XElement("SuppressDependenciesWhenPacking", "true"))));
        var projectFile = Path.Combine(fixture.Directory, "fixture.csproj");
        project.Save(projectFile);
        var configFile = Path.Combine(fixture.Directory, "NuGet.Config");
        File.WriteAllText(configFile, "<configuration><packageSources><clear /></packageSources></configuration>");
        var restore = await RunProcessAsync("dotnet", ["restore", projectFile, "--configfile", configFile]);
        Assert.True(restore.ExitCode == 0, restore.Output);
        var packageDirectory = Path.Combine(fixture.Directory, "packages");
        var packed = await RunProcessAsync("dotnet", ["pack", projectFile, "--no-build", "--no-restore", "-p:IncludeSymbols=false", "-o", packageDirectory]);
        var exercise = $$"""
            [xml]$props = [IO.File]::ReadAllText({{Quote(propertiesFile)}})
            $records = @($props.Project.ItemGroup.PublicationBaselinePackage | ForEach-Object { [pscustomobject]@{ PackageId = $_.Include; PublishedVersion = $_.PublishedVersion } })
            Assert-PublicationBaseline '{{version}}' {{Quote(baseline)}} $records
            $notes = Get-CumulativeReleaseNotes ([IO.File]::ReadAllText({{Quote(notesFile)}})) '{{version}}' {{Quote(baseline)}}
            [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($notes))
            """;
        var inspected = await RunInspectorFunctionsAsync(exercise);
        Assert.True(accepted == (packed.ExitCode == 0), packed.Output);
        Assert.True(accepted == (inspected.ExitCode == 0), inspected.Output);
        if (accepted)
        {
            Assert.Equal(expected.TrimEnd(), Encoding.UTF8.GetString(Convert.FromBase64String(inspected.Output.Trim())));
            using var archive = ZipFile.OpenRead(Path.Combine(packageDirectory, Family[0] + "." + version + ".nupkg"));
            using var metadata = archive.GetEntry(Family[0] + ".nuspec")!.Open();
            var specification = XDocument.Load(metadata);
            Assert.Equal(expected.TrimEnd(), specification.Descendants().Single(element => element.Name.LocalName == "releaseNotes").Value);
            using var history = new StreamReader(archive.GetEntry("RELEASE-NOTES.md")!.Open());
            Assert.Equal(notes, await history.ReadToEndAsync());
        }
        else
        {
            Assert.True(packed.Output.Contains("release notes", StringComparison.OrdinalIgnoreCase) || packed.Output.Contains("publication baseline", StringComparison.OrdinalIgnoreCase), packed.Output);
            Assert.False(Directory.Exists(packageDirectory) && Directory.EnumerateFiles(packageDirectory, "*.nupkg").Any());
        }
    }

    [Theory]
    [InlineData("exact", true)]
    [InlineData("historical", false)]
    [InlineData("wrong-version", false)]
    [InlineData("truncated", false)]
    [InlineData("changed-case", false)]
    [InlineData("extra-whitespace", false)]
    [InlineData("missing", false)]
    [InlineData("oversized", false)]
    public async Task InspectorRequiresExactCumulativeMetadata(string scenario, bool accepted)
    {
        const string expected = "- Selected notes.\n- Unicode: żółć; XML: <tag> & text.";
        var actual = scenario switch
        {
            "historical" => expected + "\n## 0.0.1\n- Older notes.",
            "wrong-version" => "- Older notes.",
            "truncated" => expected[..^1],
            "changed-case" => expected.ToUpperInvariant(),
            "extra-whitespace" => expected + "\n",
            "missing" => string.Empty,
            "oversized" => new string('x', 35001),
            _ => expected,
        };
        var xml = new XElement("metadata", new XElement("releaseNotes", actual));
        var result = await RunInspectorFunctionsAsync($"[xml]$metadata = {Quote(xml.ToString())}; Assert-PackageReleaseNotes 'fixture' $metadata.metadata.releaseNotes {Quote(expected)}");
        Assert.True(accepted == (result.ExitCode == 0), result.Output);
        if (!accepted) Assert.Contains("release notes metadata", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("complete", true)]
    [InlineData("earlier-six-package-history", true)]
    [InlineData("unordered", true)]
    [InlineData("partial-latest", false)]
    [InlineData("partial-history", false)]
    [InlineData("missing-package", false)]
    [InlineData("empty-index", false)]
    [InlineData("invalid-version", false)]
    [InlineData("duplicate-version", false)]
    public async Task PublicationBaselineRequiresConsistentCompleteFamily(string scenario, bool accepted)
    {
        var versions = Family.ToDictionary(id => id, _ => new List<string> { "0.1.10", "0.1.12", "0.1.14" }, StringComparer.Ordinal);
        switch (scenario)
        {
            case "earlier-six-package-history":
                foreach (var id in Family[..^1]) versions[id].Insert(0, "0.1.9");
                break;
            case "unordered": versions[Family[0]].Reverse(); break;
            case "partial-latest": versions[Family[^1]].Remove("0.1.14"); break;
            case "partial-history": versions[Family[^1]].Remove("0.1.12"); break;
            case "missing-package": versions.Remove(Family[^1]); break;
            case "empty-index": versions[Family[^1]].Clear(); break;
            case "invalid-version": versions[Family[^1]].Add("invalid"); break;
            case "duplicate-version": versions[Family[^1]].Add("0.1.14"); break;
        }
        var result = await RunInspectorFunctionsAsync($"$versions = {Quote(JsonSerializer.Serialize(versions))} | ConvertFrom-Json -AsHashtable; Resolve-PublicationBaseline $versions");
        Assert.True(accepted == (result.ExitCode == 0), result.Output);
        if (accepted) Assert.Equal("0.1.14", result.Output.Trim());
        if (scenario.StartsWith("partial-", StringComparison.Ordinal)) Assert.Contains("Partial or inconsistent publication", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BaselineDiscoveryPersistsOnlyAfterSuccessfulVerification(bool partialPublication)
    {
        using var fixture = new Fixture();
        var scripts = Path.Combine(fixture.Directory, ".github/scripts");
        Directory.CreateDirectory(scripts);
        foreach (var file in new[] { "ReleaseNotes.ps1", "Update-PublicationBaseline.ps1" })
            File.Copy(Path.Combine(RepositoryRoot, ".github/scripts", file), Path.Combine(scripts, file));
        var propertiesFile = Path.Combine(fixture.Directory, "Directory.Build.props");
        File.Copy(Path.Combine(RepositoryRoot, "Directory.Build.props"), propertiesFile);
        var before = File.ReadAllBytes(propertiesFile);
        var script = $$"""
            function Invoke-RestMethod($Uri) {
                if (-not $Uri.StartsWith('https://api.nuget.org/v3-flatcontainer/')) { throw 'Unexpected API.' }
                if ($Uri.EndsWith('/inceptus.bpmn.integration/index.json') -and ${{partialPublication.ToString().ToLowerInvariant()}}) {
                    return @{ versions = @('0.1.10') }
                }
                return @{ versions = @('0.1.10', '0.1.12') }
            }
            & {{Quote(Path.Combine(scripts, "Update-PublicationBaseline.ps1"))}} -Persist
            """;
        var result = await RunInspectorFunctionsAsync(script);
        Assert.True((result.ExitCode == 0) == !partialPublication, result.Output);
        if (partialPublication) Assert.Equal(before, File.ReadAllBytes(propertiesFile));
        else
        {
            var properties = XDocument.Load(propertiesFile);
            Assert.Equal("0.1.12", properties.Descendants("PreviousPublishedVersion").Single().Value);
            Assert.Equal(XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props")).Descendants("Version").Single().Value,
                properties.Descendants("Version").Single().Value);
            Assert.All(properties.Descendants("PublicationBaselinePackage"), element => Assert.Equal("0.1.12", (string?)element.Attribute("PublishedVersion")));
            Assert.Equal(Family, properties.Descendants("PublicationBaselinePackage").Select(element => (string)element.Attribute("Include")!));
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
        File.Copy(Path.Combine(RepositoryRoot, ".github/scripts/ReleaseNotes.ps1"), Path.Combine(fixture.Directory, "ReleaseNotes.ps1"));
        var path = Path.Combine(fixture.Directory, "exercise.ps1");
        File.WriteAllText(path, script[start..end] + Environment.NewLine + exercise);
        return await RunProcessAsync("pwsh", ["-NoProfile", "-NonInteractive", "-File", path]);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(string executable, string[] arguments)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{executable} could not start.");
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
