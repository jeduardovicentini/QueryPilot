<#
  Teste de integração no SSMS 22 real (precisa da extensão instalada e do LocalDB).
  - usa uma pasta de dados isolada (não mexe no histórico/configuração do usuário)
  - abre o SSMS conectado no LocalDB, o pacote roda SelfTest.cs e grava result.txt + screenshots
#>
param(
  [string]$Out = (Join-Path $PSScriptRoot '..\_scratch\selftest'),
  [int]$TimeoutSec = 240,
  [switch]$KeepOpen,
  [string]$Scope = ''
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

# banco de teste do autocomplete (tabelas com FKs, outro schema e uma procedure)
function Invoke-LocalDb([string]$db, [string]$sql) {
  $cn = New-Object System.Data.SqlClient.SqlConnection "Server=(localdb)\MSSQLLocalDB;Database=$db;Integrated Security=true"
  $cn.Open()
  try { $cmd = $cn.CreateCommand(); $cmd.CommandText = $sql; [void]$cmd.ExecuteNonQuery() } finally { $cn.Dispose() }
}
Invoke-LocalDb 'master' "IF DB_ID('QueryPilotSelfTest') IS NULL CREATE DATABASE QueryPilotSelfTest;"
Invoke-LocalDb 'QueryPilotSelfTest' @"
IF OBJECT_ID('dbo.OrderItem') IS NOT NULL DROP TABLE dbo.OrderItem;
IF OBJECT_ID('dbo.[Order]') IS NOT NULL DROP TABLE dbo.[Order];
IF OBJECT_ID('dbo.Customer') IS NOT NULL DROP TABLE dbo.Customer;
IF OBJECT_ID('sales.Product') IS NOT NULL DROP TABLE sales.Product;
IF SCHEMA_ID('sales') IS NULL EXEC('CREATE SCHEMA sales');
CREATE TABLE dbo.Customer (CustomerID int IDENTITY PRIMARY KEY, Name nvarchar(100) NOT NULL, Email varchar(200) NULL);
CREATE TABLE dbo.[Order] (OrderID int IDENTITY PRIMARY KEY, CustomerID int NOT NULL REFERENCES dbo.Customer(CustomerID), OrderDate datetime2 NOT NULL DEFAULT SYSDATETIME(), Total decimal(18,2) NOT NULL DEFAULT 0);
CREATE TABLE sales.Product (ProductID int PRIMARY KEY, Name nvarchar(100), Price money);
CREATE TABLE dbo.OrderItem (OrderItemID int IDENTITY PRIMARY KEY, OrderID int NOT NULL REFERENCES dbo.[Order](OrderID), ProductID int NOT NULL REFERENCES sales.Product(ProductID), Qty int NOT NULL);
"@
Invoke-LocalDb 'QueryPilotSelfTest' "CREATE OR ALTER PROCEDURE dbo.usp_GetCustomer @id int AS SELECT * FROM dbo.Customer WHERE CustomerID = @id;"

$env:SQLTOOLS_DATA_DIR = $data
$env:SQLTOOLS_SELFTEST = $Out
$env:SQLTOOLS_SELFTEST_SCOPE = $Scope
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
