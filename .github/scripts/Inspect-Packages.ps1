[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Commit,
    [switch]$RequireSourceLink
)
$ErrorActionPreference = 'Stop'
# Release-artifact inspection only; canonical suites run separately in the workflows.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
public sealed class EmbeddedSourceInspection
{
    public string Document { get; set; }
    public string RecordKind { get; set; }
    public bool Compressed { get; set; }
    public int Length { get; set; }
    public string ChecksumAlgorithm { get; set; }
    public string Checksum { get; set; }
    public string[] ForbiddenPaths { get; set; }
}
public static class ReleaseSymbols
{
    // Source directives contain file names, not application strings. Outside those directives,
    // inspect only absolute paths with source-file extensions; URLs and /_/ maps are not local paths.
    private static readonly Regex SourceDirective = new Regex(@"^\s*#\s*(?:line\b|pragma\s+checksum\b)");
    private static readonly Regex QuotedText = new Regex("\"(?<path>[^\"]*)\"");
    private static readonly Regex SourcePath = new Regex(
        @"(?<![\w:/\\])(?:file://|[A-Za-z]:[\\/]|\\\\(?=\S)|/(?=[^/\s]))[^\""'\r\n<>]*?\.(?:cshtml|razor|cs|vb|fs|fsx)(?=$|[\""'\s,;)\]])",
        RegexOptions.IgnoreCase);

    private static bool IsLocalAbsolutePath(string path)
    {
        // This is deliberately independent of the inspector host OS and checkout root.
        if (path.StartsWith("/_/", StringComparison.Ordinal)) return false;
        if (Regex.IsMatch(path, @"^[A-Za-z][A-Za-z0-9+.-]*://"))
            return path.StartsWith("file://", StringComparison.OrdinalIgnoreCase);
        return path.StartsWith("/", StringComparison.Ordinal) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) || Regex.IsMatch(path, @"^[A-Za-z]:[\\/]");
    }

    public static string[] ForbiddenSourcePaths(string source)
    {
        var findings = new List<string>();
        using var lines = new StringReader(source);
        string line;
        var number = 0;
        while ((line = lines.ReadLine()) != null)
        {
            number++;
            var directive = SourceDirective.IsMatch(line);
            var matches = directive ? QuotedText.Matches(line) : SourcePath.Matches(line);
            foreach (Match match in matches)
            {
                var path = directive ? match.Groups["path"].Value : match.Value;
                if (IsLocalAbsolutePath(path))
                    findings.Add($"line {number}, {(directive ? "source directive" : "source-path literal/comment/attribute")}: {path}");
            }
        }
        return findings.ToArray();
    }

    public static byte[] DecodeEmbeddedSource(byte[] blob)
    {
        if (blob.Length < 4) throw new InvalidDataException("Truncated EmbeddedSource header.");
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(blob);
        if (length < 0) throw new InvalidDataException("Negative EmbeddedSource length.");
        if (length == 0) return blob.Skip(4).ToArray();
        using var compressed = new MemoryStream(blob, 4, blob.Length - 4);
        using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
        using var result = new MemoryStream();
        inflater.CopyTo(result);
        if (result.Length != length) throw new InvalidDataException("EmbeddedSource decompressed length mismatch.");
        return result.ToArray();
    }

    public static EmbeddedSourceInspection[] EmbeddedSources(byte[] pdb)
    {
        var kind = new Guid("0e8a571b-6926-466e-b4ad-8ab04611f5fe");
        var sha1 = new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460");
        var sha256 = new Guid("8829d00f-11b8-4213-878b-770e8597ac16");
        using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb));
        var reader = provider.GetMetadataReader();
        var result = new List<EmbeddedSourceInspection>();
        foreach (var handle in reader.Documents)
        {
            var document = reader.GetDocument(handle);
            var name = reader.GetString(document.Name);
            var records = reader.GetCustomDebugInformation(handle)
                .Select(item => reader.GetCustomDebugInformation(item))
                .Where(item => reader.GetGuid(item.Kind) == kind).ToArray();
            if (records.Length > 1) throw new InvalidDataException($"Competing EmbeddedSource records: {name}");
            if (records.Length == 0) continue;
            var blob = reader.GetBlobBytes(records[0].Value);
            var bytes = DecodeEmbeddedSource(blob);
            var algorithm = reader.GetGuid(document.HashAlgorithm);
            var actual = algorithm == sha256 ? SHA256.HashData(bytes) : algorithm == sha1 ? SHA1.HashData(bytes) :
                throw new InvalidDataException($"Unsupported EmbeddedSource checksum algorithm: {name}");
            if (!actual.SequenceEqual(reader.GetBlobBytes(document.Hash)))
                throw new InvalidDataException($"EmbeddedSource checksum mismatch: {name}");
            using var textReader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), true);
            result.Add(new EmbeddedSourceInspection {
                Document = name, RecordKind = kind.ToString(), Compressed = BitConverter.ToInt32(blob, 0) != 0,
                Length = bytes.Length, ChecksumAlgorithm = algorithm.ToString(), Checksum = Convert.ToHexString(actual).ToLowerInvariant(),
                ForbiddenPaths = ForbiddenSourcePaths(textReader.ReadToEnd())
            });
        }
        return result.ToArray();
    }

    public static string[] Documents(byte[] pdb)
    {
        using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb));
        var reader = provider.GetMetadataReader();
        return reader.Documents.Select(handle => reader.GetString(reader.GetDocument(handle).Name)).ToArray();
    }

    public static bool SourceMatches(byte[] pdb, string name, byte[] source)
    {
        using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb));
        var reader = provider.GetMetadataReader();
        var document = reader.Documents.Select(handle => reader.GetDocument(handle))
            .Single(item => reader.GetString(item.Name) == name);
        var algorithm = reader.GetGuid(document.HashAlgorithm);
        var actual = algorithm == new Guid("8829d00f-11b8-4213-878b-770e8597ac16") ? SHA256.HashData(source) :
            algorithm == new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460") ? SHA1.HashData(source) :
            throw new InvalidDataException($"Unsupported source checksum algorithm: {name}");
        return actual.SequenceEqual(reader.GetBlobBytes(document.Hash));
    }

    public static byte[] Resource(byte[] assembly, string name)
    {
        using var pe = new PEReader(new MemoryStream(assembly));
        var reader = pe.GetMetadataReader();
        var resource = reader.ManifestResources.Select(handle => reader.GetManifestResource(handle))
            .Single(entry => reader.GetString(entry.Name) == name);
        if (!resource.Implementation.IsNil) throw new InvalidDataException("Expected embedded resource.");
        var block = pe.GetSectionData(pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress)
            .GetReader((int)resource.Offset, pe.PEHeaders.CorHeader.ResourcesDirectory.Size - (int)resource.Offset);
        return block.ReadBytes(block.ReadInt32());
    }

    public static string Inspect(byte[] pdb, byte[] assembly)
    {
        using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb));
        var reader = provider.GetMetadataReader();
        using var pe = new PEReader(new MemoryStream(assembly));
        var codeView = pe.ReadCodeViewDebugDirectoryData(pe.ReadDebugDirectory().Single(
            entry => entry.Type == DebugDirectoryEntryType.CodeView));
        if (new Guid(reader.DebugMetadataHeader.Id.Take(16).ToArray()) != codeView.Guid)
            throw new InvalidDataException("Portable PDB does not match its assembly.");
        var links = reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition)
            .Where(handle => reader.GetGuid(reader.GetCustomDebugInformation(handle).Kind) ==
                new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A"))
            .Select(handle => Encoding.UTF8.GetString(
                reader.GetBlobBytes(reader.GetCustomDebugInformation(handle).Value))).ToArray();
        if (links.Length > 1) throw new InvalidDataException("Competing Source Link records.");
        return links.SingleOrDefault() ?? "";
    }
}
'@ -ReferencedAssemblies @('System.Reflection.Metadata', 'System.Collections.Immutable', 'System.Collections', 'System.Linq', 'System.IO.Compression', 'System.Security.Cryptography', 'System.Text.RegularExpressions', 'System.Text.Encoding.Extensions', 'System.Memory')

