# Shared release-preparation and inspection functions. Loading this file performs no network access.
$documentEnginePackageIds = @(
    'Inceptus.DocumentEngine.Contracts', 'Inceptus.DocumentEngine.Runtime', 'Inceptus.DocumentEngine.Bpmn',
    'Inceptus.DocumentEngine.Organizational', 'Inceptus.DocumentEngine.Canvas2D',
    'Inceptus.DocumentEngine.Bpmn.Blazor', 'Inceptus.Bpmn.Integration'
)
function ConvertTo-ReleaseVersion([string]$Value) {
    $parsed = $null
    if ($Value -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or -not [version]::TryParse($Value, [ref]$parsed)) {
        throw "Invalid stable release version: $Value"
    }
    return $parsed
}
function Assert-PublicationBaseline([string]$Version, [string]$PreviousPublishedVersion, $Packages) {
    $current = ConvertTo-ReleaseVersion $Version
    $baseline = ConvertTo-ReleaseVersion $PreviousPublishedVersion
    if ($baseline -ge $current -or
        (@($Packages.PackageId | Sort-Object) -join ',') -cne (@($documentEnginePackageIds | Sort-Object) -join ',') -or
        @($Packages | Where-Object { $_.PublishedVersion -cne $PreviousPublishedVersion }).Count -ne 0) {
        throw "Invalid or inconsistent publication baseline for $Version."
    }
}
function Get-CumulativeReleaseNotes([string]$Text, [string]$Version, [string]$PreviousPublishedVersion) {
    $current = ConvertTo-ReleaseVersion $Version
    $baseline = ConvertTo-ReleaseVersion $PreviousPublishedVersion
    if ($baseline -ge $current) { throw "Invalid publication baseline: $PreviousPublishedVersion must precede $Version." }
    $text = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
    $headings = [regex]::Matches($text, '(?m)^##[ \t]+(?<version>[0-9]+\.[^\s]+)(?:[ \t]+[^\n]*)?$')
    $selected = [Collections.Generic.HashSet[version]]::new()
    $notes = [Text.StringBuilder]::new()
    for ($index = 0; $index -lt $headings.Count; $index++) {
        $heading = $headings[$index]
        $sectionVersion = ConvertTo-ReleaseVersion $heading.Groups['version'].Value
        if ($sectionVersion -le $baseline -or $sectionVersion -gt $current) { continue }
        $end = if ($index + 1 -lt $headings.Count) { $headings[$index + 1].Index } else { $text.Length }
        $bodyStart = $heading.Index + $heading.Length
        if (-not $selected.Add($sectionVersion) -or [string]::IsNullOrWhiteSpace($text.Substring($bodyStart, $end - $bodyStart))) {
            throw "Missing, empty or ambiguous release notes section: $sectionVersion."
        }
        [void]$notes.Append($text.Substring($heading.Index, $end - $heading.Index))
    }
    $result = $notes.ToString().TrimEnd()
    if (-not $selected.Contains($current) -or $result.Length -eq 0 -or $result.Length -gt 35000) {
        throw "Cumulative release notes must include version $Version and contain between 1 and 35000 characters; found $($result.Length)."
    }
    return $result
}
function Resolve-PublicationBaseline($PackageVersions) {
    if ((@($PackageVersions.Keys | Sort-Object) -join ',') -cne (@($documentEnginePackageIds | Sort-Object) -join ',')) {
        throw 'Publication baseline requires exactly the seven package IDs.'
    }
    $parsed = @{}
    foreach ($id in $documentEnginePackageIds) {
        $versions = @($PackageVersions[$id] | ForEach-Object { ConvertTo-ReleaseVersion $_ } | Sort-Object)
        if ($versions.Count -eq 0 -or @($versions | Select-Object -Unique).Count -ne $versions.Count) {
            throw "Missing or ambiguous published versions: $id"
        }
        $parsed[$id] = $versions
    }
    # Earlier six-package releases predate Integration; compare from the family's first possible complete version.
    $familyStart = @($documentEnginePackageIds | ForEach-Object { $parsed[$_][0] } | Sort-Object)[-1]
    $versionsToCheck = @($parsed.Values | ForEach-Object { $_ } | Where-Object { $_ -ge $familyStart } | Sort-Object -Unique)
    foreach ($version in $versionsToCheck) {
        $missing = @($documentEnginePackageIds | Where-Object { $parsed[$_] -notcontains $version })
        if ($missing.Count -gt 0) {
            throw "Partial or inconsistent publication at $version; missing packages: $($missing -join ', '). No baseline was persisted."
        }
    }
    if ($versionsToCheck.Count -eq 0) { throw 'No complete publication baseline exists.' }
    return $versionsToCheck[-1].ToString()
}
