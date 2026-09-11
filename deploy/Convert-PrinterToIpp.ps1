<#
.SYNOPSIS
    Reconecta una impresora de Windows por IPP (en vez de TCP/IP crudo / RAW-9100 o LPR) para que
    su Job Log HP vea usuario, documento y páginas/hojas reales en Auditor de impresiones.

.DESCRIPTION
    Hoy la mayoría de las PCs imprimen contra la impresora por un puerto "Standard TCP/IP" (RAW
    o LPR). Ese camino no le pasa a la impresora el usuario/documento reales del lado del trabajo
    IPP, así que su Registro de trabajos solo puede loguearlos con datos genéricos
    ("Sistema de Gestión" / CSC\usuario, sin páginas). Reconectando la cola por la URL IPP del
    equipo (`http://<ip>:631/ipp/print`), Windows usa el cliente IPP nativo (Microsoft IPP Class
    Driver) y la impresora sí registra el trabajo con páginas reales — confirmado contra la M630
    real.

    El script:
      1. Detecta la cola existente que apunta a esa IP (o usa -QueueName si se la pasás).
      2. Agrega la nueva conexión IPP con Add-Printer -ConnectionName.
      3. La renombra para que coincida con la cola vieja (así no rompe accesos directos / GPO).
      4. Opcionalmente borra la cola/puerto vieja (-RemoveOldPrinter) y restaura si era la
         predeterminada.
      5. Opcionalmente manda una página de prueba (-TestPrint).

    Sin -RemoveOldPrinter, las dos colas conviven (la nueva queda como "<Nombre> (IPP)") — pensado
    para probar primero en un PC piloto antes de un rollout por GPO/script de login.

.PARAMETER PrinterIp
    IP de la impresora, ej. 192.168.30.4.

.PARAMETER QueueName
    Nombre de la cola actual en este PC (ej. "Madero 1"). Si se omite, se intenta detectar
    automáticamente buscando una impresora cuyo puerto apunte a -PrinterIp.

.PARAMETER Port
    Puerto IPP del equipo. Default 631 (confirmado abierto en la M630 real).

.PARAMETER UseHttps
    Usa https:// en vez de http://. Requiere que Windows acepte el certificado autofirmado del
    equipo — probá primero SIN este flag.

.PARAMETER RemoveOldPrinter
    Borra la cola y el puerto TCP/IP viejos una vez que la nueva por IPP quedó instalada. Sin
    este flag quedan las dos en paralelo (recomendado para el piloto).

.PARAMETER TestPrint
    Manda una página de prueba a la impresora nueva al terminar.

.EXAMPLE
    # Piloto: agrega la cola por IPP en paralelo, sin tocar la vieja ni imprimir de prueba.
    .\Convert-PrinterToIpp.ps1 -PrinterIp 192.168.30.4 -QueueName "Madero 1"

.EXAMPLE
    # Rollout: mismo nombre final, borra la vieja, imprime de prueba.
    .\Convert-PrinterToIpp.ps1 -PrinterIp 192.168.30.4 -QueueName "Madero 1" -RemoveOldPrinter -TestPrint

.NOTES
    Requiere ejecutarse como Administrador local del PC (Add-Printer/Remove-Printer lo piden).
    Si `Add-Printer -ConnectionName` no anda en tu build de Windows (algunas versiones viejas de
    Win10 no tienen bien el cliente IPP), el mismo resultado se logra a mano: Configuración →
    Impresoras y escáneres → Agregar dispositivo → "El dispositivo que quiero no está en la
    lista" → "Seleccionar una impresora compartida por nombre" → pegar la URL IPP.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$PrinterIp,
    [string]$QueueName,
    [int]$Port = 631,
    [switch]$UseHttps,
    [switch]$RemoveOldPrinter,
    [switch]$TestPrint
)

$ErrorActionPreference = 'Stop'

function Write-Step  ($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-Ok    ($msg) { Write-Host "    OK: $msg" -ForegroundColor Green }
function Write-Warn2 ($msg) { Write-Host "    AVISO: $msg" -ForegroundColor Yellow }

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Corré esta consola como Administrador (Add-Printer / Remove-Printer lo requieren)."
}

$scheme = if ($UseHttps) { 'https' } else { 'http' }
$ippUrl = "{0}://{1}:{2}/ipp/print" -f $scheme, $PrinterIp, $Port

Write-Step "Impresora IPP objetivo: $ippUrl"

