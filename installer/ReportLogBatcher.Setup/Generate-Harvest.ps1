param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

# Generates a WiX v5 harvest fragment from a published application directory.
# Each file becomes its own component under INSTALLFOLDER, preserving the
# directory structure of the publish output. All identifiers and component GUIDs
# are derived deterministically from the files' relative paths so rebuilds
# produce stable MSI content.

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $SourcePath)) {
    throw "Publish output directory not found: $SourcePath"
}

$files = Get-ChildItem -LiteralPath $SourcePath -Recurse -File | Sort-Object -Property FullName
if ($files.Count -eq 0) {
    throw "No files found under $SourcePath"
}

$outputDirectory = Split-Path -Path $OutputPath -Parent
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

function ConvertTo-XmlEscaped([string]$value) {
    return $value.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;').Replace("'", '&apos;')
}

function New-ShortId([string]$seed) {
    $hash = [System.Security.Cryptography.MD5]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($seed))
    return ([System.BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
}

function New-DeterministicGuid([string]$seed) {
    $hash = [System.Security.Cryptography.MD5]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($seed))
    return [System.Guid]::new($hash).ToString().ToUpperInvariant()
}

function Get-RelativePath([string]$baseDirectory, [string]$targetPath) {
    $baseParts = $baseDirectory.TrimEnd('\').Split('\')
    $targetParts = $targetPath.Split('\')
    $commonCount = 0
    while ($commonCount -lt $baseParts.Count -and $commonCount -lt $targetParts.Count -and
        $baseParts[$commonCount] -eq $targetParts[$commonCount]) {
        $commonCount++
    }
    if ($commonCount -eq 0) {
        throw "No common prefix between '$baseDirectory' and '$targetPath'."
    }
    $relativeParts = [System.Collections.ArrayList]::new()
    0..($baseParts.Count - $commonCount - 1) | ForEach-Object { [void]$relativeParts.Add('..') }
    for ($i = $commonCount; $i -lt $targetParts.Count; $i++) { [void]$relativeParts.Add($targetParts[$i]) }
    return ($relativeParts -join '\')
}

# Build a tree of relative directories, each node holding its files.
$tree = @{}
foreach ($file in $files) {
    $rel = $file.FullName.Substring($SourcePath.TrimEnd('\').Length).TrimStart('\')
    $relDirectory = Split-Path -Path $rel -Parent
    if ($relDirectory -eq '') { $relDirectory = '.' }
    $parts = $relDirectory.Split('\')
    $node = $tree
    foreach ($part in $parts) {
        if (-not $node.ContainsKey($part)) { $node[$part] = @{} }
        $node = $node[$part]
    }
    if (-not $node.ContainsKey('__files')) { $node['__files'] = [System.Collections.ArrayList]::new() }
    [void]$node['__files'].Add(@{ 'RelPath' = $rel; 'File' = $file })
}

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')

function Write-Node($sb, $node, $directoryPath, $directoryId, $depth) {
    $indent = '    ' * ($depth + 2)
    $childIndent = '    ' * ($depth + 3)
    foreach ($key in $node.Keys) {
        if ($key -eq '__files') { continue }
        $childPath = if ($directoryPath -eq '.') { $key } else { "$directoryPath\$key" }
        $childId = 'dir_' + (New-ShortId "directory:$childPath")
        [void]$sb.AppendLine("$indent<Directory Id=`"$childId`" Name=`"$(ConvertTo-XmlEscaped $key)`">")
        Write-Node $sb $node[$key] $childPath $childId ($depth + 1)
        if ($node[$key].ContainsKey('__files')) {
            foreach ($entry in $node[$key]['__files']) {
                [void]$sb.AppendLine("$childIndent<Component Id=`"cmp_$(New-ShortId "component:$($entry.RelPath)")`" Guid=`"$(New-DeterministicGuid "component:$($entry.RelPath)")`">")
$source = (Get-RelativePath $outputDirectory $entry.File.FullName).Replace('\', '/')
                [void]$sb.AppendLine("$childIndent  <File Source=`"$(ConvertTo-XmlEscaped $source)`" KeyPath=`"yes`" />")
                [void]$sb.AppendLine("$childIndent</Component>")
            }
        }
        [void]$sb.AppendLine("$indent</Directory>")
    }
}

Write-Node $sb $tree '.' 'INSTALLFOLDER' 1

if ($tree.ContainsKey('__files')) {
    $indent = '    ' * 3
    foreach ($entry in $tree['__files']) {
        [void]$sb.AppendLine("$indent<Component Id=`"cmp_$(New-ShortId "component:$($entry.RelPath)")`" Guid=`"$(New-DeterministicGuid "component:$($entry.RelPath)")`">")
        $source = (Get-RelativePath $outputDirectory $entry.File.FullName).Replace('\', '/')
        [void]$sb.AppendLine("$indent  <File Source=`"$(ConvertTo-XmlEscaped $source)`" KeyPath=`"yes`" />")
        [void]$sb.AppendLine("$indent</Component>")
    }
}

[void]$sb.AppendLine('    </DirectoryRef>')
[void]$sb.AppendLine('    <ComponentGroup Id="InstalledFiles">')
foreach ($file in $files) {
    $rel = $file.FullName.Substring($SourcePath.TrimEnd('\').Length).TrimStart('\')
    [void]$sb.AppendLine("      <ComponentRef Id=`"cmp_$(New-ShortId "component:$rel")`" />")
}
[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

Set-Content -LiteralPath $OutputPath -Value $sb.ToString() -Encoding UTF8