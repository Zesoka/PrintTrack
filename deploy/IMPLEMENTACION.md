# Auditor de impresiones (PrintTrack) — Documento de implementación

**Clínica:** Santa Catalina Neurorehabilitación
**Responsable técnico:** Bruno (IT)
**Fecha:** Septiembre 2026
**Repositorio:** https://github.com/Zesoka/PrintTrack

---

## 1. Qué es y qué NO es

**Auditor de impresiones** es un sistema propio (no un producto comercial) que registra **quién imprimió qué, desde qué impresora, cuántas hojas**, en todas las sedes de la clínica, para poder auditar el uso de impresión por sede y por área.

**Decisiones de diseño explícitas, tomadas al inicio del proyecto:**

- **No es un sistema de costos.** No calcula gasto en tóner/papel ni factura por área — solo cuenta y clasifica.
- **No usa agente por PC.** No se instala nada en las computadoras de los usuarios. Toda la captura de datos es del lado del servidor, contra la red.
- No requiere credenciales de administrador de dominio ni acceso a las PCs.

**Cómo obtiene los datos** (sin agente, 100% por red):

1. **Job Log de la impresora** (HP FutureSmart): el propio panel web (EWS) de la impresora guarda un registro de cada trabajo. El servidor lo scrapea por HTTP cada ciertos minutos.
2. **IPP (`Get-Jobs`)**: protocolo estándar de impresión en red. Si la PC imprime contra la impresora por IPP (en vez de TCP/IP crudo), la impresora también guarda el trabajo del lado IPP, y ahí sí aparece la **cantidad real de páginas** — dato que el Job Log nunca expone.
3. **SNMP** (contador total del equipo): funciona en prácticamente cualquier impresora de red, incluso las muy viejas. Da un número acumulado de hojas impresas de por vida, pero **no por trabajo/usuario**.

El sistema combina las tres fuentes según lo que cada impresora soporta, y dejamos por escrito qué impresoras dan qué nivel de detalle (ver capítulo 6).

---

## 2. Arquitectura

```
                    ┌─────────────────────────────┐
                    │   CT Sede Central (Proxmox)  │
                    │   192.168.2.x                │
                    │                               │
   Sede A ───VPN───►│  Docker:                     │
   Sede B ───VPN───►│   - printtrack-server (.NET) │
   Sede C ───VPN───►│   - printtrack-db (Postgres) │
   ...    ───VPN───►│                               │
   ~11 sedes         └───────────┬───────────────────┘
   ~300 impresoras                │ HTTP/HTTPS (Job Log / IPP)
                                   │ SNMP (contadores)
                                   ▼
                          Impresoras HP en cada sede
                          (alcanzadas por ruteo FortiGate
                           site-to-site entre sedes)
```

- **Backend:** .NET 10, ASP.NET Core Razor Pages, EF Core + Npgsql.
- **Base de datos:** PostgreSQL 17.
- **Despliegue:** Docker Compose (`--profile full`), un único CT en Sede Central que llega a todas las sedes vía VPN site-to-site del FortiGate — no hace falta un servidor por sede.
- **Autenticación:** ASP.NET Core Identity con roles.

---

## 3. Cómo se levanta un cambio (flujo de trabajo)

1. Desarrollo y prueba en local.
2. `git commit` + push al repo.
3. En el CT de Sede Central:
   ```bash
   git pull
   docker compose --profile full up -d --build
   ```

Para diagnosticar cualquier problema en producción, lo primero es siempre:
```bash
docker logs printtrack-server --tail 100
```

---

## 4. Funcionalidades

### 4.1 Panel (`/`)
KPIs generales: hojas impresas hoy/mes, trabajos totales, impresoras con error.

### 4.2 Trabajos (`/Jobs`)
Listado filtrable de cada trabajo de impresión (usuario, documento, impresora, sede, área, fecha, páginas/hojas, color, dúplex, estado). Exportable a CSV. Importación manual de un archivo exportado del Job Log de una impresora, para casos puntuales.

### 4.3 Impresoras (`/Printers`)
Alta/baja/edición de impresoras, agrupadas y filtrables por **Sede**. Cada impresora tiene su configuración de Job Log y de Contador (SNMP) por separado.

