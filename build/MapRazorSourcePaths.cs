using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;

// Build tooling only. This type is neither compiled into nor distributed with engine packages.
public sealed class MapRazorSourcePaths : Microsoft.Build.Utilities.Task
{
    [Required] public ITaskItem[] Files { get; set; }
    [Required] public string PathMap { get; set; }
    [Required] public string RoslynDirectory { get; set; }

    public override bool Execute()
    {
        try
        {
            // RoslynCodeTaskFactory compiles against netstandard, whereas the pinned SDK parser
            // targets its current runtime. Invoke only public syntax APIs at task execution time;
            // do not load a second compiler version or parse C# with regular expressions.
            Assembly.LoadFrom(Path.Combine(RoslynDirectory, "Microsoft.CodeAnalysis.dll"));
            var compiler = Assembly.LoadFrom(Path.Combine(RoslynDirectory, "Microsoft.CodeAnalysis.CSharp.dll"));
            var parse = compiler.GetType("Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree", true)
                .GetMethods().Single(method => method.Name == "ParseText" && method.GetParameters().Length == 5 &&
                    method.GetParameters()[0].ParameterType == typeof(string));
            // Let the compiler parse its own escaping (,, and ==) and preserve first-match order.
            var parserType = compiler.GetType("Microsoft.CodeAnalysis.CSharp.CSharpCommandLineParser", true);
            var parser = parserType.GetProperty("Default").GetValue(null);
            var arguments = parserType.GetMethods().Single(method => method.Name == "Parse" &&
                method.GetParameters().Length == 4 && method.ReturnType.Name == "CSharpCommandLineArguments")
                .Invoke(parser, new object[] { new[] { "/pathmap:" + PathMap }, Environment.CurrentDirectory, null, null });
            var errors = (IEnumerable)arguments.GetType().GetProperty("Errors").GetValue(arguments);
            if (errors.Cast<object>().Any(error => (string)error.GetType().GetProperty("Id").GetValue(error) == "CS8101"))
                throw new InvalidOperationException("Invalid compiler PathMap.");
            var entries = (IEnumerable)arguments.GetType().GetProperty("PathMap").GetValue(arguments);
            var maps = entries.Cast<object>().Select(entry => (
                Source: Normalize((string)entry.GetType().GetProperty("Key").GetValue(entry)).TrimEnd('/') + "/",
                Destination: Normalize((string)entry.GetType().GetProperty("Value").GetValue(entry)).TrimEnd('/') + "/")).ToArray();
            if (maps.Length == 0) throw new InvalidOperationException("Expected explicit source-root PathMap entries.");

            foreach (var file in Files)
            {
                var path = file.GetMetadata("FullPath");
                var bytes = File.ReadAllBytes(path);
                var bom = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191;
                var encoding = new UTF8Encoding(bom, true);
                var source = encoding.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
                var tree = parse.Invoke(null, new object[] { source, null, "", encoding, CancellationToken.None });
                var root = tree.GetType().GetMethods().First(method => method.Name == "GetRoot" && method.GetParameters().Length == 1)
                    .Invoke(tree, new object[] { CancellationToken.None });
                var trivia = (IEnumerable)root.GetType().GetMethods().Single(method => method.Name == "DescendantTrivia" &&
                    method.GetParameters().Length == 2 && method.GetParameters()[1].ParameterType == typeof(bool))
                    .Invoke(root, new object[] { null, true });
                var replacements = new List<(int Start, int Length, string Text)>();
                foreach (var item in trivia)
                {
                    var node = item.GetType().GetMethod("GetStructure").Invoke(item, null);
                    if (node == null) continue;
                    var kind = node.GetType().Name;
                    if (kind != "LineDirectiveTriviaSyntax" && kind != "LineSpanDirectiveTriviaSyntax" &&
                        kind != "PragmaChecksumDirectiveTriviaSyntax") continue;
                    var token = node.GetType().GetProperty("File").GetValue(node);
                    var text = (string)token.GetType().GetProperty("Text").GetValue(token);
                    if (!text.StartsWith("\"", StringComparison.Ordinal)) continue;
                    var original = (string)token.GetType().GetProperty("ValueText").GetValue(token);
                    var normalized = Normalize(original);
                    string mapped = null;
                    foreach (var map in maps)
                    {
                        if (normalized.StartsWith(map.Source, Path.DirectorySeparatorChar == '\\'
                            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        {
                            mapped = map.Destination + normalized.Substring(map.Source.Length);
                            break;
                        }
                    }
                    if (mapped == null)
                    {
                        if (maps.Any(map => normalized.StartsWith(map.Destination, StringComparison.Ordinal))) continue;
                        if (normalized.StartsWith("/", StringComparison.Ordinal) || (normalized.Length > 1 && normalized[1] == ':'))
                            throw new InvalidOperationException("Generated Razor source directive is outside the configured PathMap.");
                        continue;
                    }
                    if (string.Equals(original, mapped, StringComparison.Ordinal)) continue;
                    var span = token.GetType().GetProperty("Span").GetValue(token);
                    replacements.Add(((int)span.GetType().GetProperty("Start").GetValue(span),
                        (int)span.GetType().GetProperty("Length").GetValue(span), "\"" + mapped + "\""));
                }
                // Change only parsed file tokens. Code, comments, raw/verbatim strings, directive
                // line/column ranges, checksum values, line endings and the UTF-8 BOM stay intact.
                if (replacements.Count == 0) continue;
                var result = new StringBuilder(source);
                foreach (var replacement in replacements.OrderByDescending(value => value.Start))
                    result.Remove(replacement.Start, replacement.Length).Insert(replacement.Start, replacement.Text);
                File.WriteAllText(path, result.ToString(), encoding);
            }
            return true;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(exception);
            return false;
        }
    }

    private static string Normalize(string path) { return path.Replace('\\', '/'); }
}
