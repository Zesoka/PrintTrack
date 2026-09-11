# Auditor de impresiones

Sistema de **auditoría de impresión** para Windows. Registra cada trabajo: **qué usuario**, **desde
qué impresora**, **qué documento** y **cuántas hojas**. Panel web para consultar y exportar.

No maneja costos ni cuotas — la parte monetaria la resuelve, por fuera, la empresa que provee los
equipos leyendo los contadores físicos a fin de mes.

## Componentes

| Proyecto | TFM | Rol |
|---|---|---|
| `PrintTrack.Shared` | `net10.0` | Contratos (DTOs) de la API de agente. |
| `PrintTrack.Server` | `net10.0` | API para agentes + panel web (Razor Pages) + EF Core/PostgreSQL. Corre en Docker. |
| `PrintTrack.Agent` | `net10.0-windows` | Servicio de Windows. P/Invoke a `winspool.drv`: observa las colas, registra los trabajos, permite/deniega. |

> Los nombres internos de proyectos/carpetas siguen diciendo `PrintTrack`; solo el nombre visible
> es "Auditor de impresiones".

## Cómo funciona

1. El usuario imprime → el spooler crea el trabajo → el agente lo ve y lo **pausa** un instante.
2. El agente lee del trabajo: usuario, documento, páginas, copias, color, dúplex, tamaño; y llama a
   `POST /api/agent/jobs/authorize`.
3. El servidor resuelve usuario e impresora (los **crea solos** la primera vez) y aplica las dos
   únicas reglas de bloqueo:
   - **Usuario bloqueado** (`IsBlocked`) → deniega y borra el trabajo.
   - **Impresora deshabilitada** (`IsDisabled`) → idem.
   - En cualquier otro caso → permite. Todo queda **registrado** igual (incluidos los denegados).
4. Al terminar la impresión el agente llama a `POST /api/agent/jobs/complete` y el servidor cierra
   el registro con la cantidad real de hojas.

El agente hace siempre conexión **saliente** al servidor (mismo patrón que un scanner de phpIPAM):
no hace falta que el servidor entre a ninguna sede.

## Servidor (Docker)

```bash
cd PrintTrack
docker compose --profile full up -d --build
# panel: http://localhost:8095   ·   admin / ChangeMe!123  (cambiala)
```

Puerto **8095** (no 8080, para no chocar con otros servicios). Ambos contenedores tienen
`restart: unless-stopped`, así que se recuperan solos al reiniciar la máquina. Las claves de
DataProtection (cookies / antiforgery) se guardan en el volumen `printtrack-keys`, así que las
sesiones sobreviven a un rebuild.

Config en `src/PrintTrack.Server/appsettings.json` → sección `PrintTrack`:

| Clave | Def. | Descripción |
|---|---|---|
| `AutoCreateUsers` | `true` | Crea el usuario la primera vez que un login imprime. |
| `SeedAdmin*` | — | Admin sembrado en el primer arranque. |
| `SeedAgentApiKey` | `""` | Si se define, registra una clave de agente inicial. |

Para **SQL Server** en vez de PostgreSQL: cambiá el paquete
`Npgsql.EntityFrameworkCore.PostgreSQL` por `Microsoft.EntityFrameworkCore.SqlServer`,
`o.UseNpgsql(...)` por `o.UseSqlServer(...)` en `Program.cs`, y regenerá la migración.

## Agente

Ver **[deploy/INSTALAR-AGENTE.md](deploy/INSTALAR-AGENTE.md)** — guía corta de instalación.

Resumen: generar una clave en `/AgentKeys`, publicar con
`dotnet publish src/PrintTrack.Agent -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/Agente`,
y correr `deploy/install-agent.ps1` en consola elevada.

## Panel

- **Panel** — KPIs del día (trabajos, hojas, hojas color, denegados) + top usuarios / top impresoras.
- **Trabajos** — histórico con filtros (usuario, impresora, estado, rango de fechas) y **exportar CSV**.
- **Usuarios** — hojas y trabajos de los últimos 30 días; flag *Bloquear*.
- **Impresoras** — alta automática; *Ubicación*, *Deshabilitar*, *Registrar sí/no*.
- **Claves de agente** — generar y revocar.

## Limitaciones conocidas

- **Carrera de pausado**: el agente pausa el trabajo al recibir la notificación del spooler. Para
  trabajos muy chicos existe una ventana en la que el spooler ya mandó páginas al dispositivo.
- **Color / dúplex / tamaño** salen del `DEVMODE` del trabajo; algunos drivers no lo rellenan
  (`—` en el panel).
- El recuento de hojas usa `JOB_INFO_2.TotalPages * Copies`; si el driver reporta 0 se cuenta 1.
- Autenticación de agentes por API key sobre HTTP — usá TLS (reverse proxy) en producción.
- La Sede de un trabajo sale de la clave de agente con que se reportó (una clave por sede). El Área
  sale del usuario. Los históricos se reagrupan si movés un usuario de área.

## Desarrollo

```bash
dotnet build
dotnet ef migrations script --project src/PrintTrack.Server --idempotent   # revisar el esquema
```
