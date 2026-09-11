# Auditor de impresiones

Sistema de **auditoría de impresión**. Registra cada trabajo: **qué usuario**, **desde qué
impresora**, **qué documento**, **cuándo** y —cuando la impresora lo expone— **cuántas hojas**.
Panel web para consultar y exportar. Segmentación por **sede** y por **área/departamento**.

Solo auditoría: no bloquea impresiones (sin agente en las PCs no se puede) ni maneja costos.

## Cómo obtiene los datos (sin agente por PC)

Las PCs imprimen directo por IP a cada impresora; no hay servidor de impresión ni agente. El
servidor va en un contenedor Docker con ruta de red a las impresoras (VPN site-to-site) y saca
la información de los propios equipos:

- **Job Log (HP FutureSmart)** — el servidor entra al EWS de la impresora y **lee la tabla del
  registro de trabajos** (`GET /hp/device/JobLogReport/Edit`): usuario, documento, tipo, estado y
  fecha por cada trabajo. Sondeo automático cada `JobLog:PollMinutes` + botón "traer ahora".
  Dedup por `(impresora, fecha, usuario, documento)`. El registro del equipo es un buffer
  circular: conviene sondear seguido. La tabla **no trae cantidad de páginas**, así que `Hojas`
  queda en "s/d" salvo que un equipo la exponga.
- **Contadores SNMP** — lee el contador de páginas de cada impresora (Printer-MIB
  `prtMarkerLifeCount`, o OIDs de fabricante) + hostname. Da **volumen total por impresora**,
  exacto. Sondeo cada `Meter:PollHours` + on-demand. Reporte de Δ por período con export CSV.
- **Import manual** — subir un CSV/TXT exportado a mano del registro de una impresora
  (`Trabajos → Importar de impresora`). Mismo dedup.

## Componentes

| Proyecto | TFM | Rol |
|---|---|---|
| `PrintTrack.Shared` | `net10.0` | Enums/DTOs compartidos. |
| `PrintTrack.Server` | `net10.0` | Panel web (Razor Pages) + EF Core/PostgreSQL + sondeo SNMP y Job Log. Corre en Docker. |

> Los nombres internos siguen diciendo `PrintTrack`; el nombre visible es "Auditor de impresiones".

## Servidor (Docker)

```bash
cd PrintTrack
docker compose --profile full up -d --build
# panel: http://localhost:8095   ·   admin / ChangeMe!123  (cambiala)
```

Puerto **8095**. Ambos contenedores con `restart: unless-stopped`. Las claves de DataProtection
(cookies / antiforgery) se guardan en el volumen `printtrack-keys`, así que las sesiones
sobreviven a un rebuild.

Para correr desde una imagen ya construida (mini PC con poca RAM): ver
`docker-compose.prebuilt.yml`.

Config en `src/PrintTrack.Server/appsettings.json` → sección `PrintTrack`:

| Clave | Def. | Descripción |
|---|---|---|
| `AutoCreateUsers` | `true` | Crea el usuario la primera vez que aparece en un registro. |
| `SeedAdmin*` | — | Admin sembrado en el primer arranque. |
| `Meter:PollHours` | `6` | Cada cuánto sondea SNMP. |
| `JobLog:PollMinutes` | `20` | Cada cuánto lee el registro de cada impresora. |

Para **SQL Server** en vez de PostgreSQL: cambiá el paquete
`Npgsql.EntityFrameworkCore.PostgreSQL` por `Microsoft.EntityFrameworkCore.SqlServer`,
`o.UseNpgsql(...)` por `o.UseSqlServer(...)` en `Program.cs`, y regenerá la migración.

## Panel

- **Panel** — KPIs del día + actividad por sede / por área (últimos 7 días) + top usuarios / impresoras.
- **Trabajos** — histórico con filtros (usuario, impresora, estado, sede, área, rango) y export CSV.
  También "Importar de impresora".
- **Usuarios** — hojas y trabajos de los últimos 30 días; asignación de **área**.
- **Impresoras** — alta manual; *Ubicación*, *Sede*, *Registrar sí/no*.
- **Contadores** — config SNMP por impresora, prueba de OID, lecturas manuales, reporte de Δ.
- **Job Log** — config del scrape por impresora (URL base, ruta, usuario/clave si hace falta),
  estado del último sondeo, "traer ahora".
- **Catálogos** — ABM de Sedes y Áreas.

## Limitaciones conocidas

- **Hojas por trabajo**: solo si el Job Log del equipo la expone (la LaserJet M630 no). Si no,
  el volumen de páginas sale de SNMP (por impresora, no por usuario).
- **Sin bloqueo**: al imprimir directo por IP, PrintTrack registra pero no puede frenar un
  trabajo. Eso se haría en el FortiGate o en la impresora.
- El scrape del Job Log necesita que el EWS muestre el registro (en la M630 el invitado lo ve;
  otros equipos pueden pedir usuario/clave — se cargan en la config de Job Log).
- La **Sede** de un trabajo sale de la impresora; el **Área** sale del usuario (hay que
  asignarla a mano en Usuarios). Los históricos se reagrupan si cambiás un usuario de área.

## Desarrollo

```bash
dotnet build
dotnet ef migrations script --project src/PrintTrack.Server --idempotent   # revisar el esquema
```
