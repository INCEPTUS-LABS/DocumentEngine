[CmdletBinding()]
param([switch]$Persist)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseNotes.ps1')
$versions = @{}
foreach ($id in $documentEnginePackageIds) {
    $uri = 'https://api.nuget.org/v3-flatcontainer/' + $id.ToLowerInvariant() + '/index.json'
    $versions[$id] = @((Invoke-RestMethod -Uri $uri -ErrorAction Stop).versions)
}
$baseline = Resolve-PublicationBaseline $versions
$verifiedUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
if ($Persist) {
    $path = Join-Path $PSScriptRoot '../../Directory.Build.props'
    $document = [xml]::new()
    $document.PreserveWhitespace = $true
    $document.Load($path)
    $versionNode = $document.SelectSingleNode('/Project/PropertyGroup/PreviousPublishedVersion')
    $timeNode = $document.SelectSingleNode('/Project/PropertyGroup/PublicationBaselineVerifiedUtc')
    $packages = @($document.SelectNodes('/Project/ItemGroup/PublicationBaselinePackage'))
    if ($null -eq $versionNode -or $null -eq $timeNode -or
        (@($packages.Include | Sort-Object) -join ',') -cne (@($documentEnginePackageIds | Sort-Object) -join ',')) {
        throw 'Directory.Build.props does not contain the expected publication-baseline configuration.'
    }
    $versionNode.InnerText = $baseline
    $timeNode.InnerText = $verifiedUtc
    foreach ($package in $packages) { $package.SetAttribute('PublishedVersion', $baseline) }
    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Encoding = [Text.UTF8Encoding]::new($false)
    $settings.OmitXmlDeclaration = $true
    $writer = [Xml.XmlWriter]::Create($path, $settings)
    try { $document.Save($writer) } finally { $writer.Dispose() }
}
[pscustomobject]@{
    PreviousPublishedVersion = $baseline
    VerifiedUtc = $verifiedUtc
    Source = 'https://api.nuget.org/v3-flatcontainer/'
    Persisted = [bool]$Persist
    Packages = @($documentEnginePackageIds | ForEach-Object { [pscustomobject]@{ PackageId = $_; PublishedVersion = $baseline } })
} | ConvertTo-Json -Depth 4