### 4.4 Contadores (`/Meters`)
Configuración y lectura SNMP: host, comunidad, OIDs (total/mono/color). Historial de lecturas (`MeterReadings`) para ver evolución del contador en el tiempo.

### 4.5 Job Log (`/JobLog`)
Configuración de scraping del Job Log por impresora (URL, credenciales si aplica, formato de exportación). Botón "Guardar y traer ahora" para forzar un sondeo inmediato y ver el resultado (útil al dar de alta una impresora nueva).

### 4.6 Alertas (`/Alerts`)
Lista, en una sola pantalla, **todas las impresoras con algún error** (de Job Log o de Contador), con el código/mensaje real y una explicación en criollo de qué significa y cómo solucionarlo (o si no tiene solución posible — ver capítulo 6). Se armó específicamente para no tener que entrar impresora por impresora a revisar el estado durante el relevamiento de sedes.

### 4.7 Descubrir (`/Discovery`, solo SuperAdmin)
Escaneo SNMP de un rango de IP (CIDR) que detecta impresoras de red automáticamente y permite darlas de alta en bloque, en vez de cargar cada IP a mano. Pensado para el alta masiva de una sede nueva (~30-50 impresoras cada una).

### 4.8 Catálogos / Administradores (solo SuperAdmin)
Gestión de Sedes, Áreas y usuarios administradores del sistema (no confundir con los usuarios que imprimen — estos son los que **acceden a la app**).

---

## 5. Roles y permisos (RBAC)

Se armó específicamente para que un usuario de una sede pueda **ver** su información sin poder **modificar** nada fuera de su alcance.

| Rol | Alcance | Puede ver | Puede escribir |
|---|---|---|---|
| **SuperAdmin** | Todas las sedes | Todo | Todo, incluyendo Catálogos, Administradores, Descubrir |
| **SedeAdmin** | Sedes asignadas | Solo sus sedes | Solo dentro de sus sedes (impresoras, contadores, Job Log, importar trabajos) |
| **SedeViewer** | Sedes asignadas | Solo sus sedes | Nada (solo lectura) |

Cada usuario se vincula a una o más sedes (`AdminSiteAccess`). Todas las páginas de escritura verifican `CanWrite` + `CanSeeSite(siteId)` server-side (no es solo ocultar el botón en la pantalla) — incluye una corrección aplicada durante esta implementación: la pantalla de importar CSV (`/Jobs/Import`) no tenía ningún control y se cerró ese agujero.

---

## 6. Qué se puede sacar de cada impresora — y qué NO

Este es el punto central para decidir si conviene actualizar equipos.

### 6.1 Nivel 1 — Auditoría completa (usuario + documento + páginas reales)
**Impresoras HP FutureSmart** (confirmado en M630, M527, M452dw y similares), con la PC **imprimiendo por IPP** (no por TCP/IP crudo).

- ✅ Usuario, documento, impresora, fecha/hora, sede, área
- ✅ Páginas y hojas reales por trabajo
- ✅ Color/blanco y negro, dúplex/simple

### 6.2 Nivel 2 — Auditoría de usuario/documento, sin páginas
Mismas impresoras FutureSmart, pero la PC imprime por **TCP/IP crudo (RAW 9100) o LPR** (la configuración por defecto de Windows).

- ✅ Usuario, documento, impresora, fecha/hora, sede, área
- ❌ Páginas y hojas: el Job Log HP **nunca** expone ese dato — no es un bug del sistema, es una limitación del propio Job Log.
- **Solución sin comprar nada:** reconectar la cola de impresión de la PC por IPP en vez de TCP/IP. Ver capítulo 7 — script `Convert-PrinterToIpp.ps1` ya preparado para esto.

### 6.3 Nivel 3 — Solo contador total (sin usuario ni por trabajo)
**Impresoras pre-FutureSmart**: HP LaserJet P2055/P2055dn, M3035, CM2320nf, LaserJet 400/M401, y equivalentes de esa generación.

- ✅ Contador SNMP total acumulado (hojas de por vida, vía OID estándar `1.3.6.1.2.1.43.10.2.1.4.1.1`) — confirmado funcionando en la mayoría de los equipos probados.
- ❌ Sin usuario, sin documento, sin desglose por trabajo — el firmware de esta generación no tiene Job Log ni historial IPP útil.
- **No hay reconfiguración de software que solucione esto.** Es un techo estructural del hardware/firmware del equipo.