function Read-EntryBytes($Zip, [string]$Name) {
    $entry = $Zip.GetEntry($Name)
    if ($null -eq $entry) { throw "Missing entry: $Name" }
    $stream = $entry.Open()
    $memory = [IO.MemoryStream]::new()
    try { $stream.CopyTo($memory); return ,$memory.ToArray() }
    finally { $stream.Dispose(); $memory.Dispose() }
}
function Read-EntryText($Zip, [string]$Name) {
    [Text.Encoding]::UTF8.GetString((Read-EntryBytes $Zip $Name))
}
function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
$repository = 'https://github.com/INCEPTUS-LABS/DocumentEngine'
$prefix = 'Inceptus.DocumentEngine.'
$family = @('Contracts', 'Runtime', 'Bpmn', 'Organizational', 'Canvas2D', 'Bpmn.Blazor')
$edges = @{
    Contracts = @()
    Runtime = @('Contracts')
    Bpmn = @('Contracts')
    Organizational = @('Contracts')
    Canvas2D = @('Contracts', 'Runtime')
    'Bpmn.Blazor' = @('Bpmn', 'Canvas2D', 'Contracts', 'Organizational', 'Runtime')
}
Require ($Commit -cmatch '^[0-9a-f]{40}$') 'Expected an actual full Git commit.'
if ($RequireSourceLink) {
    Require (@(git status --porcelain=v1 --untracked-files=all).Count -eq 0) 'Public artifacts require a clean checkout.'
    Require ((git rev-parse HEAD) -ceq $Commit) 'Artifact commit differs from the public checkout.'
    $trackedSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    git -c core.quotepath=false ls-files | ForEach-Object { [void]$trackedSources.Add($_) }
    Require ($LASTEXITCODE -eq 0) 'Could not enumerate committed source files.'
}
$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg')
$symbols = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.snupkg')
Require ($packages.Count -eq 6 -and $symbols.Count -eq 6) 'Expected six nupkg/snupkg pairs.'
$records = foreach ($name in $family) {
    $id = $prefix + $name
    $archive = Join-Path $PackageDirectory "$id.$Version.nupkg"
    $symbolArchive = Join-Path $PackageDirectory "$id.$Version.snupkg"
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    $symbolZip = [IO.Compression.ZipFile]::OpenRead($symbolArchive)
    try {
        [xml]$nuspec = (Read-EntryText $zip "$id.nuspec").TrimStart([char]0xFEFF)
        [xml]$symbolSpec = (Read-EntryText $symbolZip "$id.nuspec").TrimStart([char]0xFEFF)
        $metadata = $nuspec.package.metadata
        Require ($metadata.id -ceq $id -and $metadata.version -ceq $Version) "Package identity: $id"
        Require ($symbolSpec.package.metadata.id -ceq $id -and $symbolSpec.package.metadata.version -ceq $Version) "Symbol identity: $id"
        Require ($symbolSpec.package.metadata.packageTypes.packageType.name -ceq 'SymbolsPackage') "Symbol type: $id"
        Require ($metadata.repository.url -ceq $repository -and $metadata.repository.type -ceq 'git' -and $metadata.repository.commit -ceq $Commit) "Repository provenance: $id"
        Require ($metadata.license.type -ceq 'expression' -and $metadata.license.InnerText -ceq 'MIT') "MIT metadata: $id"
        Require ($metadata.copyright -ceq 'Copyright (c) 2026 Inceptus Robert Prokopczuk') "Copyright: $id"
        Require ($metadata.projectUrl -ceq 'https://inceptus.online/bpmn/') "Project URL: $id"
        Require ($metadata.readme -ceq 'README.md') "README metadata: $id"
        Require (-not [string]::IsNullOrWhiteSpace($metadata.releaseNotes)) "Release notes metadata: $id"
        foreach ($document in @('README.md', 'LICENSE', 'RELEASE-NOTES.md', 'THIRD-PARTY-NOTICES.md')) {
            $actual = Read-EntryText $zip $document
            Require ($actual -ceq [IO.File]::ReadAllText((Join-Path $PWD $document))) "Document content: $id/$document"
        }
        Require ((Read-EntryText $zip 'README.md').Contains('https://bpmn.inceptus.online')) "Application URL: $id"
        $groups = @($metadata.dependencies.group)
        Require ($groups.Count -eq 1 -and $groups[0].targetFramework -ceq 'net10.0') "Framework: $id"
        $dependencies = @($groups[0].dependency | Where-Object { $null -ne $_ })
        $external = if ($name -ceq 'Canvas2D') { @('Microsoft.JSInterop') } elseif ($name -ceq 'Bpmn.Blazor') { @('Microsoft.JSInterop', 'Microsoft.AspNetCore.Components.Web') } else { @() }
        Require ($dependencies.Count -eq ($edges[$name].Count + $external.Count)) "Dependency count: $id"
        $actualEdges = @($dependencies | Where-Object { $_.id.StartsWith($prefix) } | ForEach-Object { $_.id.Substring($prefix.Length) } | Sort-Object)
        Require (($actualEdges -join ',') -ceq (($edges[$name] | Sort-Object) -join ',')) "Family graph: $id"
        foreach ($dependency in $dependencies) {
            if ($dependency.id.StartsWith($prefix)) {
                $range = if ($name -ceq 'Canvas2D' -and $dependency.id -ceq ($prefix + 'Runtime')) { "[$Version]" } else { $Version }
                Require ($dependency.version -ceq $range) "Dependency range: $id -> $($dependency.id)"
            } else {
                Require ($dependency.id -cin $external -and $dependency.version -ceq '10.0.5') "External dependency: $id"
            }
        }
        $dllName = "lib/net10.0/$id.dll"
        $pdbName = "lib/net10.0/$id.pdb"
        $dll = Read-EntryBytes $zip $dllName
        $pdb = Read-EntryBytes $symbolZip $pdbName
        Require ($null -eq $zip.GetEntry($pdbName)) "PDB leaked into primary package: $id"
        $sourceLinkText = [ReleaseSymbols]::Inspect($pdb, $dll)
        $embeddedSources = [ReleaseSymbols]::EmbeddedSources($pdb)
        $leaks = @($embeddedSources | Where-Object { $_.ForbiddenPaths.Length -gt 0 })
        if ($leaks.Count -gt 0) {
            $details = $leaks | ForEach-Object { "$($_.Document): $($_.ForbiddenPaths.Length) occurrence(s); $($_.ForbiddenPaths[0])" }
            throw "EmbeddedSource contains machine-specific source paths: $id`n$($details -join "`n")"
        }
        if ($RequireSourceLink) {
            Require (-not [string]::IsNullOrWhiteSpace($sourceLinkText)) "Missing Source Link: $id"
            $links = ($sourceLinkText | ConvertFrom-Json -AsHashtable).documents
            Require ($links.ContainsKey('/_/*')) "Source Link does not cover deterministic source paths: $id"
            $documents = [ReleaseSymbols]::Documents($pdb)
            $unmapped = @($documents | Where-Object { -not $_.StartsWith('/_/', [StringComparison]::Ordinal) })
            Require ($unmapped.Count -eq 0) "Unmapped PDB document paths: $id $($unmapped -join ', ')"
            Require (@($documents | Where-Object { $_.StartsWith("/_/src/$id/") }).Count -gt 0) "Missing package source documents: $id"
            foreach ($url in $links.Values) {
                Require ($url -ceq "https://raw.githubusercontent.com/INCEPTUS-LABS/DocumentEngine/$Commit/*") "Unexpected Source Link target: $id"
            }
            foreach ($document in $documents) {
                if ($embeddedSources.Document -ccontains $document) { continue }
                $relative = $document.Substring(3)
                Require ($trackedSources.Contains($relative)) "Non-embedded PDB source is not in the committed tree: $id $document"
                $source = [IO.File]::ReadAllBytes((Join-Path $PWD $relative))
                Require ([ReleaseSymbols]::SourceMatches($pdb, $document, $source)) "Authored source checksum mismatch: $id $document"
            }
        }
        $entries = @($zip.Entries.FullName)
        Require (@($entries | Where-Object { $_ -match '(?i)(^|/)(bin|obj|tests|demo|\.git)/|\.csproj$|\.user$|UnitTests|IntegrationTests|Inceptus\.DocumentEngine\.Blazor\.(dll|pdb)' }).Count -eq 0) "Unexpected package content: $id"
        Require (@($entries | Where-Object { $_ -match '\.dll$' }).Count -eq 1) "Unexpected assembly: $id"
        if ($name -ceq 'Canvas2D') {
            Require ($entries -contains 'staticwebassets/inceptus.canvas2d.js') 'Canvas JavaScript missing.'
            foreach ($asset in @('index.html', 'inceptus.publish.js', 'styles.css')) {
                $bytes = [ReleaseSymbols]::Resource($dll, "Inceptus.DocumentEngine.Canvas2D.Publishing.$asset")
                $expected = [IO.File]::ReadAllBytes((Join-Path $PWD "src/$id/Publishing/Assets/$asset"))
                Require ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -ceq [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($expected))) "Embedded Publish asset: $asset"
            }
        }
        if ($name -ceq 'Bpmn.Blazor') {
            Require ($entries -contains 'staticwebassets/inceptus.presentation.js') 'Presentation JavaScript missing.'
            Require (@($entries | Where-Object { $_ -match '\.bundle\.scp\.css$' }).Count -eq 1) 'Scoped CSS missing.'
            foreach ($font in @('DejaVuSans-2.37.ttf', 'DejaVuSans-LICENSE.txt')) {
                $bytes = Read-EntryBytes $zip "staticwebassets/fonts/$font"
                $expected = [IO.File]::ReadAllBytes((Join-Path $PWD "src/$id/wwwroot/fonts/$font"))
                Require ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)) -ceq [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($expected))) "Font/attribution content: $font"
            }
        }
        [ordered]@{
            PackageId = $id; Version = $Version; Commit = $Commit
            PreparationOnly = -not [bool]$RequireSourceLink
            NupkgSha256 = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
            SnupkgSha256 = (Get-FileHash $symbolArchive -Algorithm SHA256).Hash.ToLowerInvariant()
            DllSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($dll)).ToLowerInvariant()
            PdbSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($pdb)).ToLowerInvariant()
            SourceLink = $sourceLinkText
            EmbeddedSources = @($embeddedSources)
            Dependencies = @($dependencies | ForEach-Object { "$($_.id) $($_.version)" })
        }
    } finally { $zip.Dispose(); $symbolZip.Dispose() }
}
$records | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $PackageDirectory 'provenance.json')
Write-Output 'Six package/symbol pairs passed release artifact inspection.'
