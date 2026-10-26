param(
    [Parameter(Mandatory=$true)][string]$RuntimeDirectory,
    [Parameter(Mandatory=$true)][string]$ArchivePath,
    [Parameter(Mandatory=$true)][string]$Version,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Use a stable numeric version.' }
$toolVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $RuntimeDirectory 'PclLocalTools.dll')).Version.ToString(3)
$helperVersion = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $RuntimeDirectory 'PCL Update Helper.exe')).Version.ToString(3)
if ($toolVersion -ne $Version -or $helperVersion -ne $Version) { throw 'Compiled application/helper version does not match the update manifest.' }
$names = @('Plain Craft Launcher (PCL) 本地修改版.exe','PclLocalTools.dll','PCL Local Tools.exe','PCL Local Tools.exe.config','PCL Update Helper.exe','PCL Update Helper.exe.config')
$files = @($names | ForEach-Object {
    $path = Join-Path $RuntimeDirectory $_
    $file = Get-Item -LiteralPath $path
    [ordered]@{ Name=$_.ToString(); Bytes=$file.Length; Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
})
$manifest = [ordered]@{ Format=1; Version=$Version; ArchiveName=[IO.Path]::GetFileName($ArchivePath); ArchiveSha256=(Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash; Files=$files }
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath),($manifest | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
