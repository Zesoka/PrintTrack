<# Desinstala el servicio del agente. Consola PowerShell elevada. #>
param([string]$InstallDir = "C:\Program Files\Auditor de impresiones\Agente")

$ErrorActionPreference = "SilentlyContinue"
$svcName = "PrintTrackAgent"

sc.exe stop $svcName | Out-Null
Start-Sleep -Seconds 2
sc.exe delete $svcName | Out-Null
Start-Sleep -Seconds 1
Remove-Item $InstallDir -Recurse -Force
Write-Host "Servicio y binarios eliminados."
