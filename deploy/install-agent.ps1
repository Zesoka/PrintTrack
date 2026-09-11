<#
    Instala el agente del Auditor de impresiones como servicio de Windows (LocalSystem).
    Ejecutar en una consola PowerShell ELEVADA en cada PC / servidor de impresión.

    Ejemplo:
      .\install-agent.ps1 -ServerUrl "http://auditor.miempresa.local:8095" -ApiKey "pt_xxxxx" -SourceDir "C:\ruta\publish\Agente"
#>
param(
    [Parameter(Mandatory = $true)][string]$ServerUrl,
    [Parameter(Mandatory = $true)][string]$ApiKey,
    [Parameter(Mandatory = $true)][string]$SourceDir,
    [string]$InstallDir = "C:\Program Files\Auditor de impresiones\Agente",
    [string[]]$Printers = @()
)

$ErrorActionPreference = "Stop"
$svcName = "PrintTrackAgent"   # id interno estable — el nombre visible se define abajo

if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Ejecute este script como Administrador."
}

if (-not (Test-Path $SourceDir)) {
    throw "No se encontro la carpeta: $SourceDir. Publique el agente primero (ver instructivo)."
}

Write-Host "Deteniendo servicio previo (si existe)..."
sc.exe stop $svcName | Out-Null
Start-Sleep -Seconds 2
sc.exe delete $svcName | Out-Null
Start-Sleep -Seconds 1

Write-Host "Copiando binarios a $InstallDir ..."
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item "$SourceDir\*" $InstallDir -Recurse -Force

$cfgPath = Join-Path $InstallDir "appsettings.json"
$cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
$cfg.Agent.ServerUrl = $ServerUrl
$cfg.Agent.ApiKey    = $ApiKey
$cfg.Agent.Printers  = $Printers
($cfg | ConvertTo-Json -Depth 10) | Set-Content $cfgPath -Encoding UTF8

$exe = Join-Path $InstallDir "PrintTrack.Agent.exe"
Write-Host "Creando servicio ..."
sc.exe create $svcName binPath= "`"$exe`"" start= auto DisplayName= "Auditor de impresiones - Agente" obj= "LocalSystem" | Out-Null
sc.exe description $svcName "Registra los trabajos de impresion enviados desde este equipo." | Out-Null
sc.exe failure $svcName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

Write-Host "Iniciando servicio ..."
sc.exe start $svcName | Out-Null

Write-Host "Listo. Estado:"
sc.exe query $svcName
