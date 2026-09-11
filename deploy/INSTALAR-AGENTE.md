# Instalar el agente en una PC

El agente es un **servicio de Windows** que registra los trabajos de impresión de ese equipo
(usuario, impresora, documento, hojas) y los manda al servidor. Corre solo, sin ventana, sin
que el usuario tenga que hacer nada.

Se instala en: el **servidor de impresión** de cada sede (cubre todas sus colas compartidas), o
en cada **PC** que imprima directo a una impresora por IP/USB.

---

## Requisitos

- Windows 10/11 o Windows Server.
- PowerShell **como Administrador**.
- El equipo tiene que poder llegar al servidor (`http://<servidor>:8095`).
- Una **clave de agente**: en el panel → primero creá la **Sede** en *Catálogos*, después
  **Claves de agente** → *Generar*, elegí la Sede y copiá la clave (se muestra una sola vez).
  **Una clave por sede** — todas las PC de esa sede se instalan con esa misma clave, y cada trabajo
  que reportan queda etiquetado con esa sede automáticamente.

---

## Pasos

### 1. Conseguir la carpeta del agente

Pedí a quien administra el proyecto la carpeta ya compilada (`Agente\`), o generala una vez con:

```powershell
cd <carpeta-del-proyecto>
dotnet publish src/PrintTrack.Agent -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/Agente
```

Copiá esa carpeta `Agente\` al equipo destino (p. ej. a `C:\Temp\Agente`).

### 2. Instalar (PowerShell como Administrador)

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force

.\install-agent.ps1 `
  -ServerUrl "http://<IP-o-nombre-del-servidor>:8095" `
  -ApiKey "pt_LA_CLAVE_DEL_PANEL" `
  -SourceDir "C:\Temp\Agente"
```

Opcional: `-Printers "Cola A","Cola B"` para vigilar solo esas colas (por defecto vigila todas
menos PDF / XPS / Fax / OneNote).

El script copia todo a `C:\Program Files\Auditor de impresiones\Agente`, crea el servicio
**"Auditor de impresiones - Agente"** (arranque automático, se reinicia solo si se cae) y lo inicia.

### 3. Verificar

```powershell
Get-Service PrintTrackAgent      # tiene que decir Running
```

En el panel:
- **Claves de agente** → tu clave muestra "último uso" + el nombre del equipo (puede tardar unos minutos, o al toque si imprimís algo).
- **Impresoras** → aparecen solas las de ese equipo.
- Imprimí una prueba → aparece en **Trabajos** con tu usuario y la cantidad de hojas.

---

## Desinstalar

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force
.\uninstall-agent.ps1
```

Si lo sacás para siempre, revocá también su clave en el panel.

---

## Problemas comunes

| Síntoma | Causa / solución |
|---|---|
| El servicio no arranca (`Get-Service` no dice Running) | Visor de eventos → Aplicación → origen "PrintTrack Agent". Casi siempre `ServerUrl` o `ApiKey` mal escritos en `C:\Program Files\Auditor de impresiones\Agente\appsettings.json`. |
| `sc query` no muestra nada | En PowerShell `sc` es alias de `Set-Content`. Usá `sc.exe query PrintTrackAgent` o `Get-Service PrintTrackAgent`. |
| No aparece nada en el panel | El equipo no llega al servidor. Probá desde esa PC: `curl http://<servidor>:8095/healthz` (tiene que devolver `ok`). |
| Windows bloquea el `.exe` (SmartScreen) | "Más información → Ejecutar de todos modos". En producción conviene firmar el ejecutable. |
