$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$runtime = Join-Path $root 'dist\PCL-2.99.3'
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'patch-audit.json'))) { throw 'Run Build.ps1 first.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$source = Join-Path $root "dist\source-staging-$stamp"
New-Item -ItemType Directory -Path $source -Force | Out-Null
foreach ($name in @('README.md','VERSIONS.md','使用说明.md','更新说明.md','发布说明.md','UPDATING.md','LICENCE','CONTRIBUTORS.md','Build.ps1','Package.ps1','Create-UpdateManifest.ps1')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $source
}
$acceptanceReport = Join-Path $root 'verification\FORMAL-ACCEPTANCE-2.99.3.md'
if (Test-Path -LiteralPath $acceptanceReport) { Copy-Item -LiteralPath $acceptanceReport -Destination (Join-Path $source 'FORMAL-ACCEPTANCE-2.99.3.md') }
foreach ($name in @('patch-source','toolbox-source','upstream-source')) {
    $from = Join-Path $root $name
    Get-ChildItem -LiteralPath $from -Recurse -File -Force | ForEach-Object {
        $relative = $_.FullName.Substring($from.TrimEnd('\').Length + 1)
        if ($relative -match '(^|[\\/])(bin|obj|\.git|\.buildcache)([\\/]|$)') { return }
        $target = Join-Path (Join-Path $source $name) $relative
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target
    }
}
$verification = Join-Path $source 'verification'
New-Item -ItemType Directory -Path $verification -Force | Out-Null
Get-Content -LiteralPath (Join-Path $root 'verification\fixtures\latest-results.txt') | Where-Object { $_ -notlike 'Fixture directory:*' } | Set-Content -LiteralPath (Join-Path $verification 'synthetic-results.txt') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $runtime 'patch-audit.json') -Destination $verification
if (Test-Path -LiteralPath (Join-Path $root 'verification\live-update-check.txt')) { Copy-Item -LiteralPath (Join-Path $root 'verification\live-update-check.txt') -Destination $verification }
Get-ChildItem -LiteralPath (Join-Path $root 'verification\fixtures') -Filter 'toolbox-tab-*.png' | Copy-Item -Destination $verification
Get-ChildItem -LiteralPath (Join-Path $root 'verification\fixtures') -Recurse -Filter 'update-notice.png' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Copy-Item -Destination $verification
Add-Type -AssemblyName System.IO.Compression.FileSystem
$launcherZip = Join-Path $root "dist\PCL-2.99.3-launcher-$stamp.zip"
$sourceZip = Join-Path $root "dist\PCL-2.99.3-source-$stamp.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($runtime, $launcherZip, [IO.Compression.CompressionLevel]::Optimal, $false)
[IO.Compression.ZipFile]::CreateFromDirectory($source, $sourceZip, [IO.Compression.CompressionLevel]::Optimal, $false)
foreach ($file in @($launcherZip,$sourceZip)) {
    $zip = [IO.Compression.ZipFile]::OpenRead($file)
    try {
        $invalid = @($zip.Entries | Where-Object { $_.FullName -match '(^|[\\/])(saves|\.minecraft|\.git|\.buildcache|bin|obj)([\\/]|$)' })
        if ($invalid.Count -gt 0) { throw "Unexpected personal/generated directory in package: $file" }
    } finally { $zip.Dispose() }
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash,[IO.Path]::GetFileName($file) | Add-Content -LiteralPath (Join-Path $root "dist\PACKAGE-SHA256-$stamp.txt") -Encoding utf8
}
Write-Output $launcherZip
Write-Output $sourceZip
$manifest = Join-Path $root 'dist\PCL-update.json'
& (Join-Path $root 'Create-UpdateManifest.ps1') -RuntimeDirectory $runtime -ArchivePath $launcherZip -Version '2.99.3' -OutputPath $manifest
'{0}  {1}' -f (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash,'PCL-update.json' | Add-Content -LiteralPath (Join-Path $root "dist\PACKAGE-SHA256-$stamp.txt") -Encoding utf8
Write-Output $manifest
