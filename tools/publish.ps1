param([switch]$Offline, [string]$OutputName = 'Vocabulary-win-x64')
$ErrorActionPreference = 'Stop'
if($OutputName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$'){throw '发布名称应以字母或数字开头，后接字母、数字、点、下划线或短横线。'}
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$publish = Join-Path (Join-Path $root 'artifacts') $OutputName
$cache = Join-Path $root '.cache\packages'
$restoreArgs = @("-p:RestorePackagesPath=$cache")
if($Offline){ $restoreArgs += @('--source', $cache, '-p:NuGetAudit=false') }
dotnet publish src/Vocabulary/Vocabulary.csproj -c Release -r win-x64 -p:Platform=x64 -o $publish @restoreArgs
if ($LASTEXITCODE) { throw "发布失败 $LASTEXITCODE" }

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsRoot = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if(!$vsRoot){throw '打包需要 Visual Studio 的 x64 C++ 可再分发运行库。'}
$crt = Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC\Redist\MSVC') -Directory |
    Sort-Object Name -Descending | ForEach-Object {
        Get-ChildItem -Path (Join-Path $_.FullName 'x64\Microsoft.VC*.CRT') -Directory -ErrorAction SilentlyContinue
    } | Select-Object -First 1
if(!$crt){throw '未找到 x64 CRT。'}
Get-ChildItem -LiteralPath $crt.FullName -File -Filter '*.dll' | Copy-Item -Destination $publish

$assets = Get-Content src/Vocabulary/obj/project.assets.json -Raw | ConvertFrom-Json
$notices = Join-Path $publish 'Notices'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
foreach($lib in $assets.libraries.PSObject.Properties){
    if($lib.Value.type -ne 'package'){continue}
    $path=Join-Path $cache $lib.Value.path
    Get-ChildItem -LiteralPath $path -File | Where-Object Name -match '^(LICENSE|NOTICE|THIRD-PARTY-NOTICES)(\.txt|\.md)?$' |
        ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $notices ($lib.Name.Replace('/','-')+'-'+$_.Name))}
}
Copy-Item README.md -Destination $publish
$previewDir = Join-Path $publish 'docs'
New-Item -ItemType Directory -Path $previewDir -Force | Out-Null
Copy-Item -LiteralPath docs/images -Destination $previewDir -Recurse -Force
$zip = Join-Path (Join-Path $root 'artifacts') ($OutputName + '.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$staged = Join-Path (Split-Path $zip) ([guid]::NewGuid().ToString()+'.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($publish,$staged,[IO.Compression.CompressionLevel]::Optimal,$false)
Move-Item -LiteralPath $staged -Destination $zip -Force
Get-Item -LiteralPath $zip | Select-Object FullName, Length
