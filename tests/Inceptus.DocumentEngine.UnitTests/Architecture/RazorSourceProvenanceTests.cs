using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace Inceptus.DocumentEngine.UnitTests.Architecture;

public sealed class RazorSourceProvenanceTests
{
    [Theory]
    [InlineData("D:\\a\\DocumentEngine\\DocumentEngine", "\r\n", true)]
    [InlineData("/home/runner/work/DocumentEngine/DocumentEngine", "\n", false)]
    [InlineData("D:/build=owner/source,checkout", "\n", true)]
    public async Task MapsOnlyParsedSourceDirectivesAndIsIdempotent(string sourceRoot, string newline, bool bom)
    {
        using var fixture = new BuildFixture(sourceRoot);
        var sourcePath = sourceRoot + "/src/Example.razor";
        var mappedPath = "/_/src/Example.razor";
        var directives = new[]
        {
            $"#pragma checksum \"{sourcePath}\" \"{{8829d00f-11b8-4213-878b-770e8597ac16}}\" \"aabb\"",
            $"#line 12 \"{sourcePath}\"",
            $"#line (1,1)-(1,8) \"{sourcePath}\"",
        };
        var nonDirectives = $$""""
            // #line 4 "{{sourcePath}}"
            /*
            #line 5 "{{sourcePath}}"
            */
            class Example
            {
                const string Verbatim = @"{{sourcePath}}";
                const string Raw = """
            #line 6 "{{sourcePath}}"
            """;
            }
            #line hidden
            #line default
            """".ReplaceLineEndings(newline);
        var source = string.Join(newline, directives) + newline + nonDirectives + newline;
        var expected = string.Join(newline, directives.Select(line => line.Replace(sourcePath, mappedPath, StringComparison.Ordinal)))
            + newline + nonDirectives + newline;
        var encoding = new UTF8Encoding(bom);
        File.WriteAllText(fixture.Source, source, encoding);

        var first = await fixture.RunAsync();

        Assert.True(first.ExitCode == 0, first.Output);
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(expected)), File.ReadAllBytes(fixture.Source));
        var modified = File.GetLastWriteTimeUtc(fixture.Source);
        var second = await fixture.RunAsync();
        Assert.True(second.ExitCode == 0, second.Output);
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(expected)), File.ReadAllBytes(fixture.Source));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(fixture.Source));
    }

    [Theory]
    [InlineData("C:\\Users\\another-user\\private\\Component.razor")]
    [InlineData("/Users/another-user/private/Component.razor")]
    [InlineData("\\\\private-server\\checkout\\Component.razor")]
    public async Task UnmappedAbsoluteSourceDirectivesFailWithoutChangingTheFile(string unmappedPath)
    {
        using var fixture = new BuildFixture("D:/approved/checkout");
        var source = $"#line 1 \"{unmappedPath}\"\nclass Example {{ }}\n";
        File.WriteAllText(fixture.Source, source, new UTF8Encoding(false));
        var before = File.ReadAllBytes(fixture.Source);

        var result = await fixture.RunAsync();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("outside the configured PathMap", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(fixture.Source));
    }

    [Fact]
    public void ReleaseMappingUsesSdkGenerationAndPreservesDebugGenerationAndEmbeddedSources()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot,
            "src/Inceptus.DocumentEngine.Bpmn.Blazor/Inceptus.DocumentEngine.Bpmn.Blazor.csproj"));
        var generation = Assert.Single(project.Descendants("UseRazorSourceGenerator"));
        Assert.Equal("false", generation.Value);
        Assert.Equal("'$(Configuration)' == 'Release'", (string?)generation.Parent!.Attribute("Condition"));
        Assert.Equal("$(PathMap),$([System.IO.Path]::GetPathRoot('$(MSBuildProjectDirectory)'))_$([System.IO.Path]::DirectorySeparatorChar)=/_/",
            generation.Parent.Element("PathMap")?.Value);
        Assert.Equal("../../build/RazorSourcePaths.targets", (string?)Assert.Single(project.Descendants("Import")).Attribute("Project"));

        var targets = XDocument.Load(Path.Combine(RepositoryRoot, "build/RazorSourcePaths.targets"));
        var target = Assert.Single(targets.Descendants("Target"));
        Assert.Equal("RazorGenerateComponentDefinition", (string?)target.Attribute("AfterTargets"));
        Assert.Equal("'$(Configuration)' == 'Release' and '$(UseRazorSourceGenerator)' == 'false'",
            (string?)target.Attribute("Condition"));
        var task = Assert.Single(target.Elements());
        Assert.Equal("@(_RazorComponentDefinition)", (string?)task.Attribute("Files"));
        Assert.Equal("$(PathMap)", (string?)task.Attribute("PathMap"));
        Assert.DoesNotContain(targets.Descendants().Concat(project.Descendants()), element =>
            element.Name.LocalName is "EmbeddedFiles" or "EmbedUntrackedSources" or "DebugType" or "RepositoryCommit" or "SourceRevisionId");
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

            throw new InvalidOperationException("The canonical source tree was not found.");
        }
    }

    private sealed class BuildFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "inceptus-razor-provenance-" + Guid.NewGuid().ToString("N"));
        private readonly string project;

        public BuildFixture(string sourceRoot)
        {
            Directory.CreateDirectory(directory);
            Source = Path.Combine(directory, "Example.razor.g.cs");
            project = Path.Combine(directory, "Mapping.proj");
            new XDocument(new XElement("Project",
                new XElement("Import", new XAttribute("Project", Path.Combine(RepositoryRoot, "build/RazorSourcePaths.targets"))),
                new XElement("Target", new XAttribute("Name", "ExerciseMapping"),
                    new XElement("MapRazorSourcePaths", new XAttribute("Files", Source),
                        new XAttribute("PathMap", sourceRoot.Replace(",", ",,", StringComparison.Ordinal)
                            .Replace("=", "==", StringComparison.Ordinal) + "=/_/,/_/=/_/"),
                        new XAttribute("RoslynDirectory", "$(MSBuildToolsPath)/Roslyn/bincore"))))).Save(project);
        }

        public string Source { get; }

        public async Task<(int ExitCode, string Output)> RunAsync()
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in new[] { "msbuild", project, "-t:ExerciseMapping", "-nologo", "-nodeReuse:false" })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException("MSBuild could not start.");
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

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
