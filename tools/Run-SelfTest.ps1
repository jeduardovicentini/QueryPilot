<#
  Teste de integração no SSMS 22 real (precisa da extensão instalada e do LocalDB).
  - usa uma pasta de dados isolada (não mexe no histórico/configuração do usuário)
  - abre o SSMS conectado no LocalDB, o pacote roda SelfTest.cs e grava result.txt + screenshots
#>
param(
  [string]$Out = (Join-Path $PSScriptRoot '..\_scratch\selftest'),
  [int]$TimeoutSec = 240,
  [switch]$KeepOpen
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SsmsUi.ps1')

$Out = [IO.Path]::GetFullPath($Out)
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
$data = Join-Path $Out 'data'
New-Item -ItemType Directory -Force $data | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'settings.json'),
  '{"ColorRules":[{"ServerPattern":"(localdb)\\*","Color":"#D32F2F","Label":"teste"}]}')

sqllocaldb start MSSQLLocalDB | Out-Null
$env:SQLTOOLS_DATA_DIR = $data
$env:SQLTOOLS_SELFTEST = $Out
$env:SQLTOOLS_SELFTEST_EXIT = if ($KeepOpen) { '0' } else { '1' }

$ssms = 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe'
$p = Start-Process $ssms -ArgumentList '-S', '"(localdb)\MSSQLLocalDB"', '-C', '-nosplash' -PassThru
$result = Join-Path $Out 'result.txt'
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline -and -not (Test-Path $result)) {
  # o SSMS pede confirmação para conectar no servidor passado por -S
  [void][SsmsWin]::ClickIfDialog([uint32]$p.Id, 'Connect to the following server', 'Yes')
  Start-Sleep -Seconds 2
}

if (Test-Path $result) { Get-Content $result } else { 'TIMEOUT: result.txt não foi gerado' }
if (-not $KeepOpen) {
  if (-not $p.WaitForExit(20000)) { 'SSMS não fechou sozinho; encerrando.'; Stop-Process -Id $p.Id -Force }
}
'--- log da extensão'
Get-ChildItem (Join-Path $data 'logs') -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Tail 80 }
if (Test-Path $result) { if ((Get-Content $result -Raw) -match 'RESULT: PASS') { exit 0 } }
exit 1
