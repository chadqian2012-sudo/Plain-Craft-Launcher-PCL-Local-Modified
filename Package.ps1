$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$runtime = Join-Path $root 'dist\PCL-2.99.2-preview'
if (-not (Test-Path -LiteralPath (Join-Path $runtime 'patch-audit.json'))) { throw 'Run Build.ps1 first.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$source = Join-Path $root "dist\source-staging-$stamp"
New-Item -ItemType Directory -Path $source -Force | Out-Null
foreach ($name in @('README.md','VERSIONS.md','使用说明.md','更新说明.md','发布说明.md','LICENCE','CONTRIBUTORS.md','Build.ps1','Package.ps1')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $source
}
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
Get-ChildItem -LiteralPath (Join-Path $root 'verification\fixtures') -Filter 'toolbox-tab-*.png' | Copy-Item -Destination $verification
Add-Type -AssemblyName System.IO.Compression.FileSystem
$launcherZip = Join-Path $root "dist\PCL-2.99.2-preview-launcher-$stamp.zip"
$sourceZip = Join-Path $root "dist\PCL-2.99.2-preview-source-$stamp.zip"
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