# 1) Detectar la cola vieja que apunta a esa IP (si no se pasó -QueueName)
$oldPrinter = $null
if ($QueueName) {
    $oldPrinter = Get-Printer -Name $QueueName -ErrorAction SilentlyContinue
}
if (-not $oldPrinter) {
    $ports = Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.PrinterHostAddress -eq $PrinterIp }
    foreach ($p in $ports) {
        $cand = Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.PortName -eq $p.Name } | Select-Object -First 1
        if ($cand) { $oldPrinter = $cand; break }
    }
}

$wasDefault = $false
if ($oldPrinter) {
    $QueueName  = $oldPrinter.Name
    $defaultCim = Get-CimInstance -ClassName Win32_Printer -Filter "Default = TRUE" -ErrorAction SilentlyContinue
    $wasDefault = $defaultCim -and ($defaultCim.Name -eq $oldPrinter.Name)
    $defTxt = if ($wasDefault) { ' - es la predeterminada' } else { '' }
    Write-Ok "Encontrada cola existente: '$($oldPrinter.Name)' (puerto '$($oldPrinter.PortName)')$defTxt"
}
else {
    Write-Warn2 "No se encontró una cola existente para $PrinterIp. Se va a crear una nueva sin renombrar (pasá -QueueName si la conocés)."
}

# 2) Agregar la conexión IPP — Windows negocia el driver solo (normalmente el Microsoft IPP Class Driver)
Write-Step "Agregando la impresora por IPP (puede tardar unos segundos)..."
$before = @(Get-Printer -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
Add-Printer -ConnectionName $ippUrl
Start-Sleep -Seconds 3
$after   = @(Get-Printer -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
$newName = $after | Where-Object { $before -notcontains $_ } | Select-Object -First 1

if (-not $newName) {
    $msg = @"
Add-Printer no reportó una cola nueva. Revisá 'Configuración > Impresoras y escáneres' a mano:
si '$ippUrl' ya aparece ahí, identificá su nombre y volvé a correr el script con -QueueName <ese nombre>
para que lo renombre/reemplace como corresponde. Si no aparece nada, agregala a mano con la URL de arriba
(ver .NOTES del script) - el cliente IPP nativo puede no estar disponible en este build de Windows.
"@
    throw $msg
}
Write-Ok "Impresora IPP instalada como: '$newName'"

# 3) Renombrarla para que coincida con la cola vieja (no rompe accesos directos / GPO)
if ($QueueName -and $newName -ne $QueueName) {
    $finalName = $QueueName
    if ($oldPrinter -and -not $RemoveOldPrinter) {
        $finalName = "$QueueName (IPP)"   # conviven las dos — nombre temporal para no chocar
    }
    try {
        Rename-Printer -Name $newName -NewName $finalName
        $newName = $finalName
        Write-Ok "Renombrada a: '$newName'"
    }
    catch {
        Write-Warn2 "No se pudo renombrar ($($_.Exception.Message)). Sigue como '$newName'."
    }
}

# 4) Borrar la vieja si se pidió, y volver a poner el nombre final
if ($RemoveOldPrinter -and $oldPrinter) {
    Write-Step "Borrando la cola vieja '$($oldPrinter.Name)' y su puerto..."
    $oldPortName = $oldPrinter.PortName
    Remove-Printer -Name $oldPrinter.Name -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Remove-PrinterPort -Name $oldPortName -ErrorAction SilentlyContinue
    Write-Ok "Cola vieja eliminada."

    if ($QueueName -and $newName -ne $QueueName) {
        try { Rename-Printer -Name $newName -NewName $QueueName; $newName = $QueueName; Write-Ok "Renombrada a: '$newName'" }
        catch { Write-Warn2 "No se pudo renombrar a '$QueueName' ($($_.Exception.Message))." }
    }
}

# 5) Restaurar predeterminada
if ($wasDefault) {
    try {
        $cim = Get-CimInstance -ClassName Win32_Printer -Filter "Name='$newName'"
        if ($cim) { Invoke-CimMethod -InputObject $cim -MethodName SetDefaultPrinter | Out-Null; Write-Ok "'$newName' es la predeterminada." }
    }
    catch { Write-Warn2 "No se pudo marcar como predeterminada: $($_.Exception.Message)" }
}

# 6) Prueba opcional
if ($TestPrint) {
    Write-Step "Enviando página de prueba a '$newName'..."
    try {
        rundll32 printui.dll,PrintUIEntry /k /n "$newName"
        Write-Ok "Enviada. Confirmá en la impresora."
    }
    catch { Write-Warn2 "No se pudo imprimir de prueba: $($_.Exception.Message)" }
}

Write-Host ""
Write-Host "Listo. En Auditor de impresiones -> Job Log, tocá 'Guardar y traer ahora' después del" -ForegroundColor Cyan
Write-Host "próximo trabajo de este usuario y fijate si ya aparece con Páginas/Hojas reales." -ForegroundColor Cyan
