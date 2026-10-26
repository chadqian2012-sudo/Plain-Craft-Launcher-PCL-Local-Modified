param(
    [Parameter(Mandatory=$true)][string]$OriginalExe,
    [string]$DotNet = 'dotnet',
    [string]$FixtureDirectory = ''
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root '.buildcache\cli'
$env:NUGET_PACKAGES = Join-Path $root '.buildcache\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $root '.buildcache\http'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $root '.buildcache\plugins'
$env:TEMP = Join-Path $root '.buildcache\temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$out = Join-Path $root 'dist\PCL-2.99.3'
New-Item -ItemType Directory -Path $out -Force | Out-Null
function Build-Project([string]$path) {
    & $DotNet build $path -c Release --disable-build-servers -m:1 -p:UseSharedCompilation=false -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $path" }
}
Build-Project (Join-Path $root 'toolbox-source\Host\Toolbox.Host.csproj')
Build-Project (Join-Path $root 'toolbox-source\UpdaterHost\UpdaterHost.csproj')
Build-Project (Join-Path $root 'patch-source\LauncherPatch.csproj')
Build-Project (Join-Path $root 'toolbox-source\Tests\Toolbox.Tests.csproj')
if ([string]::IsNullOrWhiteSpace($FixtureDirectory)) { $FixtureDirectory = Join-Path $root '.test' }
& (Join-Path $root 'toolbox-source\Tests\bin\Release\net48\Toolbox.Tests.exe') $FixtureDirectory
if ($LASTEXITCODE -ne 0) { throw 'Synthetic-data verification failed.' }
$evidence = Join-Path $root 'verification\fixtures'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
if ([IO.Path]::GetFullPath($FixtureDirectory) -ne [IO.Path]::GetFullPath($evidence)) {
    Copy-Item -LiteralPath (Join-Path $FixtureDirectory 'latest-results.txt') -Destination $evidence -Force
    Get-ChildItem -LiteralPath $FixtureDirectory -Filter 'toolbox-tab-*.png' | Copy-Item -Destination $evidence -Force
    Get-ChildItem -LiteralPath $FixtureDirectory -Recurse -Filter 'update-notice.png' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Copy-Item -Destination $evidence -Force
}
Copy-Item -LiteralPath (Join-Path $root 'toolbox-source\bin\Release\net48\PclLocalTools.dll') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'toolbox-source\Host\bin\Release\net48\PCL Local Tools.exe') -Destination $out -Force
$hostConfig = Join-Path $root 'toolbox-source\Host\bin\Release\net48\PCL Local Tools.exe.config'
if (Test-Path -LiteralPath $hostConfig) { Copy-Item -LiteralPath $hostConfig -Destination $out -Force }
Copy-Item -LiteralPath (Join-Path $root 'toolbox-source\UpdaterHost\bin\Release\net48\PCL Update Helper.exe'),(Join-Path $root 'toolbox-source\UpdaterHost\bin\Release\net48\PCL Update Helper.exe.config') -Destination $out -Force
$exe = Join-Path $out 'Plain Craft Launcher (PCL) 本地修改版.exe'
& $DotNet (Join-Path $root 'patch-source\bin\Release\net10.0\LauncherPatch.dll') patch $OriginalExe $exe (Join-Path $out 'patch-audit.json')
if ($LASTEXITCODE -ne 0) { throw 'Launcher patch audit failed.' }
& $DotNet (Join-Path $root 'patch-source\bin\Release\net10.0\LauncherPatch.dll') metadata-check $OriginalExe $exe
if ($LASTEXITCODE -ne 0) { throw 'Launcher metadata audit failed.' }
Copy-Item -LiteralPath (Join-Path $root 'LICENCE'),(Join-Path $root 'CONTRIBUTORS.md'),(Join-Path $root '使用说明.md'),(Join-Path $root '更新说明.md'),(Join-Path $root '发布说明.md'),(Join-Path $root 'UPDATING.md') -Destination $out -Force
$hashes = Get-ChildItem -LiteralPath $out -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash,$_.Name }
$hashes | Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt') -Encoding utf8
Write-Output "Build complete: $out"
