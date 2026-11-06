<#
  Libera o menu de topo da extensão na barra principal do SSMS 21/22 (o mesmo que Extensions › Customize Menu).
  Grava em Menus\AllowedExtensionMenus do registro privado (privateregistry.bin). Exige o SSMS fechado.
  Uso: .\Allow-TopMenu.ps1 [-Remove]
#>
param(
    [string]$MenuId = '{9B2C4D6E-1A3F-4E5B-8C7D-6F0A2B4C8E12},4096',
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
if (-not ('SqlToolsReg.Native' -as [type])) {
    Add-Type -Namespace SqlToolsReg -Name Native -MemberDefinition '[DllImport("advapi32.dll", CharSet=CharSet.Unicode)] public static extern int RegLoadAppKey(string file, out IntPtr h, int sam, int opt, int res);'
}
Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft\SSMS') -Directory -Filter '2*.0_*' -ErrorAction SilentlyContinue | ForEach-Object {
    $hive = Join-Path $_.FullName 'privateregistry.bin'
    if (-not (Test-Path $hive)) { return }
    $h = [IntPtr]::Zero
    $rc = [SqlToolsReg.Native]::RegLoadAppKey($hive, [ref]$h, 0xF003F, 0, 0)
    if ($rc -ne 0) { Write-Warning "Não abriu $hive (rc=$rc). O SSMS está aberto?"; return }
    $root = [Microsoft.Win32.RegistryKey]::FromHandle((New-Object Microsoft.Win32.SafeHandles.SafeRegistryHandle($h, $true)))
    try {
        $user = $root.OpenSubKey("Software\Microsoft\SSMS\$($_.Name)", $true)
        if (-not $user) { return }
        $menus = $user.CreateSubKey('Menus\AllowedExtensionMenus')
        $items = @(([string]$menus.GetValue('')) -split ';' | Where-Object { $_ -and $_ -ne $MenuId })
        if (-not $Remove) { $items += $MenuId }
        $menus.SetValue('', ($items -join ';'), 'String')
        $user.SetValue('ConfigurationChanged', [DateTime]::Now.ToFileTimeUtc(), 'QWord')
        Write-Host "Menu $(if ($Remove) { 'removido da' } else { 'liberado na' }) barra principal ($($_.Name))."
    } finally { $root.Dispose() }
}