### 6.4 Nivel 4 — Impresoras USB, sin red
Impresoras conectadas por USB a una sola PC (sin IP propia), o de red pero sin ningún protocolo de auditoría disponible.

- ❌ Ningún dato automático por red (ni siquiera contador).
- **Alternativa evaluada, no implementada aún** (ver capítulo 8): leer el log interno de Windows (`Print Service Operational`, evento 307), que registra usuario/documento/páginas de cualquier trabajo enviado desde esa PC sin importar la impresora. Requiere un script liviano corriendo solo en esas PCs puntuales — no es el agente pesado que se descartó al inicio del proyecto.

### 6.5 Resumen para el informe a Dirección

| Situación | Se resuelve con | Costo |
|---|---|---|
| Impresora FutureSmart, falta el dato de páginas | Reconectar por IPP (`Convert-PrinterToIpp.ps1`) | $0 — solo trabajo de IT |
| Impresora pre-FutureSmart (P2055, M3035, CM2320nf, M401, etc.) | No tiene solución de software | Reemplazo de equipo, si se quiere auditoría completa en esa sede |
| Impresora USB sin red | Script de evento de Windows en esa PC puntual (a definir) | $0 — solo trabajo de IT, o reemplazo por una de red |

El listado impresora por impresora, con su sede, modelo y estado real, se genera con la consulta SQL ya armada (ver conversación previa / `informe_impresoras.csv`) y se puede correr en cualquier momento para tener el dato actualizado.

---

## 7. Migrar una PC a impresión por IPP (ganar el dato de páginas sin comprar nada)

Script ya preparado: [`deploy/Convert-PrinterToIpp.ps1`](Convert-PrinterToIpp.ps1)

```powershell
# Piloto: agrega la cola por IPP en paralelo, sin tocar la vieja
.\Convert-PrinterToIpp.ps1 -PrinterIp 192.168.30.4 -QueueName "Madero 1"

# Una vez confirmado que anda: reemplaza la cola vieja y prueba imprimir
.\Convert-PrinterToIpp.ps1 -PrinterIp 192.168.30.4 -QueueName "Madero 1" -RemoveOldPrinter -TestPrint
```

Solo tiene sentido en impresoras **Nivel 2** (FutureSmart, ver 6.2). En las Nivel 3/4 no cambia nada.

---

## 8. Dar de alta una sede nueva

Checklist completo en [`deploy/MIGRAR-SEDE.md`](MIGRAR-SEDE.md): verificación de VPN/puertos, carga de Catálogos, escaneo con Descubrir, verificación de Job Log/IPP por impresora, verificación de Contadores, alta del admin de sede, verificación final.

---

## 9. Pendiente / propuesto (no implementado aún)

- **Lectura del log de Windows Print Service (evento 307)** para impresoras USB o sin protocolo de auditoría — script PowerShell + Tarea Programada en las PCs puntuales que lo necesiten, sin reinstalar nada del lado del servidor salvo un endpoint liviano de ingesta. Se evaluó como alternativa **híbrida** al agente completo (descartado desde el inicio del proyecto) — pendiente de decisión de negocio sobre si vale la pena para la cantidad de equipos afectados.
- **Reporte de inventario descargable desde la propia app** (hoy se genera por SQL a mano) — para no depender de acceso a la base de datos cada vez que se necesite el informe de estado por sede.

---

## 10. Seguridad y accesos

- Sin credenciales de administrador de dominio ni acceso a PCs de usuarios.
- Acceso a impresoras solo por protocolos estándar de solo-lectura (HTTP del Job Log, IPP, SNMP con comunidad de solo lectura).
- Acceso a la app por usuario/contraseña propio (ASP.NET Identity), con roles y alcance por sede (capítulo 5).
- El túnel hacia las sedes es el VPN site-to-site ya existente del FortiGate — no se abrió ningún puerto nuevo hacia Internet.

---

*Documento generado como parte de la implementación de Auditor de impresiones — Septiembre 2026.*
