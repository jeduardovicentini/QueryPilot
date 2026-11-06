<#
  Compila, roda os testes e gera dist\<Produto>-<versão>.vsix + scripts de instalação.
  Uso:  .\build.ps1            (Release)
        .\build.ps1 -Install   (também instala no SSMS 22 do usuário atual)
#>
param([switch]$Install, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild não encontrado (instale o Visual Studio ou Build Tools).' }

[xml]$props = Get-Content (Join-Path $PSScriptRoot 'Product.props')
$name = $props.Project.PropertyGroup.ProductName
$version = $props.Project.PropertyGroup.ProductVersion
$id = $props.Project.PropertyGroup.ProductId

if (-not $SkipTests) {
  Write-Host "== Testes" -ForegroundColor Cyan
  dotnet test tests\SqlTools.Core.Tests -c Release -v q --nologo
  if ($LASTEXITCODE -ne 0) { throw 'Testes falharam.' }
}

Write-Host "== Build $name $version" -ForegroundColor Cyan
& $msbuild src\SqlTools.Ssms\SqlTools.Ssms.csproj -restore -p:Configuration=Release -v:m -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build falhou.' }

$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$vsix = Join-Path $dist "$name-$version.vsix"
Copy-Item src\SqlTools.Ssms\bin\Release\net48\SqlTools.Ssms.vsix $vsix -Force

$installer = 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe'
Copy-Item tools\Allow-TopMenu.ps1 $dist -Force
@"
@echo off
rem Instala $name no SSMS 22 do usuario atual (feche o SSMS antes).
"$installer" /quiet /shutdownprocesses "%~dp0$name-$version.vsix"
if errorlevel 1 (echo Falhou. Veja %TEMP%\VSIXInstaller_*.log & pause & exit /b 1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Allow-TopMenu.ps1"
echo $name instalado. Abra o SSMS.
pause
"@ | Set-Content (Join-Path $dist "Instalar.cmd") -Encoding ASCII
@"
@echo off
rem Remove $name do SSMS 22 do usuario atual.
"$installer" /quiet /shutdownprocesses /uninstall:$id
if errorlevel 1 (echo Falhou. Veja %TEMP%\VSIXInstaller_*.log) else (echo $name removido.)
pause
"@ | Set-Content (Join-Path $dist "Desinstalar.cmd") -Encoding ASCII

Write-Host "== Pacote: $vsix" -ForegroundColor Green

if ($Install) {
  Write-Host "== Instalando no SSMS 22" -ForegroundColor Cyan
  # mesma versão já instalada é recusada; remove antes (ignora se não estiver instalada)
  Start-Process $installer -ArgumentList '/quiet', '/shutdownprocesses', "/uninstall:$id" -Wait | Out-Null
  $p = Start-Process $installer -ArgumentList '/quiet', '/shutdownprocesses', "`"$vsix`"" -Wait -PassThru
  if ($p.ExitCode -ne 0) { throw "VSIXInstaller retornou $($p.ExitCode). Veja %TEMP%\VSIXInstaller_*.log" }
  & (Join-Path $PSScriptRoot 'tools\Allow-TopMenu.ps1')
  Write-Host "Instalado." -ForegroundColor Green
}
